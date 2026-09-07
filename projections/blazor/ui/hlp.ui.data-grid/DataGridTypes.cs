using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public enum DataGridValueKind { Text, Number, Date, Boolean }

/// <summary>A grid column with a required integer removal priority; lower priorities are removed first.</summary>
public sealed record DataGridColumn<TRow>(
    string Id,
    string Header,
    Func<TRow, object?> Value,
    int RemovalPriority,
    DataGridValueKind ValueKind = DataGridValueKind.Text,
    RenderFragment<TRow>? CellTemplate = null);

public sealed record DataGridChildren<TRow>(int Count, string State, IReadOnlyList<TRow> Children);

/// <summary>
/// A lazy branch's outstanding request completes only on a distinguishable host response: the
/// observable snapshot VALUE — state plus the ordered canonical child keys — must differ from the
/// snapshot observed when the request started. Object identity, reference equality and
/// re-materialised-but-equal snapshots are never distinguishable, and Count is deliberately
/// excluded (a count refresh is not a response). Structural, not deep equality: GetRowId is the
/// component's canonical content key for a row.
/// </summary>
public static class DataGridChildrenSnapshot
{
    public static bool CompletesRequest<TRow>(
        DataGridChildren<TRow> snapshot,
        DataGridChildren<TRow> observed,
        Func<TRow, string> getRowId)
        => CompletesRequest(snapshot, Key(observed, getRowId), getRowId);

    /// <summary>
    /// The component remembers the KEY observed when the request started, never the snapshot
    /// object: a host that mutates the list it already handed over would otherwise alias the
    /// observed snapshot and the request could never complete (review 3 of ticket 230 slice 2).
    /// </summary>
    public static bool CompletesRequest<TRow>(
        DataGridChildren<TRow> snapshot,
        string observedKey,
        Func<TRow, string> getRowId)
        => snapshot.State is "loaded" or "failed" &&
           !string.Equals(Key(snapshot, getRowId), observedKey, StringComparison.Ordinal);

    public static string Key<TRow>(DataGridChildren<TRow> snapshot, Func<TRow, string> getRowId)
        => snapshot.State + "" + string.Join("", snapshot.Children.Select(getRowId));

    private static bool SameSnapshot<TRow>(
        DataGridChildren<TRow> snapshot,
        DataGridChildren<TRow> observed,
        Func<TRow, string> getRowId)
        => string.Equals(snapshot.State, observed.State, StringComparison.Ordinal) &&
           snapshot.Children.Count == observed.Children.Count &&
           snapshot.Children.Select(getRowId).SequenceEqual(observed.Children.Select(getRowId), StringComparer.Ordinal);
}
public sealed record DataGridChildrenRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("rowId")] string RowId);

/// <summary>drilldown-model.md row 1: a commit is a declared host action; the grid never navigates.</summary>
public sealed record DataGridRowActivation(
    [property: System.Text.Json.Serialization.JsonPropertyName("rowId")] string RowId);

/// <summary>
/// The grid's one list-state seam (conformance/hlp.ui.data-grid/selection-v1.json). Selection,
/// scroll offset and measured column widths travel in and out as a single object so a host that
/// closes an inspector can hand the very same object back on remount (L1701). The grid never
/// publishes selection through a second channel. SelectedRowId is drilldown-model.md row 0's
/// peeked row, remembered by id and never by row object; ScrollTop is the preserved offset in CSS
/// pixels; ColumnWidths is the last measured width per visible column.
/// </summary>
public sealed record DataGridListState(
    string? SelectedRowId,
    double ScrollTop,
    IReadOnlyDictionary<string, double> ColumnWidths)
{
    public static readonly DataGridListState Empty = new(null, 0, new Dictionary<string, double>(StringComparer.Ordinal));

    /// <summary>Compared BY VALUE: a re-materialised equal widths map is not a change.</summary>
    public static bool SameWidths(IReadOnlyDictionary<string, double> left, IReadOnlyDictionary<string, double> right)
        => left.Count == right.Count && left.All(entry => right.TryGetValue(entry.Key, out var width) && width == entry.Value);

    /// <summary>A remount renders the retained column set on its FIRST render, before measuring.</summary>
    public int? RestoredWidth => ColumnWidths.Count == 0 ? null : (int)ColumnWidths.Values.Sum();
}
