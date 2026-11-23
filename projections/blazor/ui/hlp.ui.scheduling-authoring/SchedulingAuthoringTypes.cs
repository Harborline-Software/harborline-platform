using System.Text.Json.Nodes;

namespace Harborline.UIAdapters.Blazor.Components.SchedulingAuthoring;

/// <summary>A labelled host catalogue entry.</summary>
public record ScheduleOption(string Id, string Label);
/// <summary>A registered solver and its offered guarantees.</summary>
public sealed record ScheduleBackend(string Id, string Label, IReadOnlyList<string> Guarantees) : ScheduleOption(Id, Label);
/// <summary>An admitted module; admission does not imply backend support or certification.</summary>
public sealed record ScheduleModule(string Id, string Label, string Scope) : ScheduleOption(Id, Label);
/// <summary>Host-owned picker entries. No entry is selected automatically.</summary>
public sealed record ScheduleCatalogue
{
    /// <summary>Record types.</summary>
    public IReadOnlyList<ScheduleOption> Types { get; init; } = [];
    /// <summary>Optional start-target catalogue; null permits an explicit reference.</summary>
    public IReadOnlyList<ScheduleOption>? Starts { get; init; }
    /// <summary>Legality citations.</summary>
    public IReadOnlyList<ScheduleOption> Legality { get; init; } = [];
    /// <summary>Registered solvers.</summary>
    public IReadOnlyList<ScheduleBackend> Backends { get; init; } = [];
    /// <summary>Admitted modules.</summary>
    public IReadOnlyList<ScheduleModule> Modules { get; init; } = [];
}
/// <summary>A server refusal, displayed without reinterpretation.</summary>
public sealed record ScheduleRefusal(string Code, string Pointer, JsonNode? Detail = null, string? Target = null);
