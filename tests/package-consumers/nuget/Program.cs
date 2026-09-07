using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Foundation.Enums;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation;
using Harborline.Foundation.MultiTenancy;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Session;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.WorkItems;
using Harborline.Blocks.InspectionReview;
using Harborline.Contracts.Forms;
using Harborline.Contracts.Workflow;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Builder;
using Harborline.Foundation.Forms.UI;
using Harborline.Foundation.Localization;
using Harborline.Foundation.Responsive;
using Harborline.Foundation.Theming;
using Harborline.Foundation.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Harborline.UIAdapters.Blazor.Components.AI;
using Harborline.UIAdapters.Blazor.Components.Scheduling;
using Harborline.UIAdapters.Blazor.Shell;
using Harborline.UIAdapters.Blazor.Accessibility;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Localization;
using FormsState = Harborline.Foundation.Forms;
using FormsDrafts = Harborline.Foundation.Forms.Drafts;
using FormsModel = Harborline.Foundation.Forms.Models;
using BuilderDefinitions = Harborline.Blocks.BuilderDefinitions;

if (typeof(HarborlineButton).Assembly.GetName().Name != "Harborline.UIAdapters.Blazor")
    throw new InvalidOperationException("UI assembly identity changed.");
if (typeof(ButtonVariant).Assembly.GetName().Name != "Harborline.Foundation")
    throw new InvalidOperationException("Foundation assembly identity changed.");
if (typeof(HarborlineLocaleProvider).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("Public Blazor localization interface is not packaged with the UI projection.");
if (typeof(HarborlineContextMenu).Assembly != typeof(HarborlineButton).Assembly
    || typeof(ContextMenuGroup).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("Context Menu is not packaged with the aggregate Blazor UI projection.");
if (typeof(HarborlineAccordion).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineCard).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineIconButton).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSeparator).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-02 UI surfaces are not packaged with the aggregate Blazor UI projection.");
if (typeof(HarborlineBreadcrumb).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineCheckBox).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineCollapsible).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-02-02 Blazor UI surfaces are not packaged with the aggregate projection.");
if (typeof(HarborlineFormField).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineDateField).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineDateTimeField).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineNumberField).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineRadioGroup).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlinePopover).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlinePopoverContent).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-02-03 Blazor UI surfaces are not packaged with the aggregate projection.");
if (typeof(HarborlineDateField).GetProperty(nameof(HarborlineDateField.Value))?.PropertyType != typeof(string)
    || typeof(HarborlineDateTimeField).GetProperty(nameof(HarborlineDateTimeField.Value))?.PropertyType != typeof(string)
    || typeof(HarborlineNumberField).GetProperty(nameof(HarborlineNumberField.Value))?.PropertyType != typeof(string)
    || Enum.GetNames<RadioGroupOrientation>() is not ["Vertical", "Horizontal"]
    || Enum.GetNames<PopoverSide>() is not ["Top", "Right", "Bottom", "Left"]
    || new FormFieldContextValue("amount", "amount-label", "amount-hint", true, false).DescribedBy != "amount-hint")
    throw new InvalidOperationException("Packed wave-02-03 raw-field or composition vocabulary changed.");
if (typeof(HarborlineSheet).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSpotlight).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineTextArea).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineTooltip).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineTable).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineWindow).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-02-04 Blazor UI surfaces are not packaged with the aggregate projection.");
if (typeof(HarborlineAlert).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineBadge).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineDataExportButton).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSpinner).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-03-01 Blazor UI surfaces are not packaged with the aggregate projection.");
if (typeof(HarborlineActionMenu).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineActivityLog).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineChip).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineConversationList).Assembly != typeof(HarborlineButton).Assembly
    || typeof(ScrollAffordanceObserver).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-03-02 Blazor UI surfaces are not packaged with the aggregate projection.");
if (typeof(HarborlineGuardedControl).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineInput).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineLayersRail).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineNotificationCenter).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlinePage).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSearchInput).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-03-03 Blazor UI surfaces are not packaged with the aggregate projection.");
if (Enum.GetNames<GuardedControlState>() is not ["Covered", "Armed", "Committing"]
    || Enum.GetNames<GuardedControlRecoveryReason>() is not ["Escape", "Timeout", "Navigation", "DocumentHidden", "WindowBlur", "BecameDisabled"]
    || Enum.GetNames<InputFillMode>() is not ["Solid", "Outline", "Flat"]
    || Enum.GetNames<PageHeadingLevel>() is not ["H1", "H2", "H3"]
    || Enum.GetNames<NotificationBadgeMode>() is not ["Unread", "Decisions"]
    || NotificationCenterLabels.English.Title != "Notifications")
    throw new InvalidOperationException("Packed wave-03-03 component vocabulary changed.");
if (typeof(HarborlineSegmentedControl).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSelectField).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineSwitch).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineToaster).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineUserMenu).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineAppLayout).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-03-04 Blazor UI surfaces are not packaged with the aggregate projection.");
if (Enum.GetNames<SegmentedControlSize>() is not ["Sm", "Md", "Lg", "Touch"]
    || Enum.GetNames<SelectFieldSize>() is not ["Sm", "Md", "Lg"]
    || Enum.GetNames<ToastVariant>() is not ["Default", "Success", "Error", "Warning", "Information", "Loading"]
    || Enum.GetNames<SideNavMode>() is not ["Rail", "Overlay", "Hidden", "Auto"]
    || new SelectOption("active", "Active").Value != "active"
    || new UserIdentity("Ada Lovelace").Name != "Ada Lovelace")
    throw new InvalidOperationException("Packed wave-03-04 component vocabulary changed.");
if (typeof(HarborlineChart).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineChat).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineDataGrid<>).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineGantt).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineNumericTextBox).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave-03-05 Blazor UI surfaces are not packaged with the aggregate projection.");
if (Enum.GetNames<ChartMotion>() is not ["Host", "Disabled"]
    || Enum.GetNames<ChatMessageRole>() is not ["User", "Assistant", "System"]
    || Enum.GetNames<DataGridValueKind>() is not ["Text", "Number", "Date", "Boolean"]
    || Enum.GetNames<GanttZoom>() is not ["Day", "Week", "Month"]
    || Enum.GetNames<NumericTextBoxFillMode>() is not ["Solid", "Outline", "Flat"]
    || new GanttTask("capture", "Capture", new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 12)).Id != "capture"
    || new ChatParticipant("Inspector").Name != "Inspector")
    throw new InvalidOperationException("Packed wave-03-05 component vocabulary changed.");
var uiServices = new ServiceCollection();
uiServices.AddHarborlineUiAdapters();
using (var uiProvider = uiServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
{
    using var firstScope = uiProvider.CreateScope();
    using var secondScope = uiProvider.CreateScope();
    var firstToast = firstScope.ServiceProvider.GetRequiredService<IToastService>();
    if (!ReferenceEquals(firstToast, firstScope.ServiceProvider.GetRequiredService<IToastService>())
        || ReferenceEquals(firstToast, secondScope.ServiceProvider.GetRequiredService<IToastService>()))
        throw new InvalidOperationException("Packed Toaster service is not isolated by application or circuit scope.");
}
if (Enum.GetNames<ActionMenuAlignment>() is not ["Left", "Right"]
    || Enum.GetNames<ActivityLogLayout>() is not ["List", "Table"]
    || Enum.GetNames<ActivityTone>() is not ["Neutral", "Positive", "Warning", "Danger", "Info"]
    || Enum.GetNames<ChipFillMode>() is not ["Solid", "Flat", "Outline"]
    || Enum.GetNames<ScrollAffordanceOrientation>() is not ["Horizontal", "Vertical"])
    throw new InvalidOperationException("Packed wave-03-02 component vocabulary changed.");
var packedNavCollapse = new NavCollapseController(new NavCollapseOptions(DefaultCollapsed: false), initialNarrow: true);
if (!packedNavCollapse.Snapshot.Collapsed || typeof(NavCollapseController).Assembly.GetName().Name != "Harborline.Foundation")
    throw new InvalidOperationException("App-essential wave-03-02 Nav Collapse support is not packaged with Foundation.");
var packedScroll = ScrollAffordancePolicy.Resolve(
    new ScrollAffordanceMeasurement(0, 1200, 400),
    new ScrollAffordanceOptions(ItemCount: 12));
if (!packedScroll.CanScroll || packedScroll.AnnouncementArguments["total"] != 12)
    throw new InvalidOperationException("Packed wave-03-02 Scroll Affordance policy did not preserve the frozen behavior.");
if (Enum.GetNames<AlertVariant>() is not ["Info", "Success", "Warning", "Error"]
    || Enum.GetNames<BadgeAppearance>() is not ["Solid", "Subtle", "Outline"]
    || Enum.GetNames<BadgeShape>() is not ["Rounded", "Pill", "Square"]
    || Enum.GetNames<ExportFormat>() is not ["Csv", "Xlsx", "Pdf", "Json", "Md"]
    || Enum.GetNames<SpinnerType>() is not ["Ring", "Converging"])
    throw new InvalidOperationException("Packed wave-03-01 component vocabulary changed.");
var packedSideNav = new SideNavGroup("main", [new SideNavItem("home", "Home")]);
if (packedSideNav.Items.Single().Id != "home" || typeof(SideNavGroup).Assembly.GetName().Name != "Harborline.Foundation")
    throw new InvalidOperationException("App-essential wave-03-01 Side Nav Group support is not packaged with Foundation.");
var packedUiSideNav = new SideNavNavigationGroup("main", [new SideNavNavigationItem("home", "Home")]);
if (packedUiSideNav.Items.Single().Id != "home" || typeof(SideNavNavigationGroup).Assembly.GetName().Name != "Harborline.UIAdapters.Blazor")
    throw new InvalidOperationException("App-essential wave-04-01 Side Nav UI model is not packaged with the aggregate Blazor projection.");
if (Enum.GetNames<SheetSide>() is not ["Top", "Right", "Bottom", "Left"]
    || Enum.GetNames<TooltipSide>() is not ["Top", "Right", "Bottom", "Left"]
    || Enum.GetNames<TableDensity>() is not ["Small", "Medium"]
    || Enum.GetNames<HarborlineWindowState>() is not ["Default", "Minimized", "Maximized"]
    || Enum.GetNames<TextAreaResize>() is not ["None", "Vertical", "Horizontal", "Both"])
    throw new InvalidOperationException("Packed wave-02-04 component vocabulary changed.");
if (Enum.GetNames<CheckBoxState>() is not ["Unchecked", "Checked", "Mixed"]
    || ToneStyles.Resolve(LensTone.Info).Swatch != "var(--color-secondary)"
    || FormFactorPolicy.Resolve(new(DesktopWidth: true)).Mode != FormFactorMode.Desktop
    || BreakpointQueries.Dock != "(min-width: 1280px)")
    throw new InvalidOperationException("Packed wave-02-02 support and component vocabulary changed.");
if (Enum.GetNames<AccordionMode>() is not ["Single", "Multiple"]
    || Enum.GetNames<CardAppearance>() is not ["Flat", "Raised", "Outlined", "Elevated"]
    || Enum.GetNames<IconButtonSize>() is not ["Small", "Medium", "Large", "Touch"]
    || Enum.GetNames<SeparatorOrientation>() is not ["Horizontal", "Vertical"])
    throw new InvalidOperationException("Packed wave-02 Blazor component vocabulary changed.");
if (typeof(HarborlineErrorCard).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineLoadingState).Assembly != typeof(HarborlineButton).Assembly
    || typeof(HarborlineEmptyState).Assembly != typeof(HarborlineButton).Assembly
    || typeof(TouchTargetAffordances).Assembly != typeof(HarborlineButton).Assembly
    || typeof(MediaQueryObserver).Assembly != typeof(HarborlineButton).Assembly
    || typeof(OutsidePointerObserver).Assembly != typeof(HarborlineButton).Assembly)
    throw new InvalidOperationException("App-essential wave UI surfaces are not packaged with the aggregate Blazor UI projection.");
if (Enum.GetNames<ErrorCardVariant>() is not ["Page", "Default", "Compact"]
    || Enum.GetNames<LoadingStateVariant>() is not ["Page", "Inline"])
    throw new InvalidOperationException("Packed Blazor feedback component variant vocabulary changed.");
if (typeof(HarborlineErrorCard).GetProperty(nameof(HarborlineErrorCard.Title))?.PropertyType != typeof(string)
    || typeof(HarborlineErrorCard).GetProperty(nameof(HarborlineErrorCard.RetryLabel))?.PropertyType != typeof(string)
    || typeof(HarborlineErrorCard).GetProperty(nameof(HarborlineErrorCard.Variant))?.PropertyType != typeof(ErrorCardVariant)
    || typeof(HarborlineLoadingState).GetProperty(nameof(HarborlineLoadingState.Label))?.PropertyType != typeof(string)
    || typeof(HarborlineLoadingState).GetProperty(nameof(HarborlineLoadingState.Variant))?.PropertyType != typeof(LoadingStateVariant)
    || typeof(HarborlineEmptyState).GetProperty(nameof(HarborlineEmptyState.Title))?.PropertyType != typeof(string)
    || typeof(HarborlineEmptyState).GetProperty(nameof(HarborlineEmptyState.Variant))?.PropertyType != typeof(EmptyStateVariant))
    throw new InvalidOperationException("Packed Blazor feedback component parameter surface changed.");
if (Enum.GetNames<EmptyStateVariant>() is not ["Informational", "Positive", "Actionable"]
    || TouchTargetAffordances.For(TouchTargetStrategy.GrowBox) != "hl-touch-target"
    || TouchTargetAffordances.MinimumCssPixels != 44
    || typeof(IOutsidePointerRegistration).GetMethod(nameof(IOutsidePointerRegistration.SetCallback)) is null)
    throw new InvalidOperationException("Packed Empty State, touch-target, or browser-observer vocabulary changed.");
var packedMenuGroup = new ContextMenuGroup([new ContextMenuItem("approve", "Approve")]);
if (packedMenuGroup.Items.Single().Id != "approve"
    || HarborlineContextMenu.ClampPosition(398, 298, 120, 90, 400, 300) != (272, 202))
    throw new InvalidOperationException("Packed Context Menu public model or viewport clamping behavior changed.");
if (Enum.GetNames<ButtonVariant>() is not ["Primary", "Secondary", "Danger", "Warning", "Info", "Success", "Subtle", "Transparent", "Light", "Dark"])
    throw new InvalidOperationException("ButtonVariant compatibility vocabulary changed.");
if (typeof(CanvasNode).Assembly.GetName().Name != "Harborline.Foundation"
    || typeof(CssClassComposer).Assembly != typeof(CanvasNode).Assembly
    || typeof(HarborlineDefaultStrings).Assembly != typeof(CanvasNode).Assembly
    || typeof(FormViewBinding).Assembly != typeof(CanvasNode).Assembly)
    throw new InvalidOperationException("Aggregate Foundation UI support surfaces changed assembly identity.");
var packedLayers = LayersState.Initialize(["details", "history"]);
if (packedLayers.ActiveId != "details"
    || packedLayers.Activate("history").ActiveId != "history"
    || HarborlineLocaleScope.DirectionForLocale("ar-SA") != "rtl"
    || new HarborlineLocaleScope(catalog: new Dictionary<string, string> { ["consumer.greeting"] = "Hello" }).Resolve("consumer.greeting") != "Hello")
    throw new InvalidOperationException("Packed Layers State or locale scope behavior changed.");
var selectedNodes = new List<string>();
var packedCanvas = new CanvasModel(
    [new CanvasNode("amount", "field", "Amount", 0, null)],
    null,
    selectedNodes.Add);
packedCanvas.Select("amount");
var packedLens = new AspectLens(
    "required",
    "Required fields",
    LensTone.Accent,
    AspectLensKind.Filter,
    nodeId => new AspectState(nodeId == "amount"));
var packedProvenance = new ProvenanceResolver(_ => new ProvenanceInfo(
    ProvenanceSource.Unknown,
    [ProvenanceSource.Unknown],
    Overridden: false,
    Locked: true,
    Resolved: false));
if (selectedNodes.Single() != "amount"
    || !packedLens.Project("amount").Active
    || packedProvenance.Resolve("amount") is not { Source: ProvenanceSource.Unknown, Locked: true, Resolved: false })
    throw new InvalidOperationException("Packed Aspect Lens callbacks or fail-closed provenance behavior changed.");
if (CssClassComposer.Combine("p-2", new object?[] { "text-sm", new KeyValuePair<string, bool>("active", true) }, "p-4", false)
        != "text-sm active p-4")
    throw new InvalidOperationException("Packed cn compatibility surface changed class composition behavior.");
if (HarborlineDefaultStrings.Values.Count != 713
    || HarborlineDefaultStrings.Get("common.loading") != "Loading"
    || HarborlineDefaultStrings.Interpolate(
        "Step {current} of {total}",
        new Dictionary<string, object?> { ["current"] = 2 }) != "Step 2 of {total}")
    throw new InvalidOperationException("Packed default string catalog or interpolation behavior changed.");
if (DefaultRailLabels.Value.RailRegion != "Layers"
    || DefaultRailLabels.Value.PassiveCount(3) != "+3"
    || DefaultRailLabels.Value.ViewingLens("Errors") != "Viewing: Errors")
    throw new InvalidOperationException("Packed Rail Labels surface changed.");
if (typeof(TenantId).Assembly.GetName().Name != "Harborline.Contracts")
    throw new InvalidOperationException("Contracts assembly identity changed.");
if (new TenantId("tenant-consumer").IsSystemSentinel)
    throw new InvalidOperationException("Real consumer tenant was classified as a sentinel.");
if (EntityId.Parse("property:consumer/42").ToString() != "property:consumer/42")
    throw new InvalidOperationException("EntityId package round-trip failed.");
var packedForm = FormsJson.Deserialize<FormItem>("""{"kind":"collection","key":"photos","items":[{"kind":"field","key":"photo"}]}""");
if (packedForm is not CollectionFormItem { Items.Count: 1 })
    throw new InvalidOperationException("Packed Dynamic Forms contract did not parse recursive wire data.");
var packedOptions = FormsJson.Deserialize<RuleOutcome>("""{"ruleId":"opt.result","target":"field:result","outputType":"Options","options":{"state":"Resolved","options":["PASS","FAIL"]}}""");
var packedOptionsOutcome = packedOptions.Options.HasValue ? packedOptions.Options.Value : null;
var packedOptionValues = packedOptionsOutcome is not null && packedOptionsOutcome.Options.HasValue
    ? packedOptionsOutcome.Options.Value : null;
if (packedOptions.OutputType != OutputType.Options || packedOptionValues?.Count != 2)
    throw new InvalidOperationException("Packed Dynamic Forms revision-2 contract did not parse Options rule outcomes.");
var packedViewField = FormsJson.Deserialize<FormViewField>("""{"name":"amount","label":{"defaultLocale":"en","values":{"en":"Amount","ar":"المبلغ"}},"isSensitive":true,"isReadable":false,"value":"must-not-render","rules":{"visible":true,"required":true,"readOnly":false}}""");
var packedRenderField = FormViewBinding.Normalize(
    packedViewField,
    new FormViewRendererHints(ValueKind: "decimal-string", Required: false));
if (!packedRenderField.ReadOnly
    || !packedRenderField.Required
    || packedRenderField.ValueKind != "decimal-string"
    || packedRenderField.Canonical.Value.HasValue
    || FormViewText.Resolve(packedViewField.Label, ["ar-AE"], "Field") != "المبلغ")
    throw new InvalidOperationException("Packed Form View binding weakened redaction or changed locale resolution.");
var packedRule = FormsJson.Deserialize<RuleDefinition>("""{"id":"opt.result","tier":"JsonLogic","scope":"Field","scopeTarget":"result","expression":"[\"PASS\",\"FAIL\"]","action":"Options"}""");
var packedRuleResult = new FormRuleGraph(RuleCompiler.Compile([packedRule]))
    .EvaluateInstance(RuleInstance.FromJson(new System.Text.Json.Nodes.JsonObject()));
if (!packedRuleResult.Options.TryGetValue("field:result", out var packedRuleOptions)
    || packedRuleOptions.Options?.Select(value => value?.GetValue<string>()).ToArray() is not ["PASS", "FAIL"])
    throw new InvalidOperationException("Packed Rule Runtime did not emit deterministic Options outcomes from the Forms revision-2 contract.");
var packedAuthoring = FormsJson.Deserialize<FormDefinition>("""{"id":"inspection","version":"1.0.0","status":"Published","tenant":"tenant-consumer","owner":{"scheme":"system","value":"harborline"},"schemaRef":"sha256:test","overlay":{"fields":{},"sections":[],"rules":[]},"createdAt":"2026-08-08T00:00:00Z","updatedAt":"2026-08-08T00:00:00Z","fieldsMeta":{"condition":{"type":"radio","required":true,"validations":[{"code":"required"}],"options":["PASS","FAIL"]}}}""");
var packedFieldsMeta = packedAuthoring.FieldsMeta.HasValue ? packedAuthoring.FieldsMeta.Value : null;
if (packedFieldsMeta?["condition"].Type != "radio")
    throw new InvalidOperationException("Packed Dynamic Forms revision-3 contract lost exact Harborline App authoring metadata.");
try
{
    FormsJson.Deserialize<FormItem>("""{"kind":"script","key":"unsafe"}""");
    throw new InvalidOperationException("Packed Dynamic Forms contract accepted an unknown discriminator.");
}
catch (FormsWireException error) when (error.Code == "unknown-discriminator")
{
}
var packedWorkflow = (WorkflowDefinition)WorkflowWire.Deserialize("WorkflowDefinition", """{"key":"consumer-workflow","version":"1.0.0","status":"Draft","tenant":"tenant-consumer","owner":{"scheme":"system","value":"harborline"},"title":{"defaultLocale":"en","values":{"en":"Consumer workflow"}},"mutability":"Locked","initialState":"Draft","states":[{"id":"Draft","label":{"defaultLocale":"en","values":{"en":"Draft"}},"kind":"Normal"},{"id":"Done","label":{"defaultLocale":"en","values":{"en":"Done"}},"kind":"Terminal"}],"transitions":[{"id":"complete","from":"Draft","on":"approve","to":"Done"}],"triggers":[{"id":"approve","kind":"HumanAction","task":"approval"}],"actions":[{"id":"notify","on":{"transition":"complete"},"kind":"Notify","capabilityRef":"notify.email","classification":"AP"}],"guards":[],"createdAt":"2026-08-09T00:00:00Z","updatedAt":"2026-08-09T00:00:00Z"}""");
var workflowAdmission = WorkflowAdmissionValidator.Validate(
    packedWorkflow,
    new ImmutableWorkflowAuthorityResolver(new Dictionary<string, ActionClassification>
    {
        ["notify.email"] = ActionClassification.AP,
    }));
if (!workflowAdmission.IsValid)
    throw new InvalidOperationException("Packed Workflow contract failed its package-only admission check.");
var packedEngineDefinition = new Harborline.Blocks.Workflow.WorkflowDefinitionBuilder<ConsumerFlowState, ConsumerFlowTrigger, object>()
    .StartAt(ConsumerFlowState.Draft)
    .Transition(ConsumerFlowState.Draft, ConsumerFlowTrigger.Approve, ConsumerFlowState.Done)
    .Terminal(ConsumerFlowState.Done)
    .Build();
var packedEngineRuntime = new Harborline.Blocks.Workflow.InMemoryWorkflowRuntime();
var packedEngineInstance = await packedEngineRuntime.StartAsync(packedEngineDefinition, new object());
packedEngineInstance = await packedEngineRuntime.FireAsync<ConsumerFlowState, ConsumerFlowTrigger, object>(
    packedEngineInstance.Id, ConsumerFlowTrigger.Approve);
if (!packedEngineInstance.IsTerminal
    || typeof(Harborline.Blocks.Workflow.InMemoryWorkflowRuntime).Assembly.GetName().Name != "Harborline.Blocks.Workflow"
    || typeof(Harborline.Blocks.Workflow.Interpreter.DeclarativeWorkflowInterpreter).Assembly.GetName().Name != "Harborline.Blocks.Workflow.Interpreter")
    throw new InvalidOperationException("Packed workflow engine runtime failed its package-only transition or changed assembly identity.");
if (Harborline.Blocks.Workflow.Durable.CapabilityAuthorityRegistry.Canonical.AuthorityOf("ledger.post-journal-entry")
    != Harborline.Blocks.Workflow.Durable.ActionClassification.CP)
    throw new InvalidOperationException("Packed workflow engine authority registry lost its canonical CP row.");
if (typeof(Harborline.Foundation.RuleAuthoring.SkinLowering).Assembly.GetName().Name != "Harborline.Foundation.RuleAuthoring")
    throw new InvalidOperationException("Rule Authoring assembly identity changed.");
if (typeof(Harborline.Blocks.EntityViews.IEntityReadStore).Assembly.GetName().Name != "Harborline.Blocks.EntityViews")
    throw new InvalidOperationException("Entity Views assembly identity changed.");
var packedPreviewViews = new Harborline.Blocks.EntityViews.DevelopmentPreviewAdapter("Development", TimeProvider.System);
var packedViewEntities = await packedPreviewViews.ListEntitiesAsync(null);
if (!packedViewEntities.Any(entity => entity.Id == "entity:preview/water-heater-1"))
    throw new InvalidOperationException("Packed entity-views preview adapter lost its pinned seed tree.");
if (await packedPreviewViews.GetEntityAsync("entity:missing") is not null)
    throw new InvalidOperationException("Packed entity-views read store violated the 404-only-null invariant.");
var packedViewCrumbs = await new Harborline.Blocks.EntityViews.BreadcrumbResolver(packedPreviewViews)
    .ResolveAsync(["entity:preview/building-1", "entity:unresolved"]);
if (packedViewCrumbs[0].Label != "Harborline House"
    || packedViewCrumbs[1].Label != Harborline.Blocks.EntityViews.BreadcrumbResolver.UnresolvedLabel
    || packedViewCrumbs.Any(crumb => crumb.Label.StartsWith("entity:", StringComparison.Ordinal)))
    throw new InvalidOperationException("Packed entity-views breadcrumb resolver rendered a raw entity id.");
try
{
    _ = new Harborline.Blocks.EntityViews.DevelopmentPreviewAdapter("Production", TimeProvider.System);
    throw new InvalidOperationException("Packed entity-views preview adapter did not fail closed in Production.");
}
catch (Harborline.Blocks.EntityViews.EntityViewsException error)
    when (error.Code == Harborline.Blocks.EntityViews.EntityViewsCodes.ProductionPreviewForbidden)
{
}
if (typeof(Harborline.Foundation.Scheduling.IRruleExpansionService).Assembly.GetName().Name != "Harborline.Foundation.Scheduling")
    throw new InvalidOperationException("Foundation Scheduling assembly identity changed.");
if (typeof(Harborline.Blocks.Scheduling.IScheduleReservationCoordinator).Assembly.GetName().Name != "Harborline.Blocks.Scheduling")
    throw new InvalidOperationException("Blocks Scheduling assembly identity changed.");
var packedRrule = new Harborline.Foundation.Scheduling.InMemoryRruleExpansionService();
var packedFirstOfMonth = packedRrule.ExpandOccurrences(
    "FREQ=MONTHLY;BYMONTHDAY=1", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 365, 0, new DateOnly(2026, 1, 1), "UTC");
if (packedFirstOfMonth.Count != 3 || packedFirstOfMonth[2] != new DateOnly(2026, 3, 1))
    throw new InvalidOperationException("Packed RRULE expander lost the monthly BYMONTHDAY semantics.");
var packedDueQueue = new Harborline.Foundation.Scheduling.RruleDueQueueQueryService(packedRrule);
var packedDueSeam = new Harborline.Foundation.Scheduling.CompositeDueOccurrenceSource(
[
    new Harborline.Foundation.Scheduling.RruleDueOccurrenceSource(packedDueQueue,
    [
        new Harborline.Foundation.Scheduling.DueQueueSchedule("subject:consumer", "schedule:daily", "FREQ=DAILY", new DateOnly(2026, 7, 1), null, "UTC", true),
    ]),
]);
var packedDueItems = packedDueSeam.DeriveDue([], new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 2));
if (packedDueItems.Count != 2 || packedDueItems[0].OverdueDays < packedDueItems[^1].OverdueDays)
    throw new InvalidOperationException("Packed due-occurrence seam lost the overdue-first queue ordering.");
if (typeof(Harborline.Blocks.Calendar.Services.CalendarContextQuery).Assembly.GetName().Name != "Harborline.Blocks.Calendar")
    throw new InvalidOperationException("Blocks Calendar assembly identity changed.");
if (typeof(Harborline.Blocks.Reports.IReportRunner).Assembly.GetName().Name != "Harborline.Blocks.Reports")
    throw new InvalidOperationException("Blocks Reports assembly identity changed.");
var packedSnapshotMarker = new Harborline.Blocks.Reports.InMemorySnapshotMarkerSource();
var packedMarker = await packedSnapshotMarker.CaptureAsync(new Harborline.Foundation.Assets.Common.TenantId("tenant-consumer"));
if (string.IsNullOrEmpty(packedMarker))
    throw new InvalidOperationException("Packed reports snapshot-marker source returned an empty marker.");
if (typeof(Harborline.Blocks.ActivityTimeline.IActivityEntrySource).Assembly.GetName().Name != "Harborline.Blocks.ActivityTimeline")
    throw new InvalidOperationException("Blocks ActivityTimeline assembly identity changed.");
if (typeof(Harborline.Blocks.ActivityTimeline.ActivityEntry).GetProperty("ProposedByActorId") is not null)
    throw new InvalidOperationException("Packed ActivityEntry exposes a raw actor id — the sealing FAILED condition.");
var packedAuthoringCatalog = new Harborline.Foundation.RuleAuthoring.RuleCatalog(
    new Harborline.Foundation.RuleAuthoring.InMemoryRuleCatalogStore());
var packedBlankTable = Harborline.Foundation.RuleAuthoring.RuleSeeds.BlankTableDraft();
await packedAuthoringCatalog.CreateRuleAsync(
    "consumer-route", "Consumer route", Harborline.Foundation.RuleAuthoring.RuleSkinType.Table, packedBlankTable);
var packedRefusedPublish = await Harborline.Foundation.RuleAuthoring.PublishAdmission.PublishRuleAsync(
    packedAuthoringCatalog, "consumer-route", packedBlankTable);
if (packedRefusedPublish.Ok || packedRefusedPublish.Code != Harborline.Foundation.RuleEngine.Skins.SkinCodes.NoMatchUnresolved)
    throw new InvalidOperationException("Packed authoring fence admitted an unresolved no-match table.");
var packedResolvedTable = packedBlankTable with
{
    Rows =
    [
        new Harborline.Foundation.RuleAuthoring.TableRow(
            "r1",
            new Dictionary<string, Harborline.Foundation.RuleAuthoring.TableCell>
            {
                [packedBlankTable.Columns[0].Id] = new Harborline.Foundation.RuleAuthoring.TableCell.Range("0", "100"),
            },
            "low",
            0),
    ],
    NoMatch = new Harborline.Foundation.RuleAuthoring.NoMatchPosture.Default("high"),
};
var packedAdmittedPublish = await Harborline.Foundation.RuleAuthoring.PublishAdmission.PublishRuleAsync(
    packedAuthoringCatalog, "consumer-route", packedResolvedTable);
if (!packedAdmittedPublish.Ok || packedAdmittedPublish.Version != "1.0.0")
    throw new InvalidOperationException("Packed authoring fence failed to mint 1.0.0 for a resolved table.");
if (typeof(ITenantContext).Assembly.GetName().Name != "Harborline.Foundation.MultiTenancy")
    throw new InvalidOperationException("MultiTenancy assembly identity changed.");
if (typeof(IPartyContext).Assembly.GetName().Name != "Harborline.Foundation.Authorization")
    throw new InvalidOperationException("Authorization assembly identity changed.");
var tenantContext = new ConsumerTenantContext(new TenantMetadata
{
    Id = new TenantId("tenant-consumer"),
    Name = "tenant-consumer",
});
var visible = new[]
{
    new ConsumerTenantRow(new TenantId("tenant-consumer"), "visible"),
    new ConsumerTenantRow(new TenantId("tenant-other"), "hidden"),
}.AsQueryable().WhereTenant(tenantContext).Single();
if (visible.Name != "visible")
    throw new InvalidOperationException("Packed tenant filter did not isolate the active tenant.");
var actor = new ConsumerActorContext(tenantContext.Tenant, "alice", ["inspector"]);
var expectedPartyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var partyContext = new PartyContext(
    actor,
    new ConsumerPartyResolver(new TenantId("tenant-consumer"), "alice", expectedPartyId));
if (await partyContext.GetCurrentPartyIdAsync() != expectedPartyId)
    throw new InvalidOperationException("Packed actor package did not resolve the current tenant-bound principal.");
if (typeof(BuilderDefinitions.IDefinitionLifecycleStore).Assembly.GetName().Name != "Harborline.Blocks.BuilderDefinitions")
    throw new InvalidOperationException("Builder Definitions assembly identity changed.");
if (BuilderDefinitions.DefinitionKeySuggester.Suggest("Tenant Intake", new HashSet<string>()) != "tenant-intake.v1")
    throw new InvalidOperationException("Packed Builder Definitions key suggestion changed.");
if (typeof(BuilderDefinitions.IDefinitionKeyAuthority).IsInterface is false)
    throw new InvalidOperationException("Packed Builder Definitions key-authority seam is absent.");
if (typeof(FormsState.IFormDefinitionStore).Assembly.GetName().Name != "Harborline.Foundation.Forms")
    throw new InvalidOperationException("Forms state assembly identity changed.");
var formsStateStore = new FormsState.InMemoryFormDefinitionStore();
var formsHostServices = new ServiceCollection();
formsHostServices.AddSingleton<FormsState.IFormDefinitionStore>(formsStateStore);
formsHostServices.AddHarborlineFormsAuthoringPublisher();
await using var formsHostProvider = formsHostServices.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var formsHostScope = formsHostProvider.CreateAsyncScope();
var formsAuthoringPublisher = formsHostScope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
if (typeof(FormDefinitionAuthoringPublisher).Assembly.GetName().Name != "Harborline.Foundation.Forms.Engine")
    throw new InvalidOperationException("Forms Engine assembly identity changed.");
var definitionVersion = FormsModel.SemanticVersion.Parse("1.0.0");
var stateDefinition = new FormsModel.FormDefinition(
    new FormsModel.FormDefinitionId("inspection"),
    definitionVersion,
    FormsModel.FormDefinitionStatus.Draft,
    new TenantId("tenant-consumer"),
    FormsModel.IdentityRef.System,
    new Harborline.Foundation.Assets.Common.SchemaId("sha256:consumer-inspection"),
    new FormsModel.HarborlineOverlay(
        new Dictionary<string, FormsModel.FieldOverlay>
        {
            ["condition"] = new(FormsModel.InternationalizedText.FromInvariant("Condition")),
        },
        [new FormsModel.FormSection(
            "inspection",
            FormsModel.InternationalizedText.FromInvariant("Inspection"),
            ["condition"],
            new FormsModel.SectionAccess(
                [Harborline.Contracts.Authorization.RoleReference.Domain("inspector")],
                [Harborline.Contracts.Authorization.RoleReference.Domain("inspector")]))],
        Array.Empty<FormsModel.RuleDefinition>()),
    null,
    DateTimeOffset.UnixEpoch,
    DateTimeOffset.UnixEpoch,
    new FormsModel.FormDefinitionAuthoring(
        new Dictionary<string, FormsModel.FormFieldAuthoringMetadata>
        {
            ["condition"] = new(
                "radio",
                true,
                [new FormsModel.FormFieldValidation("required")],
                ["PASS", "FAIL"]),
        }));
await formsAuthoringPublisher.RegisterAndPublishAsync(stateDefinition);
var packedStateDefinition = await formsStateStore.GetCurrentPublishedAsync(stateDefinition.Tenant, stateDefinition.Id);
if (packedStateDefinition?.Version != definitionVersion)
    throw new InvalidOperationException("Packed Forms state did not preserve immutable lifecycle versions.");
if (packedStateDefinition.Authoring?.Fields["condition"].Type != "radio")
    throw new InvalidOperationException("Packed Forms state lost exact Harborline App authoring metadata.");
var rejectedDefinition = stateDefinition with
{
    Id = new FormsModel.FormDefinitionId("inspection-malformed"),
    Overlay = stateDefinition.Overlay with
    {
        Rules =
        [
            new FormsModel.RuleDefinition(
                "condition.compute",
                FormsModel.RuleTier.JsonLogic,
                FormsModel.RuleScope.Field,
                "condition",
                "{not-json",
                FormsModel.RuleActionKind.Compute),
        ],
    },
};
try
{
    await formsAuthoringPublisher.RegisterAndPublishAsync(rejectedDefinition);
    throw new InvalidOperationException("Packed Forms Engine authoring boundary persisted an uncompilable rule.");
}
catch (FormsState.Exceptions.FormDefinitionValidationException error)
    when (error.Code == FormsState.FormDefinitionCodes.RulesUncompilable)
{
}
try
{
    await formsStateStore.GetAsync(rejectedDefinition.Tenant, rejectedDefinition.Id, rejectedDefinition.Version);
    throw new InvalidOperationException("Packed Forms Engine authoring rejection left a stored definition.");
}
catch (FormsState.Exceptions.FormDefinitionNotFoundException)
{
}
var draftService = new FormsDrafts.SubmissionDraftService(
    new FormsDrafts.AuthenticatedFormsActorScope(
        actor,
        new ConsumerPartyResolver(new TenantId("tenant-consumer"), "alice", expectedPartyId)),
    new FormsDrafts.InMemorySubmissionDraftStore(),
    TimeProvider.System);
var draftCase = FormsDrafts.DraftCaseId.NewId();
await draftService.SaveDraftAsync(
    stateDefinition.Id,
    draftCase,
    FormsDrafts.SubmissionDraftProvenance.Create(stateDefinition, ["en-US"]),
    System.Text.Encoding.UTF8.GetBytes("{\"condition\":\"PASS\"}"));
if ((await draftService.ResumeDraftAsync(stateDefinition.Id, draftCase))?.Key.PartyId != expectedPartyId)
    throw new InvalidOperationException("Packed Forms state did not tenant-and-party scope save/resume drafts.");
if (typeof(ISessionStore).Assembly.GetName().Name != "Harborline.Foundation.Session")
    throw new InvalidOperationException("Session assembly identity changed.");
var sessionStore = new InMemorySessionStore();
await sessionStore.CreateAsync(new SessionRecord
{
    SessionId = "session-consumer",
    UserId = "alice",
    TenantId = new TenantId("tenant-consumer"),
    IssuedUtc = DateTimeOffset.UnixEpoch,
    AbsoluteExpiryUtc = DateTimeOffset.UnixEpoch.AddHours(8),
    LastSeenUtc = DateTimeOffset.UnixEpoch,
    Reason = SessionEstablishmentReason.ExternalIdentity,
});
var sessionActor = await new SessionResolver(sessionStore).ResolveAsync(
    "session-consumer",
    tenantContext,
    ["inspector"],
    DateTimeOffset.UnixEpoch.AddMinutes(1),
    TimeSpan.FromMinutes(30));
if (sessionActor?.UserId != "alice" || sessionActor.Tenant?.Id != new TenantId("tenant-consumer"))
    throw new InvalidOperationException("Packed session package did not resolve the active tenant-bound actor.");
if (typeof(ISchemaRegistry).Assembly.GetName().Name != "Harborline.Kernel.SchemaValidation")
    throw new InvalidOperationException("Schema validation assembly identity changed.");
var schemaRegistry = new InMemorySchemaRegistry();
var packedSchema = await schemaRegistry.RegisterAsync("""{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""");
var packedValidation = await schemaRegistry.ValidateAsync(
    packedSchema.Id,
    System.Text.Encoding.UTF8.GetBytes("""{}"""));
if (packedValidation.IsValid || !packedValidation.Errors.Any(error => error is { Code: "required", JsonPointer: "/name" }))
    throw new InvalidOperationException("Packed schema package did not fail closed with a field-addressable required error.");

var packedWorkItemStore = new InMemoryWorkItemStore();
if (typeof(IInspectionReviewService).Assembly.GetName().Name != "Harborline.Blocks.InspectionReview")
    throw new InvalidOperationException("Inspection Review assembly identity changed.");
var packedWorkItemKernel = new WorkItemKernel(actor, partyContext, packedWorkItemStore, TimeProvider.System);
var packedWorkItem = await packedWorkItemKernel.CreateAsync(new CreateWorkItemRequest
{
    Id = "inspection-review-consumer",
    SubjectRef = "inspection-submission:consumer",
    DefinitionKey = "inspection-review",
    DefinitionVersion = "1",
    InitialStep = "review",
    StateJson = "{\"condition\":\"attention\"}",
    BasisJson = "{\"score\":7}",
    AllowedOutcomes = [new WorkItemOutcome("approve", "review", "done", WorkItemStatus.Completed)],
    IdempotencyKey = "consumer-create-1",
});
if (!packedWorkItem.IsSuccess || typeof(IWorkItemKernel).Assembly.GetName().Name != "Harborline.Kernel.WorkItems")
    throw new InvalidOperationException("Packed work-item kernel failed tenant-scoped creation or changed assembly identity.");

Console.WriteLine("packed NuGet aggregate loaded Harborline App waves through wave-03-05, including Chart, Chat, Data Grid, Gantt, and Numeric Text Box");

enum ConsumerFlowState { Draft, Done }

enum ConsumerFlowTrigger { Approve }

sealed record ConsumerTenantRow(TenantId TenantId, string Name) : IMustHaveTenant;

sealed record ConsumerTenantContext(TenantMetadata? Tenant) : ITenantContext;

sealed record ConsumerActorContext(
    TenantMetadata? Tenant,
    string UserId,
    IReadOnlyList<string> Roles) : IAuthenticatedActorContext;

sealed class ConsumerPartyResolver(
    TenantId expectedTenant,
    string expectedUser,
    Guid partyId) : IPrincipalPartyResolver
{
    public ValueTask<Guid?> ResolveAsync(
        string userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Guid?>(
            userId == expectedUser && tenantId == expectedTenant ? partyId : null);
}
