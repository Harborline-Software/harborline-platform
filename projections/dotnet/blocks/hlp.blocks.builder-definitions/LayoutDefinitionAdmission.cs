using System.Text.Json;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.References;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Stable refusal codes emitted by Layout definition admission.</summary>
public static class LayoutDefinitionCodes
{
    /// <summary>A schema-governed number is outside its inclusive range.</summary>
    public const string NumericOutOfRange = "layout.numeric.out_of_range";
    /// <summary>A closed placement or container token is unknown.</summary>
    public const string PlacementTokenUnknown = "layout.placement.token_unknown";
    /// <summary>Pixel positioning is not portable and is forbidden.</summary>
    public const string PixelPlacementForbidden = "layout.placement.pixel_forbidden";
    /// <summary>The binding cannot serve the effective intent.</summary>
    public const string IntentBindingUnsupported = "layout.intent.binding_unsupported";
    /// <summary>Capture intent is forbidden for paged output.</summary>
    public const string CaptureOnPage = "layout.intent.capture_on_page";
    /// <summary>Persisting live selection state is forbidden.</summary>
    public const string LiveSelectionForbidden = "layout.interaction.live_selection_forbidden";
    /// <summary>The definition envelope is incomplete or invalid.</summary>
    public const string EnvelopeInvalid = "layout.definition.envelope_invalid";
    /// <summary>The immutable definition version is malformed.</summary>
    public const string VersionInvalid = "layout.definition.version_invalid";
    /// <summary>The definition contains no root blocks.</summary>
    public const string TreeEmpty = "layout.tree.empty";
    /// <summary>A block identifier is missing.</summary>
    public const string BlockIdInvalid = "layout.block.id_invalid";
    /// <summary>A block identifier is duplicated.</summary>
    public const string BlockIdDuplicate = "layout.block.id_duplicate";
    /// <summary>A component kind is missing.</summary>
    public const string BlockKindInvalid = "layout.block.kind_invalid";
    /// <summary>The host has not registered the requested component kind.</summary>
    public const string BlockKindUnknown = "layout.block.kind_unknown";
    /// <summary>A block with children has no container.</summary>
    public const string BlockChildrenInvalid = "layout.block.children_invalid";
    /// <summary>A semantic binding is missing or malformed.</summary>
    public const string BindingInvalid = "layout.binding.invalid";
    /// <summary>A named placement region is missing or unknown.</summary>
    public const string ZoneUnknown = "layout.placement.zone_unknown";
    /// <summary>A static page region is malformed.</summary>
    public const string StaticRegionInvalid = "layout.page.static_region_invalid";
    /// <summary>Capture properties do not match capture intent.</summary>
    public const string CapturePropertiesInvalid = "layout.capture.properties_invalid";
    /// <summary>An embedded form reference is not immutably pinned.</summary>
    public const string FormReferenceInvalid = "layout.form_reference.invalid";
    /// <summary>An interaction target does not identify a local block.</summary>
    public const string InteractionTargetUnknown = "layout.interaction.target_unknown";
    /// <summary>A page layout, master, or run is malformed.</summary>
    public const string PageDefinitionInvalid = "layout.page.definition_invalid";
    /// <summary>A page run or master reference is unknown.</summary>
    public const string PageReferenceUnknown = "layout.page.reference_unknown";
    /// <summary>A repeating or related block is not a valid scoped container.</summary>
    public const string ScopedContainerInvalid = "layout.block.scoped_container_invalid";
    /// <summary>A submit gate requires a capture-dominant surface.</summary>
    public const string SubmitGateInvalid = "layout.capture.submit_gate_invalid";
    /// <summary>A screen arrangement would require two-dimensional scrolling at 320 CSS pixels.</summary>
    public const string ReflowForbidden = "layout.placement.reflow_forbidden";
    /// <summary>A published payload does not declare Layout's capability with an exact minimum platform version.</summary>
    public const string CapabilityUndeclared = "layout.pack.capability_undeclared";
    /// <summary>This host lacks the sealed capability or is below its minimum platform version.</summary>
    public const string CapabilityUnsupported = "layout.pack.capability_unsupported";
    /// <summary>Collection bounds sit on a non-repeating block, or no row count can satisfy them.</summary>
    public const string CollectionBoundsInvalid = "layout.collection.bounds_invalid";
    /// <summary>A placement states a span beside <c>fill</c>, which already takes the whole run.</summary>
    public const string SpanWithFill = "layout.placement.span_with_fill";
    /// <summary>A capture block names a field control the host has not registered (layout-bound-3).</summary>
    public const string FieldControlUnknown = "layout.capture.control_unknown";
    /// <summary>A field control's parameters do not satisfy the schema it declares (T-724 ruling 38).</summary>
    public const string ControlParametersInvalid = "layout.capture.control_parameters_invalid";
    /// <summary>Publication cannot find the record field a capture block's control is authored for (T-724 ruling 37).</summary>
    public const string CaptureFieldUnknown = "layout.capture.field_unknown";
    /// <summary>An authored control does not accept the field's value kind (T-724 ruling 37).</summary>
    public const string ControlValueKindMismatch = "layout.capture.control_value_kind_mismatch";
    /// <summary>An authored control names a field whose value domain picks its editor (layout-bound-10, T-724 ruling 37).</summary>
    public const string ControlDisplacesValueDomain = "layout.capture.control_displaces_value_domain";
    /// <summary>Two installed packs supply the same page layout or page master id (layout-bound-7, T-724 ruling 40).</summary>
    public const string PageSuppliedTwice = "layout.page.supplied_twice";
    /// <summary>A block's show_when does not compile at its scope (layout-auth-20, T-724 ruling 39).</summary>
    public const string GuardInvalid = "layout.guard.invalid";
    /// <summary>A declared show_when holds neither or both of expression and predicate (layout-ck-29).</summary>
    public const string GuardFormInvalid = "layout.guard.form_invalid";
    /// <summary>A show_when predicate's exact pin does not resolve in the pinned closure (layout-ck-29).</summary>
    public const string GuardUnresolved = "layout.guard.predicate_unresolved";
    /// <summary>A capture block names a validation rule the host has not registered (layout-bound-8).</summary>
    public const string ValidationRuleUnknown = "layout.capture.validation_rule_unknown";
    /// <summary>A named validation rule does not validate, or its tier's compiler refuses it (layout-bound-8).</summary>
    public const string ValidationRuleInvalid = "layout.capture.validation_rule_invalid";
    /// <summary>The acting author could not read the source a binding names (layout-auth-24).</summary>
    public const string BindingUnreadable = "layout.binding.unreadable";
    /// <summary>A filter-propagation edge targets a block whose binding a selection cannot narrow (layout-auth-36).</summary>
    public const string FilterTargetUnnarrowable = "layout.interaction.filter_target_unnarrowable";
    /// <summary>The acting author could not open a drill-through target (layout-auth-36).</summary>
    public const string DrillThroughForbidden = "layout.interaction.drill_through_forbidden";
    /// <summary>A capture block states <c>required: false</c> on a field Records requires (layout-auth-29, T-724 ruling 78).</summary>
    public const string RequirementDropped = "layout.capture.requirement_dropped";
    /// <summary>The surface's envelope does not require the package a cross-package edge targets (layout-auth-30).</summary>
    public const string ReferenceDependencyUndeclared = "layout.reference.dependency_undeclared";
    /// <summary>The producer package does not expose the definition a cross-package edge targets (layout-auth-30).</summary>
    public const string ReferenceNotExposed = "layout.reference.not_exposed";
    /// <summary>The producer exposes the targeted definition at another version or digest (layout-auth-30).</summary>
    public const string ReferenceExposureIncompatible = "layout.reference.exposure_incompatible";
}

/// <summary>
/// One structural validator used by editor validation, publication and render. Every refusal is raised in the
/// shared envelope (<see cref="DefinitionRefusalException"/>, T-724 ruling 61) with the stage that refused stated
/// by the caller, never defaulted, and with every refusal found rather than the first.
/// </summary>
public static class LayoutDefinitionAdmission
{

    /// <summary>Validates a Layout definition during authoring, as <paramref name="author"/>.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="author">The acting author's Access (layout-auth-24, layout-auth-36). Required: a missing port fails rather than skipping the checks (T-724 ruling 75).</param>
    public static void ValidateForAuthoring(LayoutDefinition definition, ILayoutAccess author)
        => ValidateForAuthoring(definition, LayoutHostRegisters.Platform, author);

    /// <summary>Validates using the host's immutable kind register, as <paramref name="author"/>.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="kinds">The host kind register.</param>
    /// <param name="author">The acting author's Access.</param>
    public static void ValidateForAuthoring(LayoutDefinition definition, LayoutBlockKindRegistry kinds, ILayoutAccess author)
        => ValidateForAuthoring(definition, new LayoutHostRegisters(kinds), author);

    /// <summary>Validates during authoring against the host's registers, as <paramref name="author"/>.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="registers">The host's bound registers.</param>
    /// <param name="author">The acting author's Access.</param>
    public static void ValidateForAuthoring(LayoutDefinition definition, LayoutHostRegisters registers, ILayoutAccess author)
    {
        ArgumentNullException.ThrowIfNull(author);
        Validate(definition, DefinitionAdmissionPhase.Author, registers, author);
    }

    /// <summary>Validates a Layout definition before an author publishes it.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="author">The publishing author's Access. Required: a missing port fails rather than skipping the checks (T-724 ruling 75).</param>
    public static void ValidateForPublish(LayoutDefinition definition, ILayoutAccess author)
        => ValidateForPublish(definition, LayoutHostRegisters.Platform, author);

    /// <summary>Admits an author's publication against the host's immutable kind register.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="kinds">The host kind register.</param>
    /// <param name="author">The publishing author's Access.</param>
    public static void ValidateForPublish(LayoutDefinition definition, LayoutBlockKindRegistry kinds, ILayoutAccess author)
        => ValidateForPublish(definition, new LayoutHostRegisters(kinds), author);

    /// <summary>Admits an author's publication against the host's registers.</summary>
    /// <param name="definition">The candidate definition.</param>
    /// <param name="registers">The host's bound registers.</param>
    /// <param name="author">The publishing author's Access.</param>
    public static void ValidateForPublish(LayoutDefinition definition, LayoutHostRegisters registers, ILayoutAccess author)
    {
        ArgumentNullException.ThrowIfNull(author);
        Validate(definition, DefinitionAdmissionPhase.Publish, registers, author);
    }

    // Authorless publication admission: pack export has no acting author, so there is no one whose
    // read to check; the reader's Access is folded in at render instead (layout-eng-15, T-724 ruling 75).
    internal static void ValidateAuthorlessPublish(LayoutDefinition definition, LayoutHostRegisters registers)
        => Validate(definition, DefinitionAdmissionPhase.Publish, registers, author: null);

    internal static void Validate(LayoutDefinition definition, DefinitionAdmissionPhase stage, LayoutBlockKindRegistry? kinds = null)
        => Validate(definition, stage, kinds is null ? LayoutHostRegisters.Platform : new LayoutHostRegisters(kinds), author: null);

    internal static void Validate(LayoutDefinition definition, DefinitionAdmissionPhase stage, LayoutHostRegisters registers, ILayoutAccess? author)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registers);
        var refusals = new List<DefinitionRefusal>();
        ValidateEnvelope(definition, refusals);
        if (stage == DefinitionAdmissionPhase.Publish) ValidateSealedCapability(definition, refusals);

        var blocks = definition.Blocks ?? [];
        if (blocks.Count == 0) Add(refusals, LayoutDefinitionCodes.TreeEmpty, "/blocks");
        var blockIds = new Dictionary<string, LayoutBlock>(StringComparer.Ordinal);
        CollectBlockIds(blocks, "/blocks", blockIds, refusals);
        var captures = HasCapture(blocks, definition.DefaultIntent);

        for (var index = 0; index < blocks.Count; index++)
        {
            ValidateBlock(
                blocks[index],
                $"/blocks/{index}",
                definition.Medium,
                definition.DefaultIntent,
                parentRegions: null,
                blockIds,
                registers,
                author,
                captures,
                publishing: stage == DefinitionAdmissionPhase.Publish,
                rowSection: null,
                refusals);
        }

        var drillTargets = definition.DrillThroughTargets ?? [];
        for (var index = 0; index < drillTargets.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(drillTargets[index]))
                Add(refusals, LayoutDefinitionCodes.InteractionTargetUnknown, $"/drill_through_targets/{index}");
            // layout-auth-36: the author may not route a reader to a surface the author could not open.
            else if (author is not null && !author.CanOpen(drillTargets[index]))
                Add(refusals, LayoutDefinitionCodes.DrillThroughForbidden, $"/drill_through_targets/{index}");
        }
        ValidatePages(definition, blockIds, registers.Pages, refusals);
        if (definition.SubmitGate is { } submitGate
            && (definition.DefaultIntent != LayoutIntent.Capture || string.IsNullOrWhiteSpace(submitGate)))
            Add(refusals, LayoutDefinitionCodes.SubmitGateInvalid, "/submit_gate");

        if (refusals.Count > 0) throw new DefinitionRefusalException(stage, refusals);
    }

    private static bool HasCapture(IReadOnlyList<LayoutBlock> blocks, LayoutIntent defaultIntent)
        => blocks.Any(block => block is not null
            && ((block.Intent ?? defaultIntent) == LayoutIntent.Capture
                || HasCapture(block.Children ?? [], defaultIntent)));

    // layout-ck-42: the signed payload carries Layout's capability and an exact minimum platform
    // version, so a host can refuse the whole pack by name. A draft may omit it; publication seals it.
    private static void ValidateSealedCapability(LayoutDefinition definition, ICollection<DefinitionRefusal> refusals)
    {
        var requirements = definition.Envelope?.Requires;
        var index = LayoutPackIdentity.SealedRequirementIndex(requirements);
        if (index < 0)
            Add(refusals, LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires");
        else if (!LayoutVersionSyntax.IsValid(requirements![index].MinimumPlatformVersion))
            Add(refusals, LayoutDefinitionCodes.CapabilityUndeclared, $"/envelope/requires/{index}/minimum_platform_version");
    }

    private static void ValidateEnvelope(LayoutDefinition definition, ICollection<DefinitionRefusal> refusals)
    {
        var envelope = definition.Envelope;
        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.Identity)
            || string.IsNullOrWhiteSpace(envelope.Tenant)
            || string.IsNullOrWhiteSpace(envelope.RetentionClass))
        {
            Add(refusals, LayoutDefinitionCodes.EnvelopeInvalid, "/envelope");
            return;
        }
        if (!LayoutVersionSyntax.IsValid(envelope.Version))
            Add(refusals, LayoutDefinitionCodes.VersionInvalid, "/envelope/version");
        if (!Enum.IsDefined(envelope.CascadeLayer))
            Add(refusals, LayoutDefinitionCodes.EnvelopeInvalid, "/envelope/cascade_layer");
        if (definition.SchemaVersion != 1)
            Add(refusals, LayoutDefinitionCodes.EnvelopeInvalid, "/schema_version");
        if (envelope.Provenance.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            Add(refusals, LayoutDefinitionCodes.EnvelopeInvalid, "/envelope/provenance");
        var requirements = envelope.Requires ?? [];
        for (var index = 0; index < requirements.Count; index++)
        {
            if (requirements[index] is null || string.IsNullOrWhiteSpace(requirements[index].Capability))
                Add(refusals, LayoutDefinitionCodes.EnvelopeInvalid, $"/envelope/requires/{index}/capability");
        }
        if (!Enum.IsDefined(definition.Medium))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, "/medium");
        if (!Enum.IsDefined(definition.DefaultIntent))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, "/default_intent");
    }

    private static void CollectBlockIds(
        IReadOnlyList<LayoutBlock> blocks,
        string pointer,
        IDictionary<string, LayoutBlock> blockIds,
        ICollection<DefinitionRefusal> refusals)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var blockPointer = $"{pointer}/{index}";
            if (block is null || string.IsNullOrWhiteSpace(block.Id))
                Add(refusals, LayoutDefinitionCodes.BlockIdInvalid, $"{blockPointer}/id");
            else if (!blockIds.TryAdd(block.Id, block))
                Add(refusals, LayoutDefinitionCodes.BlockIdDuplicate, $"{blockPointer}/id");
            if (block?.Children is { } children)
                CollectBlockIds(children, $"{blockPointer}/children", blockIds, refusals);
        }
    }

    private static void ValidateBlock(
        LayoutBlock block,
        string pointer,
        LayoutMedium medium,
        LayoutIntent inheritedIntent,
        IReadOnlySet<string>? parentRegions,
        IReadOnlyDictionary<string, LayoutBlock> blockIds,
        LayoutHostRegisters registers,
        ILayoutAccess? author,
        bool captures,
        bool publishing,
        string? rowSection,
        ICollection<DefinitionRefusal> refusals)
    {
        if (block is null) return;
        if (string.IsNullOrWhiteSpace(block.Kind))
            Add(refusals, LayoutDefinitionCodes.BlockKindInvalid, $"{pointer}/kind");
        else if (!registers.Kinds.Contains(block.Kind))
            Add(refusals, LayoutDefinitionCodes.BlockKindUnknown, $"{pointer}/kind");

        var intent = block.Intent ?? inheritedIntent;
        if (!Enum.IsDefined(intent))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/intent");
        if (medium == LayoutMedium.Page && intent == LayoutIntent.Capture)
            Add(refusals, LayoutDefinitionCodes.CaptureOnPage, $"{pointer}/intent");

        ValidateBinding(block.Binding, intent, medium, captures, $"{pointer}/binding", refusals);
        // layout-auth-24: authority travels with the read, so a block never binds a source its author
        // could not read. Static content and literal runs are read from nowhere and are never asked
        // about; each field run of a text binding is asked exactly as a record-field binding naming it.
        if (author is not null && block.Binding is LayoutTextBinding { Runs: { } runs })
        {
            for (var index = 0; index < runs.Count; index++)
                if (runs[index]?.FieldPath is { Length: > 0 } fieldPath && !author.CanRead(new LayoutRecordFieldBinding(fieldPath)))
                    Add(refusals, LayoutDefinitionCodes.BindingUnreadable, $"{pointer}/binding/runs/{index}/field_path");
        }
        else if (author is not null && block.Binding is not (null or LayoutStaticBinding) && !author.CanRead(block.Binding))
            Add(refusals, LayoutDefinitionCodes.BindingUnreadable, $"{pointer}/binding");
        ValidateContainer(block.Container, medium, $"{pointer}/container", refusals);
        ValidatePlacement(block.Placement, parentRegions, $"{pointer}/placement", refusals);

        if (!Enum.IsDefined(block.FlowRole))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/flow_role");
        if (!Enum.IsDefined(block.BreakInside))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/break_inside");
        if (block.FlowRole == LayoutFlowRole.Static
            && (medium != LayoutMedium.Page || string.IsNullOrWhiteSpace(block.StaticRegion)))
            Add(refusals, LayoutDefinitionCodes.StaticRegionInvalid, $"{pointer}/static_region");
        if (block.FlowRole == LayoutFlowRole.Flow && block.StaticRegion is not null)
            Add(refusals, LayoutDefinitionCodes.StaticRegionInvalid, $"{pointer}/static_region");

        if (block.Capture is not null && intent != LayoutIntent.Capture)
            Add(refusals, LayoutDefinitionCodes.CapturePropertiesInvalid, $"{pointer}/capture");
        if (block.Capture is { } capture)
        {
            var validationRules = capture.ValidationRules ?? [];
            for (var index = 0; index < validationRules.Count; index++)
                if (string.IsNullOrWhiteSpace(validationRules[index]))
                    Add(refusals, LayoutDefinitionCodes.CapturePropertiesInvalid, $"{pointer}/capture/validation_rules/{index}");
                // Publication resolves every name and fails closed without a register (T-724
                // ruling 36); a supplied register is honoured at every stage.
                else if (registers.ValidationRules is not null || publishing)
                    ValidateNamedRule(registers.ValidationRules, validationRules[index], $"{pointer}/capture/validation_rules/{index}", refusals);
            // layout-auth-29 (T-724 ruling 78): a surface adds a requirement and never drops one Records
            // declared. Omission is no override, so only an explicit false is checked, and publication
            // fails closed when it cannot look the field up.
            if (capture.Required == false && (registers.Fields is not null || publishing))
            {
                var field = block.Binding is LayoutRecordFieldBinding bound ? registers.Fields?.Find(bound.FieldPath) : null;
                if (field is null && publishing)
                    Add(refusals, LayoutDefinitionCodes.CaptureFieldUnknown, $"{pointer}/capture/required");
                else if (field?.Required == true)
                    Add(refusals, LayoutDefinitionCodes.RequirementDropped, $"{pointer}/capture/required");
            }
            // layout-bound-3: the control is one the host registered; with no register, none is.
            if (capture.Control is { } control)
            {
                if (string.IsNullOrWhiteSpace(control.Id) || registers.FieldControls?.Contains(control.Id) != true)
                    Add(refusals, LayoutDefinitionCodes.FieldControlUnknown, $"{pointer}/capture/control");
                else
                {
                    if (control.Parameters is { } parameters && !registers.FieldControls.AcceptsParameters(control.Id, parameters))
                        Add(refusals, LayoutDefinitionCodes.ControlParametersInvalid, $"{pointer}/capture/control/parameters");
                    if (publishing) ValidateControlField(registers, control, block.Binding, $"{pointer}/capture/control", refusals);
                }
            }
        }

        if (block.Form is { } form
            && (string.IsNullOrWhiteSpace(form.FormDefinitionId)
                || string.IsNullOrWhiteSpace(form.FormVersionId)
                || form.FormVersionId.Contains("latest", StringComparison.OrdinalIgnoreCase)))
            Add(refusals, LayoutDefinitionCodes.FormReferenceInvalid, $"{pointer}/form");
        // layout-ck-29: a declared guard holds exactly one form, at every stage. T-724 ruling 39:
        // publication compiles it as the runtime will, so a malformed one refuses here instead of
        // withholding its block on every render. A predicate resolves through the pinned closure;
        // with none supplied it resolves nothing (T-724 ruling 36's pattern).
        if (block.ShowWhen is { } guard)
        {
            if (!guard.IsWellFormed)
                Add(refusals, LayoutDefinitionCodes.GuardFormInvalid, $"{pointer}/show_when");
            else if (publishing || (guard.Predicate is not null && registers.Predicates is not null))
            {
                try
                {
                    RuleCompiler.Compile([LayoutGuardRule.For(block.Id, guard, rowSection, registers.Predicates)]);
                }
                catch (NamedReferenceException)
                {
                    Add(refusals, LayoutDefinitionCodes.GuardUnresolved, $"{pointer}/show_when/predicate");
                }
                catch (RuleCompilationException)
                {
                    Add(refusals, LayoutDefinitionCodes.GuardInvalid, $"{pointer}/show_when");
                }
            }
        }
        if (block.LiveSelection.HasValue)
            Add(refusals, LayoutDefinitionCodes.LiveSelectionForbidden, $"{pointer}/live_selection");
        if (block.Repeating && (block.Container is null || block.Binding is not (LayoutQueryBinding or LayoutRecordFieldBinding)))
            Add(refusals, LayoutDefinitionCodes.ScopedContainerInvalid, $"{pointer}/repeating");
        if (block.CollectionBounds is { } bounds
            && (!block.Repeating || bounds.Minimum < 0 || bounds.Maximum < bounds.Minimum))
            Add(refusals, LayoutDefinitionCodes.CollectionBoundsInvalid, $"{pointer}/collection_bounds");
        if (block.RelatedRelationship is { } relationship
            && (string.IsNullOrWhiteSpace(relationship) || block.Container is null || intent != LayoutIntent.Observe))
            Add(refusals, LayoutDefinitionCodes.ScopedContainerInvalid, $"{pointer}/related_relationship");

        var filterTargets = block.FilterTargets ?? [];
        for (var index = 0; index < filterTargets.Count; index++)
            if (!blockIds.TryGetValue(filterTargets[index], out var target))
                Add(refusals, LayoutDefinitionCodes.InteractionTargetUnknown, $"{pointer}/filter_targets/{index}");
            // layout-auth-36: a selection narrows a set. A query or a measure is one; a record field,
            // a template and static content are not, so an edge to one would filter nothing.
            else if (target?.Binding is not (LayoutQueryBinding or LayoutMeasureBinding))
                Add(refusals, LayoutDefinitionCodes.FilterTargetUnnarrowable, $"{pointer}/filter_targets/{index}");

        var regions = block.Container?.Regions is { } declared
            ? new HashSet<string>(declared, StringComparer.Ordinal)
            : null;
        var children = block.Children ?? [];
        if (children.Count > 0 && block.Container is null)
            Add(refusals, LayoutDefinitionCodes.BlockChildrenInvalid, $"{pointer}/container");
        for (var index = 0; index < children.Count; index++)
            ValidateBlock(children[index], $"{pointer}/children/{index}", medium, inheritedIntent, regions, blockIds, registers, author, captures, publishing,
                block.Repeating ? CollectionName(block.Binding) : rowSection, refusals);
    }

    // layout-bound-8: the named rule must be registered and validate, and the shared compiler admits
    // it by the rule's own tier (JsonLogic compiled here, JsonSchema left to the kernel validator,
    // any other tier refused). Layout never chooses the compiler.
    // With no register a name resolves to nothing, so it refuses (T-724 ruling 36).
    private static void ValidateNamedRule(LayoutValidationRuleRegistry? rules, string name, string pointer, ICollection<DefinitionRefusal> refusals)
    {
        RuleDefinition? rule = null;
        if (rules?.TryGet(name, out rule) != true || rule is null)
        {
            Add(refusals, LayoutDefinitionCodes.ValidationRuleUnknown, pointer);
            return;
        }
        if (rule.Action != RuleActionKind.Validate)
        {
            Add(refusals, LayoutDefinitionCodes.ValidationRuleInvalid, pointer);
            return;
        }
        try
        {
            RuleCompiler.Compile([rule]);
        }
        catch (RuleCompilationException)
        {
            Add(refusals, LayoutDefinitionCodes.ValidationRuleInvalid, pointer);
        }
    }

    // T-724 ruling 37: publication looks the field up. A value domain's resolver picks that field's
    // editor and Layout passes its choice through (layout-bound-10), so an authored control there
    // refuses; on any other field the control must accept the field's value kind.
    private static void ValidateControlField(LayoutHostRegisters registers, LayoutFieldControl control, LayoutBinding? binding, string pointer, ICollection<DefinitionRefusal> refusals)
    {
        var field = binding is LayoutRecordFieldBinding bound ? registers.Fields?.Find(bound.FieldPath) : null;
        if (field is null)
            Add(refusals, LayoutDefinitionCodes.CaptureFieldUnknown, pointer);
        else if (field.HasValueDomain)
            Add(refusals, LayoutDefinitionCodes.ControlDisplacesValueDomain, pointer);
        else if (registers.FieldControls!.Find(control.Id)?.ValueShapes.Contains(field.ValueShape) != true)
            Add(refusals, LayoutDefinitionCodes.ControlValueKindMismatch, pointer);
    }

    // The collection a repeating block iterates, which names its children's row section.
    private static string? CollectionName(LayoutBinding? binding) => binding switch
    {
        LayoutQueryBinding value => value.ViewDefinitionId,
        LayoutRecordFieldBinding value => value.FieldPath,
        _ => null,
    };

    private static void ValidateBinding(
        LayoutBinding? binding,
        LayoutIntent intent,
        LayoutMedium medium,
        bool captures,
        string pointer,
        ICollection<DefinitionRefusal> refusals)
    {
        if (binding is null)
        {
            Add(refusals, LayoutDefinitionCodes.BindingInvalid, pointer);
            return;
        }
        var identity = binding switch
        {
            LayoutRecordFieldBinding value => value.FieldPath,
            LayoutQueryBinding value => value.ViewDefinitionId,
            LayoutMeasureBinding value => value.MeasurePath,
            LayoutTemplateBinding value => value.TemplateDefinitionId,
            LayoutStaticBinding => "static",
            LayoutTextBinding => "text",
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(identity)) Add(refusals, LayoutDefinitionCodes.BindingInvalid, pointer);
        // layout-ck-43, layout-ck-44: a text binding has runs, each exactly a literal or a field.
        if (binding is LayoutTextBinding text)
        {
            var runs = text.Runs ?? [];
            if (runs.Count == 0) Add(refusals, LayoutDefinitionCodes.BindingInvalid, $"{pointer}/runs");
            for (var index = 0; index < runs.Count; index++)
                if (runs[index] is not { IsWellFormed: true })
                    Add(refusals, LayoutDefinitionCodes.BindingInvalid, $"{pointer}/runs/{index}");
        }

        var unsupported = (intent, binding) switch
        {
            (LayoutIntent.Capture, LayoutQueryBinding) => true,
            (LayoutIntent.Capture, LayoutMeasureBinding) => true,
            (LayoutIntent.Capture, LayoutTemplateBinding) => true,
            (LayoutIntent.Capture, LayoutTextBinding) => true,
            // layout-auth-25 (amended 2026-09-25): an issue block on page media reads a record field
            // to render it, as ADR 0092's invoice does. Reading grants no capture (layout-auth-28).
            (LayoutIntent.Issue, LayoutRecordFieldBinding or LayoutTextBinding) => !captures && medium != LayoutMedium.Page,
            _ => false,
        };
        if (unsupported) Add(refusals, LayoutDefinitionCodes.IntentBindingUnsupported, pointer);
        if (binding is LayoutStaticBinding { Content.ValueKind: JsonValueKind.Undefined })
            Add(refusals, LayoutDefinitionCodes.BindingInvalid, pointer);
        if (binding is LayoutTemplateBinding && medium != LayoutMedium.Page)
            Add(refusals, LayoutDefinitionCodes.IntentBindingUnsupported, pointer);
    }

    private static void ValidateContainer(
        LayoutContainer? container,
        LayoutMedium medium,
        string pointer,
        ICollection<DefinitionRefusal> refusals)
    {
        if (container is null) return;
        if (!Enum.IsDefined(container.Kind)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/kind");
        if (!Enum.IsDefined(container.Axis)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/axis");
        if (!Enum.IsDefined(container.Wrap)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/wrap");
        if (!Enum.IsDefined(container.JustifyItems)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/justify_items");
        if (!Enum.IsDefined(container.AlignItems)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/align_items");
        if (container.CollapseBelow is { } collapse && !Enum.IsDefined(collapse))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/collapse_below");
        if (container.Density is { } density && !Enum.IsDefined(density))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/density");
        AddNumeric(container.ColumnCount, LayoutNumericMember.ColumnCount, $"{pointer}/column_count", refusals);
        AddNumeric(container.Gap, LayoutNumericMember.Gap, $"{pointer}/gap", refusals);
        if (medium == LayoutMedium.Screen
            && container.Kind == LayoutContainerKind.Flow
            && container.Wrap == LayoutWrap.NoWrap
            && container.ColumnCount > 1)
            Add(refusals, LayoutDefinitionCodes.ReflowForbidden, pointer);

        var regions = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < (container.Regions?.Count ?? 0); index++)
        {
            var region = container.Regions![index];
            if (string.IsNullOrWhiteSpace(region) || !regions.Add(region))
                Add(refusals, LayoutDefinitionCodes.ZoneUnknown, $"{pointer}/regions/{index}");
        }
    }

    private static void ValidatePlacement(
        LayoutPlacement? placement,
        IReadOnlySet<string>? parentRegions,
        string pointer,
        ICollection<DefinitionRefusal> refusals)
    {
        if (placement is null) return;
        if (!Enum.IsDefined(placement.Width)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/width");
        if (!Enum.IsDefined(placement.Height)) Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/height");
        if (placement.JustifySelf is { } justify && !Enum.IsDefined(justify))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/justify_self");
        if (placement.AlignSelf is { } align && !Enum.IsDefined(align))
            Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/align_self");
        if (placement.Zone is { } zone && (parentRegions is null || !parentRegions.Contains(zone)))
            Add(refusals, LayoutDefinitionCodes.ZoneUnknown, $"{pointer}/zone");
        if (placement.PixelPosition is not null)
            Add(refusals, LayoutDefinitionCodes.PixelPlacementForbidden, $"{pointer}/pixel_position");
        if (placement.Span is { } span)
        {
            AddNumeric(span, LayoutNumericMember.Span, $"{pointer}/span", refusals);
            // fill takes the whole run; a span beside it is a second, contradictory width.
            if (placement.Width == LayoutSizing.Fill)
                Add(refusals, LayoutDefinitionCodes.SpanWithFill, $"{pointer}/span");
        }
        AddNumeric(placement.Grow, LayoutNumericMember.Grow, $"{pointer}/grow", refusals);
    }

    // layout-bound-7: a run cites the surface's own page definitions or those a pack supplies. A
    // local definition may not reuse a supplied id, so every citation names exactly one definition.
    private static void ValidatePages(
        LayoutDefinition definition,
        IReadOnlyDictionary<string, LayoutBlock> blockIds,
        LayoutPageRegistry? supplied,
        ICollection<DefinitionRefusal> refusals)
    {
        var layouts = new HashSet<string>(StringComparer.Ordinal);
        var pageLayouts = definition.PageLayouts ?? [];
        for (var index = 0; index < pageLayouts.Count; index++)
        {
            var layout = pageLayouts[index];
            var pointer = $"/page_layouts/{index}";
            if (layout is null)
            {
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
                continue;
            }
            if (string.IsNullOrWhiteSpace(layout.Id) || !layouts.Add(layout.Id)
                || supplied?.Layouts.ContainsKey(layout.Id) == true
                || string.IsNullOrWhiteSpace(layout.Sheet)
                || layout.Margins is null || layout.MarginBoxes is null)
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
            if (!Enum.IsDefined(layout.Orientation))
                Add(refusals, LayoutDefinitionCodes.PlacementTokenUnknown, $"{pointer}/orientation");
        }
        var masters = new HashSet<string>(StringComparer.Ordinal);
        var pageMasters = definition.PageMasters ?? [];
        for (var index = 0; index < pageMasters.Count; index++)
        {
            var master = pageMasters[index];
            var pointer = $"/page_masters/{index}";
            if (master is null)
            {
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
                continue;
            }
            if (string.IsNullOrWhiteSpace(master.Id) || !masters.Add(master.Id)
                || supplied?.Masters.ContainsKey(master.Id) == true)
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
            if (!layouts.Contains(master.PageLayoutId) && supplied?.Layouts.ContainsKey(master.PageLayoutId) != true)
                Add(refusals, LayoutDefinitionCodes.PageReferenceUnknown, $"{pointer}/page_layout_id");
            if (master.First is null || master.Left is null || master.Right is null)
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
        }
        var runs = new HashSet<string>(StringComparer.Ordinal);
        var pageRuns = definition.PageRuns ?? [];
        for (var index = 0; index < pageRuns.Count; index++)
        {
            var run = pageRuns[index];
            var pointer = $"/page_runs/{index}";
            if (run is null)
            {
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
                continue;
            }
            if (string.IsNullOrWhiteSpace(run.Id) || !runs.Add(run.Id) || run.BlockIds is null || run.BlockIds.Count == 0)
                Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, pointer);
            if (!layouts.Contains(run.PageLayoutId) && supplied?.Layouts.ContainsKey(run.PageLayoutId) != true)
                Add(refusals, LayoutDefinitionCodes.PageReferenceUnknown, $"{pointer}/page_layout_id");
            var cited = masters.Contains(run.PageMasterId)
                ? pageMasters.First(master => master?.Id == run.PageMasterId)
                : supplied?.Masters.GetValueOrDefault(run.PageMasterId);
            if (cited is null || cited.PageLayoutId != run.PageLayoutId)
                Add(refusals, LayoutDefinitionCodes.PageReferenceUnknown, $"{pointer}/page_master_id");
            var runBlocks = run.BlockIds ?? [];
            for (var blockIndex = 0; blockIndex < runBlocks.Count; blockIndex++)
                if (!blockIds.ContainsKey(runBlocks[blockIndex]))
                    Add(refusals, LayoutDefinitionCodes.PageReferenceUnknown, $"{pointer}/block_ids/{blockIndex}");
        }
        // Every run already resolves its geometry and master, locally or from a pack.
        if (definition.Medium == LayoutMedium.Page && (definition.PageRuns?.Count ?? 0) == 0)
            Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, "/page_runs");
        if (definition.Medium == LayoutMedium.Screen
            && ((definition.PageLayouts?.Count ?? 0) > 0
                || (definition.PageMasters?.Count ?? 0) > 0
                || (definition.PageRuns?.Count ?? 0) > 0))
            Add(refusals, LayoutDefinitionCodes.PageDefinitionInvalid, "/page_runs");
    }

    private static void AddNumeric(
        int value,
        LayoutNumericMember member,
        string pointer,
        ICollection<DefinitionRefusal> refusals)
    {
        if (!LayoutDefinitionSchema.Numeric(member).Contains(value))
            Add(refusals, LayoutDefinitionCodes.NumericOutOfRange, pointer);
    }

    private static void Add(ICollection<DefinitionRefusal> refusals, string code, string pointer)
        => refusals.Add(new(code, pointer));
}

/// <summary>Both renderer adapters call the same schema-backed persisted-value gate.</summary>
public static class LayoutPersistedValueAdmission
{
    /// <summary>Validates persisted values before the shared Layout runtime derives a render plan.</summary>
    /// <param name="definition">The immutable persisted Layout definition.</param>
    /// <param name="kinds">The host register, or the platform grammar when omitted.</param>
    public static void ValidateForRuntime(LayoutDefinition definition, LayoutBlockKindRegistry? kinds = null)
        => LayoutDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Render, kinds);

    /// <summary>Validates persisted values against the host's registers before the runtime flows them.</summary>
    /// <param name="definition">The immutable persisted Layout definition.</param>
    /// <param name="registers">The host's bound registers.</param>
    public static void ValidateForRuntime(LayoutDefinition definition, LayoutHostRegisters registers)
        => LayoutDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Render, registers, author: null);

    /// <summary>Validates persisted values before React rendering.</summary>
    /// <param name="definition">The persisted definition.</param>
    /// <param name="kinds">The host register, or the platform grammar when omitted.</param>
    public static void ValidateForReact(LayoutDefinition definition, LayoutBlockKindRegistry? kinds = null)
        => LayoutDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Render, kinds);

    /// <summary>Validates persisted values before Blazor rendering.</summary>
    /// <param name="definition">The persisted definition.</param>
    /// <param name="kinds">The host register, or the platform grammar when omitted.</param>
    public static void ValidateForBlazor(LayoutDefinition definition, LayoutBlockKindRegistry? kinds = null)
        => LayoutDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Render, kinds);
}
