using System.Text;

namespace Harborline.Blocks.RelativeChains;

/// <summary>Fail-closed r2 definition validation and deterministic topological ordering.</summary>
public static class RelativeChainValidator
{
    /// <summary>The maximum supported node count.</summary>
    public const int MaximumNodeCount = 1024;
    /// <summary>The maximum supported whole-day offset or tolerance.</summary>
    public const int MaximumDays = 36500;

    /// <summary>Validates a definition and returns its ordinal deterministic topological order.</summary>
    /// <param name="definition">The immutable definition revision.</param>
    /// <param name="failures">Typed deterministic failures.</param>
    /// <returns>Topologically ordered nodes, or an empty list on failure.</returns>
    public static IReadOnlyList<RelativeChainNode> Validate(RelativeChainDefinition definition, out IReadOnlyList<RelativeChainFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var found = new List<RelativeChainFailure>();
        if (definition.DefinitionRevision < 1 || definition.Nodes.Count is < 1 or > MaximumNodeCount || !ValidRef(definition.ChainDefinitionId) || !ValidRef(definition.ProviderRef) || !ValidRef(definition.ResourceRef))
            found.Add(Fail(RelativeChainFailureCode.UnsupportedDefinition, $"definition/{definition.ChainDefinitionId}/r{definition.DefinitionRevision}"));
        var indexed = definition.Nodes.Select((node, index) => (node, index)).ToArray();
        foreach (var group in indexed.GroupBy(item => item.node.NodeId, StringComparer.Ordinal).Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal))
            found.Add(Fail(RelativeChainFailureCode.DuplicateNodeId, group.Select(item => $"definition/{definition.ChainDefinitionId}/r{definition.DefinitionRevision}/node/{group.Key}/declaration/{item.index}").ToArray()));
        var ids = indexed.Select(item => item.node.NodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var node in definition.Nodes.OrderBy(node => node.NodeId, StringComparer.Ordinal))
        {
            var nodeRef = $"definition/{definition.ChainDefinitionId}/r{definition.DefinitionRevision}/node/{node.NodeId}";
            if (!ValidRef(node.NodeId) || !ValidRef(node.Anchor.Reference) || node.Tolerance.EarlyDays is < 0 or > MaximumDays || node.Tolerance.LateDays is < 0 or > MaximumDays || node.OffsetDays > MaximumDays)
                found.Add(Fail(RelativeChainFailureCode.UnsupportedDefinition, nodeRef));
            if (node.OffsetDays < 0) found.Add(Fail(RelativeChainFailureCode.UnsupportedNegativeOffset, nodeRef));
            if (!StringComparer.Ordinal.Equals(node.ProviderRef, definition.ProviderRef) || !StringComparer.Ordinal.Equals(node.ResourceRef, definition.ResourceRef))
                found.Add(Fail(RelativeChainFailureCode.UnsupportedResourceCardinality, nodeRef));
            if (node.Anchor.Kind == RelativeChainAnchorKind.Predecessor && !ids.Contains(node.Anchor.Reference))
                found.Add(Fail(RelativeChainFailureCode.UnknownPredecessor, nodeRef, $"predecessor/{node.Anchor.Reference}"));
        }
        if (found.Count != 0) { failures = found; return []; }

        var indegree = definition.Nodes.ToDictionary(node => node.NodeId, _ => 0, StringComparer.Ordinal);
        var children = definition.Nodes.ToDictionary(node => node.NodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in definition.Nodes.Where(node => node.Anchor.Kind == RelativeChainAnchorKind.Predecessor)) { indegree[node.NodeId]++; children[node.Anchor.Reference].Add(node.NodeId); }
        var ready = new SortedSet<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        var byId = definition.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var ordered = new List<RelativeChainNode>();
        while (ready.Count != 0)
        {
            var id = ready.Min!; ready.Remove(id); ordered.Add(byId[id]);
            foreach (var child in children[id].Order(StringComparer.Ordinal)) if (--indegree[child] == 0) ready.Add(child);
        }
        if (ordered.Count != definition.Nodes.Count)
        {
            var cycleRefs = indegree.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"definition/{definition.ChainDefinitionId}/r{definition.DefinitionRevision}/cycle-node/{pair.Key}").ToArray();
            failures = [Fail(RelativeChainFailureCode.DependencyCycle, cycleRefs)]; return [];
        }
        failures = []; return ordered;
    }

    private static bool ValidRef(string value) => !string.IsNullOrEmpty(value) && Encoding.UTF8.GetByteCount(value) <= RelativeChainReferenceCodec.MaximumReferenceUtf8Bytes;
    private static RelativeChainFailure Fail(RelativeChainFailureCode code, params string[] refs) => new(code, refs.Length == 0 ? [$"failure/{code}"] : refs);
}
