using System.Text.Json.Nodes;

namespace Harborline.UIAdapters.Blazor.Components.SchedulingAuthoring;

/// <summary>
/// The one projection-neutral Schedule draft (T-491/schedule-editor): host-supplied lifecycle metadata around the producer body, a
/// ScheduleDefinition in its snake_case grammar (DES-0022; ScheduleDefinitionAdmission). <c>envelope</c> is host-owned and carried
/// unchanged. <see cref="ExpectedRevision"/> is a canonical ASCII decimal Int64 string carried byte-exactly and never parsed.
/// </summary>
public sealed record SchedulingDraft(string Identity, string VersionId, string? ExpectedRevision, JsonObject Body)
{
    /// <summary>The draft the create action starts from: no identity, no revision and an empty body.</summary>
    public static SchedulingDraft Empty => new("", "", null, new JsonObject());
}

/// <summary>A host option: a labelled id, or a bare id that is also its label (the shared option shapes).</summary>
public sealed record SchedulingOption(string Id, string Label)
{
    /// <summary>A bare id is its own label.</summary>
    public static implicit operator SchedulingOption(string id) => new(id, id);
}

/// <summary>A registered solver backend and the guarantees it declares (the solver catalogue); only these guarantees are offered for it.</summary>
public sealed record SchedulingBackendOption(string Id, string Label, IReadOnlyList<string> Guarantees);

/// <summary>
/// What the host offers; the editor never chooses an option on its own, and a current value the host does not offer is shown, not
/// rewritten. Types: record types a Schedule runs on (scheduling-auth-1). Forms, Workflows, WorkItems: what a Schedule starts
/// (scheduling-auth-12). LegalityRuleSets: legality rule sets cited by id (scheduling-auth-13). Backends: registered solver backends
/// (scheduling-bound-1, -4).
/// </summary>
public sealed record SchedulingCatalogue
{
    /// <summary>Record types a Schedule runs on.</summary>
    public IReadOnlyList<SchedulingOption> Types { get; init; } = [];
    /// <summary>Forms a Schedule may start.</summary>
    public IReadOnlyList<SchedulingOption> Forms { get; init; } = [];
    /// <summary>Workflows a Schedule may start.</summary>
    public IReadOnlyList<SchedulingOption> Workflows { get; init; } = [];
    /// <summary>Work items a Schedule may start.</summary>
    public IReadOnlyList<SchedulingOption> WorkItems { get; init; } = [];
    /// <summary>Legality rule sets a Schedule may cite.</summary>
    public IReadOnlyList<SchedulingOption> LegalityRuleSets { get; init; } = [];
    /// <summary>Registered solver backends with their declared guarantees.</summary>
    public IReadOnlyList<SchedulingBackendOption> Backends { get; init; } = [];
}

/// <summary>A producer admission refusal rendered as given: a stable code at an RFC 6901 pointer.</summary>
public sealed record SchedulingDiagnostic(string Code, string Pointer);

/// <summary>Host-supplied refusals for one exact draft identity, revision and local generation; any mismatch is stale and never shown.</summary>
public sealed record SchedulingDiagnosticSet(string Identity, string? ExpectedRevision, int Generation, IReadOnlyList<SchedulingDiagnostic> Diagnostics);

/// <summary>A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision.</summary>
public sealed record SchedulingDraftChange(SchedulingDraft Draft, int Generation);

/// <summary>The canonical revision grammar: "0" or [1-9][0-9]* in ASCII, at most Int64.MaxValue, checked on the text alone.</summary>
public static class SchedulingRevisionText
{
    private const string Largest = "9223372036854775807";

    /// <summary>True for "0" or [1-9][0-9]* no greater than Int64.MaxValue, compared as text.</summary>
    public static bool IsCanonical(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > Largest.Length) return false;
        foreach (var character in text) if (character is < '0' or > '9') return false;
        if (text.Length > 1 && text[0] == '0') return false;
        return text.Length < Largest.Length || string.CompareOrdinal(text, Largest) <= 0;
    }

    /// <summary>Absent (null) or canonical. Anything else is rejected, never repaired.</summary>
    public static bool IsAcceptable(string? text) => text is null || IsCanonical(text);
}
