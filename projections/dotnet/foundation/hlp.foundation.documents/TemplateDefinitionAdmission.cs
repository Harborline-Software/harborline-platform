using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.Documents;

/// <summary>Stable refusal codes of template admission.</summary>
public static class TemplateDefinitionCodes
{
    /// <summary>An envelope member is missing or malformed (documents-ck-2).</summary>
    public const string EnvelopeInvalid = "documents.template.envelope_invalid";
    /// <summary>The template names no document type (documents-ck-3).</summary>
    public const string DocumentTypeRequired = "documents.template.document_type_required";
    /// <summary>A required body member is missing or malformed.</summary>
    public const string BodyInvalid = "documents.template.body_invalid";
    /// <summary>The surface pin resolves no published surface (documents-ck-7).</summary>
    public const string SurfaceUnresolved = "documents.template.surface_unresolved";
    /// <summary>The resolved surface is not the exact identity and version pinned.</summary>
    public const string SurfacePinMismatch = "documents.template.surface_pin_mismatch";
    /// <summary>A template composes page media only (ADR 0092, ADR 0094).</summary>
    public const string SurfaceMediumUnsupported = "documents.template.surface_medium_unsupported";
    /// <summary>A template's surface is issue-dominant; any other default intent is unsupported.</summary>
    public const string SurfaceIntentUnsupported = "documents.template.surface_intent_unsupported";
}

/// <summary>A stable, localizable refusal at an RFC 6901 pointer.</summary>
public sealed record TemplateRefusal(string Code, string Pointer);

/// <summary>The boundary a surface is admitted at.</summary>
public enum TemplateAdmissionStage
{
    /// <summary>Authoring validation and publication: the platform intent validator.</summary>
    Publish,
    /// <summary>A stored definition read back for rendering: invalid values are diagnosed, never clamped.</summary>
    Persisted,
}

/// <summary>
/// The host's binding of the Layout surface contract. Documents is a foundation assembly and never references
/// Layout's blocks-tier types; the host resolves the exact pin from the shared catalogue and runs Layout's own
/// validator, whose schema owns the numeric ranges (ADR 0099 decision 6).
/// </summary>
/// <param name="Resolve">The published surface's canonical Layout JSON, or null when the pin resolves nothing.</param>
/// <param name="Admit">Layout's admission of that JSON at the stage, as refusals relative to the surface root.</param>
public sealed record TemplateSurfaces(
    Func<TemplateSurfacePin, string?> Resolve,
    Func<string, TemplateAdmissionStage, IReadOnlyList<TemplateRefusal>> Admit);

/// <summary>Every refusal found at one admission stage.</summary>
public sealed class TemplateAdmissionException(string stage, IReadOnlyList<TemplateRefusal> refusals)
    : Exception("The template definition was refused.")
{
    /// <summary>The stable admission stage.</summary>
    public string Stage { get; } = stage;

    /// <summary>The ordered refusals.</summary>
    public IReadOnlyList<TemplateRefusal> Refusals { get; } = refusals;
}

/// <summary>One pure structural validator for authoring, publication, installation and persisted reads.</summary>
public static class TemplateDefinitionAdmission
{
    /// <summary>Returns every refusal of one template, in document order. Changes nothing.</summary>
    public static IReadOnlyList<TemplateRefusal> Validate(TemplateDefinition template, TemplateSurfaces surfaces)
        => Validate(template, surfaces, TemplateAdmissionStage.Publish);

    internal static IReadOnlyList<TemplateRefusal> Validate(
        TemplateDefinition template, TemplateSurfaces surfaces, TemplateAdmissionStage stage)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(surfaces);
        var refusals = new List<TemplateRefusal>();
        Envelope(template.Envelope, refusals);
        if (string.IsNullOrWhiteSpace(template.DocumentType))
            refusals.Add(new(TemplateDefinitionCodes.DocumentTypeRequired, "/document_type"));
        if (template.RecordType is null || Blank(template.RecordType.RecordType) || Blank(template.RecordType.Version))
            refusals.Add(new(TemplateDefinitionCodes.BodyInvalid, "/record_type"));
        if (template.Locale is null || !Enum.IsDefined(template.Locale.Kind)
            || (template.Locale.Kind == TemplateLocaleKind.Fixed) == Blank(template.Locale.Tag))
            refusals.Add(new(TemplateDefinitionCodes.BodyInvalid, "/locale"));
        if (template.Surface is null || Blank(template.Surface.SurfaceDefinitionId) || Blank(template.Surface.SurfaceVersion))
            refusals.Add(new(TemplateDefinitionCodes.BodyInvalid, "/surface"));
        else Surface(template.Surface, surfaces, stage, refusals);
        return refusals;
    }

    /// <summary>
    /// The persisted read: a stored template and the surface it pins are re-admitted before rendering, and an
    /// invalid stored value is diagnosed by name rather than clamped or normalised.
    /// </summary>
    /// <exception cref="TemplateAdmissionException">A stored value is invalid.</exception>
    public static void ValidatePersisted(TemplateDefinition template, TemplateSurfaces surfaces)
    {
        var refusals = Validate(template, surfaces, TemplateAdmissionStage.Persisted);
        if (refusals.Count > 0) throw new TemplateAdmissionException("render.runtime", refusals);
    }

    // documents-ck-7: the flat list is replaced by the pinned Layout tree. Layout's validator owns the tree,
    // placement and numeric ranges; Documents adds only what the composition requires of it.
    private static void Surface(TemplateSurfacePin pin, TemplateSurfaces surfaces, TemplateAdmissionStage stage,
        List<TemplateRefusal> refusals)
    {
        var json = surfaces.Resolve(pin);
        JsonObject? surface;
        try { surface = json is null ? null : JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { surface = null; }
        if (surface is null)
        {
            refusals.Add(new(TemplateDefinitionCodes.SurfaceUnresolved, "/surface"));
            return;
        }
        if (Text(surface["envelope"]?["identity"]) != pin.SurfaceDefinitionId)
            refusals.Add(new(TemplateDefinitionCodes.SurfacePinMismatch, "/surface/surface_definition_id"));
        if (Text(surface["envelope"]?["version"]) != pin.SurfaceVersion)
            refusals.Add(new(TemplateDefinitionCodes.SurfacePinMismatch, "/surface/surface_version"));
        if (Text(surface["medium"]) != "page")
            refusals.Add(new(TemplateDefinitionCodes.SurfaceMediumUnsupported, "/surface/medium"));
        if (Text(surface["default_intent"]) != "issue")
            refusals.Add(new(TemplateDefinitionCodes.SurfaceIntentUnsupported, "/surface/default_intent"));
        refusals.AddRange(surfaces.Admit(json!, stage).Select(refusal => refusal with { Pointer = "/surface" + refusal.Pointer }));
    }

    private static string? Text(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    /// <summary>Refuses publication by throwing every refusal.</summary>
    /// <exception cref="TemplateAdmissionException">The template was refused.</exception>
    public static void ValidateForPublish(TemplateDefinition template, TemplateSurfaces surfaces)
    {
        var refusals = Validate(template, surfaces);
        if (refusals.Count > 0) throw new TemplateAdmissionException("definition.publish", refusals);
    }

    private static void Envelope(TemplateDefinitionEnvelope? envelope, List<TemplateRefusal> refusals)
    {
        if (envelope is null)
        {
            refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope"));
            return;
        }
        if (Blank(envelope.Identity)) refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/identity"));
        if (Blank(envelope.Version)) refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/version"));
        if (Blank(envelope.Tenant)) refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/tenant"));
        if (!Enum.IsDefined(envelope.CascadeLayer)) refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/cascade_layer"));
        if (envelope.Provenance.ValueKind != JsonValueKind.Object)
            refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/provenance"));
        if (envelope.Requires is null)
        {
            refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, "/envelope/requires"));
            return;
        }
        for (var index = 0; index < envelope.Requires.Count; index++)
            if (Blank(envelope.Requires[index]?.Capability))
                refusals.Add(new(TemplateDefinitionCodes.EnvelopeInvalid, $"/envelope/requires/{index}/capability"));
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
