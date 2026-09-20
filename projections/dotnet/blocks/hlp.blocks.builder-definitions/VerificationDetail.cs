using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The released domain-facing vocabulary and Form for one verification run. Both runtime lanes
/// render this definition over these bindings, so neither authors a status label, a pass rule or a
/// failure sentence of its own.
/// </summary>
public static class VerificationDetail
{
    /// <summary>The five outcomes, as stable codes with localized labels.</summary>
    public static JsonElement Statuses { get; } = JsonSerializer.SerializeToElement(new
    {
        Passed = Text("Passed"),
        Failed = Text("Failed"),
        Vacuous = Text("Nothing was checked"),
        Unsupported = Text("Not supported"),
        Blocked = Text("Blocked"),
    });

    /// <summary>One read-only Form over a verification run and its receipt.</summary>
    public static JsonElement Definition { get; } = JsonSerializer.SerializeToElement(new
    {
        formId = "platform.detail.verification-run", version = "1.0.0",
        title = Text("Verification run"),
        description = Text("Declared business claims executed against a candidate generation through the production interpreters, with a receipt binding what was verified, what it was verified against, and what ran it."),
        sections = new[]
        {
            new
            {
                id = "run", title = Text("Verification run"),
                fields = new[]
                {
                    Field("status", "Result"),
                    Field("tenantKey", "Tenant"),
                    Field("receiptId", "Receipt"),
                    Field("candidateDigest", "Candidate generation"),
                    Field("baselineDigest", "Baseline generation"),
                    Field("suite", "Suite"),
                    Field("fixtures", "Fixtures"),
                    Field("engines", "Engines"),
                    Field("determinism", "Declared inputs"),
                    Field("cases", "Claims"),
                    Field("failures", "What failed"),
                    Field("receiptDigest", "Receipt digest"),
                },
            },
        },
    });

    /// <summary>
    /// Produces the read-only bindings from the receipt and the suite it ran. Everything shown is
    /// read out of the receipt; nothing is re-derived, defaulted or summarized into a colour the
    /// receipt does not carry.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Bind(VerificationReceipt receipt, VerificationSuite suite)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(suite);
        var titles = suite.Cases.ToDictionary(item => item.CaseId, item => item.Title, StringComparer.Ordinal);
        string Row(VerificationCaseOutcome outcome) =>
            outcome.RowId is null ? outcome.CaseId : $"{outcome.CaseId}[{outcome.RowId}]";
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = Label(receipt.Status),
            ["tenantKey"] = receipt.TenantKey,
            ["receiptId"] = receipt.ReceiptId,
            ["candidateDigest"] = receipt.CandidateDigest,
            ["baselineDigest"] = receipt.BaselineDigest,
            ["suite"] = $"{receipt.Suite.Key} {receipt.Suite.Revision}: {receipt.Suite.Digest}",
            ["fixtures"] = string.Join("\n", receipt.Fixtures.Select(fixture => $"{fixture.Key}: {fixture.Digest}")),
            ["engines"] = string.Join("\n", receipt.Engines.Select(engine => $"{engine.Key} {engine.Revision}: {engine.Digest}")),
            ["determinism"] = string.Join("\n", suite.Fixtures
                .Where(fixture => receipt.Fixtures.Any(used => used.Key == fixture.FixtureId))
                .Select(Determinism)),
            ["cases"] = string.Join("\n", receipt.Outcomes.Select(outcome =>
                $"{Row(outcome)} — {titles.GetValueOrDefault(outcome.CaseId, outcome.CaseId)}: {Label(outcome.Status)}")),
            ["failures"] = string.Join("\n", receipt.Outcomes
                .Where(outcome => outcome.Status != VerificationStatus.Passed)
                .SelectMany(outcome => outcome.Observations.Where(observation => !observation.Matched)
                    .Select(observation => $"{Row(outcome)} {Name(observation)}: expected {observation.ExpectedJson}, actual {observation.ActualJson}")
                    .DefaultIfEmpty($"{Row(outcome)}: {Label(outcome.Status)}{(outcome.Reason is null ? string.Empty : $" — {outcome.Reason}")}"))),
            ["receiptDigest"] = receipt.Digest,
        });
    }

    /// <summary>The released English label for one outcome; surfaces never author their own.</summary>
    public static string Label(VerificationStatus status) =>
        Statuses.GetProperty(status.ToString()).GetProperty("values").GetProperty("en").GetString()!;

    private static string Name(VerificationObservation observation) =>
        observation.Target is null ? observation.PredicateId : $"{observation.PredicateId}:{observation.Target}";

    private static string Determinism(VerificationFixture fixture) => string.Create(CultureInfo.InvariantCulture,
        $"{fixture.FixtureId}: {fixture.Instant.ToUniversalTime():O} {fixture.TimeZone} {fixture.Locale} seed={fixture.IdentifierSeed} ordering={fixture.Ordering} actor={fixture.Actor} grants={Join(fixture.Grants.Select(grant => $"{grant.RoleKey}@{grant.Scope}"))} ports={Join(fixture.Ports.Select(port => $"{port.PortId}={port.Simulator}"))}");

    private static string Join(IEnumerable<string> values)
    {
        var array = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return array.Length == 0 ? "none" : string.Join(",", array);
    }

    private static object Text(string text) => new { defaultLocale = "en", values = new { en = text } };
    private static object Field(string name, string label) => new
    {
        name, label = Text(label), controlHint = "readonly", isSensitive = false, isReadable = true, readOnly = true,
    };
}
