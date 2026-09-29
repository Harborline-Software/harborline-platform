using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>A pinned host projection of the admitted model for exactly one tenant and schema.</summary>
public sealed record FormSchemaFieldBindings(TenantId Tenant, string SchemaRef,
    IReadOnlyDictionary<string, FieldBindingDefinition> Fields);

/// <summary>Adapts the host's admitted Records/schema model; this is not another definition store.</summary>
public interface IFormFieldBindingSource
{
    ValueTask<FormSchemaFieldBindings?> ResolveAsync(TenantId tenant, string schemaRef,
        CancellationToken cancellationToken = default);
}
