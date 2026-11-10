using System.Text.Json.Nodes;

namespace Harborline.UIAdapters.Blazor.Components.BookingAuthoring;

/// <summary>
/// The one projection-neutral Booking draft (T-606/member-editors): host-supplied lifecycle metadata around the producer body, a
/// Resource or a Bookable in the Booking grammar (DES-0025; BookingDefinitionAdmission). <c>kind</c> is <c>resource</c> or
/// <c>bookable</c>; <c>envelope</c> is host-owned and carried unchanged. <see cref="ExpectedRevision"/> is a canonical ASCII decimal
/// Int64 string carried byte-exactly and never parsed.
/// </summary>
public sealed record BookingDraft(string Identity, string VersionId, string? ExpectedRevision, JsonObject Body)
{
    /// <summary>The placeholder before the host supplies a draft; never shown or reported.</summary>
    public static BookingDraft Empty => new("", "", null, new JsonObject());
}

/// <summary>A host option: a labelled id, or a bare id that is also its label (the shared option shapes).</summary>
public sealed record BookingOption(string Id, string Label)
{
    /// <summary>A bare id is its own label.</summary>
    public static implicit operator BookingOption(string id) => new(id, id);
}

/// <summary>
/// What the host offers; the editor never chooses an option on its own. ResourceTypes: record types bearing the bookable-resource
/// trait (booking-auth-1). BookableTypes: record types a Bookable is offered against (booking-auth-6). Resources: admitted Resources a
/// Bookable may require (booking-auth-7). MaintenanceSources: members of the Resource record's own state that mark it out of service
/// (booking-auth-5); maintenance is read, never authored as blocks.
/// </summary>
public sealed record BookingCatalogue
{
    /// <summary>Record types bearing the bookable-resource trait.</summary>
    public IReadOnlyList<BookingOption> ResourceTypes { get; init; } = [];
    /// <summary>Record types a Bookable is offered against.</summary>
    public IReadOnlyList<BookingOption> BookableTypes { get; init; } = [];
    /// <summary>Admitted Resources a Bookable may require.</summary>
    public IReadOnlyList<BookingOption> Resources { get; init; } = [];
    /// <summary>Resource record state members that mark it out of service.</summary>
    public IReadOnlyList<BookingOption> MaintenanceSources { get; init; } = [];
}

/// <summary>A producer admission refusal rendered as given: a stable code at an RFC 6901 pointer.</summary>
public sealed record BookingDiagnostic(string Code, string Pointer);

/// <summary>Host-supplied refusals for one exact draft identity, revision and local generation; any mismatch is stale and never shown.</summary>
public sealed record BookingDiagnosticSet(string Identity, string? ExpectedRevision, int Generation, IReadOnlyList<BookingDiagnostic> Diagnostics);

/// <summary>A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision.</summary>
public sealed record BookingDraftChange(BookingDraft Draft, int Generation);

/// <summary>The canonical revision grammar: "0" or [1-9][0-9]* in ASCII, at most Int64.MaxValue, checked on the text alone.</summary>
public static class BookingRevisionText
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
