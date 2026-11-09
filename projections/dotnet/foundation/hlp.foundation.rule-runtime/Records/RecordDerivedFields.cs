using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;
using Harborline.Foundation.RuleEngine.References;
using Harborline.Foundation.RuleEngine.Standings;

namespace Harborline.Foundation.RuleEngine.Records;

/// <summary>A field of the bound Record Type that names its calculation by exact pin (DES-0018 <c>rules-ck-23</c>).</summary>
/// <param name="FieldKey">The derived field's key.</param>
/// <param name="Pin">The calculation's exact name, version and digest.</param>
public sealed record RecordFieldCalculation(string FieldKey, ExactPin Pin);

/// <summary>A derived field the mutate stage could not fill, by field and stable code. Never the body.</summary>
/// <param name="FieldKey">The derived field's key.</param>
/// <param name="Code">One of the <see cref="RecordDerivedFields"/> codes.</param>
public sealed record RecordDerivedFieldRefusal(string FieldKey, string Code);

/// <summary>The mutate stage's result: the derived candidate, or the refusal that stops the write.</summary>
/// <param name="Candidate">The candidate with every derived field filled; null when refused.</param>
/// <param name="Refusal">The refusal; null when every derived field resolved.</param>
public sealed record RecordDerivation(JsonObject? Candidate, RecordDerivedFieldRefusal? Refusal);

/// <summary>
/// The derived fields of a Records write's mutate stage (ADR 0124 decision 1, ADR 0038, DES-0015 <c>records-eng-5</c>):
/// each field that names a calculation takes that calculation's value over the bound candidate, before validation.
/// Nobody edits a derived value (DES-0030 <c>field-runtime-ck-4</c>), so a supplied value is replaced, as the Forms
/// engine replaces a submitted value with its computed one. Fails closed: a pin the closure cannot resolve, a
/// calculation that reads anything but the record's own fields, a cycle, or a value that does not resolve refuses.
/// </summary>
/// <remarks>
/// A calculation may read another derived field; it runs after that field is filled, so it never reads a supplied value
/// for it. Records is kernel and cannot reference Rules, so the caller turns the field's pin into an <see cref="ExactPin"/>.
/// </remarks>
public static class RecordDerivedFields
{
    /// <summary>The pin is not in the pinned closure, its digest differs, or no closure was composed.</summary>
    public const string Unresolved = "records.mutate.calculation_unresolved";

    /// <summary>The calculation reads something other than the record's own fields, or derived fields read each other in a cycle.</summary>
    public const string Unreadable = "records.mutate.calculation_unreadable";

    /// <summary>The calculation's value did not resolve (a type error, a division by zero, an upstream error).</summary>
    public const string Failed = "records.mutate.calculation_failed";

    private static readonly EvaluationAdmission Admission =
        BorrowerEnvironmentAdmission.Admit(RecordWriteRules.Declaration).For(RecordWriteRules.Phase);

    /// <summary>
    /// Fills every derived field of <paramref name="bound"/>, which is not changed, at the act <paramref name="instant"/>.
    /// With no calculations the candidate is a copy of <paramref name="bound"/>.
    /// </summary>
    public static RecordDerivation Derive(JsonObject bound, IReadOnlyList<RecordFieldCalculation> calculations, PinnedClosure? closure,
        DateTimeOffset instant, RuleEngineLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(calculations);
        var candidate = (JsonObject)bound.DeepClone();
        var pending = new List<(RecordFieldCalculation Field, HashSet<string> Reads)>();
        foreach (var field in calculations)
        {
            ArgumentNullException.ThrowIfNull(field);
            NamedCalculation calculation;
            try { calculation = closure?.Calculation(field.Pin) ?? throw new NamedReferenceException(NamedReferences.Unresolved, "no closure"); }
            catch (NamedReferenceException) { return Refused(field, Unresolved); }
            try { pending.Add((field, StandingRuleDefinition.FieldsRead(NamedReferences.Rule("calculation:" + field.FieldKey, calculation.Expression, RuleActionKind.Compute)))); }
            catch (ArgumentException) { return Refused(field, Unreadable); }
        }

        var evaluator = new GuardEvaluator(new RecordWriteRules.PinnedClock(instant), limits ?? RuleEngineLimits.Default);
        while (pending.Count > 0)
        {
            // Ready: reads no derived field still waiting, so it never sees a supplied value for one.
            var ready = pending.FindIndex(item => !pending.Any(other => item.Reads.Contains(other.Field.FieldKey)));
            if (ready < 0) return Refused(pending[0].Field, Unreadable);
            var field = pending[ready].Field;
            var snapshot = RuleContextSnapshot.Capture(candidate.ToDictionary(member => member.Key, member => member.Value?.DeepClone()));
            var value = NamedReferences.Compute(field.Pin, closure!, evaluator, snapshot, Admission, cancellationToken);
            if (value.State != ValueState.Resolved) return Refused(field, Failed);
            candidate[field.FieldKey] = value.Value?.DeepClone();
            pending.RemoveAt(ready);
        }
        return new(candidate, null);
    }

    private static RecordDerivation Refused(RecordFieldCalculation field, string code) => new(null, new(field.FieldKey, code));
}
