using System.Text.Json;

using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

/// <summary>
/// T-583 slice 4: DES-0052 layout-eng-30. Layout renders DES-0056's authorized receipt and follows its stable link to
/// Access's decision and four-stage trace. The shared fixture is the one platform model the React and Blazor probes render.
/// </summary>
public sealed class LayoutExecutionObservationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact(DisplayName = "layout-eng-30: the receipt fixture preserves run and decision identities and Access's four ordered, versioned stages")]
    public async Task TheReceiptPreservesIdentitiesAndFourOrderedVersionedStages()
    {
        var receipt = Receipt("receipt");
        Assert.Equal(("run.workflow.invoice-approval.0001", "succeeded", "audit.decision.16300000-0000-0000-0000-000000000001"),
            (receipt.RunId, receipt.Status, receipt.AccessDecisionId));
        Assert.Equal([1, 2, 3], receipt.Trace.Select(step => step.Ordinal));

        string? asked = null;
        var trace = await LayoutExecutionObservation.ReadAccessTraceAsync(receipt, (decision, _) => { asked = decision; return ValueTask.FromResult(Read("valid")); });

        // The link is followed by the receipt's stable identity, and the stored stages come back as stored.
        Assert.Equal(receipt.AccessDecisionId, asked);
        Assert.Equal(LayoutAccessEvidence.Valid, trace.Evidence);
        Assert.Equal(2, trace.Version);
        Assert.Equal([(1, "act"), (2, "effective-roles"), (3, "standings"), (4, "verdict")], trace.Stages.Select(step => (step.Ordinal, step.Stage)));
        AssertMatchesFixture("valid", trace);
    }

    [Theory(DisplayName = "layout-eng-30: absent, missing, forbidden, malformed and valid evidence stay distinct, and an absent decision is never read")]
    [InlineData("valid", LayoutAccessEvidence.Valid)]
    [InlineData("missing", LayoutAccessEvidence.Missing)]
    [InlineData("forbidden", LayoutAccessEvidence.Forbidden)]
    [InlineData("malformed", LayoutAccessEvidence.Malformed)]
    public async Task EvidenceOutcomesStayDistinct(string name, LayoutAccessEvidence expected)
    {
        var trace = await LayoutExecutionObservation.ReadAccessTraceAsync(Receipt("receipt"), (_, _) => ValueTask.FromResult(Read(name)));
        Assert.Equal(expected, trace.Evidence);
        AssertMatchesFixture(name, trace);

        // No recorded decision is absence, not a missing trace: nothing is read.
        var reads = 0;
        var absent = await LayoutExecutionObservation.ReadAccessTraceAsync(Receipt("absentReceipt"), (_, _) => { reads++; return ValueTask.FromResult(Read(name)); });
        Assert.Equal((LayoutAccessEvidence.Absent, 0), (absent.Evidence, reads));
        AssertMatchesFixture("absent", absent);

        // A guard that refused before Access decided recorded no decision either.
        Assert.Equal(LayoutAccessEvidence.Absent, LayoutExecutionObservation.Classify(new(AuthorizationTraceAvailability.PreDecisionRefusal, null, [], null, new("guard", "", ""))).Evidence);
        // An unsupported version is malformed, never valid.
        var future = Read("valid") with { Version = AuthorizationDecisionEvidence.CurrentVersion + 1 };
        Assert.Equal(LayoutAccessEvidence.Malformed, LayoutExecutionObservation.Classify(future).Evidence);
    }

    [Fact(DisplayName = "layout-eng-30: mixed deciding facts select the declared grant, never the first generic deciding prefix")]
    public void MixedDecidingFactsSelectTheDeclaredGrant()
    {
        var read = Read("mixed");
        Assert.StartsWith("deciding:standing:", read.Steps[1].Facts.First(fact => fact.StartsWith("deciding:", StringComparison.Ordinal)), StringComparison.Ordinal);

        var trace = LayoutExecutionObservation.Classify(read);
        Assert.Equal("grant-164@3", trace.DecidingGrant);
        AssertMatchesFixture("mixed", trace);

        // With no grant fact, no grant is named, whatever other deciding facts say.
        var noGrant = new AuthorizationTraceRead(read.Availability, read.Version,
            [.. read.Steps.Select(step => step.Ordinal == 2 ? new AuthorizationTraceStep(2, step.Stage, ["deciding:none", "deciding:standing:approver-rule"]) : step)], null);
        Assert.Null(LayoutExecutionObservation.Classify(noGrant).DecidingGrant);
    }

    private static void AssertMatchesFixture(string name, LayoutAccessTrace trace)
    {
        var expected = Fixture().GetProperty("traces").GetProperty(name);
        Assert.Equal(expected.GetProperty("evidence").GetString(), trace.Evidence.ToString().ToLowerInvariant());
        Assert.Equal(expected.GetProperty("version").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("version").GetInt32(), trace.Version);
        Assert.Equal(expected.GetProperty("decidingGrant").GetString(), trace.DecidingGrant);
        Assert.Equal(Steps(expected.GetProperty("stages")).Select(Shape), trace.Stages.Select(Shape));
    }

    private static string Shape(AuthorizationTraceStep step) => $"{step.Ordinal}|{step.Stage}|{string.Join("\n", step.Facts)}";

    private static LayoutExecutionReceipt Receipt(string name) => Fixture().GetProperty(name).Deserialize<LayoutExecutionReceipt>(Web)!;

    private static AuthorizationTraceRead Read(string name)
    {
        var read = Fixture().GetProperty("reads").GetProperty(name);
        var version = read.GetProperty("version");
        return new(Enum.Parse<AuthorizationTraceAvailability>(read.GetProperty("availability").GetString()!),
            version.ValueKind == JsonValueKind.Null ? null : version.GetInt32(), Steps(read.GetProperty("steps")), null);
    }

    private static AuthorizationTraceStep[] Steps(JsonElement steps) => [.. steps.EnumerateArray().Select(step => new AuthorizationTraceStep(
        step.GetProperty("ordinal").GetInt32(), step.GetProperty("stage").GetString()!, step.GetProperty("facts").EnumerateArray().Select(fact => fact.GetString()!)))];

    private static JsonElement Fixture() =>
        JsonElement.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "execution-observation.json")));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Harborline.Platform.slnx");
    }
}
