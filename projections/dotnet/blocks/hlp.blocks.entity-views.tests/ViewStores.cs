using System.Text;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;

namespace Harborline.Blocks.EntityViews.Tests;

/// <summary>
/// A shared definition store composed as a host composes it for Views: <see cref="ViewDefinitionCatalogue.Admission"/> and
/// <see cref="ViewDefinitionCatalogue.Dependencies"/> for <see cref="DefinitionKind.Views"/>, and a pass-through Records
/// registry holding a published <c>work-item</c> Record Type in each tenant the tests use.
/// </summary>
internal static class ViewStores
{
    public static readonly string[] WorkItemFields = ["title", "state", "assignee", "priority", "tags"];

    public static ViewAdmissionRegisters Registers(IReadOnlyList<ViewKindDescriptor>? kinds = null) => new(
        kinds ?? [new("layout.table", "hlp.ui.data-grid", [ViewShapeRole.Title])],
        [new("work.count", ["format"])],
        new ViewExpressionFunctionRegistry(),
        [new("helm.counter", ["measure"])],
        [],
        []);

    public static (ViewDefinitionCatalogue Catalogue, IVersionedDefinitionStore Shared) Create(
        ViewAdmissionRegisters? registers = null,
        params string[] tenants)
    {
        var shared = new InMemoryVersionedDefinitionStore(
            new Dictionary<DefinitionKind, DefinitionAdmission>
            {
                [DefinitionKind.Views] = ViewDefinitionCatalogue.Admission(registers ?? Registers()),
                [DefinitionKind.Records] = (_, _) => [],
            },
            new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
            {
                [DefinitionKind.Views] = ViewDefinitionCatalogue.Dependencies,
            });
        foreach (var tenant in tenants.Length == 0 ? ["tenant-a"] : tenants)
        {
            PublishRecordType(shared, tenant, "work-item", WorkItemFields);
        }
        return (new ViewDefinitionCatalogue(shared), shared);
    }

    /// <summary>A Views catalogue over a freshly composed store, with <c>work-item</c> published in each tenant.</summary>
    public static ViewDefinitionCatalogue Catalogue(params string[] tenants) => Create(null, tenants).Catalogue;


    public static void PublishRecordType(IVersionedDefinitionStore shared, string tenant, string recordTypeId, IEnumerable<string> fields)
    {
        var document = new RecordTypeDocument(
            new RecordsDefinitionEnvelope(tenant, "operations", new DefinitionContractVersion(1, 0)),
            recordTypeId,
            recordTypeId,
            fields.Select(field => new FieldDefinition(field, field)).ToArray());
        var key = RecordTypeDefinitionStore.KeyOf(tenant, recordTypeId);
        var body = Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document));
        shared.SaveDraftAsync(new DefinitionDocument(key, "1.0.0", "1.0.0", body), 0, $"seed-{tenant}-{recordTypeId}")
            .AsTask().GetAwaiter().GetResult();
        shared.PublishAsync(key, "1.0.0", 1, $"publish-{tenant}-{recordTypeId}").AsTask().GetAwaiter().GetResult();
    }
}
