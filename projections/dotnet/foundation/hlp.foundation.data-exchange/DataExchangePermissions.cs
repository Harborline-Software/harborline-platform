namespace Harborline.Foundation.DataExchange;

/// <summary>Closed capability names for the Data Exchange boundary; target writes remain independently gated.</summary>
public static class DataExchangePermissions
{
    /// <summary>Permission to author exchange definitions.</summary>
    public const string Author = "data-exchange:author";
    /// <summary>Permission to create dry-run reviews.</summary>
    public const string DryRun = "data-exchange:dry-run";
    /// <summary>Permission to commit an approved dry run.</summary>
    public const string Commit = "data-exchange:commit";
    /// <summary>Permission to read run results.</summary>
    public const string ReadRunResults = "data-exchange:read-run-results";

    /// <summary>Every Data Exchange permission, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } = [Author, DryRun, Commit, ReadRunResults];
}
