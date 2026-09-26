using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.RuleEngine.References;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Identifies the definition layer that supplied a Layout revision.</summary>
public enum LayoutCascadeLayer
{
    /// <summary>The kernel-owned baseline.</summary>
    KernelCore,
    /// <summary>A platform subsystem baseline.</summary>
    Subsystem,
    /// <summary>A platform package contribution.</summary>
    PlatformPackage,
    /// <summary>A domain package contribution.</summary>
    DomainPackage,
    /// <summary>A tenant-owned configuration contribution.</summary>
    TenantConfiguration,
}

/// <summary>Identifies the presentation medium governed by a Layout definition.</summary>
public enum LayoutMedium
{
    /// <summary>An interactive screen surface.</summary>
    Screen,
    /// <summary>A paged document surface.</summary>
    Page,
}

/// <summary>Identifies the semantic purpose of a Layout block.</summary>
public enum LayoutIntent
{
    /// <summary>Captures member input.</summary>
    Capture,
    /// <summary>Presents member data for observation.</summary>
    Observe,
    /// <summary>Issues a governed document.</summary>
    Issue,
}

/// <summary>Identifies the structure used to arrange a block's children.</summary>
public enum LayoutContainerKind
{
    /// <summary>Places children along one axis.</summary>
    Stack,
    /// <summary>Wraps children through available inline space.</summary>
    Flow,
    /// <summary>Places children in named regions.</summary>
    Areas,
}

/// <summary>Identifies the primary container axis.</summary>
public enum LayoutAxis
{
    /// <summary>The writing mode's inline axis.</summary>
    Inline,
    /// <summary>The writing mode's block axis.</summary>
    Block,
}

/// <summary>Identifies whether a container may wrap children.</summary>
public enum LayoutWrap
{
    /// <summary>Allows wrapping.</summary>
    Wrap,
    /// <summary>Keeps children on one run.</summary>
    NoWrap,
}

/// <summary>Identifies tokenized alignment.</summary>
public enum LayoutAlignment
{
    /// <summary>Aligns to the start edge.</summary>
    Start,
    /// <summary>Aligns to the center.</summary>
    Center,
    /// <summary>Aligns to the end edge.</summary>
    End,
    /// <summary>Stretches through the available track.</summary>
    Stretch,
}

/// <summary>Identifies tokenized member sizing.</summary>
public enum LayoutSizing
{
    /// <summary>Sizes to content.</summary>
    Hug,
    /// <summary>Fills available space.</summary>
    Fill,
    /// <summary>Uses the component's governed fixed size.</summary>
    Fixed,
}

/// <summary>Identifies a container collapse threshold.</summary>
public enum LayoutCollapseToken
{
    /// <summary>The small container threshold.</summary>
    Sm,
    /// <summary>The medium container threshold.</summary>
    Md,
    /// <summary>The large container threshold.</summary>
    Lg,
}

/// <summary>Identifies governed visual density.</summary>
public enum LayoutDensity
{
    /// <summary>Uses comfortable density.</summary>
    Comfortable,
    /// <summary>Uses compact density.</summary>
    Compact,
}

/// <summary>Identifies whether a page block participates in document flow.</summary>
public enum LayoutFlowRole
{
    /// <summary>Participates in normal page flow.</summary>
    Flow,
    /// <summary>Occupies a page master's static region.</summary>
    Static,
}

/// <summary>Identifies page-breaking behavior inside a block.</summary>
public enum LayoutBreakInside
{
    /// <summary>Uses the renderer's normal page-break behavior.</summary>
    Auto,
    /// <summary>Requests that the renderer avoid a page break.</summary>
    AvoidPage,
}

/// <summary>Identifies page orientation.</summary>
public enum LayoutPageOrientation
{
    /// <summary>Portrait orientation.</summary>
    Portrait,
    /// <summary>Landscape orientation.</summary>
    Landscape,
}

/// <summary>Declares a platform capability required by a Layout revision.</summary>
/// <param name="Capability">The stable capability identifier.</param>
/// <param name="MinimumPlatformVersion">The optional minimum platform version.</param>
public sealed record LayoutDefinitionRequirement(
    string Capability,
    string? MinimumPlatformVersion = null);

/// <summary>Carries identity, tenancy, provenance, retention, and capability metadata.</summary>
/// <param name="Identity">The stable definition identifier.</param>
/// <param name="Version">The immutable semantic revision.</param>
/// <param name="Tenant">The owning tenant identifier.</param>
/// <param name="CascadeLayer">The contributing cascade layer.</param>
/// <param name="Provenance">The producer-supplied provenance document.</param>
/// <param name="RetentionClass">The governed retention class.</param>
/// <param name="LegalHold">Whether legal hold applies.</param>
/// <param name="Requires">The required platform capabilities.</param>
public sealed record LayoutDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    LayoutCascadeLayer CascadeLayer,
    JsonElement Provenance,
    string RetentionClass,
    bool LegalHold,
    IReadOnlyList<LayoutDefinitionRequirement> Requires);

/// <summary>Describes the tokenized arrangement of a block's children.</summary>
/// <param name="Kind">The container structure.</param>
/// <param name="Axis">The primary axis.</param>
/// <param name="Wrap">The wrapping behavior.</param>
/// <param name="ColumnCount">The governed column count.</param>
/// <param name="Gap">The governed gap token.</param>
/// <param name="CollapseBelow">The optional container collapse threshold.</param>
/// <param name="Density">The optional density token.</param>
/// <param name="Regions">The optional named regions.</param>
/// <param name="JustifyItems">The default inline alignment.</param>
/// <param name="AlignItems">The default block alignment.</param>
public sealed record LayoutContainer(
    LayoutContainerKind Kind,
    LayoutAxis Axis = LayoutAxis.Block,
    LayoutWrap Wrap = LayoutWrap.NoWrap,
    int ColumnCount = 1,
    int Gap = 0,
    LayoutCollapseToken? CollapseBelow = null,
    LayoutDensity? Density = null,
    IReadOnlyList<string>? Regions = null,
    LayoutAlignment JustifyItems = LayoutAlignment.Stretch,
    LayoutAlignment AlignItems = LayoutAlignment.Stretch);

/// <summary>Describes a member's tokenized placement inside its parent container.</summary>
/// <param name="Zone">The optional named region.</param>
/// <param name="Width">The width behavior.</param>
/// <param name="Height">The height behavior.</param>
/// <param name="Span">The governed track span, or <see langword="null"/> when the member states none (auto placement). Absent, an explicit 1, and <c>fill</c> are distinct states.</param>
/// <param name="Grow">The governed growth weight.</param>
/// <param name="JustifySelf">The optional inline override.</param>
/// <param name="AlignSelf">The optional block override.</param>
/// <param name="PixelPosition">A diagnostic-only field that admission always refuses.</param>
public sealed record LayoutPlacement(
    string? Zone = null,
    LayoutSizing Width = LayoutSizing.Hug,
    LayoutSizing Height = LayoutSizing.Hug,
    int? Span = null,
    int Grow = 0,
    LayoutAlignment? JustifySelf = null,
    LayoutAlignment? AlignSelf = null,
    string? PixelPosition = null);

/// <summary>Names the immutable business or presentation source consumed by a block.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "binding_kind")]
[JsonDerivedType(typeof(LayoutRecordFieldBinding), "record_field")]
[JsonDerivedType(typeof(LayoutQueryBinding), "query")]
[JsonDerivedType(typeof(LayoutMeasureBinding), "measure")]
[JsonDerivedType(typeof(LayoutTemplateBinding), "template")]
[JsonDerivedType(typeof(LayoutStaticBinding), "static")]
[JsonDerivedType(typeof(LayoutTextBinding), "text")]
public abstract record LayoutBinding;

/// <summary>Binds a block to a business record field.</summary>
/// <param name="FieldPath">The stable field path.</param>
public sealed record LayoutRecordFieldBinding(string FieldPath) : LayoutBinding;

/// <summary>Binds a block to a released query view definition.</summary>
/// <param name="ViewDefinitionId">The released view definition identifier.</param>
public sealed record LayoutQueryBinding(string ViewDefinitionId) : LayoutBinding;

/// <summary>Binds a block to a governed measure.</summary>
/// <param name="MeasurePath">The stable measure path.</param>
public sealed record LayoutMeasureBinding(string MeasurePath) : LayoutBinding;

/// <summary>Binds a page block to a released template definition.</summary>
/// <param name="TemplateDefinitionId">The released template identifier.</param>
public sealed record LayoutTemplateBinding(string TemplateDefinitionId) : LayoutBinding;

/// <summary>Binds a block to immutable static content.</summary>
/// <param name="Content">The static JSON value.</param>
public sealed record LayoutStaticBinding(JsonElement Content) : LayoutBinding;

/// <summary>
/// DES-0052 layout-ck-43 — a line of text composed of runs, in order: literal runs and record-field
/// runs (the successor of DES-0021 <c>documents-ck-10</c>). The runtime composes it; it is output,
/// so a capture block cannot bind it.
/// </summary>
/// <param name="Runs">The ordered runs.</param>
public sealed record LayoutTextBinding(IReadOnlyList<LayoutTextRun> Runs) : LayoutBinding;

/// <summary>
/// One run of a <see cref="LayoutTextBinding"/>: exactly one of a literal <see cref="Text"/> or a
/// record <see cref="FieldPath"/>. A field run may carry a <see cref="Fallback"/> literal shown when
/// its value is absent, meaning null or missing and nothing else: an empty string is a value
/// (layout-ck-44, the successor of DES-0021 <c>documents-ck-13</c>).
/// </summary>
/// <param name="Text">The literal text of a literal run.</param>
/// <param name="FieldPath">The record field a field run reads.</param>
/// <param name="Fallback">The literal a field run shows when its value is absent.</param>
public sealed record LayoutTextRun(string? Text = null, string? FieldPath = null, string? Fallback = null)
{
    /// <summary>Whether the run is exactly one literal or one field, with a fallback only on a field.</summary>
    [JsonIgnore]
    public bool IsWellFormed => Text is not null
        ? FieldPath is null && Fallback is null
        : !string.IsNullOrWhiteSpace(FieldPath);
}

/// <summary>Describes capture behavior without embedding control-specific state.</summary>
/// <param name="Required">An override that adds a requirement (<see langword="true"/>). Omitted is no override, so Records' own
/// requirement stands; an explicit <see langword="false"/> on a field Records requires is refused (layout-auth-29, T-724 ruling 78).</param>
/// <param name="ValidationRules">The stable validation rule identifiers.</param>
/// <param name="PromptOverride">The optional governed prompt override.</param>
/// <param name="Control">The optional registered field control and its parameters (layout-bound-3).</param>
public sealed record LayoutCaptureProperties(
    bool? Required,
    IReadOnlyList<string> ValidationRules,
    string? PromptOverride = null,
    LayoutFieldControl? Control = null);

/// <summary>
/// DES-0052 layout-bound-3 — a registered field control picked for a capture block's field and
/// parameterised. The control is developer-supplied; the author only names and configures it.
/// </summary>
/// <param name="Id">The control's identifier in the host's <see cref="LayoutFieldControlRegistry"/>.</param>
/// <param name="Parameters">The optional parameters, a JSON object the control interprets.</param>
public sealed record LayoutFieldControl(string Id, JsonElement? Parameters = null);

/// <summary>Pins an embedded form to both its stable identity and immutable version.</summary>
/// <param name="FormDefinitionId">The stable form definition identifier.</param>
/// <param name="FormVersionId">The immutable form version identifier.</param>
public sealed record LayoutFormReference(
    string FormDefinitionId,
    string FormVersionId);

/// <summary>
/// The inclusive row-count bounds a repeating block narrows its collection to (DES-0052
/// layout-ck-40). Runtime data outside them refuses; it is never truncated.
/// </summary>
/// <param name="Minimum">The inclusive minimum row count.</param>
/// <param name="Maximum">The inclusive maximum row count, or <see langword="null"/> for no upper bound.</param>
public sealed record LayoutCollectionBounds(int Minimum, int? Maximum = null)
{
    /// <summary>Whether <paramref name="count"/> rows lie inside the bounds.</summary>
    public bool Contains(int count) => count >= Minimum && (Maximum is null || count <= Maximum);
}

/// <summary>
/// DES-0052 layout-ck-29 — a block's guard. It holds exactly one of <see cref="Expression"/> (Rules
/// text, compiled at publish, T-724 ruling 39) or <see cref="Predicate"/> (a Rules named predicate
/// by its exact pin, resolved from the definition's pinned closure, rules-ck-22). An absent guard
/// means the block always shows; a declared guard holding neither form or both refuses, and one
/// that cannot be evaluated withholds its block.
/// </summary>
/// <param name="Expression">The Rules expression text.</param>
/// <param name="Predicate">The exact pin of a named predicate.</param>
public sealed record LayoutShowWhen(string? Expression = null, ExactPin? Predicate = null)
{
    /// <summary>Whether the guard holds exactly one form.</summary>
    [JsonIgnore]
    public bool IsWellFormed => string.IsNullOrWhiteSpace(Expression) != (Predicate is null);
}

/// <summary>Represents one ordered node in the Layout definition tree.</summary>
/// <param name="Id">The definition-local block identifier.</param>
/// <param name="Kind">The released component kind.</param>
/// <param name="Binding">The block's semantic binding.</param>
/// <param name="Children">The ordered child blocks.</param>
/// <param name="Intent">The optional intent override.</param>
/// <param name="Container">The optional child container.</param>
/// <param name="Placement">The optional parent-relative placement.</param>
/// <param name="FlowRole">The page flow role.</param>
/// <param name="StaticRegion">The optional page-master region.</param>
/// <param name="BreakBefore">Whether to request a break before the block.</param>
/// <param name="BreakAfter">Whether to request a break after the block.</param>
/// <param name="BreakInside">The inside-break behavior.</param>
/// <param name="Repeating">Whether the container repeats its children in per-row scopes.</param>
/// <param name="RelatedRelationship">The optional related-record relationship.</param>
/// <param name="ShowWhen">The optional guard (layout-ck-29). Absent, the block always shows.</param>
/// <param name="Capture">The optional capture properties.</param>
/// <param name="DefaultSelection">The optional immutable default selection.</param>
/// <param name="FilterTargets">The optional local block filter targets.</param>
/// <param name="Form">The optional immutable form reference.</param>
/// <param name="LiveSelection">A diagnostic-only field that admission always refuses.</param>
/// <param name="CollectionBounds">The optional row-count bounds a repeating block narrows to.</param>
public sealed record LayoutBlock(
    string Id,
    string Kind,
    LayoutBinding Binding,
    IReadOnlyList<LayoutBlock> Children,
    LayoutIntent? Intent = null,
    LayoutContainer? Container = null,
    LayoutPlacement? Placement = null,
    LayoutFlowRole FlowRole = LayoutFlowRole.Flow,
    string? StaticRegion = null,
    bool BreakBefore = false,
    bool BreakAfter = false,
    LayoutBreakInside BreakInside = LayoutBreakInside.Auto,
    bool Repeating = false,
    string? RelatedRelationship = null,
    LayoutShowWhen? ShowWhen = null,
    LayoutCaptureProperties? Capture = null,
    JsonElement? DefaultSelection = null,
    IReadOnlyList<string>? FilterTargets = null,
    LayoutFormReference? Form = null,
    JsonElement? LiveSelection = null,
    LayoutCollectionBounds? CollectionBounds = null);

/// <summary>Describes a page layout's four governed margins.</summary>
/// <param name="Top">The top margin token.</param>
/// <param name="Right">The right margin token.</param>
/// <param name="Bottom">The bottom margin token.</param>
/// <param name="Left">The left margin token.</param>
public sealed record LayoutPageMargins(
    string Top,
    string Right,
    string Bottom,
    string Left);

/// <summary>Describes the static header and footer boxes reserved by a page layout.</summary>
/// <param name="HeaderHeight">The header height token.</param>
/// <param name="FooterHeight">The footer height token.</param>
public sealed record LayoutMarginBoxes(
    string HeaderHeight,
    string FooterHeight);

/// <summary>Defines physical page geometry without document content.</summary>
/// <param name="Id">The definition-local page layout identifier.</param>
/// <param name="Sheet">The governed sheet token.</param>
/// <param name="Orientation">The page orientation.</param>
/// <param name="Margins">The page margins.</param>
/// <param name="MarginBoxes">The reserved header and footer boxes.</param>
public sealed record LayoutPageLayoutDefinition(
    string Id,
    string Sheet,
    LayoutPageOrientation Orientation,
    LayoutPageMargins Margins,
    LayoutMarginBoxes MarginBoxes);

/// <summary>Assigns static regions for one page-master variant.</summary>
/// <param name="LeftRegion">The optional left region name.</param>
/// <param name="CenterRegion">The optional center region name.</param>
/// <param name="RightRegion">The optional right region name.</param>
public sealed record LayoutMasterVariant(
    string? LeftRegion,
    string? CenterRegion,
    string? RightRegion);

/// <summary>Defines first, left, and right static-region assignments.</summary>
/// <param name="Id">The definition-local page master identifier.</param>
/// <param name="PageLayoutId">The referenced page layout identifier.</param>
/// <param name="First">The first-page variant.</param>
/// <param name="Left">The left-page variant.</param>
/// <param name="Right">The right-page variant.</param>
public sealed record LayoutPageMasterDefinition(
    string Id,
    string PageLayoutId,
    LayoutMasterVariant First,
    LayoutMasterVariant Left,
    LayoutMasterVariant Right);

/// <summary>Assigns ordered flow blocks to a page layout and master.</summary>
/// <param name="Id">The definition-local page run identifier.</param>
/// <param name="PageLayoutId">The referenced page layout identifier.</param>
/// <param name="PageMasterId">The referenced page master identifier.</param>
/// <param name="BlockIds">The ordered flow block identifiers.</param>
public sealed record LayoutPageRun(
    string Id,
    string PageLayoutId,
    string PageMasterId,
    IReadOnlyList<string> BlockIds);

/// <summary>The platform-owned, projection-neutral Layout definition contract.</summary>
/// <param name="Envelope">The immutable definition envelope.</param>
/// <param name="SchemaVersion">The Layout schema revision.</param>
/// <param name="Medium">The screen or page medium.</param>
/// <param name="DefaultIntent">The intent inherited by blocks without an override.</param>
/// <param name="Blocks">The ordered root block tree.</param>
/// <param name="PageLayouts">The page geometry definitions.</param>
/// <param name="PageMasters">The static-region definitions.</param>
/// <param name="PageRuns">The flow-content assignments.</param>
/// <param name="SubmitGate">The optional submit gate: exactly one of a role, a standing or an authorization capability.</param>
/// <param name="DrillThroughTargets">The released drill-through target identifiers.</param>
public sealed record LayoutDefinition(
    LayoutDefinitionEnvelope Envelope,
    int SchemaVersion,
    LayoutMedium Medium,
    LayoutIntent DefaultIntent,
    IReadOnlyList<LayoutBlock> Blocks,
    IReadOnlyList<LayoutPageLayoutDefinition> PageLayouts,
    IReadOnlyList<LayoutPageMasterDefinition> PageMasters,
    IReadOnlyList<LayoutPageRun> PageRuns,
    LayoutSubmitGate? SubmitGate,
    IReadOnlyList<string> DrillThroughTargets);

/// <summary>
/// DES-0052 layout-auth-23 — a capture-dominant surface's submit gate. It names exactly one of a
/// role, a record standing or an authorization capability (T-724 ruling 77); the capability arm
/// resolves through Access's capability register (T-747).
/// </summary>
/// <param name="Role">The role a submitter must hold.</param>
/// <param name="Standing">The standing a submitter must have on the record.</param>
/// <param name="Capability">The authorization capability the submit is gated on.</param>
public sealed record LayoutSubmitGate(
    RoleReference? Role = null,
    RecordStandingReference? Standing = null,
    AuthorizationCapabilityReference? Capability = null)
{
    /// <summary>Whether the gate names exactly one arm.</summary>
    [JsonIgnore]
    public bool IsWellFormed => (Role is null ? 0 : 1) + (Standing is null ? 0 : 1) + (Capability is null ? 0 : 1) == 1;
}
