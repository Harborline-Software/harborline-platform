using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Harborline.Foundation.Definitions;

namespace Harborline.Foundation.Taxonomy;

/// <summary>Content kind 8, the api's <c>PackContentKind.TaxonomyDefinition</c>. 7 is <c>TemplateDefinition</c> and was a collision (T-738).</summary>
public static class TaxonomyPackIdentity { public const int ContentKind = 8; }
public sealed record TaxonomyDefinitionId(string Vendor, string Domain, string TaxonomyName)
{
    public override string ToString() => $"{Vendor}.{Domain}.{TaxonomyName}";
    public static TaxonomyDefinitionId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty)) throw new FormatException("A taxonomy id requires exactly three non-empty dot-separated segments.");
        return new(parts[0], parts[1], parts[2]);
    }
}
public sealed record TaxonomyNodeId(TaxonomyDefinitionId Definition, string Code) { public override string ToString() => $"{Definition}/{Code}"; }
public enum TaxonomyGovernanceRegime { Civilian, Enterprise, Authoritative }
public enum TaxonomyNodeStatus { Active, Tombstoned }
public enum TaxonomyCascadeLayer { Base, Tenant }
public sealed record TaxonomyDefinitionRequirement(string Capability, string? MinimumPlatformVersion = null);
public sealed record TaxonomyDefinitionEnvelope(string Identity, string Version, string Tenant, TaxonomyCascadeLayer CascadeLayer, JsonElement Provenance, IReadOnlyList<TaxonomyDefinitionRequirement> Requires, DefinitionContractVersion? Contract);
public sealed record TaxonomyLineage(string Source, string AncestorVersion, string DerivingActor, DateTimeOffset Time, string Reason);
public sealed record DisplayHistoryEntry(string Display, string Description, DateTimeOffset ChangedAt);
public sealed record TaxonomyOverlayReference(TaxonomyDefinitionId VendorDefinitionId, string VendorVersion);
public sealed record TaxonomyOverlayDesignation(string VendorNodeCode, string? Display = null, string? Description = null);
// Nullable members with no positional default (ParentCode, PublishedAt, TombstonedAt, SuccessorCode,
// DeprecationReason) trail the required ones: RespectRequiredConstructorParameters treats a
// parameter with no default as required regardless of its nullable annotation, and our own writer
// (DefaultIgnoreCondition.WhenWritingNull) omits a null member from canonical JSON, so an omitted
// member needs a default to round-trip.
public sealed record TaxonomyNode(string Code, string Display, string Description, TaxonomyNodeStatus Status, IReadOnlyList<DisplayHistoryEntry> DisplayHistoryEntries, string? ParentCode = null, DateTimeOffset? PublishedAt = null, DateTimeOffset? TombstonedAt = null, string? SuccessorCode = null, string? DeprecationReason = null);
public sealed record TaxonomyDefinition(string Tenant, TaxonomyDefinitionId DefinitionId, string Version, TaxonomyGovernanceRegime Governance, string Owner, IReadOnlyList<TaxonomyNode> Nodes, int SchemaVersion = 1, TaxonomyDefinitionEnvelope? Envelope = null, TaxonomyLineage? DerivedFrom = null, TaxonomyOverlayReference? Overlay = null, IReadOnlyList<TaxonomyOverlayDesignation>? OverlayDesignations = null);
public enum TaxonomyAdmissionPhase { Author, Publish, Install }
public sealed record TaxonomyCatalogueCoordinates(string Tenant, TaxonomyDefinitionId DefinitionId, string Version);
public sealed record TaxonomyRefusal(string Code, string Pointer);
public sealed class TaxonomyAdmissionException(IReadOnlyList<TaxonomyRefusal> refusals) : Exception("The Taxonomy definition was refused.") { public IReadOnlyList<TaxonomyRefusal> Refusals { get; } = refusals; }

public static class TaxonomyDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static byte[] SerializeCanonical(TaxonomyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var source = JsonSerializer.SerializeToNode(definition, Options) ?? throw new JsonException("The Taxonomy definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n'); return stream.ToArray();
    }
    public static TaxonomyDefinition Deserialize(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<TaxonomyDefinition>(json, Options) ?? throw new JsonException("The Taxonomy definition payload is null.");
    public static TaxonomyDefinition Deserialize(string json) { ArgumentException.ThrowIfNullOrWhiteSpace(json); return JsonSerializer.Deserialize<TaxonomyDefinition>(json, Options) ?? throw new JsonException("The Taxonomy definition payload is null."); }
    private static JsonSerializerOptions CreateOptions()
    {
        // RespectNullableAnnotations/RespectRequiredConstructorParameters turn a missing or null
        // required member (e.g. owner, nodes itself, definition_id) into a JsonException at
        // Deserialize, so AdmitJson refuses definition.body_invalid instead of handing Validate a
        // definition built from CLR defaults (mirrors the AssistanceDefinition producer, platform PR
        // #154). Neither option enforces nullability of a collection's own ELEMENTS (a JSON `null`
        // inside the nodes array still deserializes), which is why Validate also guards node entries.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, WriteIndented = false };
        // T-572 slice 2: an envelope may still omit `contract`; slice 3 refuses that through DefinitionContractWindow.Check, not as a missing constructor member.
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { info => { foreach (var property in info.Properties) if (property.PropertyType == typeof(DefinitionContractVersion)) property.IsRequired = false; } } };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)); options.Converters.Add(new DefinitionIdConverter()); return options;
    }
    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value.OrderBy(property => property.Key, StringComparer.Ordinal).Select(property => KeyValuePair.Create(property.Key, property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Canonicalize(item)).ToArray()), _ => node.DeepClone(),
    };
    private sealed class DefinitionIdConverter : JsonConverter<TaxonomyDefinitionId>
    {
        public override TaxonomyDefinitionId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("A taxonomy definition id must be a string.");
            try { return TaxonomyDefinitionId.Parse(reader.GetString()!); }
            catch (FormatException) { throw new JsonException("A taxonomy definition id must have exactly three non-empty dot-separated segments."); }
        }
        public override void Write(Utf8JsonWriter writer, TaxonomyDefinitionId value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}

public static class TaxonomyDefinitionAdmission
{
    public const string HarborlineActorId = "harborline";
    public const int MaximumParentTraversalDepth = 64;
    /// <summary>Admits a canonical JSON body. <paramref name="window"/> is the host's application-contract window from the platform seed (T-572), required at every phase, install included.</summary>
    public static IReadOnlyList<TaxonomyRefusal> AdmitJson(string bodyJson, TaxonomyAdmissionPhase phase, DefinitionContractWindow window, TaxonomyCatalogueCoordinates? catalogue = null, TaxonomyDefinition? previous = null)
    {
        try { using var document = JsonDocument.Parse(bodyJson); if (document.RootElement.ValueKind != JsonValueKind.Object) return [new("definition.settings_not_object", "/")]; }
        catch (JsonException) { return [new("definition.body_invalid", "/")]; } catch (ArgumentNullException) { return [new("definition.body_invalid", "/")]; }
        TaxonomyDefinition definition; try { definition = TaxonomyDefinitionJson.Deserialize(bodyJson); } catch (JsonException) { return [new("definition.body_invalid", "/")]; }
        var refusals = Validate(definition, phase, window, previous).ToList();
        if (catalogue is not null && phase != TaxonomyAdmissionPhase.Author)
        {
            if (definition.Tenant != catalogue.Tenant) refusals.Add(new("definition.catalogue_mismatch", "/tenant"));
            if (definition.DefinitionId != catalogue.DefinitionId) refusals.Add(new("definition.catalogue_mismatch", "/definition_id"));
            if (definition.Version != catalogue.Version) refusals.Add(new("definition.catalogue_mismatch", "/version"));
        }
        return refusals;
    }
    public static TaxonomyDefinition Require(TaxonomyDefinition definition, TaxonomyAdmissionPhase phase, DefinitionContractWindow window, TaxonomyDefinition? previous = null, TaxonomyDefinition? vendor = null)
    {
        var refusals = Validate(definition, phase, window, previous, vendor); return refusals.Count == 0 ? definition : throw new TaxonomyAdmissionException(refusals);
    }
    /// <summary><paramref name="vendor"/> is the resolved vendor scheme an overlay names by reference
    /// (taxonomy-auth-9). It is optional because admission at Author phase may run before the vendor
    /// is resolvable; when supplied, taxonomy-auth-22 (an overlay copying a vendor node) and an
    /// overlay designation naming an unknown vendor node both refuse here, at the same admission call
    /// every other structural refusal runs through, rather than only in a separately callable helper.</summary>
    public static IReadOnlyList<TaxonomyRefusal> Validate(TaxonomyDefinition definition, TaxonomyAdmissionPhase phase, DefinitionContractWindow window, TaxonomyDefinition? previous = null, TaxonomyDefinition? vendor = null)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(window); var refusals = new List<TaxonomyRefusal>();
        if (!IsThreePartVersion(definition.Version)) refusals.Add(new("definition.version_invalid", "/version"));
        if (definition.SchemaVersion != 1) refusals.Add(new("definition.schema_version_unsupported", "/schema_version"));
        if (definition.Envelope is null) { if (phase != TaxonomyAdmissionPhase.Author) refusals.Add(new("definition.envelope_required", "/envelope")); }
        else if (definition.Envelope.Identity != definition.DefinitionId.ToString() || definition.Envelope.Version != definition.Version || definition.Envelope.Tenant != definition.Tenant) refusals.Add(new("definition.envelope_mismatch", "/envelope"));
        // T-572 (rulings 85-88, Q6): the contract travels with the envelope, inside the host's window.
        var contract = definition.Envelope is null ? null : window.Check(definition.Envelope.Contract, null);
        if (contract is not null) refusals.Add(new(contract.Code, contract.Pointer));
        if (phase == TaxonomyAdmissionPhase.Author && definition.Governance == TaxonomyGovernanceRegime.Authoritative) refusals.Add(new("definition.governance_not_authorable", "/governance"));
        if (definition.Governance == TaxonomyGovernanceRegime.Authoritative && definition.Owner != HarborlineActorId) refusals.Add(new("definition.authoritative_owner_invalid", "/owner"));
        ValidateOverlay(definition, refusals);
        // System.Text.Json's RespectNullableAnnotations does not enforce nullability of a
        // collection's own elements (a JSON `null` entry in "nodes" still deserializes), so a null
        // list or a null/malformed element refuses here rather than the loops below dereferencing it.
        if (definition.Nodes is null) { refusals.Add(new("definition.body_invalid", "/nodes")); return refusals; }
        if (definition.Nodes.Count == 0 && phase != TaxonomyAdmissionPhase.Author) refusals.Add(new("definition.no_terms", "/nodes"));
        var byCode = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            var node = definition.Nodes[index];
            if (node is null) { refusals.Add(new("definition.body_invalid", $"/nodes/{index}")); continue; }
            if (node.Code is null) { refusals.Add(new("definition.body_invalid", $"/nodes/{index}/code")); continue; }
            if (!byCode.TryAdd(node.Code, index)) refusals.Add(new("definition.node_code_duplicate", $"/nodes/{index}/code"));
        }
        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            var node = definition.Nodes[index];
            if (node is null || node.Code is null) continue;
            // An overlay may attach a tenant-owned node below a vendor node. That parent cannot be
            // resolved here because admission receives only the overlay definition.
            if (definition.Overlay is null && node.ParentCode is not null && !byCode.ContainsKey(node.ParentCode)) refusals.Add(new("definition.parent_unknown", $"/nodes/{index}/parent_code"));
            if (node.Status == TaxonomyNodeStatus.Tombstoned && string.IsNullOrWhiteSpace(node.DeprecationReason)) refusals.Add(new("definition.deprecation_reason_required", $"/nodes/{index}/deprecation_reason"));
            if (node.SuccessorCode is not null) { if (!byCode.TryGetValue(node.SuccessorCode, out var successor)) refusals.Add(new("definition.successor_unknown", $"/nodes/{index}/successor_code")); else if (definition.Nodes[successor].Status == TaxonomyNodeStatus.Tombstoned) refusals.Add(new("definition.successor_tombstoned", $"/nodes/{index}/successor_code")); }
        }
        ValidateParents(definition.Nodes, byCode, refusals); if (previous is not null) ValidatePrevious(definition, previous, refusals);
        if (vendor is not null && definition.Overlay is not null) ValidateOverlayAgainstVendor(definition, vendor, refusals);
        return refusals;
    }
    private static void ValidateOverlayAgainstVendor(TaxonomyDefinition definition, TaxonomyDefinition vendor, List<TaxonomyRefusal> refusals)
    {
        var vendorCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in vendor.Nodes ?? []) if (node?.Code is not null) vendorCodes.Add(node.Code);
        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            var node = definition.Nodes[index];
            // taxonomy-auth-22: an overlay's own nodes must be new, never a re-declaration of one of
            // the vendor's own concepts (FHIR CodeSystem supplement: a supplement adds designations to
            // existing codes, it never redefines one as if it were its own new concept).
            if (node?.Code is not null && vendorCodes.Contains(node.Code)) refusals.Add(new("overlay.copies_vendor_node", $"/nodes/{index}/code"));
        }
        if (definition.OverlayDesignations is null) return;
        for (var index = 0; index < definition.OverlayDesignations.Count; index++)
        {
            var designation = definition.OverlayDesignations[index];
            // taxonomy-auth-10: a designation adds a label/description to an EXISTING vendor concept;
            // one naming a code the vendor scheme does not have refuses rather than publishing inert.
            if (designation?.VendorNodeCode is not null && !vendorCodes.Contains(designation.VendorNodeCode)) refusals.Add(new("overlay.designation_vendor_node_unknown", $"/overlay_designations/{index}/vendor_node_code"));
        }
    }
    private static void ValidateOverlay(TaxonomyDefinition definition, List<TaxonomyRefusal> refusals)
    {
        var overlayMode = definition.Overlay is not null || definition.OverlayDesignations is not null;
        if (overlayMode && definition.Overlay is null)
        {
            refusals.Add(new("overlay.reference_missing", "/overlay"));
        }
        else if (definition.Overlay is not null)
        {
            var vendorId = definition.Overlay.VendorDefinitionId;
            if (vendorId is null || string.IsNullOrWhiteSpace(vendorId.Vendor) || string.IsNullOrWhiteSpace(vendorId.Domain) || string.IsNullOrWhiteSpace(vendorId.TaxonomyName)) refusals.Add(new("overlay.vendor_definition_id_invalid", "/overlay/vendor_definition_id"));
            if (string.IsNullOrWhiteSpace(definition.Overlay.VendorVersion)) refusals.Add(new("overlay.vendor_version_missing", "/overlay/vendor_version"));
        }
        if (definition.OverlayDesignations is null) return;
        var designatedCodes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < definition.OverlayDesignations.Count; index++)
        {
            var designation = definition.OverlayDesignations[index];
            if (designation is null) { refusals.Add(new("overlay.designation_invalid", $"/overlay_designations/{index}")); continue; }
            if (string.IsNullOrWhiteSpace(designation.VendorNodeCode)) { refusals.Add(new("overlay.vendor_node_code_missing", $"/overlay_designations/{index}/vendor_node_code")); continue; }
            if (!designatedCodes.TryAdd(designation.VendorNodeCode, index)) refusals.Add(new("overlay.vendor_node_code_duplicate", $"/overlay_designations/{index}/vendor_node_code"));
        }
    }
    private static void ValidateParents(IReadOnlyList<TaxonomyNode> nodes, IReadOnlyDictionary<string, int> byCode, List<TaxonomyRefusal> refusals)
    {
        for (var start = 0; start < nodes.Count; start++)
        {
            if (nodes[start] is null || nodes[start].Code is null) continue;
            var seen = new List<int>(); var positions = new Dictionary<int, int>(); var current = start; var depth = 0;
            while (true)
            {
                if (positions.TryGetValue(current, out var cycleStart)) { refusals.Add(new("definition.parent_cycle", $"/nodes/{start}/parent_code~cycle:{string.Join(',', seen.Skip(cycleStart).Select(index => nodes[index].Code))}")); break; }
                var currentNode = nodes[current];
                if (currentNode is null || currentNode.Code is null) break;
                positions[current] = seen.Count; seen.Add(current); var parentCode = currentNode.ParentCode;
                if (parentCode is null || !byCode.TryGetValue(parentCode, out var parent)) break;
                if (depth == MaximumParentTraversalDepth) { refusals.Add(new("definition.traversal_depth_exceeded", $"/nodes/{start}/parent_code")); break; }
                current = parent; depth++;
            }
        }
    }
    private static void ValidatePrevious(TaxonomyDefinition definition, TaxonomyDefinition previous, List<TaxonomyRefusal> refusals)
    {
        if (previous.Nodes is null) { refusals.Add(new("definition.body_invalid", "/previous/nodes")); return; }
        var priorNodes = new Dictionary<string, TaxonomyNode>(StringComparer.Ordinal);
        for (var index = 0; index < previous.Nodes.Count; index++)
        {
            var priorNode = previous.Nodes[index];
            if (priorNode is null) { refusals.Add(new("definition.body_invalid", $"/previous/nodes/{index}")); continue; }
            if (priorNode.Code is null) { refusals.Add(new("definition.body_invalid", $"/previous/nodes/{index}/code")); continue; }
            if (!priorNodes.TryAdd(priorNode.Code, priorNode)) refusals.Add(new("definition.body_invalid", $"/previous/nodes/{index}/code"));
        }
        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            var node = definition.Nodes[index];
            if (node is null || node.Code is null || !priorNodes.TryGetValue(node.Code, out var prior)) continue;
            // This is the producer-level proxy for never reusing a retired code; general identity reuse requires durable node history.
            if (prior.Status == TaxonomyNodeStatus.Tombstoned && node.Status == TaxonomyNodeStatus.Active) refusals.Add(new("definition.tombstone_reactivated", $"/nodes/{index}/status"));
            if (node.DisplayHistoryEntries.Count < prior.DisplayHistoryEntries.Count || !prior.DisplayHistoryEntries.SequenceEqual(node.DisplayHistoryEntries.Take(prior.DisplayHistoryEntries.Count))) refusals.Add(new("definition.display_history_not_append_only", $"/nodes/{index}/display_history_entries"));
        }
    }
    private static bool IsThreePartVersion(string? version) { var parts = version?.Split('.', StringSplitOptions.None); return parts is { Length: 3 } && parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)); }
}
public sealed record TaxonomyDefinitionPackageEntry(string DefinitionId, string Version, ReadOnlyMemory<byte> Content) { public int ContentKind => TaxonomyPackIdentity.ContentKind; }
public static class TaxonomyDefinitionPackExporter
{
    public static TaxonomyDefinitionPackageEntry Export(TaxonomyDefinition definition, DefinitionContractWindow window, TaxonomyDefinition? previous = null, TaxonomyDefinition? vendor = null)
    {
        TaxonomyDefinitionAdmission.Require(definition, TaxonomyAdmissionPhase.Publish, window, previous, vendor); return new(definition.DefinitionId.ToString(), definition.Version, TaxonomyDefinitionJson.SerializeCanonical(definition));
    }
}
