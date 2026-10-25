using System.Text.Json.Nodes;

namespace Harborline.UIAdapters.Blazor.Components.RecordsAuthoring;

/// <summary>
/// The one projection-neutral Records draft: host-supplied lifecycle metadata around the current producer Record Type
/// body (RecordTypeDocument, snake_case). The body is carried as JSON so every member the editor does not edit, known or
/// unknown, is preserved as given. <see cref="ExpectedRevision"/> is the producer revision as the host transports it: a
/// canonical ASCII decimal Int64 string (never a number, so values above 2^53 survive), or null for none. The editor carries it
/// byte-exactly and never parses, rounds, increments or defaults it.
/// </summary>
public sealed record RecordsDraft(string Identity, string VersionId, string? ExpectedRevision, string? DefinitionKind, JsonObject Body)
{
    /// <summary>A new empty draft: no identity, no revision, no definition kind and an empty body.</summary>
    public static RecordsDraft Empty => new("", "", null, null, new JsonObject());
}

/// <summary>A field kind and version the host offers. The editor never selects one on its own.</summary>
public sealed record RecordsKindOption(string KindId, string Version);

/// <summary>A producer diagnostic rendered as given: a stable code at an RFC 6901 pointer.</summary>
public sealed record RecordsDiagnostic(string Code, string Pointer)
{
    /// <summary>The producer's target, such as a cross-package pin's endpoint; null or empty when the refusal names none.</summary>
    public string? Target { get; init; }
}

/// <summary>A producer warning rendered as given: a stable code at an RFC 6901 pointer. It never blocks and is never a refusal.</summary>
public sealed record RecordsWarning(string Code, string Pointer);

/// <summary>Host-supplied diagnostics for one exact draft identity, revision and local generation.</summary>
public sealed record RecordsDiagnosticSet(string Identity, string? ExpectedRevision, int Generation, IReadOnlyList<RecordsDiagnostic> Diagnostics)
{
    /// <summary>The producer's publication-warning preview for the same draft, shown apart from the diagnostics.</summary>
    public IReadOnlyList<RecordsWarning> Warnings { get; init; } = [];
}

/// <summary>A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision. Local state only; nothing is persisted.</summary>
public sealed record RecordsDraftChange(RecordsDraft Draft, int Generation);

/// <summary>
/// The disabled host contract's revision grammar (RecordsDraftHostContract.cs:56-57,72-99): <c>0</c> or <c>[1-9][0-9]*</c> in
/// ASCII, at most 9223372036854775807. Checked on the text alone, so no <see cref="System.Globalization.NumberStyles"/>,
/// culture or trimming can widen it. A host value that fails is rejected, never repaired.
/// </summary>
internal static class RecordsRevisionText
{
    private const string Largest = "9223372036854775807";

    /// <summary>True only for a canonical decimal revision string.</summary>
    internal static bool IsCanonical(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > Largest.Length) return false;
        foreach (var c in text)
        {
            if (c is < '0' or > '9') return false;
        }

        if (text.Length > 1 && text[0] == '0') return false;
        // Equal-length strings of ASCII digits without a leading zero order exactly as their values do.
        return text.Length < Largest.Length || string.CompareOrdinal(text, Largest) <= 0;
    }

    /// <summary>A host revision is usable only when it is absent (null) or canonical.</summary>
    internal static bool IsAcceptable(string? revision) => revision is null || IsCanonical(revision);
}
