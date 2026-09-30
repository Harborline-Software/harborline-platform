namespace Harborline.UIAdapters.Blazor.Components.DataExchange;

/// <summary>A selectable option in the data exchange authoring form: id and label.</summary>
public sealed record DataExchangeOption(string Id, string Label)
{
    /// <summary>A bare catalogue id is also its label (ADR 0096 option shapes).</summary>
    public static implicit operator DataExchangeOption(string id) => new(id, id);
}
/// <summary>A column found in the source and whether it is selected for import.</summary>
public sealed record DiscoveredSourceColumn(string Name, bool Selected);
/// <summary>One column mapping: source column, canonical target, datatype, whether required, null and default values, separator and transform.</summary>
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

/// <summary>The draft of a data exchange being authored: source, connector, mappings, key columns, replay policy and schedule.</summary>
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
    /// <summary>Server identity of the persisted definition; empty for a new draft.</summary>
    public string Identity { get; init; } = "";
    /// <summary>Server revision the draft was loaded from; a change is a revision change.</summary>
    public string ExpectedRevision { get; init; } = "";
    /// <summary>The file format of the source, csv by default.</summary>
    public string FormatCapability { get; init; } = "csv";
    /// <summary>The reference dataset the exchange reads or checks against.</summary>
    public string ReferenceDataset { get; init; } = "";
    /// <summary>The pack distribution the exchange feeds into.</summary>
    public string PackDistribution { get; init; } = "";
    /// <summary>The feed distribution the exchange publishes to.</summary>
    public string FeedDistribution { get; init; } = "";

    /// <summary>An empty draft with the default append replay policy.</summary>
    public static DataExchangeAuthoringDraft Empty { get; } = new("", "", "", "", [], [], [], "append", "");
}

/// <summary>The options the authoring form offers: source capabilities, canonical targets, datatypes, transforms and schedules.</summary>
public sealed record DataExchangeAuthoringCatalogue(
    IReadOnlyList<DataExchangeOption> SourceCapabilities,
    IReadOnlyList<DataExchangeOption> CanonicalTargets,
    IReadOnlyList<DataExchangeOption> Datatypes,
    IReadOnlyList<DataExchangeOption> Transforms,
    IReadOnlyList<DataExchangeOption> Schedules)
{
    /// <summary>The file formats offered by the form, csv by default.</summary>
    public IReadOnlyList<DataExchangeOption> Formats { get; init; } = [new("csv", "CSV")];
}

/// <summary>Counts of rows by outcome in a dry run: applied, skipped, conflicted, rejected, failed and halted.</summary>
public sealed record DataExchangeRunCensus(int Applied, int Skipped, int Conflicted, int Rejected, int Failed, int Halted);
/// <summary>The result of a dry run: its id, status, staleness, checkpoint, row census and refusals.</summary>
public sealed record DataExchangeRunSummary(
    string DryRunId,
    string Status,
    bool Stale,
    string CandidateCheckpoint,
    DataExchangeRunCensus Census,
    IReadOnlyList<string> Refusals,
    string? BatchIdentity = null);

/// <summary>A refusal from the authoring flow: the stage it happened at, its code and where to go to fix it.</summary>
public sealed record DataExchangeAuthoringRefusal(string Stage, string Code, string TargetHref);
