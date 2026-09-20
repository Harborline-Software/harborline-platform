using Harborline.Contracts.Authorization;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine;

internal sealed record FormBoundField(ICompiledFieldKind Kind, ResolvedFieldConstraints Constraints, bool Readable);

internal sealed class FormFieldBinding(IFormFieldBindingSource source, IFieldKindRuntime kinds, IFieldDomainRuntime domains)
{
    public IReadOnlyList<FieldRefusal> Validate(FormBoundField binding, System.Text.Json.JsonElement value, string pointer)
        => domains.Validate(binding.Constraints, binding.Kind, value, pointer);

    public async ValueTask<IReadOnlyDictionary<string, FormBoundField>> ResolveAsync(
        FormExecutionScope scope, FormDefinition definition, CancellationToken cancellationToken)
    {
        var fields = await ResolveAsync(new FieldDomainScope(scope.Tenant, scope.ActorId), definition, cancellationToken);
        return fields.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            Readable = FormCandidateEvaluator.HasAnyRole(scope, pair.Value.Constraints.ReadRoleIds.Select(RoleReference.Domain).ToArray()),
        }, StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyDictionary<string, FormBoundField>> ResolveAsync(
        FieldDomainScope scope, FormDefinition definition, CancellationToken cancellationToken)
    {
        var model = await source.ResolveAsync(scope.Tenant, definition.SchemaRef.Value, cancellationToken);
        if (model is null || model.Tenant != scope.Tenant || model.SchemaRef != definition.SchemaRef.Value)
            throw Refuse("field.binding_unresolved", "");
        var result = new Dictionary<string, FormBoundField>(StringComparer.Ordinal);
        foreach (var name in definition.Overlay.Fields.Keys)
        {
            var pointer = Pointer(name);
            if (!model.Fields.TryGetValue(name, out var field)) throw Refuse("field.binding_unresolved", pointer);
            var kind = kinds.Bind(field.Kind, pointer);
            var narrowed = field.Constraints;
            if (definition.Authoring?.Fields.TryGetValue(name, out var metadata) == true)
            {
                if (metadata.Validations?.Any(validation => validation.Code is
                    "minLength" or "maxLength" or "minimum" or "maximum" or "multipleOf"
                    or "total_digits" or "fraction_digits" or "min_length" or "max_length" or "max_bytes") == true)
                    throw Refuse("field.kind_limit_redeclared", pointer);
                narrowed = narrowed with { Required = metadata.Required,
                    ValueDomain = metadata.Options is { } options ? new(LiteralValues: options) : narrowed.ValueDomain };
            }
            var proof = await domains.NarrowAsync(field.Constraints, narrowed,
                scope, pointer, cancellationToken);
            result.Add(name, new(kind, proof, false));
        }
        return result;
    }

    internal static string Pointer(string name) => "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    internal static FieldAdmissionException Refuse(string code, string pointer)
        => new([new(code, pointer, "The form field binding is not admitted.")]);
}
