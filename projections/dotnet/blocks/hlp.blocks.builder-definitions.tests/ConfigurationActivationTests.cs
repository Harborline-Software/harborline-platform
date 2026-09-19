using System.Text.Json;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class ConfigurationActivationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static ConfigurationReference Reference(string key) => new(key, "1.0.0", new string('a', 64));
    private static ResolvedConfiguration Input(string tenant = "tenant-a", string owner = "a") => new(tenant, ["a", "b"],
        [new(Reference("a"), [Reference("form")], []), new(Reference("b"), [Reference("form")], [])],
        [new("form", owner)], Reference("platform"), []);
    private static ConfigurationGeneration Baseline() => ConfigurationGeneration.Resolve(Input());
    private static ConfigurationGeneration Candidate() => ConfigurationGeneration.Resolve(Input(owner: "b"));
    private static ConfigurationProjectionValidation Valid(ConfigurationGeneration baseline, ConfigurationGeneration candidate) =>
        new(Reference("projection"), []);
    private static ConfigurationActivationAuthority Allow(ConfigurationActivationRequest request) => new(true, "access-decision-1");
    private static ConfigurationPreparation Prepare() => ConfigurationPreparation.Prepare(Baseline(), Baseline().Digest, Candidate(), Valid);
    private static ConfigurationActivationRequest Request() => new(Prepare().Prepared!, "actor-1", new("intent-1", "Activate checked release"));

    [Fact]
    public void Prepared_identity_and_ownership_are_the_resolved_immutable_snapshot()
    {
        var input = Input(owner: "b");
        var candidate = ConfigurationGeneration.Resolve(input);
        var baseline = Baseline();
        var preparation = ConfigurationPreparation.Prepare(baseline, baseline.Digest, candidate, (seenBaseline, seenCandidate) =>
        {
            Assert.Same(baseline, seenBaseline);
            Assert.Same(candidate, seenCandidate);
            return Valid(seenBaseline, seenCandidate);
        });
        Assert.Same(candidate, preparation.Prepared!.Candidate);
        Assert.Equal("b", Assert.Single(preparation.Prepared.Ownership).PackageKey);
        Assert.Throws<NotSupportedException>(() => ((IList<ConfigurationOwnership>)preparation.Prepared.Ownership)[0] = new("form", "a"));
        Assert.Equal("Preparing", ConfigurationActivationDetail.Bind(preparation)["status"]);
        Assert.Equal(baseline.Digest, ConfigurationActivationDetail.Bind(preparation)["effectiveDigest"]);
    }

    [Theory]
    [InlineData("projection-failed", "forms/invoice")]
    [InlineData("compatibility-incompatible", "records/invoice")]
    [InlineData("compatibility-unknown", "workflows/review")]
    public void Named_preparation_failures_preserve_the_prior_generation(string code, string target)
    {
        var findings = new[] { new ConfigurationActivationRefusal(code, target, "Cannot prepare this component.") };
        var preparation = ConfigurationPreparation.Prepare(Baseline(), Baseline().Digest, Candidate(), (_, _) => new(Reference("projection"), findings));
        findings[0] = new("changed", "changed", "changed");
        Assert.Null(preparation.Prepared);
        Assert.Equal(code, Assert.Single(preparation.Refusals).Code);
        var values = ConfigurationActivationDetail.Bind(preparation);
        Assert.Equal("Refused", values["status"]);
        Assert.Equal(Baseline().Digest, values["effectiveDigest"]);
        Assert.NotEqual(values["candidateDigest"], values["effectiveDigest"]);
        Assert.Contains(target, values["refusals"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-result")]
    [InlineData("missing-findings")]
    [InlineData("invalid-finding")]
    [InlineData("missing-projection")]
    [InlineData("invalid-projection")]
    public void Incomplete_validation_never_produces_a_prepared_token(string failure)
    {
        var validation = failure switch
        {
            "missing-result" => null!,
            "missing-findings" => new ConfigurationProjectionValidation(Reference("projection"), null!),
            "invalid-finding" => new ConfigurationProjectionValidation(Reference("projection"), [new("", "", "")]),
            "missing-projection" => new ConfigurationProjectionValidation(null!, []),
            _ => new ConfigurationProjectionValidation(Reference("projection") with { Digest = "bad" }, []),
        };
        var preparation = ConfigurationPreparation.Prepare(Baseline(), Baseline().Digest, Candidate(), (_, _) => validation);
        Assert.Null(preparation.Prepared);
        Assert.Single(preparation.Refusals);
    }

    [Fact]
    public void Stale_preparation_refuses_before_projection_and_retains_both_baselines()
    {
        var preparation = ConfigurationPreparation.Prepare(Baseline(), Candidate().Digest, Candidate(), (_, _) => throw new InvalidOperationException());
        Assert.Equal("configuration-baseline-stale", Assert.Single(preparation.Refusals).Code);
        Assert.Equal(Candidate().Digest, preparation.ExpectedBaselineDigest);
        Assert.Equal(Baseline().Digest, preparation.Baseline.Digest);
    }

    [Fact]
    public void Cross_tenant_preparation_and_switch_refuse_before_callbacks()
    {
        var other = ConfigurationGeneration.Resolve(Input("other-tenant"));
        var preparation = ConfigurationPreparation.Prepare(Baseline(), Baseline().Digest, other, (_, _) => throw new InvalidOperationException());
        Assert.Equal("configuration-tenant-mismatch", Assert.Single(preparation.Refusals).Code);
        var decision = ConfigurationActivation.DecideCompareAndSwap(other, Request(), _ => throw new InvalidOperationException());
        Assert.Equal("configuration-tenant-mismatch", decision.Refusal!.Code);
    }

    [Fact]
    public void Baseline_changed_after_preparation_refuses_without_reauthorizing_or_retrying()
    {
        var current = Candidate();
        var decision = ConfigurationActivation.DecideCompareAndSwap(current, Request(), _ => throw new InvalidOperationException());
        Assert.Null(decision.NewGeneration);
        Assert.Equal("configuration-baseline-stale", decision.Refusal!.Code);
        Assert.Same(current, ConfigurationActivationOutcome.Refused(decision).EffectiveGeneration);
        Assert.Throws<ArgumentException>(() => ConfigurationActivationOutcome.ConfirmCommitted(decision));
    }

    [Theory]
    [InlineData("principal", "configuration-principal-required")]
    [InlineData("intent", "configuration-evidence-required")]
    [InlineData("reason", "configuration-evidence-required")]
    public void Required_switch_inputs_refuse_before_authority(string missing, string code)
    {
        var request = Request();
        request = missing switch
        {
            "principal" => request with { Principal = "" },
            "intent" => request with { EvidenceIntent = new("", "reason") },
            _ => request with { EvidenceIntent = new("intent-1", "") },
        };
        var decision = ConfigurationActivation.DecideCompareAndSwap(Baseline(), request, _ => throw new InvalidOperationException());
        Assert.Equal(code, decision.Refusal!.Code);
    }

    [Theory]
    [InlineData(false, "access-denial")]
    [InlineData(true, "")]
    public void Authority_denial_or_missing_decision_identity_refuses(bool allowed, string decisionId)
    {
        var decision = ConfigurationActivation.DecideCompareAndSwap(Baseline(), Request(), _ => new(allowed, decisionId));
        Assert.Equal("configuration-authority-refused", decision.Refusal!.Code);
        Assert.Equal("Refused", ConfigurationActivationDetail.Bind(ConfigurationActivationOutcome.Refused(decision))["status"]);
    }

    [Fact]
    public void One_decision_binds_all_inputs_and_fresh_authority_before_host_confirmation()
    {
        var request = Request();
        var calls = 0;
        var decision = ConfigurationActivation.DecideCompareAndSwap(Baseline(), request, seen =>
        {
            calls++;
            Assert.Same(request, seen);
            Assert.Equal("b", Assert.Single(seen.Prepared.Ownership).PackageKey);
            Assert.Equal("intent-1", seen.EvidenceIntent.Id);
            Assert.Equal("actor-1", seen.Principal);
            return Allow(seen);
        });
        Assert.Equal(1, calls);
        Assert.Equal("access-decision-1", decision.Authority!.DecisionId);
        Assert.Same(request.Prepared.Candidate, decision.NewGeneration);
        Assert.Throws<ArgumentException>(() => ConfigurationActivationOutcome.Refused(decision));
        var outcome = ConfigurationActivationOutcome.ConfirmCommitted(decision);
        Assert.Same(request.Prepared.Candidate, outcome.EffectiveGeneration);
        Assert.Equal("Effective", ConfigurationActivationDetail.Bind(outcome)["status"]);
    }

    [Fact]
    public void Projection_lost_at_switch_can_be_refused_without_reporting_candidate_effective()
    {
        var outcome = ConfigurationActivationOutcome.Refused(Baseline(), Request(), new("projection-missing", "projection", "Prepared projection is unavailable."));
        Assert.Equal(Baseline().Digest, outcome.EffectiveGeneration.Digest);
        Assert.Equal("Refused", ConfigurationActivationDetail.Bind(outcome)["status"]);
        Assert.Throws<ArgumentException>(() => ConfigurationActivationOutcome.ConfirmCommitted(outcome.Decision));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interrupted_preparation_produces_no_token_or_effective_outcome(bool cancelled)
    {
        ConfigurationPreparation? result = null;
        var baseline = Baseline();
        var before = baseline.References.GetRawText();
        Exception crash = cancelled ? new OperationCanceledException() : new IOException("Projection interrupted");
        Assert.Same(crash, Record.Exception(() => result = ConfigurationPreparation.Prepare(baseline, baseline.Digest, Candidate(), (_, _) => throw crash)));
        Assert.Null(result);
        Assert.Equal(before, baseline.References.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interrupted_switch_decision_produces_no_success_and_can_be_rechecked(bool cancelled)
    {
        var request = Request();
        ConfigurationActivationDecision? result = null;
        Exception crash = cancelled ? new OperationCanceledException() : new IOException("Authority interrupted");
        Assert.Same(crash, Record.Exception(() => result = ConfigurationActivation.DecideCompareAndSwap(Baseline(), request, _ => throw crash)));
        Assert.Null(result);
        // A later call must still check the current baseline, not resume an old decision.
        var stale = ConfigurationActivation.DecideCompareAndSwap(Candidate(), request, Allow);
        Assert.Equal("configuration-baseline-stale", stale.Refusal!.Code);
    }

    [Fact]
    public void Interruption_after_the_switch_decision_does_not_make_preparation_effective()
    {
        var preparation = Prepare();
        var baseline = preparation.Baseline;
        var request = new ConfigurationActivationRequest(preparation.Prepared!, "actor-1", new("intent-1", "Activate checked release"));
        void InterruptBeforeCommit()
        {
            var decision = ConfigurationActivation.DecideCompareAndSwap(baseline, request, Allow);
            Assert.NotNull(decision.NewGeneration);
            // Simulated host interruption before its durable commit: no success attestation is made.
            throw new IOException("Interrupted before commit");
        }
        Assert.Throws<IOException>(InterruptBeforeCommit);
        var values = ConfigurationActivationDetail.Bind(preparation);
        Assert.Equal("Preparing", values["status"]);
        Assert.Equal(baseline.Digest, values["effectiveDigest"]);
    }

    [Fact]
    public void A_second_prepared_request_cannot_overwrite_the_first_committed_generation()
    {
        var pinnedRead = Baseline();
        var first = Request();
        var second = Request() with { EvidenceIntent = new("intent-2", "Another activation") };
        var firstDecision = ConfigurationActivation.DecideCompareAndSwap(pinnedRead, first, Allow);
        // Model the host's committed snapshot as an immutable value; no persistence implementation is claimed.
        var current = ConfigurationActivationOutcome.ConfirmCommitted(firstDecision).EffectiveGeneration;
        var secondDecision = ConfigurationActivation.DecideCompareAndSwap(current, second, _ => throw new InvalidOperationException());
        Assert.Equal("configuration-baseline-stale", secondDecision.Refusal!.Code);
        Assert.Same(current, ConfigurationActivationOutcome.Refused(secondDecision).EffectiveGeneration);
        Assert.Equal(Baseline().Digest, pinnedRead.Digest);
        Assert.Equal("a", pinnedRead.References.GetProperty("ownership")[0].GetProperty("packageKey").GetString());
        Assert.Equal("b", current.References.GetProperty("ownership")[0].GetProperty("packageKey").GetString());
    }

    [Fact]
    public void Producer_bindings_match_the_one_fixture_consumed_by_both_renderers()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/activation.json")));
        using var generation = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/generation.json")));
        var input = generation.RootElement.GetProperty("input").Deserialize<ResolvedConfiguration>(JsonOptions)!;
        var baseline = ConfigurationGeneration.Resolve(input);
        var candidate = ConfigurationGeneration.Resolve(input with { Ownership = [new("form", "b"), new("rule", "a"), new("shared", "c")] });
        var failed = ConfigurationPreparation.Prepare(baseline, baseline.Digest, candidate, (_, _) =>
            new(Reference("projection"), [new("projection-failed", "forms/invoice", "Invoice form projection failed.")]));
        var prepared = ConfigurationPreparation.Prepare(baseline, baseline.Digest, candidate, Valid);
        var decision = ConfigurationActivation.DecideCompareAndSwap(baseline,
            new(prepared.Prepared!, "actor-1", new("intent-1", "Activate checked release")), Allow);
        var bindings = new[]
        {
            ConfigurationActivationDetail.Released(baseline, candidate), ConfigurationActivationDetail.Bind(prepared),
            ConfigurationActivationDetail.Bind(failed), ConfigurationActivationDetail.Bind(ConfigurationActivationOutcome.ConfirmCommitted(decision)),
        };
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(4, cases.Length);
        for (var i = 0; i < cases.Length; i++)
        {
            var values = cases[i].GetProperty("values");
            Assert.Equal(bindings[i].Count, values.EnumerateObject().Count());
            foreach (var value in values.EnumerateObject()) Assert.Equal(value.Value.GetString(), bindings[i][value.Name]);
        }
        using var pack = JsonDocument.Parse(PlatformPackageSeed.Export());
        var payload = pack.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7")
            .GetProperty("content").GetProperty("payload");
        Assert.Equal(ConfigurationActivationDetail.Definition.GetRawText(), payload.GetProperty("configurationActivationDetail").GetRawText());
        Assert.Equal(ConfigurationActivationDetail.Statuses.GetRawText(), payload.GetProperty("configurationActivationStatuses").GetRawText());
    }
}
