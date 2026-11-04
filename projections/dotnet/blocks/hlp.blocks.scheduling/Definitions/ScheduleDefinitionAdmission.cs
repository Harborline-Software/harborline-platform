using System.Globalization;
using System.Text.Json;
using System.Xml;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Scheduling.Definitions;

/// <summary>The structural check of a typed schedule. Every refusal is a stable code and an RFC 6901 pointer.</summary>
internal static class ScheduleDefinitionAdmission
{
    private static readonly string[] InstantFormats = ["yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", "yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'"];

    internal static List<DefinitionRefusal> Validate(ScheduleDefinition definition, DefinitionDocument document,
        DefinitionAdmissionPhase phase, DefinitionContractWindow window)
    {
        var refusals = new List<DefinitionRefusal>();
        Envelope(definition.Envelope, document, phase, window, refusals);
        if (string.IsNullOrWhiteSpace(definition.Name)) refusals.Add(new(ScheduleDefinitionCodes.NameRequired, "/name"));
        if (definition.Starts is null || string.IsNullOrWhiteSpace(definition.Starts.TargetId))
            refusals.Add(new(ScheduleDefinitionCodes.StartsRequired, "/starts"));
        if (definition.Basis is not null) Basis(definition.Basis, refusals);
        Window(definition, refusals);
        if (definition.Tolerance is { } tolerance) Tolerance(tolerance, "/tolerance", refusals);
        if (definition.Grace is { } grace) Duration(grace, "/grace", refusals);
        if (definition.Escalation is { } escalation)
        {
            Duration(escalation.After, "/escalation/after", refusals);
            if (string.IsNullOrWhiteSpace(escalation.To)) refusals.Add(new(ScheduleDefinitionCodes.EscalationInvalid, "/escalation/to"));
        }
        Modules(definition, refusals);
        if (definition.LegalityRef is { } legality && string.IsNullOrWhiteSpace(legality))
            refusals.Add(new(ScheduleDefinitionCodes.LegalityRefInvalid, "/legality_ref"));
        return refusals;
    }

    private static void Envelope(ScheduleDefinitionEnvelope? envelope, DefinitionDocument document, DefinitionAdmissionPhase phase,
        DefinitionContractWindow window, List<DefinitionRefusal> refusals)
    {
        if (envelope is null)
        {
            if (phase != DefinitionAdmissionPhase.Author) refusals.Add(new(ScheduleDefinitionCodes.EnvelopeRequired, "/envelope"));
            return;
        }
        if (envelope.Identity != document.Key.DefinitionId || envelope.Tenant != document.Key.Tenant)
            refusals.Add(new(ScheduleDefinitionCodes.EnvelopeMismatch, "/envelope"));
        if (envelope.Provenance is not { ValueKind: JsonValueKind.Object })
            refusals.Add(new(ScheduleDefinitionCodes.EnvelopeInvalid, "/envelope/provenance"));
        if (string.IsNullOrWhiteSpace(envelope.RetentionClass))
            refusals.Add(new(ScheduleDefinitionCodes.EnvelopeInvalid, "/envelope/retention_class"));
        if (envelope.Requires is null) refusals.Add(new(ScheduleDefinitionCodes.EnvelopeInvalid, "/envelope/requires"));
        foreach (var (requirement, index) in (envelope.Requires ?? []).Select((value, index) => (value, index)))
            if (requirement is null || string.IsNullOrWhiteSpace(requirement.Capability))
                refusals.Add(new(ScheduleDefinitionCodes.EnvelopeInvalid, $"/envelope/requires/{index}"));
        var contract = window.Check(envelope.Contract, null);
        if (contract is not null) refusals.Add(new(contract.Code, contract.Pointer));
    }

    private static void Basis(ScheduleBasis basis, List<DefinitionRefusal> refusals)
    {
        switch (basis)
        {
            case FixedCalendarBasis calendar when string.IsNullOrWhiteSpace(calendar.Rrule):
                refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, "/basis/rrule"));
                break;
            case CompletionOffsetBasis completion:
                Duration(completion.Offset, "/basis/offset", refusals);
                break;
            case ThresholdBasis threshold when string.IsNullOrWhiteSpace(threshold.Reading):
                refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, "/basis/reading"));
                break;
            case EventAnchoredChainBasis chain:
                Chain(chain, refusals);
                break;
        }
    }

    private static void Chain(EventAnchoredChainBasis chain, List<DefinitionRefusal> refusals)
    {
        if (string.IsNullOrWhiteSpace(chain.Anchor)) refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, "/basis/anchor"));
        if (chain.Nodes is not { Count: > 0 })
        {
            refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, "/basis/nodes"));
            return;
        }
        var earlier = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (node, index) in chain.Nodes.Select((value, index) => (value, index)))
        {
            var pointer = $"/basis/nodes/{index}";
            if (node is null)
            {
                refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, pointer));
                continue;
            }
            if (string.IsNullOrWhiteSpace(node.NodeId) || earlier.Contains(node.NodeId))
                refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, pointer + "/node_id"));
            // Only the chain has an external anchor, so moving it moves every node (L449).
            if (node.AfterNodeId is { } after && !earlier.Contains(after))
                refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, pointer + "/after_node_id"));
            Duration(node.Offset, pointer + "/offset", refusals);
            if (node.Tolerance is null) refusals.Add(new(ScheduleDefinitionCodes.BasisInvalid, pointer + "/tolerance"));
            else Tolerance(node.Tolerance, pointer + "/tolerance", refusals);
            if (node.NodeId is not null) earlier.Add(node.NodeId);
        }
    }

    private static void Window(ScheduleDefinition definition, List<DefinitionRefusal> refusals)
    {
        if (definition.Window is not { } window)
        {
            if (definition.Basis is not EventAnchoredChainBasis) refusals.Add(new(ScheduleDefinitionCodes.WindowRequired, "/window"));
            return;
        }
        // A window is an interval: an equal or reversed pair is the single point L483 refuses.
        var ordered = definition.Basis is null
            ? Instant(window.Earliest) is { } opens && Instant(window.Latest) is { } closes && opens < closes
            : SignedDuration(window.Earliest) is { } from && SignedDuration(window.Latest) is { } to && from < to;
        if (!ordered) refusals.Add(new(ScheduleDefinitionCodes.WindowInvalid, "/window"));
    }

    private static void Tolerance(ScheduleTolerance tolerance, string pointer, List<DefinitionRefusal> refusals)
    {
        Duration(tolerance.Early, pointer + "/early", refusals);
        Duration(tolerance.Late, pointer + "/late", refusals);
    }

    private static void Modules(ScheduleDefinition definition, List<DefinitionRefusal> refusals)
    {
        var modules = definition.Modules ?? [];
        // None names no planner (L1149), so there is nothing for a module to compile into.
        if (modules.Count > 0 && definition.ProblemClass == ScheduleProblemClass.None)
            refusals.Add(new(ScheduleDefinitionCodes.ModulesWithoutProblemClass, "/modules"));
        foreach (var (module, index) in modules.Select((value, index) => (value, index)))
            if (module is null) refusals.Add(new(ScheduleDefinitionCodes.ModuleInvalid, $"/modules/{index}"));
            else if (string.IsNullOrWhiteSpace(module.Module)) refusals.Add(new(ScheduleDefinitionCodes.ModuleInvalid, $"/modules/{index}/module"));
    }

    private static void Duration(string? value, string pointer, List<DefinitionRefusal> refusals)
    {
        if (SignedDuration(value) is not { } duration || duration < TimeSpan.Zero)
            refusals.Add(new(ScheduleDefinitionCodes.DurationInvalid, pointer));
    }

    // ponytail: XmlConvert counts a month as 30 days and a year as 365, enough to order a window; a calendar-exact
    // comparison belongs with the occurrence generator that owns dates.
    private static TimeSpan? SignedDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return XmlConvert.ToTimeSpan(value); }
        catch (Exception exception) when (exception is FormatException or OverflowException) { return null; }
    }

    private static DateTimeOffset? Instant(string? value)
        => DateTimeOffset.TryParseExact(value, InstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant
            : null;
}
