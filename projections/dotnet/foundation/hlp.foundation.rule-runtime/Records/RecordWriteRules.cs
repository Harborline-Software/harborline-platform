using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Standings;

namespace Harborline.Foundation.RuleEngine.Records;

/// <summary>
/// A record write a bound rule refused: the rule, and why. <see cref="Code"/> is the rule's own id when its
/// predicate is false; any other code means the rule could not be interpreted, which refuses too (L274).
/// </summary>
public sealed record RecordRuleRefusal(string RuleId, string Code);

/// <summary>
/// The Rules stage of a Records write (DES-0018 <c>rules-eng-13</c>/<c>rules-eng-14</c>, DES-0029
/// <c>kernel-core-ck-10</c>): the record-level rules one record type's active schema generation binds, bound
/// once when that generation activates and evaluated on every write after mutation and before commit.
/// </summary>
/// <remarks>
/// A record rule reuses the schema-scoped shape of <see cref="StandingRuleDefinition"/> (T-978 owner ruling,
/// 2026-09-28): a <c>harborline-jsonlogic/v1</c> validation predicate that reads only top-level record fields.
/// It reads the record and the act instant, never the principal: Rules narrows, it never decides access.
/// </remarks>
public sealed class RecordWriteRules
{
    /// <summary>
    /// The Records borrower's expression environment, as released for DES-0015 (<c>records-auth-12</c>). Records
    /// is kernel and cannot reference Rules, so its declaration row is stated here, where it is admitted.
    /// </summary>
    public static BorrowerEnvironmentDeclaration Declaration { get; } = new(
        Borrower: "records-auth-12",
        Grammar: BorrowerEnvironmentAdmission.Grammar,
        Variables: new Dictionary<string, string> { ["field"] = "record field", ["row"] = "child row", ["caller"] = "authenticated-principal" },
        Operations: ["var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=",
            "+", "-", "*", "/", "%", "min", "max", "in", "cat", "agg", "money.add", "money.sub", "money.mul", "date.add", "date.diff",
            "date.today", "coding.is"],
        Effects: [BorrowerEnvironmentAdmission.FieldRead],
        MissingValues: "missing-field-reads-null",
        TimeSource: "evaluated-at",
        TimeZone: "utc",
        Phases: new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false,
            [EvaluationPhase.PublishValidation] = true,
            [EvaluationPhase.Render] = false,
            [EvaluationPhase.Submission] = true,
            [EvaluationPhase.Run] = false,
            [EvaluationPhase.SignOff] = false,
        },
        Replay: "deterministic");

    /// <summary>The phase a record write evaluates in: the record is being submitted for commit.</summary>
    public const EvaluationPhase Phase = EvaluationPhase.Submission;

    private static readonly EvaluationAdmission Admission = BorrowerEnvironmentAdmission.Admit(Declaration).For(Phase);

    private readonly RuleEngineLimits _limits;

    private RecordWriteRules(string recordType, RuleDefinition[] rules, RuleEngineLimits limits)
        => (RecordType, Rules, _limits) = (recordType, Array.AsReadOnly(rules), limits);

    /// <summary>The record type these rules bind to.</summary>
    public string RecordType { get; }

    /// <summary>The bound rules, in evaluation order (rule id, ordinal).</summary>
    public IReadOnlyList<RuleDefinition> Rules { get; }

    /// <summary>
    /// Binds <paramref name="rules"/> to <paramref name="recordType"/> when its schema generation activates.
    /// Refuses by <see cref="ArgumentException"/> a blank type, a duplicate rule id, or any rule outside the
    /// schema-scoped shape; a rule that does not compile refuses by <c>RuleCompilationException</c>.
    /// </summary>
    public static RecordWriteRules Bind(string recordType, IReadOnlyList<RuleDefinition> rules, RuleEngineLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            StandingRuleDefinition.RequireValidationShape(rule);
            _ = StandingRuleDefinition.FieldsRead(rule);
        }
        if (rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            throw new ArgumentException("Record rule ids must be unique.", nameof(rules));
        return new(recordType, [.. rules.OrderBy(rule => rule.Id, StringComparer.Ordinal)], limits ?? RuleEngineLimits.Default);
    }

    /// <summary>
    /// Evaluates every bound rule on <paramref name="record"/>, which must be exactly the state the write will
    /// commit (L272), at the act <paramref name="instant"/>. Returns the first refusal in rule order, or
    /// <see langword="null"/> when every rule passes. Fails closed: a rule that cannot be interpreted refuses.
    /// </summary>
    public RecordRuleRefusal? Evaluate(JsonObject record, DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var snapshot = RuleContextSnapshot.Capture(record.ToDictionary(field => field.Key, field => field.Value?.DeepClone()));
        var guard = new GuardEvaluator(new PinnedClock(instant), _limits);
        foreach (var rule in Rules)
        {
            var verdict = guard.EvaluateGuard(rule, snapshot, RuleEvalScope.Root, Admission, cancellationToken);
            if (!verdict.Ok) return new(rule.Id, verdict.Error?.Code ?? rule.Id);
        }
        return null;
    }

    private sealed class PinnedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant.ToUniversalTime();
    }
}
