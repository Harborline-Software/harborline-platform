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
    public static DataExchangeAuthoringDraft Empty { get; } = new("", "", "", "", [], [], [], "idempotent", "");
}

public sealed record DataExchangeAuthoringCatalogue(
    IReadOnlyList<DataExchangeOption> SourceCapabilities,
    IReadOnlyList<DataExchangeOption> CanonicalTargets,
    IReadOnlyList<DataExchangeOption> Datatypes,
    IReadOnlyList<DataExchangeOption> Transforms,
    IReadOnlyList<DataExchangeOption> Schedules);

public sealed record DataExchangeRunCensus(int Applied, int Skipped, int Conflicted, int Rejected, int Failed, int Halted);
public sealed record DataExchangeRunSummary(
    string DryRunId,
    string Status,
    bool Stale,
    string CandidateCheckpoint,
    DataExchangeRunCensus Census,
    IReadOnlyList<string> Refusals);
