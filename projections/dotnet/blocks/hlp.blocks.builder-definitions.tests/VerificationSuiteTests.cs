using System.Text.Json.Nodes;
using System.Text.Json;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-463. The declarative verification producer, exercised through the one Records-and-Rules example
/// both runtime lanes author and run: <c>conformance/hlp.blocks.builder-definitions/verification.json</c>.
/// </summary>
public sealed class VerificationSuiteTests
{
    private static JsonElement Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "conformance"))) root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root!.FullName, "conformance/hlp.blocks.builder-definitions/verification.json")));
        return document.RootElement.Clone();
    }

    private static JsonElement Run(string step) => Fixture().GetProperty("runs").EnumerateArray()
        .Single(item => item.GetProperty("step").GetString() == step);

    private static void AssertFixture(string step, IReadOnlyDictionary<string, string> bound)
    {
        var expected = Run(step).GetProperty("values");
        foreach (var field in expected.EnumerateObject()) Assert.Equal(field.Value.GetString(), bound[field.Name]);
        Assert.Equal(expected.EnumerateObject().Count(), bound.Count);
    }

    // Acceptance 1: one invariant and one parameterized examples table, without executable scripts.
    [Fact]
    public void The_suite_is_one_invariant_and_one_examples_table_and_carries_no_executable_content()
    {
        var suite = VerificationExample.Suite;
        var invariant = suite.Cases.Single(item => item.CaseId == "approval-authority");
        var parameterized = suite.Cases.Single(item => item.CaseId == "invoice-total");
        Assert.False(invariant.IsParameterized);
        Assert.Empty(invariant.Rows);
        Assert.True(parameterized.IsParameterized);
        Assert.Equal(["ten-at-one-hundred", "three-at-four-hundred", "two-at-one-hundred"],
            parameterized.Rows.Select(row => row.RowId).Order(StringComparer.Ordinal));

        // Nothing in a case is executable: an action and a predicate can only be named, and only
        // from the closed catalogue, so there is no place a script, expression or address could go.
        foreach (var item in suite.Cases)
        {
            Assert.NotNull(VerificationCatalog.Action(item.ActionId));
            foreach (var assertion in item.Assertions) Assert.NotNull(VerificationCatalog.Predicate(assertion.PredicateId));
        }
        var document = JsonSerializer.Deserialize<JsonElement>(suite.Document.Span);
        Assert.Equal("harborline.verification-suite/v1", document.GetProperty("contract").GetString());
        Assert.Equal(Fixture().GetProperty("suiteDigest").GetString(), suite.Digest);
        Assert.Equal(Fixture().GetProperty("catalogueDigest").GetString(), VerificationCatalog.Reference.Digest);

        // The suite the app lanes submit is this document, and reading it back cannot smuggle in a
        // case authoring would have refused: Parse re-admits through Declare.
        var parsed = VerificationSuite.Parse(Fixture().GetProperty("suite").GetRawText(), out var refusals);
        Assert.Empty(refusals);
        Assert.Equal(suite.Digest, parsed!.Digest);

        // Parse refuses by name rather than throwing, and it refuses a document written against
        // another catalogue rather than silently re-deriving its meaning under this one.
        static string Refusal(string json)
        {
            Assert.Null(VerificationSuite.Parse(json, out var refused));
            return Assert.Single(refused).Code;
        }
        Assert.Equal("verification-suite-document-invalid", Refusal("{"));
        Assert.Equal("verification-suite-contract-unknown", Refusal("""{"contract":"other/v1"}"""));
        Assert.Equal("verification-catalogue-mismatch", Refusal($$"""
            {"contract":"harborline.verification-suite/v1","suiteId":"s","version":"1.0.0","catalogue":"{{new string('0', 64)}}"}
            """));
        // A member that is not a string is dropped rather than thrown on, so it lands on a named
        // refusal. Here the dropped member is the case's only bound input.
        var mangled = JsonNode.Parse(Fixture().GetProperty("suite").GetRawText())!;
        mangled["cases"]![0]!["inputs"]!["recordType"] = 7;
        Assert.Null(VerificationSuite.Parse(mangled.ToJsonString(), out var mangledRefusals));
        Assert.Contains(mangledRefusals, refusal => refusal.Code == "verification-input-unbound");
        // An instant that does not parse is an undeclared instant, not a zero one.
        var undated = JsonNode.Parse(Fixture().GetProperty("suite").GetRawText())!;
        undated["fixtures"]![0]!["instant"] = "not-an-instant";
        Assert.Null(VerificationSuite.Parse(undated.ToJsonString(), out var undatedRefusals));
        Assert.Contains(undatedRefusals, refusal => refusal.Code == "verification-fixture-input-required");
    }

    // Acceptance 2: time, identifiers, locale, ordering, actor authority and simulated ports are
    // explicit inputs, and a fixture that omits one is refused rather than defaulted.
    [Fact]
    public void Every_determinism_input_is_explicit_and_omitting_one_refuses()
    {
        var clerk = VerificationExample.Suite.Fixtures.Single(fixture => fixture.FixtureId == "clerk");
        Assert.Equal(VerificationExample.Instant, clerk.Instant);
        Assert.Equal("Europe/London", clerk.TimeZone);
        Assert.Equal("en-GB", clerk.Locale);
        Assert.Equal("t-463-invoice", clerk.IdentifierSeed);
        Assert.Equal("ordinal-by-key", clerk.Ordering);
        Assert.Equal("dana.okafor", clerk.Actor);
        Assert.Equal([new VerificationGrant("invoice.author", "finance")], clerk.Grants);
        Assert.Equal(["notifications.email", "payments.remit"], clerk.Ports.Select(port => port.PortId).Order(StringComparer.Ordinal));

        foreach (var blank in new[]
                 {
                     clerk with { TimeZone = " " }, clerk with { Locale = " " }, clerk with { IdentifierSeed = " " },
                     clerk with { Ordering = " " }, clerk with { Actor = " " },
                 })
        {
            VerificationSuite.TryDeclare("s", "1.0.0", [blank], VerificationExample.Suite.Cases, out var refusals);
            Assert.Contains(refusals, refusal => refusal.Code == "verification-fixture-input-required");
        }
    }

    // Acceptance 3: every case starts from a clean fixture, and the run records expected and actual
    // typed observations rather than a colour.
    [Fact]
    public void Each_case_names_one_fixture_and_every_observation_carries_expected_and_actual()
    {
        var suite = VerificationExample.Suite;
        foreach (var item in suite.Cases)
            Assert.Contains(suite.Fixtures, fixture => fixture.FixtureId == item.FixtureId);
        // The two claims start from different fixtures, and each fixture is digested on its own, so
        // a receipt binds the exact starting world each case ran from.
        Assert.NotEqual(suite.FixtureDigest("clerk"), suite.FixtureDigest("approver"));
        Assert.Equal(["approver", "clerk"], VerificationExample.Clean.Fixtures.Select(fixture => fixture.Key));
        Assert.Equal(suite.FixtureDigest("clerk"),
            VerificationExample.Clean.Fixtures.Single(fixture => fixture.Key == "clerk").Digest);

        foreach (var outcome in VerificationExample.BusinessRuleDefect.Outcomes)
            foreach (var observation in outcome.Observations)
            {
                var predicate = VerificationCatalog.Predicate(observation.PredicateId)!;
                Assert.True(VerificationCatalog.IsWellTyped(predicate.Expects, observation.ExpectedJson));
                Assert.True(VerificationCatalog.IsWellTyped(predicate.Expects, observation.ActualJson));
                Assert.Equal(observation.Matched, observation.FindingCode is null);
            }
    }

    // Acceptance 4: a check containing no meaningful assertion cannot pass. Admission refuses it,
    // and even if one reached a run, the outcome's status is derived and cannot be Passed.
    [Fact]
    public void A_check_containing_no_meaningful_assertion_cannot_pass()
    {
        var suite = VerificationExample.Suite;
        var empty = Fixture().GetProperty("emptyCase");

        // Authoring refuses the empty case by name, with the message the surfaces show.
        Assert.Null(VerificationSuite.TryDeclare("tenant-a.invoice-verification", "1.0.0", suite.Fixtures,
            [VerificationExample.EmptyCase], out var refusals));
        var expected = empty.GetProperty("refusals").EnumerateArray()
            .Select(refusal => new VerificationRefusal(refusal.GetProperty("code").GetString()!,
                refusal.GetProperty("target").GetString()!, refusal.GetProperty("message").GetString()!)).ToArray();
        Assert.Equal(expected, refusals);
        Assert.Equal("verification-assertion-required", refusals[0].Code);

        // "Meaningful" is enforced structurally, not by taste. Each of these is an assertion that
        // could never disagree with anything, and each is refused by its own name.
        var invariant = suite.Cases.Single(item => item.CaseId == "approval-authority");
        void Refused(string code, params VerificationAssertion[] assertions) =>
            Assert.Contains(Declare(invariant with { Assertions = assertions }), refusal => refusal.Code == code);
        Refused("verification-predicate-unknown", Assertion("outcome.looksFine", null, "true"));
        Refused("verification-expected-invalid", Assertion("outcome.accepted", null, null));
        Refused("verification-expected-invalid", Assertion("outcome.accepted", null, "   "));
        Refused("verification-expected-invalid", Assertion("outcome.accepted", null, "null"));
        Refused("verification-expected-invalid", Assertion("outcome.accepted", null, "\"false\""));
        Refused("verification-assertion-target-invalid", Assertion("record.number", null, "1"));
        Refused("verification-assertion-duplicate",
            Assertion("outcome.accepted", null, "true"), Assertion("outcome.accepted", null, "false"));
        // An unbound input has no determinate result, so asserting over it is not a check either.
        Assert.Contains(Declare(invariant with { Inputs = new Dictionary<string, string>(StringComparer.Ordinal) }),
            refusal => refusal.Code == "verification-input-unbound");
        // A row that states no expectation is a row that asserts nothing.
        var parameterized = suite.Cases.Single(item => item.CaseId == "invoice-total");
        Assert.Contains(Declare(parameterized with
        {
            Rows = [parameterized.Rows[0] with { Expected = new Dictionary<string, string>(StringComparer.Ordinal) }],
        }), refusal => refusal.Code == "verification-expected-invalid");

        // The second gate: an outcome's status is derived from what was observed. There is no
        // constructor, factory or setter anywhere that produces Passed with nothing observed.
        Assert.Equal(VerificationStatus.Vacuous, VerificationCaseOutcome.Observed("approval-authority", null, []).Status);
        Assert.Equal(VerificationStatus.Unsupported,
            VerificationCaseOutcome.Unsupported("approval-authority", null, "no simulator").Status);
        Assert.DoesNotContain(typeof(VerificationCaseOutcome).GetConstructors(),
            constructor => constructor.IsPublic);
        Assert.DoesNotContain(typeof(VerificationCaseOutcome).GetProperty(nameof(VerificationCaseOutcome.Status))!
            .GetAccessors(nonPublic: true), accessor => accessor.ReturnType == typeof(void));
        // And a receipt carrying one vacuous outcome is not a passing run.
        var vacuous = VerificationReceipt.Mint("receipt-vacuous", "tenant-a", VerificationExample.Candidate.Digest,
            VerificationExample.Baseline.Digest, suite, VerificationExample.Engines, VerificationExample.Instant,
            VerificationExample.Completed,
            [
                VerificationCaseOutcome.Observed("approval-authority", null, []),
                .. VerificationExample.Clean.Outcomes.Where(outcome => outcome.CaseId == "invoice-total"),
            ], out _);
        Assert.Equal(VerificationStatus.Vacuous, vacuous.Status);
        Assert.NotEqual(VerificationStatus.Passed, vacuous.Status);
    }

    private static VerificationAssertion Assertion(string predicateId, string? target, string? expected) =>
        new(predicateId, target, expected);

    private static IReadOnlyList<VerificationRefusal> Declare(VerificationCase item)
    {
        VerificationSuite.TryDeclare("s", "1.0.0", VerificationExample.Suite.Fixtures, [item], out var refusals);
        return refusals;
    }

    // Acceptance 5: the receipt binds candidate, baseline, suite, fixture and engine digests.
    [Fact]
    public void The_receipt_binds_candidate_baseline_suite_fixture_and_engine_digests()
    {
        var suite = VerificationExample.Suite;
        var receipt = VerificationExample.Clean;
        Assert.Equal(VerificationExample.Candidate.Digest, receipt.CandidateDigest);
        Assert.Equal(VerificationExample.Baseline.Digest, receipt.BaselineDigest);
        Assert.Equal(suite.Digest, receipt.Suite.Digest);
        Assert.Equal([suite.FixtureDigest("approver"), suite.FixtureDigest("clerk")],
            receipt.Fixtures.Select(fixture => fixture.Digest));
        Assert.Contains(VerificationCatalog.Reference, receipt.Engines);
        Assert.Equal(Run("passed").GetProperty("receiptDigest").GetString(), receipt.Digest);

        // Every binding is required, so a run that cannot name one is not evidence and is not minted.
        void Refused(string code, Action mint) =>
            Assert.Contains(Assert.Throws<VerificationSuiteRefusedException>(mint).Refusals, refusal => refusal.Code == code);
        Refused("verification-candidate-required", () => Mint(candidate: "not-a-digest"));
        Refused("verification-baseline-required", () => Mint(baseline: ""));
        Refused("verification-engine-catalogue-required", () => Mint(engines: [.. VerificationExample.Engines.Skip(1)]));
        Refused("verification-engine-required", () => Mint(engines: [VerificationCatalog.Reference]));
        Refused("verification-receipt-id-required", () => Mint(receiptId: " "));
        // A dropped case cannot be hidden: the receipt must answer every declared case and row.
        Refused("verification-outcome-missing", () => Mint(outcomes: [.. receipt.Outcomes.Skip(1)]));
        Refused("verification-outcome-unknown", () => Mint(outcomes:
            [.. receipt.Outcomes, VerificationCaseOutcome.Observed("invented", null, [])]));

        // Repeating the catalogue is not a second engine, and the engine set, not the caller's
        // ordering, decides the digest.
        Refused("verification-engine-required", () => Mint(engines:
            [VerificationCatalog.Reference, VerificationCatalog.Reference]));
        Assert.Equal(receipt.Digest, Mint(engines: [.. VerificationExample.Engines.Reverse(),
            VerificationCatalog.Reference]).Digest);

        // An outcome that observed anything must have observed every assertion its case declares,
        // so a run cannot drop the one assertion it would have failed and still report Passed.
        var invariant = receipt.Outcomes.Single(outcome => outcome.CaseId == "approval-authority");
        var rows = receipt.Outcomes.Where(outcome => outcome.CaseId != "approval-authority").ToArray();
        Refused("verification-observation-missing", () => Mint(outcomes:
            [VerificationCaseOutcome.Observed("approval-authority", null, [invariant.Observations[0]]), .. rows]));
        Refused("verification-observation-unexpected", () => Mint(outcomes:
            [
                VerificationCaseOutcome.Observed("approval-authority", null,
                    [.. invariant.Observations, new("record.number", "1.0.0", "/total", "1", "1", true)]),
                .. rows,
            ]));

        // The digest is over the canonical document, so a receipt cannot be re-read as anything else.
        var document = JsonSerializer.Deserialize<JsonElement>(receipt.Document.Span);
        Assert.Equal("harborline.verification-receipt/v1", document.GetProperty("contract").GetString());
        Assert.Equal(receipt.CandidateDigest, document.GetProperty("candidateDigest").GetString());
        Assert.NotEqual(receipt.Digest, VerificationExample.BusinessRuleDefect.Digest);
    }

    private static VerificationReceipt Mint(string? receiptId = null, string? candidate = null, string? baseline = null,
        IReadOnlyList<ConfigurationReference>? engines = null, IReadOnlyList<VerificationCaseOutcome>? outcomes = null) =>
        VerificationReceipt.Mint(receiptId ?? "receipt-1", "tenant-a", candidate ?? VerificationExample.Candidate.Digest,
            baseline ?? VerificationExample.Baseline.Digest, VerificationExample.Suite,
            engines ?? VerificationExample.Engines, VerificationExample.Instant, VerificationExample.Completed,
            outcomes ?? VerificationExample.Clean.Outcomes, out _);

    // Acceptance 6: React and Blazor render the same released definition over the same fixture.
    // The producer's half is that every binding both lanes show comes from here.
    [Fact]
    public void The_released_definition_and_bindings_are_the_ones_both_lanes_render()
    {
        AssertFixture("passed", VerificationDetail.Bind(VerificationExample.Clean, VerificationExample.Suite));
        AssertFixture("business-rule-defect", VerificationDetail.Bind(VerificationExample.BusinessRuleDefect, VerificationExample.Suite));
        AssertFixture("authorization-defect", VerificationDetail.Bind(VerificationExample.AuthorizationDefect, VerificationExample.Suite));

        var payload = JsonSerializer.Deserialize<JsonElement>(PlatformPackageSeed.Export()).GetProperty("items")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7")
            .GetProperty("content").GetProperty("payload");
        Assert.Equal(VerificationDetail.Definition.GetRawText(), payload.GetProperty("verificationRunDetail").GetRawText());
        Assert.Equal(VerificationDetail.Statuses.GetRawText(), payload.GetProperty("verificationRunStatuses").GetRawText());
        Assert.Equal(VerificationCatalog.Reference.Digest,
            payload.GetProperty("verificationCatalogue").GetProperty("digest").GetString());
        foreach (var status in Enum.GetValues<VerificationStatus>())
            Assert.False(string.IsNullOrWhiteSpace(VerificationDetail.Label(status)));
    }

    // Acceptance 7: two deliberate defects, each failing its own claim and not the other.
    [Fact]
    public void A_business_rule_defect_and_an_authorization_defect_each_fail_their_own_case()
    {
        static IReadOnlyList<string> FailedCases(VerificationReceipt receipt) => receipt.Outcomes
            .Where(outcome => outcome.Status != VerificationStatus.Passed)
            .Select(outcome => outcome.CaseId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(VerificationStatus.Passed, VerificationExample.Clean.Status);
        Assert.Empty(FailedCases(VerificationExample.Clean));

        // The business-rule defect fails every examples row of the business-rule claim, and the
        // authorization invariant is untouched.
        Assert.Equal(VerificationStatus.Failed, VerificationExample.BusinessRuleDefect.Status);
        Assert.Equal(["invoice-total"], FailedCases(VerificationExample.BusinessRuleDefect));
        Assert.Equal(3, VerificationExample.BusinessRuleDefect.Outcomes
            .Count(outcome => outcome.CaseId == "invoice-total" && outcome.Status == VerificationStatus.Failed));

        // The authorization defect fails the authorization invariant, and every examples row is
        // untouched — which is what makes the suite a detector and not a mood.
        Assert.Equal(VerificationStatus.Failed, VerificationExample.AuthorizationDefect.Status);
        Assert.Equal(["approval-authority"], FailedCases(VerificationExample.AuthorizationDefect));
        Assert.All(VerificationExample.AuthorizationDefect.Outcomes.Where(outcome => outcome.CaseId == "invoice-total"),
            outcome => Assert.Equal(VerificationStatus.Passed, outcome.Status));

        // Each failure names the observation that disagreed, not only that something did.
        Assert.Contains("record.number:/total: expected 1000, actual 110",
            VerificationDetail.Bind(VerificationExample.BusinessRuleDefect, VerificationExample.Suite)["failures"], StringComparison.Ordinal);
        Assert.Contains("authorization.decision: expected \"refused\", actual \"allowed\"",
            VerificationDetail.Bind(VerificationExample.AuthorizationDefect, VerificationExample.Suite)["failures"], StringComparison.Ordinal);
    }
}
