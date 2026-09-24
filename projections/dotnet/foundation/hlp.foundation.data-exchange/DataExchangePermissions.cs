namespace Harborline.Foundation.DataExchange;

/// <summary>Closed capability names for the Data Exchange boundary; target writes remain independently gated.</summary>
public static class DataExchangePermissions
{
    public const string Author = "data-exchange:author";
    public const string DryRun = "data-exchange:dry-run";
    public const string Commit = "data-exchange:commit";
    public const string ReadRunResults = "data-exchange:read-run-results";

    public static IReadOnlyList<string> All { get; } = [Author, DryRun, Commit, ReadRunResults];
}
