using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Contract = Harborline.Contracts.Forms;
using State = Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Foundation.Forms.Engine;

internal sealed record FormCandidateEvaluation(
    JsonDocument AcceptedCandidate,
    IReadOnlyList<Contract.ValidationError> Errors,
    IReadOnlySet<string> HiddenFields,
    IReadOnlySet<string> ReadOnlyFields,
    RuleEvaluationResult? Rules) : IDisposable
{
    public void Dispose() => AcceptedCandidate.Dispose();
}

internal static class FormCandidateEvaluator
{
    public static async ValueTask<FormCandidateEvaluation> EvaluateAsync(
        FormExecutionScope scope,
        State.FormDefinition definition,
        JsonDocument candidate,
        ISchemaRegistry schemas,
        int maximumCandidateBytes,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate.RootElement);
        if (bytes.Length > maximumCandidateBytes)
            return Invalid(candidate, Error(
                "",
                "Candidate exceeds the configured byte ceiling.",
                Contract.ValidationErrorKind.ResourceBound,
                "candidate-too-large",
                new Dictionary<string, string>
                {
                    ["limit"] = maximumCandidateBytes.ToString(CultureInfo.InvariantCulture),
                    ["bytes"] = bytes.Length.ToString(CultureInfo.InvariantCulture),
                }));
        if (candidate.RootElement.ValueKind != JsonValueKind.Object)
            return Invalid(candidate, Error("", "Candidate must be a JSON object.", Contract.ValidationErrorKind.Schema, "type"));

        var objectNode = JsonNode.Parse(candidate.RootElement.GetRawText())!.AsObject();
        var errors = new List<Contract.ValidationError>();
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var readOnly = new HashSet<string>(StringComparer.Ordinal);

        ApplyWriteAuthorization(scope, definition, objectNode, errors);
        var rules = EvaluateRules(definition, objectNode, cancellationToken);
        var hiddenPages = ApplyRuleProjection(definition, objectNode, rules, hidden, readOnly, errors, cancellationToken);
        foreach (var key in hidden) objectNode.Remove(key);

        var acceptedBytes = JsonSerializer.SerializeToUtf8Bytes(objectNode);
        SchemaValidationResult schemaResult;
        try
        {
            schemaResult = await schemas.ValidateAsync(new SchemaId(definition.SchemaRef.Value), acceptedBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (SchemaNotFoundException)
        {
            errors.Add(Error("", "The form schema was not found.", Contract.ValidationErrorKind.NotFound, "form.engine.not-found"));
            schemaResult = new(false, Array.Empty<SchemaValidationError>());
        }

        errors.AddRange(schemaResult.Errors
            .Where(row => !TargetsPrunedField(row.JsonPointer, hidden, readOnly))
            .Select(row => Error(row.JsonPointer, row.Message, Contract.ValidationErrorKind.Schema, row.Code, row.Params)));
        return new(JsonDocument.Parse(acceptedBytes), errors, hidden, readOnly, rules);
    }

    private static void ApplyWriteAuthorization(
        FormExecutionScope scope,
        State.FormDefinition definition,
        JsonObject candidate,
        List<Contract.ValidationError> errors)
    {
        foreach (var section in definition.Overlay.Sections)
        {
            var sectionWritable = HasAnyRole(scope, section.Access.WriteRoles);
            foreach (var fieldName in EnumerateFieldNames(section))
            {
                if (!candidate.ContainsKey(fieldName) || !definition.Overlay.Fields.TryGetValue(fieldName, out var field)) continue;
                var fieldWritable = field.FieldWriteRoles is null || HasAnyRole(scope, field.FieldWriteRoles);
                if (!sectionWritable || !fieldWritable)
                    errors.Add(Error($"/{EscapePointer(fieldName)}", "The field is not writable.", Contract.ValidationErrorKind.Authorization, "form.engine.denied"));
            }
        }
    }

    private static RuleEvaluationResult? EvaluateRules(State.FormDefinition definition, JsonObject candidate, CancellationToken cancellationToken)
    {
        if (definition.Overlay.Rules.Count == 0) return null;
        var compiled = RuleCompiler.Compile(definition.Overlay.Rules.Select(FormContractMapper.ToContractRule).ToArray());
        if (compiled.RuleCount == 0) return null;
        return new FormRuleGraph(compiled).EvaluateInstance(RuleInstance.FromJson(candidate), cancellationToken);
    }

    private static HashSet<string> ApplyRuleProjection(
        State.FormDefinition definition,
        JsonObject candidate,
        RuleEvaluationResult? result,
        HashSet<string> hidden,
        HashSet<string> readOnly,
        List<Contract.ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var hiddenPages = new HashSet<string>(StringComparer.Ordinal);
        var hiddenSections = new HashSet<string>(StringComparer.Ordinal);
        if (result is null)
        {
            EvaluatePageGuards(definition, candidate, hiddenPages, hiddenSections, cancellationToken);
            ExpandHiddenSections(definition, hiddenSections, hidden);
            return hiddenPages;
        }
        foreach (var (target, state) in result.Visibility)
        {
            if (target.StartsWith("field:", StringComparison.Ordinal))
            {
                var field = target["field:".Length..];
                if (!state.Visible) hidden.Add(field);
                if (state.ReadOnly) readOnly.Add(field);
                if (state.Required && state.Visible && !state.ReadOnly && IsEmpty(candidate, field))
                    errors.Add(Error($"/{EscapePointer(field)}", "The field is required.", Contract.ValidationErrorKind.Schema, "required", new Dictionary<string, string> { ["field"] = field }));
            }
            else if (target.StartsWith("section:", StringComparison.Ordinal) && !state.Visible)
                hiddenSections.Add(target["section:".Length..]);
        }

        ExpandHiddenSections(definition, hiddenSections, hidden);

        EvaluatePageGuards(definition, candidate, hiddenPages, hiddenSections, cancellationToken);
        ExpandHiddenSections(definition, hiddenSections, hidden);

        foreach (var (target, computed) in result.Values)
        {
            if (!target.StartsWith("field:", StringComparison.Ordinal)) continue;
            var field = target["field:".Length..];
            if (computed.State == ValueState.Resolved) candidate[field] = computed.Value?.DeepClone();
            else errors.Add(Error($"/{EscapePointer(field)}", "A calculated value could not be resolved.", Contract.ValidationErrorKind.Schema, computed.Error?.Code ?? "rule.unresolved", computed.Error?.Params));
        }

        foreach (var outcome in result.Validations)
        {
            if (outcome.Validity is not { Ok: false } validity) continue;
            var pointer = TargetPointer(outcome.Target.Key);
            if (pointer.Length > 1 && hidden.Contains(pointer[1..])) continue;
            var boundPages = definition.Overlay.Pages?.Where(page => page.Checks?.Contains(outcome.RuleId, StringComparer.Ordinal) == true).ToArray() ?? [];
            var onlyHiddenPageCheck = boundPages.Length > 0 && boundPages.All(page => hiddenPages.Contains(page.Id));
            if (onlyHiddenPageCheck) continue;
            errors.Add(Error(pointer, "A form rule failed.", Contract.ValidationErrorKind.Schema, validity.Error?.Code ?? outcome.RuleId, validity.Error?.Params));
        }
        if (result.HasPending)
            errors.Add(Error("", "A calculated value is pending.", Contract.ValidationErrorKind.ResourceBound, "rule.pending"));
        return hiddenPages;
    }

    private static void ExpandHiddenSections(
        State.FormDefinition definition,
        IReadOnlySet<string> hiddenSections,
        HashSet<string> hidden)
    {
        foreach (var section in definition.Overlay.Sections.Where(row => hiddenSections.Contains(row.Id)))
        {
            foreach (var field in EnumerateFieldNames(section)) hidden.Add(field);
            foreach (var item in section.Items ?? Array.Empty<State.FormItem>())
                if (item.Kind != State.FormItemKind.Field) hidden.Add(item.Key);
        }
    }

    private static void EvaluatePageGuards(
        State.FormDefinition definition,
        JsonObject candidate,
        HashSet<string> hiddenPages,
        HashSet<string> hiddenSections,
        CancellationToken cancellationToken)
    {
        if (definition.Overlay.Pages is not { Count: > 0 }) return;
        var context = candidate.ToDictionary(row => row.Key, row => row.Value?.DeepClone(), StringComparer.Ordinal);
        var evaluator = new GuardEvaluator();
        foreach (var page in definition.Overlay.Pages.Where(row => row.VisibleWhen is not null))
        {
            var guard = new Contract.RuleDefinition
            {
                Id = $"page-guard:{page.Id}", Tier = Contract.RuleTier.JsonLogic, Scope = Contract.RuleScope.Schema,
                ScopeTarget = "", Expression = page.VisibleWhen!, Action = Contract.RuleActionKind.Validate,
            };
            if (evaluator.EvaluateGuard(guard, context, cancellationToken).Ok) continue;
            hiddenPages.Add(page.Id);
            foreach (var section in page.Sections) hiddenSections.Add(section);
        }
    }

    internal static IReadOnlySet<string> ReadableFields(FormExecutionScope scope, State.FormDefinition definition)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in definition.Overlay.Sections)
        {
            if (!HasAnyRole(scope, section.Access.ReadRoles)) continue;
            foreach (var fieldName in EnumerateFieldNames(section))
            {
                if (!definition.Overlay.Fields.TryGetValue(fieldName, out var field)) continue;
                if (field.FieldReadRoles is null || HasAnyRole(scope, field.FieldReadRoles)) result.Add(fieldName);
            }
        }
        return result;
    }

    internal static bool HasAnyRole(FormExecutionScope scope, IReadOnlyList<RoleReference> required) =>
        required.Count == 0
        || scope.RoleVocabulary is not null
        && scope.HeldRoles is not null
        && RoleGateResolver.Allows(new RoleGate(required), scope.RoleVocabulary, scope.HeldRoles);

    internal static IEnumerable<string> EnumerateFieldNames(State.FormSection section) =>
        section.Fields.Concat(section.Items is null ? [] : EnumerateItems(section.Items)).Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateItems(IEnumerable<State.FormItem> items)
    {
        foreach (var item in items)
        {
            if (item.Kind == State.FormItemKind.Field) yield return item.Key;
            if (item.Items is not null)
                foreach (var child in EnumerateItems(item.Items)) yield return child;
        }
    }

    private static FormCandidateEvaluation Invalid(JsonDocument candidate, params Contract.ValidationError[] errors) =>
        new(JsonDocument.Parse(candidate.RootElement.GetRawText()), errors, new HashSet<string>(), new HashSet<string>(), null);

    private static Contract.ValidationError Error(
        string pointer,
        string message,
        Contract.ValidationErrorKind kind,
        string? code,
        IReadOnlyDictionary<string, string>? parameters = null) => new()
        {
            JsonPointer = pointer,
            Message = message,
            Kind = kind,
            Code = code is null ? default : Contract.Optional<string>.Some(code),
            Params = parameters is null ? default : Contract.Optional<IReadOnlyDictionary<string, string>>.Some(parameters),
        };

    private static bool IsEmpty(JsonObject candidate, string field) =>
        !candidate.TryGetPropertyValue(field, out var node)
        || node is null
        || node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length == 0
        || node is JsonArray array && array.Count == 0;

    private static bool TargetsPrunedField(string pointer, IReadOnlySet<string> hidden, IReadOnlySet<string> readOnly)
    {
        if (!pointer.StartsWith("/", StringComparison.Ordinal)) return false;
        var segment = pointer[1..].Split('/', 2)[0].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        return hidden.Contains(segment) || readOnly.Contains(segment);
    }

    private static string TargetPointer(string target)
    {
        if (target.StartsWith("field:", StringComparison.Ordinal)) return "/" + EscapePointer(target["field:".Length..]);
        if (target.StartsWith("row:", StringComparison.Ordinal)) return "/" + target["row:".Length..];
        if (target.StartsWith("agg:", StringComparison.Ordinal))
        {
            var value = target["agg:".Length..];
            var slash = value.IndexOf('/');
            return slash < 0 ? "" : "/" + EscapePointer(value[..slash]);
        }
        return "";
    }

    private static string EscapePointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
