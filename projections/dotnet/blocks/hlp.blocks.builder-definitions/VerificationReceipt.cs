using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The outcome vocabulary. Nothing outside this set is a result, and skipping is not passing.</summary>
public enum VerificationStatus
{
    /// <summary>Every observation was made and every one matched.</summary>
    Passed,

    /// <summary>At least one observation disagreed with its expected value.</summary>
    Failed,

    /// <summary>Nothing was observed. It is not a pass, and a receipt carrying one cannot pass.</summary>
    Vacuous,

    /// <summary>The runner cannot execute this case deterministically and says so.</summary>
    Unsupported,

    /// <summary>An input the case needs is unavailable, so it was not run.</summary>
    Blocked,
}

/// <summary>
/// One expected-and-actual pair. Both sides are typed canonical JSON, so a receipt can be re-read
/// and compared later without re-running anything, and a failure names a stable code and pointer.
/// </summary>
/// <param name="PredicateId">The registered predicate that made the observation.</param>
/// <param name="PredicateVersion">Its contract version at the time of the run.</param>
/// <param name="Target">The target inside the channel, or null.</param>
/// <param name="ExpectedJson">The expected value the case declared.</param>
/// <param name="ActualJson">The value the production interpreter produced.</param>
/// <param name="Matched">Whether the two agreed.</param>
/// <param name="FindingCode">A stable finding code when they did not.</param>
/// <param name="Pointer">An RFC 6901 pointer into the observed document when one applies.</param>
public sealed record VerificationObservation(string PredicateId, string PredicateVersion, string? Target,
    string ExpectedJson, string ActualJson, bool Matched, string? FindingCode = null, string? Pointer = null);

/// <summary>
/// The result of one case, or of one examples row. Its status is <em>derived</em> from the
/// observations it carries and cannot be supplied: there is no constructor, factory or setter that
/// produces <see cref="VerificationStatus.Passed"/> without at least one matched observation. That
/// is the second half of "a check containing no meaningful assertion cannot pass" — admission stops
/// an empty case being authored, and this stops an empty run reporting green if one ever is.
/// </summary>
public sealed class VerificationCaseOutcome
{
    private VerificationCaseOutcome(string caseId, string? rowId, VerificationStatus status,
        IReadOnlyList<VerificationObservation> observations, string? reason)
    {
        CaseId = caseId;
        RowId = rowId;
        Status = status;
        Observations = observations;
        Reason = reason;
    }

    /// <summary>The case this outcome belongs to.</summary>
    public string CaseId { get; }

    /// <summary>The examples row, or null for an invariant.</summary>
    public string? RowId { get; }

    /// <summary>The derived status.</summary>
    public VerificationStatus Status { get; }

    /// <summary>Every expected-and-actual observation the run made.</summary>
    public IReadOnlyList<VerificationObservation> Observations { get; }

    /// <summary>Why the case was not run, for unsupported and blocked outcomes only.</summary>
    public string? Reason { get; }

    /// <summary>Records what the run observed and derives the status from it.</summary>
    public static VerificationCaseOutcome Observed(string caseId, string? rowId,
        IReadOnlyList<VerificationObservation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentNullException.ThrowIfNull(observations);
        var made = Array.AsReadOnly(observations.ToArray());
        var status = made.Count == 0 ? VerificationStatus.Vacuous
            : made.All(observation => observation.Matched) ? VerificationStatus.Passed : VerificationStatus.Failed;
        return new(caseId, rowId, status, made, null);
    }

    /// <summary>Records a case the runner cannot execute deterministically. It is not a pass.</summary>
    public static VerificationCaseOutcome Unsupported(string caseId, string? rowId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(caseId, rowId, VerificationStatus.Unsupported, [], reason);
    }

    /// <summary>Records a case whose inputs were unavailable. It is not a pass.</summary>
    public static VerificationCaseOutcome Blocked(string caseId, string? rowId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(caseId, rowId, VerificationStatus.Blocked, [], reason);
    }
}

/// <summary>
/// One immutable run receipt. It binds what was verified (the candidate), what it was verified
/// against (the baseline), what was run (the suite and each fixture it used) and what ran it (the
/// engine identities, including the action and predicate catalogue), so the statement it carries is
/// reproducible rather than a colour. The receipt digest is taken over exactly those bytes.
/// </summary>
public sealed class VerificationReceipt
{
    private const string Contract = "harborline.verification-receipt/v1";
    private readonly byte[] canonical;

    private VerificationReceipt(string receiptId, string tenantKey, string candidateDigest, string baselineDigest,
        ConfigurationReference suite, IReadOnlyList<ConfigurationReference> fixtures,
        IReadOnlyList<ConfigurationReference> engines, DateTimeOffset startedAt, DateTimeOffset completedAt,
        IReadOnlyList<VerificationCaseOutcome> outcomes, VerificationStatus status, byte[] canonical)
    {
        ReceiptId = receiptId;
        TenantKey = tenantKey;
        CandidateDigest = candidateDigest;
        BaselineDigest = baselineDigest;
        Suite = suite;
        Fixtures = fixtures;
        Engines = engines;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Outcomes = outcomes;
        Status = status;
        this.canonical = canonical;
        Digest = Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    /// <summary>The host's stable identity for this run; a proposed change binds a check to it.</summary>
    public string ReceiptId { get; }

    /// <summary>The tenant the candidate and baseline belong to.</summary>
    public string TenantKey { get; }

    /// <summary>The complete candidate generation the suite was run against.</summary>
    public string CandidateDigest { get; }

    /// <summary>The complete baseline generation the candidate was prepared over.</summary>
    public string BaselineDigest { get; }

    /// <summary>The suite identity, version and digest.</summary>
    public ConfigurationReference Suite { get; }

    /// <summary>One reference per fixture the run used, with that fixture's own digest.</summary>
    public IReadOnlyList<ConfigurationReference> Fixtures { get; }

    /// <summary>Every engine identity that produced an observation, including the catalogue.</summary>
    public IReadOnlyList<ConfigurationReference> Engines { get; }

    /// <summary>The admitted instant the run began at.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The admitted instant the run completed at.</summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>One outcome per invariant case and per examples row.</summary>
    public IReadOnlyList<VerificationCaseOutcome> Outcomes { get; }

    /// <summary>The derived run status; it is Passed only when every outcome passed.</summary>
    public VerificationStatus Status { get; }

    /// <summary>Lowercase SHA-256 over the canonical receipt document.</summary>
    public string Digest { get; }

    /// <summary>The canonical receipt document; the digest is taken over exactly these bytes.</summary>
    public ReadOnlyMemory<byte> Document => canonical;

    /// <summary>
    /// Mints one receipt, or refuses by name. The refusals are the bindings this contract exists to
    /// guarantee: the run must name a candidate, a baseline, the exact suite and each fixture digest
    /// the suite itself derives, and the catalogue that defined its predicates. A receipt missing any
    /// of those is not evidence, so it is not minted.
    /// </summary>
    public static VerificationReceipt Mint(string receiptId, string tenantKey, string candidateDigest,
        string baselineDigest, VerificationSuite suite, IReadOnlyList<ConfigurationReference> engines,
        DateTimeOffset startedAt, DateTimeOffset completedAt, IReadOnlyList<VerificationCaseOutcome> outcomes,
        out IReadOnlyList<VerificationRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(outcomes);
        var found = new List<VerificationRefusal>();
        void Refuse(string code, string target, string message) => found.Add(new(code, target, message));
        if (string.IsNullOrWhiteSpace(receiptId)) Refuse("verification-receipt-id-required", "receiptId", "A receipt identity is required.");
        if (string.IsNullOrWhiteSpace(tenantKey)) Refuse("verification-receipt-tenant-required", "tenantKey", "A receipt names the tenant it was run in.");
        if (!IsDigest(candidateDigest)) Refuse("verification-candidate-required", "candidateDigest", "A receipt binds the exact candidate generation it verified.");
        if (!IsDigest(baselineDigest)) Refuse("verification-baseline-required", "baselineDigest", "A receipt binds the baseline the candidate was prepared over.");
        if (completedAt < startedAt) Refuse("verification-run-interval-invalid", "completedAt", "A run cannot complete before it starts.");

        var used = suite.Cases.Select(item => item.FixtureId).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new ConfigurationReference(id, suite.Version, suite.FixtureDigest(id))).ToArray();
        var declaredEngines = engines.OrderBy(engine => engine.Key, StringComparer.Ordinal).ToArray();
        if (!declaredEngines.Any(engine => engine == VerificationCatalog.Reference))
            Refuse("verification-engine-catalogue-required", "engines",
                "The receipt does not carry the action and predicate catalogue that defined its observations.");
        if (declaredEngines.Any(engine => !IsDigest(engine.Digest)))
            Refuse("verification-engine-digest-invalid", "engines", "An engine identity has no SHA-256 digest.");
        if (declaredEngines.Length < 2)
            Refuse("verification-engine-required", "engines",
                "The receipt names no interpreter beyond the catalogue, so it does not say what executed the cases.");

        // Every declared case must be answered, and only declared cases may answer. A suite that
        // silently dropped a case would otherwise report green on the cases it liked.
        var expected = suite.Cases.SelectMany(item => item.IsParameterized
            ? item.Rows.Select(row => (item.CaseId, RowId: (string?)row.RowId))
            : [(item.CaseId, (string?)null)]).ToHashSet();
        var answered = outcomes.Select(outcome => (outcome.CaseId, outcome.RowId)).ToHashSet();
        foreach (var missing in expected.Except(answered).OrderBy(key => key.Item1, StringComparer.Ordinal))
            Refuse("verification-outcome-missing", missing.Item1,
                $"The run reports no outcome for {missing.Item1}{(missing.Item2 is null ? string.Empty : $" row {missing.Item2}")}.");
        foreach (var extra in answered.Except(expected).OrderBy(key => key.CaseId, StringComparer.Ordinal))
            Refuse("verification-outcome-unknown", extra.CaseId,
                $"The run reports an outcome for {extra.CaseId}, which the suite does not declare.");
        if (answered.Count != outcomes.Count)
            Refuse("verification-outcome-duplicate", "outcomes", "Two outcomes answer the same case and row.");

        refusals = Array.AsReadOnly(found.ToArray());
        if (found.Count > 0) throw new VerificationSuiteRefusedException(refusals);

        var ordered = outcomes.OrderBy(outcome => outcome.CaseId, StringComparer.Ordinal)
            .ThenBy(outcome => outcome.RowId ?? string.Empty, StringComparer.Ordinal).ToArray();
        // Derived, never supplied: one vacuous, unsupported or blocked outcome is enough to deny a
        // passing run, so a suite cannot be made green by leaving a case unobserved.
        var status = ordered.All(outcome => outcome.Status == VerificationStatus.Passed)
            ? VerificationStatus.Passed
            : ordered.Any(outcome => outcome.Status == VerificationStatus.Failed)
                ? VerificationStatus.Failed
                : ordered.Any(outcome => outcome.Status == VerificationStatus.Vacuous)
                    ? VerificationStatus.Vacuous
                    : ordered.Any(outcome => outcome.Status == VerificationStatus.Blocked)
                        ? VerificationStatus.Blocked
                        : VerificationStatus.Unsupported;
        var suiteReference = new ConfigurationReference(suite.SuiteId, suite.Version, suite.Digest);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", Contract);
            writer.WriteString("receiptId", receiptId);
            writer.WriteString("tenantKey", tenantKey);
            writer.WriteString("candidateDigest", candidateDigest);
            writer.WriteString("baselineDigest", baselineDigest);
            writer.WritePropertyName("suite");
            Reference(writer, suiteReference);
            writer.WriteStartArray("fixtures");
            foreach (var fixture in used) Reference(writer, fixture);
            writer.WriteEndArray();
            writer.WriteStartArray("engines");
            foreach (var engine in declaredEngines) Reference(writer, engine);
            writer.WriteEndArray();
            writer.WriteString("startedAt", Instant(startedAt));
            writer.WriteString("completedAt", Instant(completedAt));
            writer.WriteString("status", status.ToString());
            writer.WriteStartArray("outcomes");
            foreach (var outcome in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("caseId", outcome.CaseId);
                if (outcome.RowId is null) writer.WriteNull("rowId"); else writer.WriteString("rowId", outcome.RowId);
                writer.WriteString("status", outcome.Status.ToString());
                if (outcome.Reason is null) writer.WriteNull("reason"); else writer.WriteString("reason", outcome.Reason);
                writer.WriteStartArray("observations");
                foreach (var observation in outcome.Observations)
                {
                    writer.WriteStartObject();
                    writer.WriteString("predicateId", observation.PredicateId);
                    writer.WriteString("predicateVersion", observation.PredicateVersion);
                    if (observation.Target is null) writer.WriteNull("target"); else writer.WriteString("target", observation.Target);
                    writer.WriteString("expected", observation.ExpectedJson);
                    writer.WriteString("actual", observation.ActualJson);
                    writer.WriteBoolean("matched", observation.Matched);
                    if (observation.FindingCode is null) writer.WriteNull("findingCode"); else writer.WriteString("findingCode", observation.FindingCode);
                    if (observation.Pointer is null) writer.WriteNull("pointer"); else writer.WriteString("pointer", observation.Pointer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new(receiptId, tenantKey, candidateDigest, baselineDigest, suiteReference, Array.AsReadOnly(used),
            Array.AsReadOnly(declaredEngines), startedAt, completedAt, Array.AsReadOnly(ordered), status, stream.ToArray());
    }

    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Reference(Utf8JsonWriter writer, ConfigurationReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString("key", reference.Key);
        writer.WriteString("revision", reference.Revision);
        writer.WriteString("digest", reference.Digest);
        writer.WriteEndObject();
    }
}
