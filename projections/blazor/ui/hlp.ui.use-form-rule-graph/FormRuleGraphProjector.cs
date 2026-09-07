namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>
/// The reusable rule-graph projection result — the .NET shared-contract projection of
/// <c>hlp.ui.use-form-rule-graph</c>. Mirrors the React hook's frozen surface: the
/// projected view (hidden fields, sections, and nested items omitted; required,
/// read-only, and presentation outcomes applied), the effective values with Resolved
/// computed cells merged over the caller's, and the fail-closed save gate.
/// </summary>
public sealed record FormRuleGraphProjection(
    SchemaFormView View,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyDictionary<string, object?> ComputedValues,
    SchemaFormRuleEvaluation? Evaluation,
    Exception? EvaluationError,
    bool SaveBlocked);

/// <summary>
/// Projects a base <see cref="SchemaFormView"/> through an injected
/// <see cref="ISchemaFormRuleGraph"/>. A null graph is exact pass-through with an open
/// save gate. Evaluation is stateless per call, so a replaced graph re-seeds cleanly. A
/// pending, errored, or throwing evaluation blocks save (<c>form-rule-graph.evaluation-failed</c>)
/// and the base view passes through unprojected. Composes the same projector
/// <see cref="HarborlineSchemaForm"/> evaluates through, so the two surfaces cannot drift.
/// </summary>
public static class FormRuleGraphProjector
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyTables =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public static FormRuleGraphProjection Project(
        SchemaFormView view,
        ISchemaFormRuleGraph? graph,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        var candidate = values is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(values, StringComparer.Ordinal);

        SchemaFormRuleEvaluation? evaluation = null;
        Exception? evaluationError = null;
        if (graph is not null)
        {
            try
            {
                evaluation = graph.EvaluateInstance(new(candidate, EmptyTables));
            }
            catch (Exception error)
            {
                evaluationError = error;
            }
        }

        var projection = SchemaFormRuleProjector.Project(view, evaluation);
        var effective = new Dictionary<string, object?>(candidate, StringComparer.Ordinal);
        foreach (var pair in projection.ComputedValues) effective[pair.Key] = pair.Value;

        var saveBlocked = graph is not null && (evaluationError is not null
            || evaluation is null
            || evaluation.HasPending
            || evaluation.IsSaveBlocked
            || evaluation.Values.Values.Any(value =>
                value.State is SchemaFormRuleValueState.Pending or SchemaFormRuleValueState.Error));

        var projectedView = ReferenceEquals(projection.Sections, view.Sections)
            ? view
            : view with { Sections = projection.Sections };
        return new(projectedView, effective, projection.ComputedValues, evaluation, evaluationError, saveBlocked);
    }
}
