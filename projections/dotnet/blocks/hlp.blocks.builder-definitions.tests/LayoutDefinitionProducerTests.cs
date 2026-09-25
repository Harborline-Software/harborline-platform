using System.Text;
using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class LayoutDefinitionProducerTests
{
    // Publication resolves the fixture's named validation rule (T-724 ruling 36).
    private static readonly LayoutHostRegisters Hosted = new(LayoutBlockKindRegistry.Platform, ValidationRules: new LayoutValidationRuleRegistry(
    [
        new Harborline.Contracts.Forms.RuleDefinition
        {
            Id = "customer.name.required",
            Tier = Harborline.Contracts.Forms.RuleTier.JsonLogic,
            Scope = Harborline.Contracts.Forms.RuleScope.Schema,
            ScopeTarget = "",
            Expression = "{\"!!\":[{\"var\":\"customer.name\"}]}",
            Action = Harborline.Contracts.Forms.RuleActionKind.Validate,
        },
    ]));

    [Theory]
    [InlineData(LayoutCompositionKind.Form)]
    [InlineData(LayoutCompositionKind.Template)]
    [InlineData(LayoutCompositionKind.Report)]
    public void CompositionDetachCreatesAnIndependentCandidateAndKeepsTheNamedArtefact(LayoutCompositionKind kind)
    {
        var definition = kind == LayoutCompositionKind.Template ? PageDefinition() : ScreenDefinition();
        var before = LayoutDefinitionJson.SerializeCanonical(definition);
        var reference = new LayoutCompositionReference(kind, "composition.customer", "2.0.0", "surface.customer", "1.0.0");
        var detached = LayoutComposition.Detach(reference, definition,
            definition.Envelope with { Identity = "surface.detached", Version = "1.0.0" }, LayoutTestAccess.GrantsAll);
        Assert.Equal("surface.detached", detached.Envelope.Identity);
        Assert.Equal("composition.customer", reference.DefinitionId);
        Assert.Equal("2.0.0", reference.Version);
        Assert.IsAssignableFrom<IList<LayoutBlock>>(detached.Blocks).Clear();
        Assert.Equal(before, LayoutDefinitionJson.SerializeCanonical(definition));
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData(" ", "1.0.0")]
    [InlineData("surface.customer", "")]
    [InlineData("surface.customer", "1.0.0+")]
    [InlineData("surface.customer", "1.0.0-alpha.01")]
    public void CompositionDetachRefusesMalformedSurfacePinsEvenWhenTheyMatchTheSource(string identity, string version)
    {
        var definition = ScreenDefinition();
        var source = definition with { Envelope = definition.Envelope with { Identity = identity, Version = version } };
        var reference = new LayoutCompositionReference(LayoutCompositionKind.Form, "composition.customer", "2.0.0", identity, version);
        var draft = definition.Envelope with { Identity = "surface.detached", Version = "1.0.0" };

        var error = Assert.Throws<ArgumentException>(() => LayoutComposition.Detach(reference, source, draft, LayoutTestAccess.GrantsAll));

        Assert.Equal("reference", error.ParamName);
        Assert.StartsWith("layout.composition.detach_invalid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedContainersAndSubmitGatesRefuseMalformedAuthoring()
    {
        AssertRefusal(ScreenDefinition(block => block.Id == "query" ? block with { Container = null } : block),
            LayoutDefinitionCodes.ScopedContainerInvalid, "/blocks/0/children/1/repeating");
        AssertRefusal(ScreenDefinition() with { DefaultIntent = LayoutIntent.Observe },
            LayoutDefinitionCodes.SubmitGateInvalid, "/submit_gate");
        AssertRefusal(ScreenDefinition() with { SchemaVersion = 0 }, LayoutDefinitionCodes.EnvelopeInvalid, "/schema_version");
        var mixed = ScreenDefinition(block => block.Id == "static"
            ? block with { Intent = LayoutIntent.Issue, Binding = new LayoutRecordFieldBinding("customer.name") } : block);
        LayoutDefinitionAdmission.ValidateForPublish(mixed, Hosted, LayoutTestAccess.GrantsAll);
    }

    [Fact(DisplayName = "layout-ck-43, layout-ck-44: a text binding of literal and field runs, with a field run's fallback, round-trips and refuses a malformed run or capture")]
    public void ATextBindingRoundTripsAndRefusesAMalformedRunOrCapture()
    {
        var text = new LayoutTextBinding([new LayoutTextRun(Text: "Bill to: "), new LayoutTextRun(FieldPath: "customer.name", Fallback: "Customer")]);
        var definition = PageDefinition(block => block.Id == "document" ? block with { Binding = text } : block);
        LayoutDefinitionAdmission.ValidateForPublish(definition, Hosted, LayoutTestAccess.GrantsAll);

        var canonical = LayoutDefinitionJson.SerializeCanonical(definition);
        var json = Encoding.UTF8.GetString(canonical);
        Assert.Contains("{\"binding_kind\":\"text\",\"runs\":[{\"text\":\"Bill to: \"},{\"fallback\":\"Customer\",\"field_path\":\"customer.name\"}]}", json, StringComparison.Ordinal);
        Assert.Equal(canonical, LayoutDefinitionJson.SerializeCanonical(LayoutDefinitionJson.Deserialize(canonical)));

        AssertRefusal(PageDefinition(block => block.Id == "document" ? block with { Binding = new LayoutTextBinding([]) } : block),
            LayoutDefinitionCodes.BindingInvalid, "/blocks/0/children/1/binding/runs");
        AssertRefusal(PageDefinition(block => block.Id == "document" ? block with { Binding = new LayoutTextBinding([new LayoutTextRun(FieldPath: " ")]) } : block),
            LayoutDefinitionCodes.BindingInvalid, "/blocks/0/children/1/binding/runs/0");
        // A run is exactly one of a literal or a field; a fallback belongs to a field run only.
        foreach (var malformed in new[] { new LayoutTextRun(), new LayoutTextRun(Text: "x", FieldPath: "customer.name"), new LayoutTextRun(Text: "x", Fallback: "y") })
            AssertRefusal(PageDefinition(block => block.Id == "document" ? block with { Binding = new LayoutTextBinding([new LayoutTextRun(Text: "ok"), malformed]) } : block),
                LayoutDefinitionCodes.BindingInvalid, "/blocks/0/children/1/binding/runs/1");
        // Composed text is output: a capture block cannot bind it.
        AssertRefusal(ScreenDefinition(block => block.Id == "capture" ? block with { Binding = text } : block),
            LayoutDefinitionCodes.IntentBindingUnsupported, "/blocks/0/children/0/binding");
    }

    [Fact(DisplayName = "layout-auth-25 (amended 2026-09-25): an issue block on page media reads a record field to render it; a screen surface that captures nothing still refuses one, and page media still never capture")]
    public void AnIssueBlockOnPageMediaReadsARecordField()
    {
        // ADR 0092 decision 1's invoice: record fields, intent issue, page media.
        var invoice = PageDefinition(block => block.Id == "document"
            ? block with { Binding = new LayoutRecordFieldBinding("customer.name") } : block);
        LayoutDefinitionAdmission.ValidateForPublish(invoice, Hosted, LayoutTestAccess.GrantsAll);

        // Read-for-rendering only: the same block on a screen that captures nothing still refuses,
        AssertRefusal(invoice with { Medium = LayoutMedium.Screen, PageLayouts = [], PageMasters = [], PageRuns = [] },
            LayoutDefinitionCodes.IntentBindingUnsupported, "/blocks/0/children/1/binding");
        // and reading grants no capture: a capture block on page media refuses as before (layout-auth-28).
        AssertRefusal(PageDefinition(block => block.Id == "document"
            ? block with { Binding = new LayoutRecordFieldBinding("customer.name"), Intent = LayoutIntent.Capture } : block),
            LayoutDefinitionCodes.CaptureOnPage, "/blocks/0/children/1/intent");
    }

    [Fact]
    public void HostKindRegisterIsSharedByProducerAndBothPersistedAdmissions()
    {
        var definition = ScreenDefinition(block => block with { Kind = "host.component" });
        var kinds = new LayoutBlockKindRegistry(["host.component"]);
        LayoutDefinitionAdmission.ValidateForAuthoring(definition, kinds, LayoutTestAccess.GrantsAll);
        var entry = LayoutDefinitionPackageExporter.Export(definition, Hosted with { Kinds = kinds });
        var roundTrip = LayoutDefinitionJson.Deserialize(entry.Content.Payload.Span);
        LayoutPersistedValueAdmission.ValidateForReact(roundTrip, kinds);
        LayoutPersistedValueAdmission.ValidateForBlazor(roundTrip, kinds);
        Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutPersistedValueAdmission.ValidateForReact(definition));
    }

    [Fact]
    public void UnknownKindsRefuseAndAbsentIntentUsesTheSurfaceDefault()
    {
        AssertRefusal(ScreenDefinition(block => block.Id == "query" ? block with { Kind = "unknown.component" } : block),
            LayoutDefinitionCodes.BlockKindUnknown, "/blocks/0/children/1/kind");
        var definition = ScreenDefinition(block => block.Id switch
        {
            "root" => block with { Intent = LayoutIntent.Capture },
            "query" => block with { Intent = null },
            _ => block,
        });
        LayoutDefinitionAdmission.ValidateForPublish(definition with { DefaultIntent = LayoutIntent.Observe, SubmitGate = null }, Hosted, LayoutTestAccess.GrantsAll);
    }

    [Fact]
    public void ExistingDefinitionKindWireValuesRemainStable()
    {
        Assert.Equal(0, (int)DefinitionKind.Forms);
        Assert.Equal(1, (int)DefinitionKind.Workflows);
        Assert.NotEqual(DefinitionKind.Forms, DefinitionKind.Layout);
        Assert.NotEqual(DefinitionKind.Workflows, DefinitionKind.Layout);
    }

    [Theory]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+bad+metadata")]
    [InlineData("1.0.0-alpha.01")]
    [InlineData("01.0.0")]
    public void MalformedSemanticVersionsRefuseAtAdmission(string version)
    {
        var definition = ScreenDefinition();
        AssertRefusal(definition with { Envelope = definition.Envelope with { Version = version } },
            LayoutDefinitionCodes.VersionInvalid, "/envelope/version");
    }

    [Fact(DisplayName = "layout-ck-9..14,38: the platform schema owns closed tokens and four inclusive numeric ranges")]
    public void SchemaOwnsClosedTokensAndNumericRanges()
    {
        Assert.Equal(["sm", "md", "lg"], LayoutDefinitionSchema.CollapseTokens);
        Assert.DoesNotContain("compact", LayoutDefinitionSchema.CollapseTokens);
        Assert.Equal(new LayoutNumericRange(1, 12), LayoutDefinitionSchema.Numeric(LayoutNumericMember.Span));
        Assert.Equal(new LayoutNumericRange(0, 12), LayoutDefinitionSchema.Numeric(LayoutNumericMember.Grow));
        Assert.Equal(new LayoutNumericRange(1, 12), LayoutDefinitionSchema.Numeric(LayoutNumericMember.ColumnCount));
        Assert.Equal(new LayoutNumericRange(0, 8), LayoutDefinitionSchema.Numeric(LayoutNumericMember.Gap));

        using var schema = JsonDocument.Parse(LayoutDefinitionSchema.CanonicalJson);
        Assert.Equal("https://schemas.harborline.software/layout/definition/v1", schema.RootElement.GetProperty("$id").GetString());
    }

    [Fact(DisplayName = "Layout definition members round-trip in tree order; lifecycle proof belongs to T-620")]
    public void DefinitionMembersRoundTripThroughCanonicalJsonInTreeOrder()
    {
        var definition = ScreenDefinition();

        LayoutDefinitionAdmission.ValidateForPublish(definition, Hosted, LayoutTestAccess.GrantsAll);
        var canonical = LayoutDefinitionJson.SerializeCanonical(definition);
        var roundTrip = LayoutDefinitionJson.Deserialize(canonical);

        Assert.Equal(canonical, LayoutDefinitionJson.SerializeCanonical(roundTrip));
        Assert.Equal("tenant-a", roundTrip.Envelope.Tenant);
        Assert.Equal(LayoutCascadeLayer.DomainPackage, roundTrip.Envelope.CascadeLayer);
        Assert.Equal("regulated", roundTrip.Envelope.RetentionClass);
        Assert.True(roundTrip.Envelope.LegalHold);
        Assert.Equal(["records", LayoutPackIdentity.Capability], roundTrip.Envelope.Requires.Select(requirement => requirement.Capability));
        Assert.Equal(LayoutMedium.Screen, roundTrip.Medium);
        Assert.Equal(LayoutIntent.Capture, roundTrip.DefaultIntent);
        Assert.Equal(["root", "capture", "query", "measure", "static"], Flatten(roundTrip.Blocks).Select(block => block.Id));

        var capture = Flatten(roundTrip.Blocks).Single(block => block.Id == "capture");
        Assert.Equal(LayoutIntent.Capture, capture.Intent);
        Assert.IsType<LayoutRecordFieldBinding>(capture.Binding);
        Assert.Equal("form.customer", capture.Form!.FormDefinitionId);
        Assert.Equal("form.customer@2.1.0", capture.Form.FormVersionId);
        Assert.True(capture.Capture!.Required);
        Assert.Equal(["customer.name.required"], capture.Capture.ValidationRules);
        Assert.Equal("Customer name", capture.Capture.PromptOverride);
        Assert.Equal(LayoutAlignment.Start, capture.Placement!.JustifySelf);
        Assert.Equal(LayoutAlignment.Center, capture.Placement.AlignSelf);

        var query = Flatten(roundTrip.Blocks).Single(block => block.Id == "query");
        Assert.IsType<LayoutQueryBinding>(query.Binding);
        Assert.True(query.Repeating);
        Assert.Equal("customer.orders", query.RelatedRelationship);
        Assert.Equal("{\"!!\":[{\"var\":\"field.customer.name\"}]}", query.ShowWhen!.Expression);
        Assert.Null(query.ShowWhen.Predicate);
        Assert.IsType<LayoutMeasureBinding>(Flatten(roundTrip.Blocks).Single(block => block.Id == "measure").Binding);
        var staticBlock = Flatten(roundTrip.Blocks).Single(block => block.Id == "static");
        Assert.IsType<LayoutStaticBinding>(staticBlock.Binding);
        Assert.True(staticBlock.BreakAfter);
        Assert.Equal(["query"], Flatten(roundTrip.Blocks).Single(block => block.Id == "measure").FilterTargets);
        Assert.Equal(["surface.customer-detail"], roundTrip.DrillThroughTargets);
        Assert.Equal("role.customer-editor", roundTrip.SubmitGate);

        var json = Encoding.UTF8.GetString(canonical);
        Assert.Contains("\"form_definition_id\":\"form.customer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"form_version_id\":\"form.customer@2.1.0\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latest", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"order\"", json, StringComparison.Ordinal);
        // layout-ck-29: the guard is an object holding its one form, never a bare string.
        Assert.Contains("\"show_when\":{\"expression\":\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("is_well_formed", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "layout-ck-16..20,24,33: page geometry, masters, static regions and page runs round-trip")]
    public void PageDefinitionsAndStaticRegionsRoundTrip()
    {
        var definition = PageDefinition();

        LayoutDefinitionAdmission.ValidateForPublish(definition, Hosted, LayoutTestAccess.GrantsAll);
        var roundTrip = LayoutDefinitionJson.Deserialize(LayoutDefinitionJson.SerializeCanonical(definition));

        Assert.Equal(LayoutMedium.Page, roundTrip.Medium);
        Assert.Equal("a4", Assert.Single(roundTrip.PageLayouts).Sheet);
        Assert.Equal(LayoutPageOrientation.Portrait, Assert.Single(roundTrip.PageLayouts).Orientation);
        Assert.Equal("page.invoice", Assert.Single(roundTrip.PageMasters).PageLayoutId);
        Assert.Equal("header.center", Assert.Single(roundTrip.PageMasters).First.CenterRegion);
        Assert.Equal(["page-root"], Assert.Single(roundTrip.PageRuns).BlockIds);
        var template = Assert.IsType<LayoutTemplateBinding>(Flatten(roundTrip.Blocks).Single(block => block.Id == "document").Binding);
        Assert.Equal("template.invoice", template.TemplateDefinitionId);
        var header = Flatten(roundTrip.Blocks).Single(block => block.Id == "header");
        Assert.Equal(LayoutFlowRole.Static, header.FlowRole);
        Assert.Equal("header.center", header.StaticRegion);
        Assert.Equal(LayoutBreakInside.AvoidPage, Flatten(roundTrip.Blocks).Single(block => block.Id == "document").BreakInside);
    }

    [Fact]
    public void PageRunsRefuseUnknownGeometryAndDuplicateRunIdentities()
    {
        var definition = PageDefinition();
        AssertRefusal(definition with { PageRuns = [definition.PageRuns[0] with { PageLayoutId = "missing" }] },
            LayoutDefinitionCodes.PageReferenceUnknown, "/page_runs/0/page_layout_id");
        AssertRefusal(definition with { PageRuns = [definition.PageRuns[0], definition.PageRuns[0]] },
            LayoutDefinitionCodes.PageDefinitionInvalid, "/page_runs/1");
        AssertRefusal(definition with { PageLayouts = [null!] },
            LayoutDefinitionCodes.PageDefinitionInvalid, "/page_layouts/0");
    }

    [Fact]
    public void InteractionRefusalsIdentifyTheirArrayElement()
    {
        AssertRefusal(ScreenDefinition(block => block.Id == "measure"
            ? block with { FilterTargets = ["query", "missing"] } : block),
            LayoutDefinitionCodes.InteractionTargetUnknown, "/blocks/0/children/2/filter_targets/1");
        AssertRefusal(ScreenDefinition() with { DrillThroughTargets = ["surface.customer-detail", ""] },
            LayoutDefinitionCodes.InteractionTargetUnknown, "/drill_through_targets/1");
    }

    [Fact(DisplayName = "layout-eng-25,27: validate, publish, React and Blazor admission use the same schema bounds")]
    public void NumericEndpointsAndBothPersistedAdmissionsUseTheSchema()
    {
        foreach (var member in Enum.GetValues<LayoutNumericMember>())
        {
            var range = LayoutDefinitionSchema.Numeric(member);
            var minimum = WithNumeric(ScreenDefinition(), member, range.Minimum);
            var maximum = WithNumeric(ScreenDefinition(), member, range.Maximum);
            var below = WithNumeric(ScreenDefinition(), member, range.Minimum - 1);
            var above = WithNumeric(ScreenDefinition(), member, range.Maximum + 1);

            LayoutDefinitionAdmission.ValidateForAuthoring(minimum, LayoutTestAccess.GrantsAll);
            LayoutDefinitionAdmission.ValidateForAuthoring(maximum, LayoutTestAccess.GrantsAll);
            LayoutDefinitionAdmission.ValidateForPublish(minimum, Hosted, LayoutTestAccess.GrantsAll);
            LayoutDefinitionAdmission.ValidateForPublish(maximum, Hosted, LayoutTestAccess.GrantsAll);
            LayoutPersistedValueAdmission.ValidateForReact(minimum);
            LayoutPersistedValueAdmission.ValidateForReact(maximum);
            LayoutPersistedValueAdmission.ValidateForBlazor(minimum);
            LayoutPersistedValueAdmission.ValidateForBlazor(maximum);

            AssertRangeRefusal(() => LayoutDefinitionAdmission.ValidateForAuthoring(below, LayoutTestAccess.GrantsAll), member, "definition.validate");
            AssertRangeRefusal(() => LayoutDefinitionAdmission.ValidateForAuthoring(above, LayoutTestAccess.GrantsAll), member, "definition.validate");
            AssertRangeRefusal(() => LayoutDefinitionAdmission.ValidateForPublish(below, Hosted, LayoutTestAccess.GrantsAll), member, "definition.publish");
            AssertRangeRefusal(() => LayoutDefinitionAdmission.ValidateForPublish(above, Hosted, LayoutTestAccess.GrantsAll), member, "definition.publish");
            AssertRangeRefusal(() => LayoutPersistedValueAdmission.ValidateForReact(below), member, "render.react");
            AssertRangeRefusal(() => LayoutPersistedValueAdmission.ValidateForReact(above), member, "render.react");
            AssertRangeRefusal(() => LayoutPersistedValueAdmission.ValidateForBlazor(below), member, "render.blazor");
            AssertRangeRefusal(() => LayoutPersistedValueAdmission.ValidateForBlazor(above), member, "render.blazor");
        }
    }

    [Fact(DisplayName = "layout-auth-25,27,28,37: unsupported intent, page capture, unknown tokens, pixels and live selection refuse by code and pointer")]
    public void InvalidAuthoringMutationsRefuseByCodeAndPointer()
    {
        AssertRefusal(
            ScreenDefinition(block => block.Id == "capture" ? block with { Binding = new LayoutMeasureBinding("revenue.total") } : block),
            LayoutDefinitionCodes.IntentBindingUnsupported,
            "/blocks/0/children/0/binding");
        AssertRefusal(
            PageDefinition(block => block.Id == "document" ? block with { Intent = LayoutIntent.Capture } : block),
            LayoutDefinitionCodes.CaptureOnPage,
            "/blocks/0/children/1/intent");
        AssertRefusal(
            ScreenDefinition(block => block.Id == "root"
                ? block with { Container = block.Container! with { CollapseBelow = (LayoutCollapseToken)999 } }
                : block),
            LayoutDefinitionCodes.PlacementTokenUnknown,
            "/blocks/0/container/collapse_below");
        AssertRefusal(
            ScreenDefinition(block => block.Id == "capture"
                ? block with { Placement = block.Placement! with { PixelPosition = "12px" } }
                : block),
            LayoutDefinitionCodes.PixelPlacementForbidden,
            "/blocks/0/children/0/placement/pixel_position");
        AssertRefusal(
            ScreenDefinition(block => block.Id == "query"
                ? block with { LiveSelection = JsonSerializer.SerializeToElement(new { row = "customer-42" }) }
                : block),
            LayoutDefinitionCodes.LiveSelectionForbidden,
            "/blocks/0/children/1/live_selection");
    }

    [Fact(DisplayName = "layout-ck-41 (owner ruling 2026-09-24): an omitted span, span 1 and fill are three distinct persisted states")]
    public void AnOmittedSpanSpanOneAndFillPersistDistinctly()
    {
        // The capture block's persisted placement object, after publish admission and a canonical round trip.
        System.Text.Json.Nodes.JsonObject Persisted(LayoutPlacement placement)
        {
            var definition = ScreenDefinition(block => block.Id == "capture" ? block with { Placement = placement } : block);
            LayoutDefinitionAdmission.ValidateForPublish(definition, Hosted, LayoutTestAccess.GrantsAll);
            var root = System.Text.Json.Nodes.JsonNode.Parse(LayoutDefinitionJson.SerializeCanonical(definition))!;
            var capture = root["blocks"]![0]!["children"]!.AsArray().Single(child => (string?)child!["id"] == "capture")!;
            return capture["placement"]!.AsObject();
        }

        Assert.False(Persisted(new LayoutPlacement()).ContainsKey("span"));
        Assert.Equal(1, (int)Persisted(new LayoutPlacement(Span: 1))["span"]!);
        var fill = Persisted(new LayoutPlacement(Width: LayoutSizing.Fill));
        Assert.False(fill.ContainsKey("span"));
        Assert.Equal("fill", (string?)fill["width"]);
    }

    [Fact(DisplayName = "layout-ck-41 (owner ruling 2026-09-24): a span beside fill refuses")]
    public void ASpanBesideFillRefuses()
    {
        AssertRefusal(ScreenDefinition(block => block.Id == "capture"
                ? block with { Placement = new LayoutPlacement(Width: LayoutSizing.Fill, Span: 4) } : block),
            LayoutDefinitionCodes.SpanWithFill, "/blocks/0/children/0/placement/span");
    }

    [Fact(DisplayName = "T-582 item 5: all five bindings survive pack export and host admission as references")]
    public void AllFiveBindingsSurvivePackExportAndInstallAsReferences()
    {
        var pack = new[]
        {
            LayoutDefinitionPackageExporter.Export(ScreenDefinition(), Hosted),
            LayoutDefinitionPackageExporter.Export(PageDefinition(), Hosted),
        };
        LayoutPackHostAdmission.Admit(pack, new Dictionary<string, string> { [LayoutPackIdentity.Capability] = "1.0.0" });

        var installed = pack
            .SelectMany(entry => Flatten(LayoutDefinitionJson.Deserialize(entry.Content.Payload.Span).Blocks))
            .Select(block => block.Binding)
            .ToArray();

        // Each kind still names what it binds; nothing was resolved into the payload.
        Assert.Contains(new LayoutQueryBinding("view.customer-orders"), installed);
        Assert.Contains(new LayoutMeasureBinding("orders.total"), installed);
        Assert.Contains(new LayoutTemplateBinding("template.invoice"), installed);
        Assert.Contains(installed, binding => binding is LayoutRecordFieldBinding { FieldPath: "customer.name" });
        Assert.Contains(installed, binding => binding is LayoutStaticBinding { Content.ValueKind: JsonValueKind.Object });
    }

    [Fact(DisplayName = "layout-ck-40: a repeating block admits bounds it can satisfy")]
    public void ARepeatingBlockAdmitsSatisfiableBounds()
    {
        LayoutDefinitionAdmission.ValidateForPublish(ScreenDefinition(block => block.Id == "query"
            ? block with { CollectionBounds = new LayoutCollectionBounds(0, 20) } : block), Hosted, LayoutTestAccess.GrantsAll);
    }

    [Theory(DisplayName = "layout-ck-40: malformed or misplaced collection bounds refuse")]
    [InlineData("query", -1, 5)]
    [InlineData("query", 3, 2)]
    [InlineData("measure", 0, 5)]
    public void MalformedOrMisplacedCollectionBoundsRefuse(string blockId, int minimum, int maximum)
    {
        var definition = ScreenDefinition(block => block.Id == blockId
            ? block with { CollectionBounds = new LayoutCollectionBounds(minimum, maximum) } : block);
        var pointer = blockId == "query" ? "/blocks/0/children/1/collection_bounds" : "/blocks/0/children/2/collection_bounds";

        AssertRefusal(definition, LayoutDefinitionCodes.CollectionBoundsInvalid, pointer);
    }

    [Fact(DisplayName = "layout-ck-42: publication refuses a Layout that does not declare platform.layout in its payload")]
    public void PublicationRefusesALayoutThatDoesNotDeclareItsCapability()
    {
        var definition = ScreenDefinition();
        var undeclared = definition with
        {
            Envelope = definition.Envelope with { Requires = [new LayoutDefinitionRequirement("records", "1.0.0")] },
        };

        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(undeclared, Hosted, LayoutTestAccess.GrantsAll));

        Assert.Equal("definition.publish", refused.Stage);
        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires")], refused.Refusals);
    }

    [Theory(DisplayName = "layout-ck-42: publication refuses platform.layout without an exact minimum platform version")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("latest")]
    public void PublicationRefusesTheCapabilityWithoutAnExactMinimumPlatformVersion(string? minimum)
    {
        var definition = ScreenDefinition();
        var unversioned = definition with
        {
            Envelope = definition.Envelope with
            {
                Requires = [new LayoutDefinitionRequirement("records", "1.0.0"), new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, minimum)],
            },
        };

        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(unversioned, Hosted, LayoutTestAccess.GrantsAll));

        Assert.Equal(
            [new LayoutDefinitionRefusal(LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires/1/minimum_platform_version")],
            refused.Refusals);
    }

    [Fact(DisplayName = "layout-ck-42: a second platform.layout declaration refuses at publish and at install")]
    public void ASecondCapabilityDeclarationRefusesAtPublishAndInstall()
    {
        var definition = ScreenDefinition();
        var doubled = definition with
        {
            Envelope = definition.Envelope with
            {
                Requires =
                [
                    new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0"),
                    new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "3.0.0"),
                ],
            },
        };

        var published = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(doubled, Hosted, LayoutTestAccess.GrantsAll));
        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.CapabilityUndeclared, "/envelope/requires")], published.Refusals);

        var entry = new LayoutDefinitionPackageEntry("surface.customer", "1.0.0",
            PlatformPackageContent.PresentJson(LayoutDefinitionJson.SerializeCanonical(doubled)));
        var installed = Assert.Throws<LayoutPackUnsupportedException>(() => LayoutPackHostAdmission.Admit(
            [entry], new Dictionary<string, string> { [LayoutPackIdentity.Capability] = "1.5.0" }));
        Assert.Equal(LayoutDefinitionCodes.CapabilityUndeclared, installed.Code);
    }

    [Fact(DisplayName = "layout-ck-42: a draft may omit the capability; only the sealed payload must carry it")]
    public void AuthoringDoesNotRequireTheCapability()
    {
        var definition = ScreenDefinition();

        LayoutDefinitionAdmission.ValidateForAuthoring(definition with { Envelope = definition.Envelope with { Requires = [] } }, LayoutTestAccess.GrantsAll);
    }

    [Theory(DisplayName = "layout-ck-42: a host at or above the sealed minimum admits the pack")]
    [InlineData("1.0.0")]
    [InlineData("1.4.2")]
    public void AHostThatSatisfiesTheSealedCapabilityAdmitsThePack(string hostVersion)
    {
        var pack = new[] { LayoutDefinitionPackageExporter.Export(ScreenDefinition(), Hosted) };

        LayoutPackHostAdmission.Admit(pack, new Dictionary<string, string> { [LayoutPackIdentity.Capability] = hostVersion });
    }

    [Theory(DisplayName = "layout-ck-42: a host lacking the capability or the version refuses the whole pack, naming both")]
    [InlineData(null)]
    [InlineData("0.9.0")]
    [InlineData("1.0.0-rc.1")]
    public void AnUnsupportedHostRefusesTheWholePackNamingTheCapabilityAndVersion(string? hostVersion)
    {
        var supported = ScreenDefinition();
        var demanding = supported with
        {
            Envelope = supported.Envelope with
            {
                Identity = "surface.later",
                Requires = [new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")],
            },
        };
        var host = hostVersion is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [LayoutPackIdentity.Capability] = hostVersion };

        var refused = Assert.Throws<LayoutPackUnsupportedException>(() => LayoutPackHostAdmission.Admit(
            [LayoutDefinitionPackageExporter.Export(supported, Hosted), LayoutDefinitionPackageExporter.Export(demanding, Hosted)], host));

        Assert.Equal(LayoutDefinitionCodes.CapabilityUnsupported, refused.Code);
        Assert.Equal(LayoutPackIdentity.Capability, refused.Capability);
        Assert.Equal("1.0.0", refused.MinimumPlatformVersion);
        Assert.Contains(LayoutPackIdentity.Capability, refused.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", refused.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "layout-ck-42: a payload stripped of its capability is refused, never admitted as unconstrained")]
    public void APayloadStrippedOfItsCapabilityIsRefused()
    {
        var definition = ScreenDefinition();
        var stripped = definition with { Envelope = definition.Envelope with { Requires = [] } };
        var entry = new LayoutDefinitionPackageEntry("surface.customer", "1.0.0",
            PlatformPackageContent.PresentJson(LayoutDefinitionJson.SerializeCanonical(stripped)));

        var refused = Assert.Throws<LayoutPackUnsupportedException>(() => LayoutPackHostAdmission.Admit(
            [entry], new Dictionary<string, string> { [LayoutPackIdentity.Capability] = "9.0.0" }));

        Assert.Equal(LayoutDefinitionCodes.CapabilityUndeclared, refused.Code);
    }

    [Fact(DisplayName = "Layout producer: unique pack identity and provider-neutral canonical export; lifecycle is T-620")]
    public void DefinitionExportsThroughThePlatformDocumentBoundary()
    {
        Assert.Equal(3, (int)DefinitionKind.Layout);
        Assert.NotEqual((int)DefinitionKind.Forms, (int)DefinitionKind.Layout);
        Assert.NotEqual((int)DefinitionKind.Workflows, (int)DefinitionKind.Layout);

        var entry = LayoutDefinitionPackageExporter.Export(ScreenDefinition(), Hosted);

        Assert.Equal(DefinitionKind.Layout, entry.Kind);
        Assert.Equal(17, entry.ContentKind);
        Assert.Equal(12, entry.Primitive);
        Assert.Equal("surface.customer", entry.DefinitionId);
        Assert.Equal("1.0.0", entry.Version);
        Assert.Equal(PlatformPackageContentClassification.Present, entry.Content.Classification);
        var roundTrip = LayoutDefinitionJson.Deserialize(entry.Content.Payload.Span);
        Assert.Equal("surface.customer", roundTrip.Envelope.Identity);
        Assert.Equal(entry.Content.Payload.ToArray(), LayoutDefinitionJson.SerializeCanonical(roundTrip));
    }

    [Fact(DisplayName = "layout-eng-25 producer admission: invalid definitions cannot export")]
    public void InvalidDefinitionIsNotExported()
    {
        var invalid = WithNumeric(ScreenDefinition(), LayoutNumericMember.Span, 0);

        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionPackageExporter.Export(invalid, Hosted));

        Assert.Equal("definition.publish", error.Stage);
        Assert.Contains(error.Refusals, refusal => refusal is
        {
            Code: LayoutDefinitionCodes.NumericOutOfRange,
            Pointer: "/blocks/0/children/0/placement/span",
        });
    }

    private static LayoutDefinition ScreenDefinition(Func<LayoutBlock, LayoutBlock>? mutate = null)
    {
        var blocks = new[]
        {
            Block(
                "root",
                "layout.stack",
                new LayoutStaticBinding(JsonSerializer.SerializeToElement(new { title = "Customer" })),
                intent: null,
                container: new LayoutContainer(
                    LayoutContainerKind.Stack,
                    Axis: LayoutAxis.Block,
                    Wrap: LayoutWrap.NoWrap,
                    ColumnCount: 2,
                    Gap: 4,
                    CollapseBelow: LayoutCollapseToken.Md,
                    Density: LayoutDensity.Comfortable,
                    Regions: ["main"],
                    JustifyItems: LayoutAlignment.Stretch,
                    AlignItems: LayoutAlignment.Start),
                children:
                [
                    Block(
                        "capture",
                        "layout.field",
                        new LayoutRecordFieldBinding("customer.name"),
                        LayoutIntent.Capture,
                        placement: new LayoutPlacement(
                            "main",
                            LayoutSizing.Fill,
                            LayoutSizing.Hug,
                            Grow: 0,
                            JustifySelf: LayoutAlignment.Start,
                            AlignSelf: LayoutAlignment.Center),
                        capture: new LayoutCaptureProperties(true, ["customer.name.required"], "Customer name"),
                        form: new LayoutFormReference("form.customer", "form.customer@2.1.0")),
                    Block(
                        "query",
                        "layout.table",
                        new LayoutQueryBinding("view.customer-orders"),
                        LayoutIntent.Observe,
                        container: new LayoutContainer(LayoutContainerKind.Stack),
                        placement: new LayoutPlacement("main", LayoutSizing.Fill, LayoutSizing.Hug, Grow: 1),
                        repeating: true,
                        relatedRelationship: "customer.orders",
                        showWhen: new LayoutShowWhen(Expression: "{\"!!\":[{\"var\":\"field.customer.name\"}]}"),
                        defaultSelection: JsonSerializer.SerializeToElement(new { status = "open" })),
                    Block(
                        "measure",
                        "layout.metric",
                        new LayoutMeasureBinding("orders.total"),
                        LayoutIntent.Observe,
                        placement: new LayoutPlacement("main", LayoutSizing.Hug, LayoutSizing.Hug, Span: 1, Grow: 0),
                        filterTargets: ["query"]),
                    Block(
                        "static",
                        "layout.text",
                        new LayoutStaticBinding(JsonSerializer.SerializeToElement(new { text = "Help" })),
                        LayoutIntent.Observe,
                        placement: new LayoutPlacement("main", LayoutSizing.Hug, LayoutSizing.Hug, Span: 1, Grow: 0),
                        breakAfter: true),
                ]),
        };
        return Definition(LayoutMedium.Screen, LayoutIntent.Capture, Map(blocks, mutate), submitGate: "role.customer-editor", drillTargets: ["surface.customer-detail"]);
    }

    private static LayoutDefinition PageDefinition(Func<LayoutBlock, LayoutBlock>? mutate = null)
    {
        var blocks = new[]
        {
            Block(
                "page-root",
                "layout.stack",
                new LayoutStaticBinding(JsonSerializer.SerializeToElement(new { })),
                LayoutIntent.Issue,
                container: new LayoutContainer(LayoutContainerKind.Stack, ColumnCount: 1, Gap: 2),
                children:
                [
                    Block(
                        "header",
                        "layout.text",
                        new LayoutStaticBinding(JsonSerializer.SerializeToElement(new { text = "Invoice" })),
                        LayoutIntent.Issue,
                        flowRole: LayoutFlowRole.Static,
                        staticRegion: "header.center"),
                    Block(
                        "document",
                        "layout.document",
                        new LayoutTemplateBinding("template.invoice"),
                        LayoutIntent.Issue,
                        breakBefore: true,
                        breakInside: LayoutBreakInside.AvoidPage),
                ]),
        };
        return Definition(
            LayoutMedium.Page,
            LayoutIntent.Issue,
            Map(blocks, mutate),
            pageLayouts:
            [
                new LayoutPageLayoutDefinition(
                    "page.invoice",
                    "a4",
                    LayoutPageOrientation.Portrait,
                    new LayoutPageMargins("12mm", "14mm", "12mm", "14mm"),
                    new LayoutMarginBoxes("10mm", "10mm")),
            ],
            pageMasters:
            [
                new LayoutPageMasterDefinition(
                    "master.invoice",
                    "page.invoice",
                    new LayoutMasterVariant("header.left", "header.center", "header.right"),
                    new LayoutMasterVariant("header.left", "header.center", "header.right"),
                    new LayoutMasterVariant("header.left", "header.center", "header.right")),
            ],
            pageRuns: [new LayoutPageRun("run.invoice", "page.invoice", "master.invoice", ["page-root"])]);
    }

    private static LayoutDefinition Definition(
        LayoutMedium medium,
        LayoutIntent defaultIntent,
        IReadOnlyList<LayoutBlock> blocks,
        string? submitGate = null,
        IReadOnlyList<string>? drillTargets = null,
        IReadOnlyList<LayoutPageLayoutDefinition>? pageLayouts = null,
        IReadOnlyList<LayoutPageMasterDefinition>? pageMasters = null,
        IReadOnlyList<LayoutPageRun>? pageRuns = null) => new(
        new LayoutDefinitionEnvelope(
            "surface.customer",
            "1.0.0",
            "tenant-a",
            LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { package = "customer-domain", source = "authoring" }),
            "regulated",
            LegalHold: true,
            Requires: [new LayoutDefinitionRequirement("records", "1.0.0"), new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")]),
        SchemaVersion: 1,
        Medium: medium,
        DefaultIntent: defaultIntent,
        Blocks: blocks,
        PageLayouts: pageLayouts ?? [],
        PageMasters: pageMasters ?? [],
        PageRuns: pageRuns ?? [],
        SubmitGate: submitGate,
        DrillThroughTargets: drillTargets ?? []);

    private static LayoutBlock Block(
        string id,
        string kind,
        LayoutBinding binding,
        LayoutIntent? intent,
        LayoutContainer? container = null,
        LayoutPlacement? placement = null,
        IReadOnlyList<LayoutBlock>? children = null,
        LayoutFlowRole flowRole = LayoutFlowRole.Flow,
        string? staticRegion = null,
        bool breakBefore = false,
        bool breakAfter = false,
        LayoutBreakInside breakInside = LayoutBreakInside.Auto,
        bool repeating = false,
        string? relatedRelationship = null,
        LayoutShowWhen? showWhen = null,
        LayoutCaptureProperties? capture = null,
        JsonElement? defaultSelection = null,
        IReadOnlyList<string>? filterTargets = null,
        LayoutFormReference? form = null,
        JsonElement? liveSelection = null) => new(
            id,
            kind,
            binding,
            children ?? [],
            intent,
            container,
            placement,
            flowRole,
            staticRegion,
            breakBefore,
            breakAfter,
            breakInside,
            repeating,
            relatedRelationship,
            showWhen,
            capture,
            defaultSelection,
            filterTargets ?? [],
            form,
            liveSelection);

    private static LayoutBlock[] Map(
        IEnumerable<LayoutBlock> blocks,
        Func<LayoutBlock, LayoutBlock>? mutate) => blocks
        .Select(block =>
        {
            var nested = block with { Children = Map(block.Children, mutate) };
            return mutate is null ? nested : mutate(nested);
        })
        .ToArray();

    private static IEnumerable<LayoutBlock> Flatten(IEnumerable<LayoutBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            foreach (var child in Flatten(block.Children)) yield return child;
        }
    }

    private static LayoutDefinition WithNumeric(LayoutDefinition source, LayoutNumericMember member, int value)
        => source with
        {
            Blocks = Map(source.Blocks, block => (member, block.Id) switch
            {
                (LayoutNumericMember.ColumnCount, "root") => block with { Container = block.Container! with { ColumnCount = value } },
                (LayoutNumericMember.Gap, "root") => block with { Container = block.Container! with { Gap = value } },
                (LayoutNumericMember.Span, "capture") => block with { Placement = block.Placement! with { Span = value, Width = LayoutSizing.Hug } },
                (LayoutNumericMember.Grow, "capture") => block with { Placement = block.Placement! with { Grow = value } },
                _ => block,
            }),
        };

    private static void AssertRangeRefusal(Action action, LayoutNumericMember member, string stage)
    {
        var error = Assert.Throws<LayoutDefinitionAdmissionException>(action);
        Assert.Equal(stage, error.Stage);
        var refusal = Assert.Single(error.Refusals, refusal => refusal.Code == LayoutDefinitionCodes.NumericOutOfRange);
        Assert.Equal(member switch
        {
            LayoutNumericMember.ColumnCount => "/blocks/0/container/column_count",
            LayoutNumericMember.Gap => "/blocks/0/container/gap",
            LayoutNumericMember.Span => "/blocks/0/children/0/placement/span",
            LayoutNumericMember.Grow => "/blocks/0/children/0/placement/grow",
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        }, refusal.Pointer);
    }

    private static void AssertRefusal(LayoutDefinition definition, string code, string pointer)
    {
        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() =>
            LayoutDefinitionAdmission.ValidateForAuthoring(definition, LayoutTestAccess.GrantsAll));
        Assert.Contains(error.Refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
    }
}
