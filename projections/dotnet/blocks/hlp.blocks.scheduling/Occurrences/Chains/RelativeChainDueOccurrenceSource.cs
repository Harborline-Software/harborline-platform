using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling;

/// <summary>Projects only current ledger revisions through the same occurrence seam as every other basis.</summary>
public sealed class RelativeChainDueOccurrenceSource : IDueOccurrenceSource
{
    /// <inheritdoc />
    public IReadOnlyList<SchedulingOccurrence> DeriveDue(DueOccurrenceRequest request, DateTimeOffset kernelInstant)
    {
        var chain = request.Chain ?? throw new OccurrenceRefusalException("scheduling.occurrence.anchor_required");
        var basis = (EventAnchoredChainBasis)request.Series.Definition.Basis!;
        if (chain.ChainInstanceId != request.Series.SeriesId || chain.SubjectRef != request.Series.SubjectRef
            || chain.Definition.Anchor != basis.Anchor || !chain.Definition.Nodes.SequenceEqual(basis.Nodes))
            throw new OccurrenceRefusalException("scheduling.occurrence.chain_binding_invalid");
        var result = new InMemoryRelativeChainExpansionService().Expand(chain with { RecordedAt = kernelInstant });
        if (result.Failures.Count != 0) throw new OccurrenceRefusalException("scheduling.occurrence.chain_" + result.Failures[0].Code);
        return result.CurrentOccurrences.Where(o => OccurrenceTime.InRange(request, o.DueDate)).Select(o =>
        {
            var node = basis.Nodes.Single(n => n.NodeId == o.OccurrenceId.NodeId);
            return OccurrenceTime.Create(request, kernelInstant, o.OccurrenceId.ToString(), o.DueDate, node.Window, node.Tolerance);
        }).ToArray();
    }
}
