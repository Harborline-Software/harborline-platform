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
    /// <summary>Expands the whole pinned scheme, including tombstoned nodes; collection expansion is intentionally not provided.</summary>
    public IReadOnlyList<string> ExpandWholeScheme(TaxonomyDefinition definition)
    {
        Index(definition); return definition.Nodes.Select(node => node.Code).ToArray();
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
