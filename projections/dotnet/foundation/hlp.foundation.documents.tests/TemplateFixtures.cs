using System.Text;
using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>
/// Supplied Layout contract fixtures (T-593 item 5): a published page surface resolved by exact pin,
/// admitted by Layout's own validator. Live composition over the Layout store stays with T-490.
/// </summary>
internal static class TemplateFixtures
{
    public const string Tenant = "tenant-a";
    public const string SurfaceId = "surface.invoice";
    public const string SurfaceVersion = "1.0.0";

    public static JsonElement Provenance { get; } = JsonDocument.Parse("""{"pack":"general","pack_version":"1.0.0"}""").RootElement.Clone();

    public static TemplateDefinition Template(string version = "1.0.0") => new(
        new TemplateDefinitionEnvelope("template.invoice", version, Tenant, TemplateCascadeLayer.DomainPackage, Provenance,
            [new TemplateRequirement("platform.documents", "1.0.0")]),
        "invoice",
        new TemplateRecordTypeBinding("invoice", "1"),
        new TemplateLocalePolicy(TemplateLocaleKind.Fixed, "en-US"),
        new TemplateStyle("Harborline Software"),
        new TemplateSurfacePin(SurfaceId, SurfaceVersion));

    /// <summary>A depth-one page surface with intent issue: the flat list read as a Layout tree (ADR 0094).</summary>
    public static LayoutDefinition Surface(
        LayoutMedium medium = LayoutMedium.Page,
        LayoutIntent intent = LayoutIntent.Issue,
        IReadOnlyList<LayoutBlock>? blocks = null)
    {
        blocks ??=
        [
            new("masthead", "layout.text", new LayoutStaticBinding(Json("\"Invoice\"")), []),
            new("lines", "layout.table", new LayoutQueryBinding("view.invoice-lines"),
                [new("description", "layout.text", new LayoutStaticBinding(Json("\"Description\"")), [])],
                Container: new(LayoutContainerKind.Stack), Repeating: true),
        ];
        var page = medium == LayoutMedium.Page;
        return new(
            new LayoutDefinitionEnvelope(SurfaceId, SurfaceVersion, Tenant, LayoutCascadeLayer.DomainPackage,
                Provenance, "standard", false, [new(LayoutPackIdentity.Capability, "1.0.0")]),
            1, medium, intent, blocks,
            page ? [new("a4", "a4", LayoutPageOrientation.Portrait, new("md", "md", "md", "md"), new("sm", "sm"))] : [],
            page ? [new("default", "a4", new(null, null, null), new(null, null, null), new(null, null, null))] : [],
            page ? [new("body", "a4", "default", blocks.Select(block => block.Id).ToArray())] : [],
            null, []);
    }

    /// <summary>The host's binding of the supplied surface: the shared catalogue's exact pin and Layout's admission.</summary>
    public static TemplateSurfaces Surfaces(LayoutDefinition? surface = null) => Surfaces(
        Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(surface ?? Surface())));

    public static TemplateSurfaces Surfaces(string surfaceJson) => new(
        pin => pin == new TemplateSurfacePin(SurfaceId, SurfaceVersion) ? surfaceJson : null,
        (json, stage) =>
        {
            try
            {
                var definition = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));
                if (stage == TemplateAdmissionStage.Persisted) LayoutPersistedValueAdmission.ValidateForRuntime(definition);
                else LayoutDefinitionAdmission.ValidateForPublish(definition);
                return [];
            }
            catch (LayoutDefinitionAdmissionException refused)
            {
                return refused.Refusals.Select(refusal => new TemplateRefusal(refusal.Code, refusal.Pointer)).ToArray();
            }
        });

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
