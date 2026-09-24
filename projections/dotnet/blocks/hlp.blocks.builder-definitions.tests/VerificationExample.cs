using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-463. The one canonical Records-and-Rules example: an invoice whose total is a business rule and
/// whose approved status is an authorization rule. It is built once here and consumed by the
/// producer test, by both platform SchemaForm renderer tests and by both app lanes, through
/// <c>conformance/hlp.blocks.builder-definitions/verification.json</c>.
/// </summary>
internal static class VerificationExample
{
    internal static readonly DateTimeOffset Instant = DateTimeOffset.Parse("2026-09-20T09:00:00Z", CultureInfo.InvariantCulture);
    internal static readonly DateTimeOffset Completed = Instant.AddSeconds(3);

    private static ConfigurationReference Ref(string key, string revision, char fill) => new(key, revision, new string(fill, 64));

    /// <summary>A generation over one finance package owning the invoice record, rule and access definitions.</summary>
    internal static ConfigurationGeneration Generation(string revision) => ConfigurationGeneration.Resolve(
        new ResolvedConfiguration("tenant-a", ["finance"],
            [new(Ref("finance", revision, 'a'),
                [Ref("records/invoice", revision, 'b'), Ref("rules/invoice-total", revision, 'c'),
                    Ref("access/invoice", revision, 'd')], [])],
            [new("records/invoice", "finance"), new("rules/invoice-total", "finance"), new("access/invoice", "finance")],
            Ref("platform", "1.0.0", 'e'), []));

    /// <summary>The effective baseline the candidate is prepared over.</summary>
    internal static ConfigurationGeneration Baseline { get; } = Generation("1.0.0");

    /// <summary>The candidate generation the suite is run against.</summary>
    internal static ConfigurationGeneration Candidate { get; } = Generation("1.1.0");

    /// <summary>Every engine that produced an observation, including the catalogue that defined them.</summary>
    internal static IReadOnlyList<ConfigurationReference> Engines { get; } =
    [
        VerificationCatalog.Reference,
        Engine("harborline.verification-runner"),
        Engine("hlp.foundation.forms-engine"),
        Engine("hlp.foundation.rule-runtime"),
        Engine("hlp.contracts.authorization"),
    ];

    private static ConfigurationReference Engine(string key) => new(key, "1.0.0",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}@1.0.0"))));

    private static VerificationFixture Fixture(string id, string actor, params VerificationGrant[] grants) =>
        new(id, Instant, "Europe/London", "en-GB", "t-463-invoice", "ordinal-by-key", actor, grants,
            [new("notifications.email", "record-only"), new("payments.remit", "denied")],
            [new("supplier", """{"supplierKey":"contoso","name":"Contoso Ltd"}""")]);

    private static string Values(int quantity, int unitPrice, string? status = null) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"supplier":"contoso","quantity":{{quantity}},"unitPrice":{{unitPrice}}{{(status is null ? string.Empty : $",\"status\":\"{status}\"")}}}""");

    /// <summary>
    /// The canonical suite: one invariant over an authorization rule, one examples table over a
    /// business rule. The two claims are deliberately independent — the invariant never asserts a
    /// total and the examples table never asserts an authority — so one defect can fail one claim.
    /// </summary>
    internal static VerificationSuite Suite { get; } = VerificationSuite.Declare(
        "tenant-a.invoice-verification", "1.0.0",
        [
            // The grants are at the install root. A record-scoped act canonicalises to
            // `/records/<recordId>`, and a scope contains only `/`, itself, or its own `/` prefix —
            // so a bare package name like `finance` reads as `/finance`, contains nothing in the
            // records tree, and would leave the actor holding nothing for `records:write`.
            Fixture("clerk", "dana.okafor", new VerificationGrant("invoice.author", "/")),
            Fixture("approver", "moss.adeyemi", new VerificationGrant("invoice.author", "/"), new VerificationGrant("invoice.approver", "/")),
        ],
        [
            new("approval-authority", "Only an approver may create an invoice already marked approved.",
                "clerk", "records.create",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["recordType"] = "\"invoice\"",
                    ["values"] = Values(4, 500, "approved"),
                },
                [],
                [
                    new("outcome.accepted", null, "false"),
                    new("outcome.refusalCode", null, "\"records-authority-insufficient\""),
                    new("outcome.refusalPointer", null, "\"/values/status\""),
                    new("authorization.decision", null, "\"refused\""),
                ]),
            new("invoice-total", "An invoice total is its quantity times its unit price.",
                "approver", "records.create",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["recordType"] = "\"invoice\"" },
                [
                    Row("two-at-one-hundred", Values(2, 100), 200),
                    Row("ten-at-one-hundred", Values(10, 100), 1000),
                    Row("three-at-four-hundred", Values(3, 400), 1200),
                ],
                [
                    new("record.number", "/total", null),
                    new("outcome.accepted", null, null),
                ]),
        ],
        out _);

    private static VerificationExamplesRow Row(string rowId, string values, int total) => new(rowId,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["values"] = values },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["record.number:/total"] = total.ToString(CultureInfo.InvariantCulture),
            ["outcome.accepted"] = "true",
        });

    /// <summary>A suite whose one case asserts nothing; admission refuses it and it never runs.</summary>
    internal static VerificationCase EmptyCase { get; } = new("asserts-nothing",
        "A case someone forgot to finish.", "clerk", "records.create",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recordType"] = "\"invoice\"",
            ["values"] = Values(1, 1),
        },
        [], []);

    /// <summary>The clean run: every claim holds against the candidate.</summary>
    internal static VerificationReceipt Clean { get; } = Mint("receipt-1",
    [
        Authority(accepted: false, code: "records-authority-insufficient", pointer: "/values/status", decision: "refused"),
        Total("two-at-one-hundred", 200, 200),
        Total("ten-at-one-hundred", 1000, 1000),
        Total("three-at-four-hundred", 1200, 1200),
    ]);

    /// <summary>
    /// The business-rule defect: the candidate's invoice-total rule adds where it should multiply.
    /// Every examples row disagrees and the authorization invariant is untouched.
    /// </summary>
    internal static VerificationReceipt BusinessRuleDefect { get; } = Mint("receipt-2",
    [
        Authority(accepted: false, code: "records-authority-insufficient", pointer: "/values/status", decision: "refused"),
        Total("two-at-one-hundred", 200, 102),
        Total("ten-at-one-hundred", 1000, 110),
        Total("three-at-four-hundred", 1200, 403),
    ]);

    /// <summary>
    /// The authorization defect: the candidate's access definition also grants the approval
    /// permission to the author role. The invariant disagrees and every examples row is untouched.
    /// </summary>
    internal static VerificationReceipt AuthorizationDefect { get; } = Mint("receipt-3",
    [
        Authority(accepted: true, code: "", pointer: "", decision: "allowed"),
        Total("two-at-one-hundred", 200, 200),
        Total("ten-at-one-hundred", 1000, 1000),
        Total("three-at-four-hundred", 1200, 1200),
    ]);

    /// <summary>
    /// The vacuous run: the authorization invariant observed nothing at all — no simulator refused
    /// it, no assertion disagreed, it simply was not checked — while every examples row passed.
    /// Observing nothing is legal and derives <see cref="VerificationStatus.Vacuous"/>, so the whole
    /// receipt mints as Vacuous rather than Passed, and every surface must show it that way.
    /// </summary>
    internal static VerificationReceipt Vacuous { get; } = Mint("receipt-4",
    [
        VerificationCaseOutcome.Observed("approval-authority", null, []),
        Total("two-at-one-hundred", 200, 200),
        Total("ten-at-one-hundred", 1000, 1000),
        Total("three-at-four-hundred", 1200, 1200),
    ]);

    private static VerificationReceipt Mint(string receiptId, IReadOnlyList<VerificationCaseOutcome> outcomes) =>
        VerificationReceipt.Mint(receiptId, "tenant-a", Candidate.Digest, Baseline.Digest, Suite, Engines,
            Instant, Completed, outcomes, out _);

    private static VerificationCaseOutcome Authority(bool accepted, string code, string pointer, string decision) =>
        VerificationCaseOutcome.Observed("approval-authority", null,
        [
            new("outcome.accepted", "1.0.0", null, "false", accepted ? "true" : "false", !accepted,
                accepted ? "verification-expected-mismatch" : null, accepted ? "/outcome/accepted" : null),
            new("outcome.refusalCode", "1.0.0", null, "\"records-authority-insufficient\"", Quote(code),
                code == "records-authority-insufficient", code == "records-authority-insufficient" ? null : "verification-expected-mismatch",
                code == "records-authority-insufficient" ? null : "/outcome/refusalCode"),
            new("outcome.refusalPointer", "1.0.0", null, "\"/values/status\"", Quote(pointer),
                pointer == "/values/status", pointer == "/values/status" ? null : "verification-expected-mismatch",
                pointer == "/values/status" ? null : "/outcome/refusalPointer"),
            new("authorization.decision", "1.0.0", null, "\"refused\"", Quote(decision), decision == "refused",
                decision == "refused" ? null : "verification-expected-mismatch",
                decision == "refused" ? null : "/authorization/decision"),
        ]);

    private static VerificationCaseOutcome Total(string rowId, int expected, int actual) =>
        VerificationCaseOutcome.Observed("invoice-total", rowId,
        [
            new("record.number", "1.0.0", "/total", Number(expected), Number(actual), expected == actual,
                expected == actual ? null : "verification-expected-mismatch", expected == actual ? null : "/total"),
            new("outcome.accepted", "1.0.0", null, "true", "true", true),
        ]);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
