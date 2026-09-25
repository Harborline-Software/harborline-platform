using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleEngine.Standings;

/// <summary>A record-shaped candidate for standing evaluation.</summary>
public sealed record StandingRecord(string RecordId, string RecordType, IReadOnlyDictionary<string, JsonNode?> FieldValues);

/// <summary>
/// One rule's verdict on one record: the deciding rule identity, the standing and a refusal code. It carries no
/// input value (the no-raw-input trace property); values are a separately authorized evidence read.
/// </summary>
public sealed record StandingDecision(string RuleId, string RuleVersion, StandingReference Standing, bool Carries, string? RefusalCode);

/// <summary>The standings one visible record carries, with every applicable rule's decision.</summary>
public sealed record StandingRowOutcome(string RecordId, IReadOnlyList<StandingReference> Standings, IReadOnlyList<StandingDecision> Decisions);

/// <summary>
/// An authorized standing evaluation over a result set: the visible row count, per-standing counts over every
/// visible row, one page of outcomes, and the number of predicate evaluations it took.
/// </summary>
public sealed record StandingSetResult(
    int VisibleCount,
    IReadOnlyDictionary<StandingReference, int> Counts,
    IReadOnlyList<StandingRowOutcome> Page,
    int PredicateEvaluations);

/// <summary>
/// Evaluates standings over a result set (DES-0018 <c>rules-eng-20</c>). Authorization applies first: each candidate
/// passes the host's Access check before any predicate reads it, and counts and pages are taken over the
/// visible rows only. A predicate is evaluated once per distinct tuple of the fields it declares, so rows that
/// share those values share one evaluation, and rows that differ never share a verdict. Evaluation requires the
/// borrower's admission (<c>rules-eng-26</c>). The evaluator takes no principal, role or grant: Rules narrows, it
/// never decides access.
/// </summary>
public sealed class StandingEvaluator(RuleEngineLimits? limits = null)
{
    private readonly RuleEngineLimits _limits = limits ?? RuleEngineLimits.Default;

    /// <summary>Evaluates <paramref name="definitions"/> over the candidates <paramref name="authorize"/> admits.</summary>
    /// <param name="candidates">The complete tenant-bound candidate set, before any count or page.</param>
    /// <param name="authorize">The host's Access row check, bound to the reading principal and instant.</param>
    /// <param name="definitions">The installed standing rules.</param>
    /// <param name="admission">The borrower's admission evidence for this evaluation.</param>
    /// <param name="instant">The act instant every predicate evaluates at.</param>
    /// <param name="skip">Visible rows to skip before the page.</param>
    /// <param name="take">The page size.</param>
    /// <param name="cancellationToken">Cancels evaluation.</param>
    public async ValueTask<StandingSetResult> EvaluateSetAsync(
        IEnumerable<StandingRecord> candidates,
        Func<StandingRecord, CancellationToken, ValueTask<bool>> authorize,
        IReadOnlyList<StandingRuleDefinition> definitions,
        EvaluationAdmission? admission,
        DateTimeOffset instant,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        var ordered = definitions.OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ThenBy(rule => rule.RuleVersion, StringComparer.Ordinal).ToArray();
        var guard = new GuardEvaluator(new PinnedClock(instant), _limits);
        var verdicts = new Dictionary<string, Validity>(StringComparer.Ordinal);
        var counts = new Dictionary<StandingReference, int>();
        var page = new List<StandingRowOutcome>();
        int visible = 0;

        foreach (var record in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(record);
            if (!await authorize(record, cancellationToken).ConfigureAwait(false)) continue;

            var decisions = new List<StandingDecision>();
            var carried = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var rule in ordered.Where(rule => string.Equals(rule.RecordType, record.RecordType, StringComparison.Ordinal)))
            {
                var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
                foreach (var field in rule.InputFields)
                    inputs[field] = record.FieldValues.TryGetValue(field, out var value) ? value : null;
                string key = new JsonArray(rule.RuleId, rule.RuleVersion,
                    new JsonArray([.. rule.InputFields.Select(field => inputs[field]?.DeepClone())])).ToJsonString();
                if (!verdicts.TryGetValue(key, out var verdict))
                {
                    verdict = guard.EvaluateGuard(rule.Predicate, RuleContextSnapshot.Capture(inputs), RuleEvalScope.Root, admission, cancellationToken);
                    verdicts.Add(key, verdict);
                }
                if (verdict.Ok) carried.Add(rule.Standing.Name);
                decisions.Add(new(rule.RuleId, rule.RuleVersion, rule.Standing, verdict.Ok, RefusalOf(rule, verdict)));
            }

            foreach (var name in carried) counts[new StandingReference(name)] = counts.GetValueOrDefault(new StandingReference(name)) + 1;
            if (visible >= skip && page.Count < take)
                page.Add(new(record.RecordId, [.. carried.Select(name => new StandingReference(name))], decisions));
            visible++;
        }

        return new(visible, counts, page, verdicts.Count);
    }

    // A predicate that is simply false reports its own rule id; only an engine refusal is a refusal code.
    private static string? RefusalOf(StandingRuleDefinition rule, Validity verdict)
        => verdict.Ok || verdict.Error is not { } error || error.Code == rule.RuleId ? null : error.Code;

    private sealed class PinnedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
