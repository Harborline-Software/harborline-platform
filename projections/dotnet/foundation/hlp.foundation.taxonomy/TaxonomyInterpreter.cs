namespace Harborline.Foundation.Taxonomy;

/// <summary>A pinned location of a taxonomy definition.</summary>
public sealed record TaxonomyDefinitionCoordinates(string Tenant, TaxonomyDefinitionId DefinitionId, string Version);

/// <summary>A pinned location of a classification within a taxonomy definition.</summary>
public sealed record TaxonomyClassificationReference(string Tenant, TaxonomyDefinitionId DefinitionId, string Version, string Code)
{
    public TaxonomyDefinitionCoordinates DefinitionCoordinates => new(Tenant, DefinitionId, Version);
}

/// <summary>A successful definition lookup.</summary>
public sealed record ResolvedTaxonomyDefinition(TaxonomyDefinition Definition) : TaxonomyDefinitionResolution;

/// <summary>A normal, typed refusal for a definition lookup that found no pinned definition.</summary>
public sealed record TaxonomyDefinitionNotFound(TaxonomyDefinitionCoordinates Coordinates) : TaxonomyDefinitionResolution;

/// <summary>The result of resolving a pinned definition; misses do not throw.</summary>
public abstract record TaxonomyDefinitionResolution;

/// <summary>The result of expanding a named collection in a pinned taxonomy definition.</summary>
public abstract record TaxonomyCollectionExpansionResult;

/// <summary>A successful named-collection expansion in the collection's declared concept order.</summary>
public sealed record ResolvedTaxonomyCollectionExpansion(IReadOnlyList<string> Codes) : TaxonomyCollectionExpansionResult;

/// <summary>A typed refusal for a collection name absent from the pinned taxonomy definition.</summary>
public sealed record TaxonomyCollectionExpansionNotFound(string CollectionName) : TaxonomyCollectionExpansionResult;

/// <summary>The result of expanding an overlay; a missing overlay or vendor definition is a typed
/// refusal naming which coordinates were not found, never an empty expansion.</summary>
public abstract record TaxonomyOverlayExpansionResult;

/// <summary>A successful overlay expansion: the vendor's codes at the requested version, then the overlay's own.</summary>
public sealed record ResolvedTaxonomyOverlayExpansion(IReadOnlyList<string> Codes) : TaxonomyOverlayExpansionResult;

/// <summary>The overlay itself, or the vendor definition at the requested version, was not found.</summary>
public sealed record TaxonomyOverlayExpansionNotFound(TaxonomyDefinitionCoordinates Coordinates) : TaxonomyOverlayExpansionResult;

/// <summary>The vendor definition id and version named by the overlay resolves to more than one
/// tenant's registration; expansion refuses rather than silently picking whichever the index
/// happens to return first.</summary>
public sealed record TaxonomyOverlayExpansionAmbiguousVendor(TaxonomyDefinitionId VendorDefinitionId, string VendorVersion, IReadOnlyList<string> Tenants) : TaxonomyOverlayExpansionResult;

/// <summary>taxonomy-auth-22: the overlay's own node set re-declares one of the vendor's own codes
/// instead of layering a designation onto it.</summary>
public sealed record TaxonomyOverlayExpansionCopiesVendorNode(string Code) : TaxonomyOverlayExpansionResult;

/// <summary>taxonomy-auth-10: an overlay designation names a vendor node code the vendor scheme does not have.</summary>
public sealed record TaxonomyOverlayExpansionUnknownDesignation(string VendorNodeCode) : TaxonomyOverlayExpansionResult;

/// <summary>An overlay designation's effective display/description, layered over the vendor node's own
/// values without changing the vendor node's own definition (taxonomy-auth-10).</summary>
public sealed record ResolvedTaxonomyOverlayDesignation(string VendorNodeCode, string Display, string Description);

/// <summary>A classification resolved within its pinned definition.</summary>
public sealed record ResolvedTaxonomyClassification(TaxonomyClassificationReference Reference, TaxonomyNode Node)
{
    public bool IsActive => Node.Status == TaxonomyNodeStatus.Active;
}

/// <summary>Thrown only when defensive traversal detects malformed graph data.</summary>
public sealed class TaxonomyTraversalException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>Thrown when an overlay violates its reference-only relationship to its vendor scheme.</summary>
public sealed class TaxonomyOverlayException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>A typed refusal for a succession chain that cannot reach an active node.</summary>
public sealed record TaxonomySuccessionRefusal(string Code, string Reason) : TaxonomySuccessionResult;

/// <summary>The result of following succession: either an active node or a typed refusal.</summary>
public abstract record TaxonomySuccessionResult;

/// <summary>A successful succession resolution.</summary>
public sealed record ResolvedTaxonomySuccession(TaxonomyNode Node) : TaxonomySuccessionResult;

[Flags]
public enum TaxonomyNodeChangeFields { None = 0, Display = 1, Description = 2, Status = 4, ParentCode = 8, SuccessorCode = 16 }
public sealed record TaxonomyNodeChange(string Code, TaxonomyNode Previous, TaxonomyNode Current, TaxonomyNodeChangeFields Fields);
public sealed record TaxonomyDefinitionChangeSet(IReadOnlyList<TaxonomyNode> AddedNodes, IReadOnlyList<TaxonomyNodeChange> ChangedNodes, IReadOnlyList<TaxonomyNode> TombstonedNodes);

/// <summary>Pure, in-memory resolver and traversal engine for pinned taxonomy definitions.</summary>
public sealed class TaxonomyInterpreter
{
    private readonly Dictionary<TaxonomyDefinitionCoordinates, TaxonomyDefinition> definitions;

    public TaxonomyInterpreter(IEnumerable<TaxonomyDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var indexed = new Dictionary<TaxonomyDefinitionCoordinates, TaxonomyDefinition>();
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            var coordinates = CoordinatesOf(definition);
            if (!indexed.TryAdd(coordinates, definition)) throw new ArgumentException($"The pinned definition '{coordinates}' appears more than once.", nameof(definitions));
        }
        this.definitions = indexed;
    }

    public TaxonomyDefinitionResolution ResolveDefinition(TaxonomyDefinitionCoordinates coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return definitions.TryGetValue(coordinates, out var definition) ? new ResolvedTaxonomyDefinition(definition) : new TaxonomyDefinitionNotFound(coordinates);
    }

    public ResolvedTaxonomyClassification? ResolveClassification(TaxonomyDefinition definition, TaxonomyClassificationReference reference)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(reference);
        if (CoordinatesOf(definition) != reference.DefinitionCoordinates) throw new ArgumentException("The classification reference does not pin the supplied definition.", nameof(reference));
        // A malformed/blank code is an ordinary "not found" outcome (taxonomy-eng-2), not a boundary error.
        if (string.IsNullOrEmpty(reference.Code)) return null;
        return Index(definition).TryGetValue(reference.Code, out var node) ? new ResolvedTaxonomyClassification(reference, node) : null;
    }

    public IReadOnlyList<ResolvedTaxonomyClassification?> ResolveClassifications(TaxonomyDefinition definition, IReadOnlyList<TaxonomyClassificationReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references.Select(reference => reference is null ? null : ResolveClassification(definition, reference)).ToArray();
    }

    public IReadOnlyList<TaxonomyNode> GetAncestors(TaxonomyDefinition definition, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var byCode = Index(definition); if (!byCode.TryGetValue(code, out var current)) return [];
        return WalkParents(byCode, current).ToArray();
    }
    /// <summary>Returns descendants in pre-order, with siblings ordered by first appearance in <see cref="TaxonomyDefinition.Nodes"/>.</summary>
    public IReadOnlyList<TaxonomyNode> GetDescendants(TaxonomyDefinition definition, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var byCode = Index(definition); if (!byCode.ContainsKey(code)) return [];
        var children = definition.Nodes.Where(node => node.ParentCode is not null).GroupBy(node => node.ParentCode!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var result = new List<TaxonomyNode>(); var seen = new HashSet<string>(StringComparer.Ordinal) { code };
        Visit(code, 0); return result;

        void Visit(string parentCode, int depth)
        {
            if (!children.TryGetValue(parentCode, out var directChildren)) return;
            if (depth >= TaxonomyDefinitionAdmission.MaximumParentTraversalDepth) throw TraversalDepth();
            foreach (var child in directChildren)
            {
                if (!seen.Add(child.Code)) throw new TaxonomyTraversalException("taxonomy.parent_cycle", "A parent cycle was encountered during traversal.");
                result.Add(child); Visit(child.Code, depth + 1);
            }
        }
    }

    public bool Subsumes(TaxonomyDefinition definition, string ancestorCode, string descendantCode)
    {
        ArgumentNullException.ThrowIfNull(ancestorCode); ArgumentNullException.ThrowIfNull(descendantCode);
        var byCode = Index(definition); if (!byCode.TryGetValue(ancestorCode, out _) || !byCode.TryGetValue(descendantCode, out var current)) return false;
        if (ancestorCode == descendantCode) return true;
        foreach (var parent in WalkParents(byCode, current)) { if (parent.Code == ancestorCode) return true; }
        return false;
    }

    public TaxonomySuccessionResult ResolveSuccession(TaxonomyDefinition definition, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var byCode = Index(definition); if (!byCode.TryGetValue(code, out var current)) return new TaxonomySuccessionRefusal(code, "classification_unknown");
        var seen = new HashSet<string>(StringComparer.Ordinal); var depth = 0;
        while (true)
        {
            if (!seen.Add(current.Code)) return new TaxonomySuccessionRefusal(code, "successor_cycle");
            if (current.Status == TaxonomyNodeStatus.Active) return new ResolvedTaxonomySuccession(current);
            if (current.SuccessorCode is null) return new TaxonomySuccessionRefusal(code, "successor_missing");
            if (depth >= TaxonomyDefinitionAdmission.MaximumParentTraversalDepth) return new TaxonomySuccessionRefusal(code, "successor_depth_exceeded");
            if (!byCode.TryGetValue(current.SuccessorCode, out var successor)) return new TaxonomySuccessionRefusal(code, "successor_unknown");
            current = successor;
            depth++;
        }
    }
    /// <summary>Expands the whole pinned scheme, including tombstoned nodes.</summary>
    public IReadOnlyList<string> ExpandWholeScheme(TaxonomyDefinition definition)
    {
        Index(definition); return definition.Nodes.Select(node => node.Code).ToArray();
    }

    /// <summary>Expands a named collection from the supplied pinned definition. An absent name is a
    /// typed refusal rather than an empty expansion, so a caller cannot treat a miss as an allowed
    /// value domain.</summary>
    /// <param name="definition">The pinned scheme that owns the named collection.</param>
    /// <param name="collectionName">The collection name declared by that scheme.</param>
    /// <returns>The declared concept codes in their authored order, or a refusal naming the missing collection.</returns>
    public TaxonomyCollectionExpansionResult ExpandCollection(TaxonomyDefinition definition, string collectionName)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(collectionName);
        Index(definition);
        var collection = (definition.Collections ?? []).FirstOrDefault(candidate => candidate is not null && StringComparer.Ordinal.Equals(candidate.Name, collectionName));
        if (collection is null) return new TaxonomyCollectionExpansionNotFound(collectionName);
        if (collection.NodeCodes is null) throw new TaxonomyTraversalException("taxonomy.definition_malformed", "The collection has no node-code collection.");
        return new ResolvedTaxonomyCollectionExpansion(collection.NodeCodes.ToArray());
    }

    /// <summary>Expands an overlay against the vendor version selected for this call, not the version
    /// recorded when it was authored (taxonomy-eng-12): calling this again after a new vendor release
    /// is registered under the same <see cref="TaxonomyDefinitionId"/>, at a higher version, picks up
    /// the vendor's new concepts with no change to the overlay itself. Every refusal is a typed
    /// member of <see cref="TaxonomyOverlayExpansionResult"/>, never a silently empty expansion and
    /// never an exception, matching this interpreter's other business-rule refusals (e.g.
    /// <see cref="TaxonomySuccessionResult"/>) rather than its traversal-integrity exceptions.</summary>
    public TaxonomyOverlayExpansionResult ExpandOverlay(TaxonomyDefinitionCoordinates overlayCoordinates, string vendorVersion)
    {
        var overlayResolution = ResolveDefinition(overlayCoordinates);
        if (overlayResolution is TaxonomyDefinitionNotFound overlayNotFound) return new TaxonomyOverlayExpansionNotFound(overlayNotFound.Coordinates);
        var overlay = ((ResolvedTaxonomyDefinition)overlayResolution).Definition;
        if (overlay.Overlay is null) throw new TaxonomyOverlayException("taxonomy.overlay_reference_missing", "The overlay has no vendor reference.");

        // Matched by (DefinitionId, Version) only, deliberately: a vendor scheme is registered once
        // and named by many tenants' overlays, so its own coordinates carry whichever tenant it was
        // indexed under. More than one match means two DIFFERENT registrations collided on the same
        // vendor id/version, which is ambiguous and refuses rather than picking the first arbitrarily.
        var vendorMatches = definitions.Where(entry => entry.Key.DefinitionId == overlay.Overlay.VendorDefinitionId && entry.Key.Version == vendorVersion).ToArray();
        if (vendorMatches.Length == 0) return new TaxonomyOverlayExpansionNotFound(new("", overlay.Overlay.VendorDefinitionId, vendorVersion));
        if (vendorMatches.Length > 1) return new TaxonomyOverlayExpansionAmbiguousVendor(overlay.Overlay.VendorDefinitionId, vendorVersion, vendorMatches.Select(entry => entry.Key.Tenant).ToArray());
        var vendor = vendorMatches[0].Value;

        var vendorCodes = Index(vendor);
        var copiedCode = Index(overlay).Keys.FirstOrDefault(vendorCodes.ContainsKey);
        if (copiedCode is not null) return new TaxonomyOverlayExpansionCopiesVendorNode(copiedCode);

        var unknownDesignation = (overlay.OverlayDesignations ?? []).Select(designation => designation.VendorNodeCode).FirstOrDefault(code => code is not null && !vendorCodes.ContainsKey(code));
        if (unknownDesignation is not null) return new TaxonomyOverlayExpansionUnknownDesignation(unknownDesignation);

        return new ResolvedTaxonomyOverlayExpansion(ExpandWholeScheme(vendor).Concat(ExpandWholeScheme(overlay)).ToArray());
    }

    /// <summary>Resolves each overlay designation's effective display and description: the overlay's
    /// own value where it supplied one, the vendor node's own value otherwise (taxonomy-auth-10 — a
    /// designation adds a label/description without changing the vendor node's own definition).
    /// Throws for a designation naming a code the vendor scheme does not have; callers that already
    /// went through <see cref="ExpandOverlay"/> or vendor-aware <c>Validate</c> will not hit this.</summary>
    public IReadOnlyList<ResolvedTaxonomyOverlayDesignation> ResolveOverlayDesignations(TaxonomyDefinition overlay, TaxonomyDefinition vendor)
    {
        ArgumentNullException.ThrowIfNull(overlay); ArgumentNullException.ThrowIfNull(vendor);
        var vendorNodes = Index(vendor);
        var results = new List<ResolvedTaxonomyOverlayDesignation>();
        foreach (var designation in overlay.OverlayDesignations ?? [])
        {
            if (designation.VendorNodeCode is null || !vendorNodes.TryGetValue(designation.VendorNodeCode, out var vendorNode))
                throw new TaxonomyOverlayException("taxonomy.overlay_designation_vendor_node_unknown", $"Overlay designation names unknown vendor node '{designation.VendorNodeCode}'.");
            results.Add(new(designation.VendorNodeCode, designation.Display ?? vendorNode.Display, designation.Description ?? vendorNode.Description));
        }
        return results;
    }

    public TaxonomyDefinitionChangeSet Diff(TaxonomyDefinition previous, TaxonomyDefinition current)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        var previousIdentity = CoordinatesOf(previous) with { Version = "" };
        var currentIdentity = CoordinatesOf(current) with { Version = "" };
        if (previousIdentity != currentIdentity) throw new ArgumentException("A definition diff requires the same tenant and definition id.");
        var prior = Index(previous); var added = new List<TaxonomyNode>(); var changed = new List<TaxonomyNodeChange>(); var tombstoned = new List<TaxonomyNode>();
        foreach (var node in current.Nodes)
        {
            if (!prior.TryGetValue(node.Code, out var old)) { added.Add(node); continue; }
            var fields = ChangedFields(old, node);
            if (fields != TaxonomyNodeChangeFields.None) changed.Add(new TaxonomyNodeChange(node.Code, old, node, fields));
            if (old.Status != TaxonomyNodeStatus.Tombstoned && node.Status == TaxonomyNodeStatus.Tombstoned) tombstoned.Add(node);
        }
        return new TaxonomyDefinitionChangeSet(added, changed, tombstoned);
    }

    private static TaxonomyDefinitionCoordinates CoordinatesOf(TaxonomyDefinition definition) => new(definition.Tenant, definition.DefinitionId, definition.Version);
    /// <summary>Walks direct <see cref="TaxonomyNode.ParentCode"/> edges from <paramref name="start"/> to the root,
    /// yielding each parent in order. Shared by <see cref="GetAncestors"/> and <see cref="Subsumes"/> so the cycle
    /// guard and depth bound (<see cref="TaxonomyDefinitionAdmission.MaximumParentTraversalDepth"/>) live in one place.</summary>
    private static IEnumerable<TaxonomyNode> WalkParents(IReadOnlyDictionary<string, TaxonomyNode> byCode, TaxonomyNode start)
    {
        var current = start; var seen = new HashSet<string>(StringComparer.Ordinal) { current.Code }; var depth = 0;
        while (current.ParentCode is not null)
        {
            if (depth >= TaxonomyDefinitionAdmission.MaximumParentTraversalDepth) throw TraversalDepth();
            if (!byCode.TryGetValue(current.ParentCode, out var parent)) throw new TaxonomyTraversalException("taxonomy.parent_unknown", $"Parent '{current.ParentCode}' is not in the definition.");
            if (!seen.Add(parent.Code)) throw new TaxonomyTraversalException("taxonomy.parent_cycle", "A parent cycle was encountered during traversal.");
            yield return parent; current = parent; depth++;
        }
    }
    private static Dictionary<string, TaxonomyNode> Index(TaxonomyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition); if (definition.Nodes is null) throw new TaxonomyTraversalException("taxonomy.definition_malformed", "The definition has no node collection.");
        var indexed = new Dictionary<string, TaxonomyNode>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (node is null || string.IsNullOrEmpty(node.Code) || !indexed.TryAdd(node.Code, node)) throw new TaxonomyTraversalException("taxonomy.definition_malformed", "The definition contains a null, blank, or duplicate node code.");
        }
        return indexed;
    }
    private static TaxonomyTraversalException TraversalDepth() => new("taxonomy.traversal_depth_exceeded", $"Traversal exceeded the maximum depth of {TaxonomyDefinitionAdmission.MaximumParentTraversalDepth}.");
    private static TaxonomyNodeChangeFields ChangedFields(TaxonomyNode previous, TaxonomyNode current)
    {
        var fields = TaxonomyNodeChangeFields.None;
        if (!StringComparer.Ordinal.Equals(previous.Display, current.Display)) fields |= TaxonomyNodeChangeFields.Display;
        if (!StringComparer.Ordinal.Equals(previous.Description, current.Description)) fields |= TaxonomyNodeChangeFields.Description;
        if (previous.Status != current.Status) fields |= TaxonomyNodeChangeFields.Status;
        if (!StringComparer.Ordinal.Equals(previous.ParentCode, current.ParentCode)) fields |= TaxonomyNodeChangeFields.ParentCode;
        if (!StringComparer.Ordinal.Equals(previous.SuccessorCode, current.SuccessorCode)) fields |= TaxonomyNodeChangeFields.SuccessorCode;
        return fields;
    }
}
