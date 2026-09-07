using System.Security.Cryptography;
using System.Text;

namespace Harborline.Blocks.RelativeChains;

/// <summary>A persistence-free deterministic reconciler for an explicitly supplied chain ledger.</summary>
public sealed class InMemoryRelativeChainExpansionService : IRelativeChainExpansionService
{
    /// <inheritdoc />
    public RelativeChainExpansionResult Expand(RelativeChainExpansionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedVersion = request.PriorLedger.LedgerVersion;
        var ledgerFailure = ValidateLedger(request);
        if (ledgerFailure is not null) return Result([], [], [], [ledgerFailure], expectedVersion);
        var order = RelativeChainValidator.Validate(request.Definition, out var definitionFailures);
        if (definitionFailures.Count != 0) return Result(Current(request.PriorLedger), [], [], definitionFailures, expectedVersion);

        var priorCurrent = CurrentByNode(request.PriorLedger);
        var appended = new List<RelativeChainOccurrence>();
        var edges = new List<OccurrenceSupersession>();
        var failures = new List<RelativeChainFailure>();
        var projected = new Dictionary<string, RelativeChainOccurrence>(StringComparer.Ordinal);
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        var anchors = request.Anchors.GroupBy(anchor => anchor.AnchorRef, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.OrderByDescending(anchor => anchor.AnchorRevision).First(), StringComparer.Ordinal);

        foreach (var node in order)
        {
            DateOnly basisDate;
            string basisRef;
            int basisRevision;
            SupersessionReasonCode terminalReason;
            string trigger;
            if (node.Anchor.Kind == RelativeChainAnchorKind.External)
            {
                trigger = $"anchor/{node.Anchor.Reference}";
                if (!anchors.TryGetValue(node.Anchor.Reference, out var anchor))
                {
                    failures.Add(Fail(RelativeChainFailureCode.MissingAnchor, trigger, NodeRef(request, node.NodeId)));
                    unavailable.Add(node.NodeId); continue;
                }
                trigger = $"anchor/{anchor.AnchorRef}/r{anchor.AnchorRevision}";
                if (anchor.State == AnchorSnapshotState.Deleted || anchor.AnchorDate is null)
                {
                    failures.Add(Fail(RelativeChainFailureCode.DeletedAnchor, anchor.ExplanationRefs.Count == 0 ? [trigger] : anchor.ExplanationRefs.ToArray()));
                    unavailable.Add(node.NodeId); terminalReason = SupersessionReasonCode.AnchorDeleted;
                    Terminal(node.NodeId, terminalReason, trigger, request, priorCurrent, edges); continue;
                }
                basisDate = anchor.AnchorDate.Value; basisRef = anchor.AnchorRef; basisRevision = anchor.AnchorRevision;
            }
            else
            {
                trigger = $"predecessor/{node.Anchor.Reference}";
                if (unavailable.Contains(node.Anchor.Reference) || !projected.TryGetValue(node.Anchor.Reference, out var predecessor))
                {
                    failures.Add(Fail(RelativeChainFailureCode.DeletedAnchor, trigger, NodeRef(request, node.NodeId)));
                    unavailable.Add(node.NodeId); terminalReason = SupersessionReasonCode.AnchorDeleted;
                    Terminal(node.NodeId, terminalReason, trigger, request, priorCurrent, edges); continue;
                }
                basisDate = predecessor.DueDate; basisRef = predecessor.OccurrenceId.ToString(); basisRevision = predecessor.OccurrenceId.OccurrenceRevision;
            }

            DateOnly due;
            DateOnly earliest;
            DateOnly latest;
            try { due = basisDate.AddDays(node.OffsetDays); earliest = due.AddDays(-node.Tolerance.EarlyDays); latest = due.AddDays(node.Tolerance.LateDays); }
            catch (ArgumentOutOfRangeException)
            {
                failures.Add(Fail(RelativeChainFailureCode.DateArithmeticOverflow, NodeRef(request, node.NodeId))); unavailable.Add(node.NodeId); continue;
            }
            var fingerprint = Fingerprint(node);
            priorCurrent.TryGetValue(node.NodeId, out var old);
            if (old is not null && Equivalent(old, request, node, basisRef, basisRevision, due, earliest, latest, fingerprint))
            {
                projected[node.NodeId] = old; continue;
            }
            var nextRevision = NextRevision(request.PriorLedger, node.NodeId);
            var id = new RelativeChainOccurrenceId(request.ChainInstanceId, node.NodeId, due, nextRevision);
            var occurrence = new RelativeChainOccurrence(id, request.Definition.DefinitionRevision, basisRef, basisRevision, due, earliest, latest, request.SubjectRef, node.ProviderRef, node.ResourceRef, fingerprint, [NodeRef(request, node.NodeId), trigger]);
            appended.Add(occurrence); projected[node.NodeId] = occurrence;
            if (old is not null)
            {
                var reason = Reason(old, occurrence, node.Anchor.Kind);
                edges.Add(Edge(old, occurrence, reason, trigger, request.RecordedAt));
            }
        }

        var definedIds = request.Definition.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in priorCurrent.Where(pair => !definedIds.Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            edges.Add(Edge(removed.Value, null, SupersessionReasonCode.DefinitionRevised, $"definition/{request.Definition.ChainDefinitionId}/r{request.Definition.DefinitionRevision}", request.RecordedAt));

        var allOccurrences = request.PriorLedger.Occurrences.Concat(appended).ToArray();
        var allEdges = request.PriorLedger.Supersessions.Concat(edges).ToArray();
        var superseded = allEdges.Select(edge => edge.SupersededOccurrenceId).ToHashSet();
        var current = allOccurrences.Where(item => !superseded.Contains(item.OccurrenceId)).OrderBy(item => item.OccurrenceId.NodeId, StringComparer.Ordinal).ThenBy(item => item.OccurrenceId.OccurrenceRevision).ToArray();
        return Result(current, appended, edges, failures, expectedVersion);
    }

    private static RelativeChainFailure? ValidateLedger(RelativeChainExpansionRequest request)
    {
        if (request.PriorLedger.LedgerVersion < 0) return Fail(RelativeChainFailureCode.CorruptLedger, "ledger/version");
        var ids = request.PriorLedger.Occurrences.Select(item => item.OccurrenceId).ToHashSet();
        if (ids.Count != request.PriorLedger.Occurrences.Count) return Fail(RelativeChainFailureCode.CorruptLedger, "ledger/duplicate-occurrence");
        var superseded = new HashSet<RelativeChainOccurrenceId>();
        foreach (var edge in request.PriorLedger.Supersessions)
        {
            if (!ids.Contains(edge.SupersededOccurrenceId) || !superseded.Add(edge.SupersededOccurrenceId)) return Fail(RelativeChainFailureCode.CorruptLedger, $"supersession/{edge.SupersessionId}");
            if (edge.SuccessorOccurrenceId is { } successor && (!ids.Contains(successor) || !StringComparer.Ordinal.Equals(successor.ChainInstanceId, edge.ChainInstanceId) || !StringComparer.Ordinal.Equals(successor.NodeId, edge.NodeId) || successor.OccurrenceRevision <= edge.SupersededOccurrenceId.OccurrenceRevision)) return Fail(RelativeChainFailureCode.CorruptLedger, $"supersession/{edge.SupersessionId}");
        }
        var forks = request.PriorLedger.Occurrences.Where(item => StringComparer.Ordinal.Equals(item.OccurrenceId.ChainInstanceId, request.ChainInstanceId) && !superseded.Contains(item.OccurrenceId)).GroupBy(item => item.OccurrenceId.NodeId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return forks is null ? null : Fail(RelativeChainFailureCode.AmbiguousCurrentOccurrence, $"chain/{request.ChainInstanceId}/node/{forks.Key}");
    }

    private static Dictionary<string, RelativeChainOccurrence> CurrentByNode(RelativeChainLedger ledger) => Current(ledger).ToDictionary(item => item.OccurrenceId.NodeId, StringComparer.Ordinal);
    private static IReadOnlyList<RelativeChainOccurrence> Current(RelativeChainLedger ledger)
    {
        var superseded = ledger.Supersessions.Select(edge => edge.SupersededOccurrenceId).ToHashSet();
        return ledger.Occurrences.Where(item => !superseded.Contains(item.OccurrenceId)).OrderBy(item => item.OccurrenceId.NodeId, StringComparer.Ordinal).ToArray();
    }
    private static bool Equivalent(RelativeChainOccurrence old, RelativeChainExpansionRequest request, RelativeChainNode node, string basisRef, int basisRevision, DateOnly due, DateOnly earliest, DateOnly latest, string fingerprint) =>
        StringComparer.Ordinal.Equals(old.BasisRef, basisRef) && old.BasisRevision == basisRevision && old.DueDate == due && old.EarliestDate == earliest && old.LatestDate == latest && StringComparer.Ordinal.Equals(old.SubjectRef, request.SubjectRef) && StringComparer.Ordinal.Equals(old.ProviderRef, node.ProviderRef) && StringComparer.Ordinal.Equals(old.ResourceRef, node.ResourceRef) && StringComparer.Ordinal.Equals(old.DerivationFingerprint, fingerprint);
    private static int NextRevision(RelativeChainLedger ledger, string nodeId)
    {
        var maximum = ledger.Occurrences.Where(item => StringComparer.Ordinal.Equals(item.OccurrenceId.NodeId, nodeId)).Select(item => item.OccurrenceId.OccurrenceRevision).DefaultIfEmpty(0).Max();
        return checked(maximum + 1);
    }
    private static SupersessionReasonCode Reason(RelativeChainOccurrence old, RelativeChainOccurrence next, RelativeChainAnchorKind kind)
    {
        if (!StringComparer.Ordinal.Equals(old.SubjectRef, next.SubjectRef) || !StringComparer.Ordinal.Equals(old.ProviderRef, next.ProviderRef) || !StringComparer.Ordinal.Equals(old.ResourceRef, next.ResourceRef)) return SupersessionReasonCode.BindingRevised;
        if (!StringComparer.Ordinal.Equals(old.DerivationFingerprint, next.DerivationFingerprint)) return SupersessionReasonCode.DefinitionRevised;
        if (kind == RelativeChainAnchorKind.Predecessor) return SupersessionReasonCode.PredecessorRevised;
        return old.DueDate == next.DueDate ? SupersessionReasonCode.AnchorRevised : SupersessionReasonCode.AnchorMoved;
    }
    private static void Terminal(string nodeId, SupersessionReasonCode reason, string trigger, RelativeChainExpansionRequest request, IReadOnlyDictionary<string, RelativeChainOccurrence> current, ICollection<OccurrenceSupersession> edges)
    { if (current.TryGetValue(nodeId, out var old)) edges.Add(Edge(old, null, reason, trigger, request.RecordedAt)); }
    private static OccurrenceSupersession Edge(RelativeChainOccurrence old, RelativeChainOccurrence? next, SupersessionReasonCode reason, string trigger, DateTimeOffset at)
    {
        var successor = next?.OccurrenceId;
        var material = $"{old.OccurrenceId}|{successor?.ToString() ?? "-"}|{reason}|{trigger}";
        var id = "rcs1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return new(id, old.OccurrenceId.ChainInstanceId, old.OccurrenceId.NodeId, old.OccurrenceId, successor, reason, trigger, at, [$"occurrence/{old.OccurrenceId}", trigger]);
    }
    private static string Fingerprint(RelativeChainNode node)
    {
        var material = $"{(int)node.Anchor.Kind}|{RelativeChainReferenceCodec.Escape(node.Anchor.Reference)}|{node.OffsetDays}|{node.Tolerance.EarlyDays}|{node.Tolerance.LateDays}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
    private static string NodeRef(RelativeChainExpansionRequest request, string nodeId) => $"definition/{request.Definition.ChainDefinitionId}/r{request.Definition.DefinitionRevision}/node/{nodeId}";
    private static RelativeChainFailure Fail(RelativeChainFailureCode code, params string[] refs) => new(code, refs.Length == 0 ? [$"failure/{code}"] : refs);
    private static RelativeChainExpansionResult Result(IReadOnlyList<RelativeChainOccurrence> current, IReadOnlyList<RelativeChainOccurrence> appended, IReadOnlyList<OccurrenceSupersession> edges, IReadOnlyList<RelativeChainFailure> failures, long version) => new(current, appended, edges, failures, version, ["relative-chain/expansion/r2"]);
}
