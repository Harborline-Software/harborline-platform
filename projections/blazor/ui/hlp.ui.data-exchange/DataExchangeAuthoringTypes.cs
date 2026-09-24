namespace Harborline.UIAdapters.Blazor.Components.DataExchange;

public sealed record DataExchangeOption(string Id, string Label);
public sealed record DiscoveredSourceColumn(string Name, bool Selected);
public sealed record DataExchangeMappingRow(
    string SourceColumn,
    string CanonicalTarget,
    string TargetPointer,
    string Datatype,
    bool Required,
    string NullValue,
    string DefaultValue,
    string Separator,
    string Transform);

public sealed record DataExchangeAuthoringDraft(
    string Name,
    string SourceCapability,
    string ConnectorVersion,
    string SecretReference,
    IReadOnlyList<DiscoveredSourceColumn> DiscoveredColumns,
    IReadOnlyList<DataExchangeMappingRow> Mappings,
    IReadOnlyList<string> ExternalKeyColumns,
    string ReplayPolicy,
    string ScheduleReference)
{
    public string FormatCapability { get; init; } = "csv";
    public string ReferenceDataset { get; init; } = "";
    public string PackDistribution { get; init; } = "";
    public string FeedDistribution { get; init; } = "";

    public static DataExchangeAuthoringDraft Empty { get; } = new("", "", "", "", [], [], [], "append", "");
}

public sealed record DataExchangeAuthoringCatalogue(
    IReadOnlyList<DataExchangeOption> SourceCapabilities,
    IReadOnlyList<DataExchangeOption> CanonicalTargets,
    IReadOnlyList<DataExchangeOption> Datatypes,
    IReadOnlyList<DataExchangeOption> Transforms,
    IReadOnlyList<DataExchangeOption> Schedules)
{
    public IReadOnlyList<DataExchangeOption> Formats { get; init; } = [new("csv", "CSV")];
}

public sealed record DataExchangeRunCensus(int Applied, int Skipped, int Conflicted, int Rejected, int Failed, int Halted);
public sealed record DataExchangeRunSummary(
    string DryRunId,
    string Status,
    bool Stale,
    string CandidateCheckpoint,
    DataExchangeRunCensus Census,
    IReadOnlyList<string> Refusals,
    string? BatchIdentity = null);

public sealed record DataExchangeAuthoringRefusal(string Stage, string Code, string TargetHref);
