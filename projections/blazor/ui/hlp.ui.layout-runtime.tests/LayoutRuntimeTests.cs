using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bunit;
using Harborline.Foundation.RuleAuthoring;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Harborline.UIAdapters.Blazor.Components.RuleAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LayoutRuntimeTests : BunitContext
{
    [Fact]
    public void ScreenFlowKeepsOrderStaticRegionsAndVersionProvenance()
    {
        var plan = new LayoutRuntimePlan("invoice", "version-7", "screen", [new("title", "layout.text", 0), new("table", "layout.table", 1)], [new("header", "layout.text", 0, "header.center")]);
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan));
        Assert.Single(cut.FindAll("[data-fragmentainer=screen]"));
        Assert.Equal(["title", "table"], cut.FindAll("[data-layout-block]").Select(node => node.GetAttribute("data-layout-block")));
        Assert.Single(cut.FindAll("[data-layout-static-region=header\\.center]"));
        Assert.Contains("version-7", cut.Markup);
    }

    [Fact]
    public void PersistedInvalidValueRendersDiagnosticWithoutClamp()
    {
        var plan = new LayoutRuntimePlan("invoice", "version-7", "page", [], [], [new("layout.numeric.out_of_range", "/blocks/0/placement/span")]);
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan));
        Assert.Contains("layout.numeric.out_of_range at /blocks/0/placement/span", cut.Markup);
        Assert.Single(cut.FindAll("[role=alert]"));
    }

    [Fact(DisplayName = "layout-eng-26: a full-data, deny-all authority renders every block read-only and leaves zero live submit controls")]
    public void ADenyAllAuthorityRendersReadOnlyWithNoSubmitControl()
    {
        var plan = DenyAll();
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan).Add(x => x.OnSubmit, () => throw new InvalidOperationException("No submit is live.")));
        var blocks = cut.FindAll("[data-layout-block]");
        Assert.Equal(["name", "orders", "orders-total"], blocks.Select(block => block.GetAttribute("data-layout-block")));
        Assert.All(blocks, block => Assert.Equal("true", block.GetAttribute("data-layout-readonly")));
        Assert.Empty(cut.FindAll("[data-layout-submit], button, input[type=submit]"));
        // No authority at all is read-only too: the lane never assumes one.
        Assert.Empty(Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan with { Authority = null })).FindAll("button"));
    }

    [Fact(DisplayName = "layout-eng-26: an admitted authority renders one submit control, which hands the submit to the host")]
    public void AnAdmittedAuthorityRendersOneSubmitControl()
    {
        var submitted = 0;
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters
            .Add(x => x.Plan, DenyAll() with { Authority = new(CanSubmit: true) })
            .Add(x => x.OnSubmit, () => submitted++));
        Assert.Empty(cut.FindAll("[data-layout-readonly]"));
        cut.FindAll("[data-layout-submit]").Single().Click();
        Assert.Equal(1, submitted);
    }

    [Fact(DisplayName = "T-583 item 2: the editor displays the platform refusal payload verbatim, stage, code, pointer and target, and invents no message")]
    public void TheEditorDisplaysTheRefusalPayloadVerbatim()
    {
        var refusal = JsonSerializer.Deserialize<LayoutRefusalEnvelope>(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "refusal-envelope.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty).Add(x => x.Catalogue, new LayoutAuthoringCatalogue([], [])).Add(x => x.Refusal, refusal));
        var alert = cut.Find("[role=alert]");
        Assert.Equal("install", alert.GetAttribute("data-refusal-stage"));
        var items = cut.FindAll("[role=alert] li");
        Assert.Equal(refusal.Refusals.Select(item => (item.Code, item.Pointer, item.Target)),
            items.Select(item => (item.GetAttribute("data-refusal-code")!, item.GetAttribute("data-refusal-pointer")!, item.GetAttribute("data-refusal-target"))));
        // Only the payload's own values: nothing looked up from a local string table.
        Assert.Equal(refusal.Refusals.Select(item => $"install: {item.Code} at {item.Pointer} ({item.Target})"), items.Select(item => item.TextContent));
    }

    [Fact(DisplayName = "T-583 item 2: a refusal without a target shows none, and no refusal renders no alert")]
    public void ARefusalWithoutATargetShowsNone()
    {
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty).Add(x => x.Catalogue, new LayoutAuthoringCatalogue([], []))
            .Add(x => x.Refusal, new LayoutRefusalEnvelope("author", [new("layout.reference.not_exposed", "/blocks/0/binding")])));
        var item = cut.Find("[role=alert] li");
        Assert.Equal("author: layout.reference.not_exposed at /blocks/0/binding", item.TextContent);
        Assert.False(item.HasAttribute("data-refusal-target"));
        Assert.Empty(Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty).Add(x => x.Catalogue, new LayoutAuthoringCatalogue([], []))).FindAll("[role=alert]"));
    }

    // The one platform model both lanes render (LayoutAuthorityGateTests derives the same flow and authority).
    private static LayoutRuntimePlan DenyAll()
        => JsonSerializer.Deserialize<LayoutRuntimePlan>(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "deny-all-authority.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void EditorOffersTheClosedLayoutGrammarWithoutAReadingOrderOrNumericControl()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Medium = "page", Blocks = [new("dashboard", "layout.dashboard-widget")], PageRuns = [new("run-1", "letter", "letter-master")] })
            .Add(x => x.Catalogue, new LayoutAuthoringCatalogue([new("layout.table", "Table"), new("layout.dashboard-widget", "Dashboard widget")], ["header.center"], [new("letter", "Letter")], [new("letter-master", "Letter master")], ["header.center"], [new("revenue-glance", "Revenue glance")]))
            .Add(x => x.ValueChanged, value => changed = value));
        Assert.Contains("sm", cut.Find("[aria-label='Collapse below']").TextContent);
        Assert.DoesNotContain("xl", cut.Find("[aria-label='Collapse below']").TextContent);
        Assert.Contains("capture", cut.Find("[aria-label='Default intent']").TextContent);
        Assert.Contains("areas", cut.Find("[aria-label='Container flow']").TextContent);
        Assert.Contains("Revenue glance", cut.Find("[aria-label='Block 1 Helm widget']").TextContent);
        Assert.Contains("Letter", cut.Find("[aria-label='Page run 1 layout']").TextContent);
        Assert.DoesNotContain("reading order", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("input[type=number]"));
        cut.FindAll("button").Single(button => button.TextContent == "Add block").Click();
        Assert.Equal("layout.table", changed!.Blocks.Last().Kind);
    }

    [Fact(DisplayName = "layout-auth-13..16: the editor binds a block to any of the five kinds and names it from the catalogue")]
    public void EditorBindsABlockToAnyOfTheFiveKindsAndNamesItFromTheCatalogue()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("total", "layout.table", new("measure", "invoice.total"))] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        var kind = cut.Find("[aria-label='Block 1 binding kind']");
        foreach (var label in new[] { "Record field", "Query", "Measure", "Template", "Static content" })
            Assert.Contains(label, kind.TextContent, StringComparison.Ordinal);
        // The name list follows the chosen kind and offers nothing from another kind.
        Assert.Contains("Invoice total", cut.Find("[aria-label='Block 1 binding name']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Open invoices", cut.Find("[aria-label='Block 1 binding name']").TextContent, StringComparison.Ordinal);

        // Rebinding to another kind preserves the block and clears the name (layout-auth-31).
        kind.Change("query");
        Assert.Equal(new LayoutAuthoringBinding("query", ""), changed!.Blocks.Single().Binding);
        Assert.Equal("total", changed.Blocks.Single().Id);
    }

    [Fact(DisplayName = "layout-auth-31: the editor marks an unbound block as needing a binding instead of removing it")]
    public void EditorMarksAnUnboundBlockAsNeedingABindingInsteadOfRemovingIt()
    {
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("orphan", "layout.table", new("query", ""))] })
            .Add(x => x.Catalogue, Catalogue()));

        Assert.Equal("Block 1 needs a binding", cut.Find("[role=status]").TextContent);
        Assert.Single(cut.FindAll("[aria-label='Block 1 binding kind']"));
    }

    [Fact]
    public void EditorAuthorsStaticContentOnTheBlockRatherThanLookingItUp()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("notice", "layout.table", new("static", ""))] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.Empty(cut.FindAll("[aria-label='Block 1 binding name']"));
        cut.Find("[aria-label='Block 1 binding content']").Change("Registered office: Leeds");
        Assert.Equal(new LayoutAuthoringBinding("static", "Registered office: Leeds"), changed!.Blocks.Single().Binding);
    }

    [Fact(DisplayName = "layout-auth-18: a repeating block binds a collection and its row subtree is authored once")]
    public void RepeatingBlockBindsACollectionAndItsRowSubtreeIsAuthoredOnce()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("lines", "layout.table", new("query", "views.invoice-lines")),
            new("amount", "layout.table", new("record_field", "line.amount"), ParentId: "lines"),
            new("total", "layout.table", new("measure", "invoice.total")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        // Only a collection binding (a query or a record field) can repeat; a measure cannot.
        Assert.Empty(cut.FindAll("[aria-label='Block 3 repeats per row']"));
        cut.Find("[aria-label='Block 1 repeats per row']").Change(true);
        Assert.Equal([blocks[0] with { Repeating = true, Container = "stack" }, blocks[1], blocks[2]], changed!.Blocks);

        // The row subtree is authored once, as the repeating block's children: a block may be
        // placed inside it, and never inside itself or its own descendant.
        var parentOfLines = cut.Find("[aria-label='Block 1 parent']").TextContent;
        Assert.DoesNotContain("Block 1", parentOfLines, StringComparison.Ordinal);
        Assert.DoesNotContain("Block 2", parentOfLines, StringComparison.Ordinal);
        Assert.Contains("Block 3", parentOfLines, StringComparison.Ordinal);
        cut.Find("[aria-label='Block 3 parent']").Change("lines");
        Assert.Equal([blocks[0] with { Container = "stack" }, blocks[1], blocks[2] with { ParentId = "lines" }], changed.Blocks);
    }

    [Fact(DisplayName = "layout-auth-18: rebinding a repeating block to a kind that is not a collection stops it repeating")]
    public void RebindingARepeatingBlockToANonCollectionKindStopsItRepeating()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("lines", "layout.table", new("query", "views.invoice-lines"), Repeating: true)] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.True(cut.Find("[aria-label='Block 1 repeats per row']").HasAttribute("checked"));
        cut.Find("[aria-label='Block 1 binding kind']").Change("measure");
        Assert.Equal(new LayoutAuthoringBlock("lines", "layout.table", new("measure", "")), changed!.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-19: a related block traverses a declared Records relationship and persists only its key")]
    public void RelatedBlockTraversesADeclaredRelationshipAndPersistsOnlyItsKey()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("supplier", "layout.table", new("record_field", "supplier.name")),
            new("entry", "layout.table", new("record_field", "invoice.reference"), Intent: "capture"),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue() with { Relationships = [new("invoice.supplier", "Invoice supplier")] })
            .Add(x => x.ValueChanged, value => changed = value));

        // A related block observes; a capture block is never offered a traversal.
        Assert.Empty(cut.FindAll("[aria-label='Block 2 related record']"));
        // Only relationships Records declares are offered, and the block stores the key alone.
        var related = cut.Find("[aria-label='Block 1 related record']");
        Assert.Equal(["Not related", "Invoice supplier"], related.QuerySelectorAll("option").Select(option => option.TextContent));
        Assert.Equal(["", "invoice.supplier"], related.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        related.Change("invoice.supplier");
        Assert.Equal([blocks[0] with { RelatedRelationship = "invoice.supplier" }, blocks[1]], changed!.Blocks);
    }

    [Fact(DisplayName = "layout-auth-19: a related block that stops observing drops its relationship")]
    public void RelatedBlockThatStopsObservingDropsItsRelationship()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("supplier", "layout.table", new("record_field", "supplier.name"), RelatedRelationship: "invoice.supplier")] })
            .Add(x => x.Catalogue, Catalogue() with { Relationships = [new("invoice.supplier", "Invoice supplier")] })
            .Add(x => x.ValueChanged, value => changed = value));

        cut.Find("[aria-label='Block 1 intent']").Change("capture");
        Assert.Equal(new LayoutAuthoringBlock("supplier", "layout.table", new("record_field", "supplier.name"), Intent: "capture"), changed!.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-20: show_when is written in Rules grammar and stored verbatim for the shared engine")]
    public void ShowWhenIsWrittenInRulesGrammarAndStoredVerbatim()
    {
        LayoutAuthoringDraft? changed = null;
        var block = new LayoutAuthoringBlock("notice", "layout.table", new("record_field", "invoice.note"));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block with { ShowWhen = new(Expression: "{\"var\":\"field.flagged\"}") }] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));
        var guard = cut.Find("[aria-label='Block 1 show when']");
        Assert.Equal("{\"var\":\"field.flagged\"}", guard.GetAttribute("value"));

        // The editor holds no conditional grammar of its own: the Rules expression is stored as written.
        const string expression = " {\"==\": [{\"var\": \"field.status\"}, \"open\"]}";
        guard.Change(expression);
        Assert.Equal(block with { ShowWhen = new(Expression: expression) }, changed!.Blocks.Single());
        // Clearing the guard removes it rather than storing an empty expression.
        cut.Find("[aria-label='Block 1 show when']").Change("");
        Assert.Equal(block, changed.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-21: a capture block adds a requirement and named validation rules and never drops a declared requirement")]
    public void CaptureBlockAddsARequirementAndNamedRulesAndNeverDropsADeclaredRequirement()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("reference", "layout.table", new("record_field", "invoice.reference"), Intent: "capture"),
            new("note", "layout.table", new("record_field", "invoice.note"), Intent: "capture"),
            new("total", "layout.table", new("measure", "invoice.total")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue() with { RequiredFields = ["invoice.reference"], ValidationRules = [new("rules.iban", "IBAN checksum")] })
            .Add(x => x.ValueChanged, value => changed = value));

        // Records declares the reference required: the block shows it, and cannot remove it.
        var declared = cut.Find("[aria-label='Block 1 required']");
        Assert.True(declared.HasAttribute("checked"));
        Assert.True(declared.HasAttribute("disabled"));
        // Only a capture block narrows capture; an observing block is offered neither control.
        Assert.Empty(cut.FindAll("[aria-label='Block 3 required']"));
        Assert.Empty(cut.FindAll("[aria-label='Block 3 validation rule IBAN checksum']"));

        // A block may add a requirement Records did not declare, and name a registered rule.
        Assert.False(cut.Find("[aria-label='Block 2 required']").HasAttribute("checked"));
        cut.Find("[aria-label='Block 2 required']").Change(true);
        Assert.Equal(new LayoutAuthoringCapture(Required: true), changed!.Blocks[1].Capture);
        cut.Find("[aria-label='Block 2 validation rule IBAN checksum']").Change(true);
        Assert.Null(changed.Blocks[1].Capture!.Required);
        Assert.Equal(["rules.iban"], changed.Blocks[1].Capture!.ValidationRules!);
        Assert.Equal([blocks[0], blocks[2]], [changed.Blocks[0], changed.Blocks[2]]);
    }

    [Fact(DisplayName = "layout-auth-29: unchecking an added requirement omits it rather than storing false (T-724 ruling 78)")]
    public void UncheckingAnAddedRequirementOmitsIt()
    {
        LayoutAuthoringDraft? changed = null;
        var block = new LayoutAuthoringBlock("note", "layout.table", new("record_field", "invoice.note"), Intent: "capture", Capture: new(Required: true, ValidationRules: ["rules.iban"]));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block] })
            .Add(x => x.Catalogue, Catalogue() with { ValidationRules = [new("rules.iban", "IBAN checksum")] })
            .Add(x => x.ValueChanged, value => changed = value));

        cut.Find("[aria-label='Block 1 required']").Change(false);
        // Omission is no override, so Records' own requirement stands; false would be refused on a required field.
        var capture = changed!.Blocks.Single().Capture!;
        Assert.Null(capture.Required);
        Assert.Equal(["rules.iban"], capture.ValidationRules!);
    }

    [Fact(DisplayName = "layout-auth-21: a block that stops capturing drops its capture properties")]
    public void BlockThatStopsCapturingDropsItsCaptureProperties()
    {
        LayoutAuthoringDraft? changed = null;
        var block = new LayoutAuthoringBlock("note", "layout.table", new("record_field", "invoice.note"), Intent: "capture", Capture: new(Required: true, ValidationRules: ["rules.iban"]));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block] })
            .Add(x => x.Catalogue, Catalogue() with { ValidationRules = [new("rules.iban", "IBAN checksum")] })
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.True(cut.Find("[aria-label='Block 1 validation rule IBAN checksum']").HasAttribute("checked"));
        cut.Find("[aria-label='Block 1 validation rule IBAN checksum']").Change(false);
        Assert.Equal(new LayoutAuthoringCapture(Required: true), changed!.Blocks.Single().Capture);
        cut.Find("[aria-label='Block 1 intent']").Change("observe");
        Assert.Equal(new LayoutAuthoringBlock("note", "layout.table", new("record_field", "invoice.note"), Intent: "observe"), changed.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-22: a capture block overrides its prompt for this surface only")]
    public void CaptureBlockOverridesItsPromptForThisSurfaceOnly()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("reference", "layout.table", new("record_field", "invoice.reference"), Intent: "capture"),
            new("total", "layout.table", new("measure", "invoice.total")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        // A prompt override narrows capture, so an observing block is not offered one.
        Assert.Empty(cut.FindAll("[aria-label='Block 2 prompt override']"));
        // The override lives on this block of this surface; the field's own prompt is untouched.
        cut.Find("[aria-label='Block 1 prompt override']").Change("Supplier reference");
        Assert.Equal(blocks[0] with { Capture = new(PromptOverride: "Supplier reference") }, changed!.Blocks[0]);
        Assert.Equal(blocks[1], changed.Blocks[1]);
    }

    [Fact(DisplayName = "layout-auth-22: clearing a prompt override removes it")]
    public void ClearingAPromptOverrideRemovesIt()
    {
        LayoutAuthoringDraft? changed = null;
        var block = new LayoutAuthoringBlock("reference", "layout.table", new("record_field", "invoice.reference"), Intent: "capture");
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block with { Capture = new(Required: true, PromptOverride: "Supplier reference") }] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.Equal("Supplier reference", cut.Find("[aria-label='Block 1 prompt override']").GetAttribute("value"));
        cut.Find("[aria-label='Block 1 prompt override']").Change("");
        Assert.Equal(block with { Capture = new(Required: true) }, changed!.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-33: a block declares the selection it opens with, on that block only")]
    public void BlockDeclaresTheSelectionItOpensWith()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("status", "layout.list", new("query", "views.invoice-statuses")),
            new("invoices", "layout.table", new("query", "views.open-invoices")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [blocks[0], blocks[1] with { DefaultSelection = "inv-1" }] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));
        Assert.Equal("inv-1", cut.Find("[aria-label='Block 2 default selection']").GetAttribute("value"));

        cut.Find("[aria-label='Block 1 default selection']").Change("overdue");
        Assert.Equal([blocks[0] with { DefaultSelection = "overdue" }, blocks[1] with { DefaultSelection = "inv-1" }], changed!.Blocks);
        // Clearing the default removes it; the block then opens with no selection.
        cut.Find("[aria-label='Block 2 default selection']").Change("");
        Assert.Equal(blocks, changed.Blocks);
    }

    [Fact(DisplayName = "layout-auth-34: a block declares which other blocks on this surface its selection filters")]
    public void BlockDeclaresWhichOtherBlocksItsSelectionFilters()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("status", "layout.table", new("query", "views.invoice-statuses")),
            new("invoices", "layout.table", new("query", "views.open-invoices")),
            new("total", "layout.table", new("measure", "invoice.total")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [blocks[0] with { FilterTargets = ["total"] }, blocks[1], blocks[2]] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        // The targets are the surface's other blocks; a block never filters itself.
        Assert.Empty(cut.FindAll("[aria-label='Block 1 filters Block 1']"));
        Assert.True(cut.Find("[aria-label='Block 1 filters Block 3']").HasAttribute("checked"));
        cut.Find("[aria-label='Block 1 filters Block 2']").Change(true);
        Assert.Equal(["total", "invoices"], changed!.Blocks[0].FilterTargets!);
        cut.Find("[aria-label='Block 1 filters Block 3']").Change(false);
        Assert.Equal(blocks, changed.Blocks);

        // Removing a block removes every edge to it, so no filter points outside the surface.
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Remove block 3").Click();
        Assert.Equal([blocks[0], blocks[1]], changed.Blocks);
    }

    [Fact(DisplayName = "layout-auth-35: the surface declares its drill-through targets by reference to released surfaces")]
    public void SurfaceDeclaresItsDrillThroughTargets()
    {
        LayoutAuthoringDraft? changed = null;
        var value = LayoutAuthoringDraft.Empty with { DrillThroughTargets = ["surface.supplier"] };
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, value)
            .Add(x => x.Catalogue, Catalogue() with { DrillTargets = [new("surface.supplier", "Supplier"), new("surface.payment", "Payment")] })
            .Add(x => x.ValueChanged, next => changed = next));

        Assert.True(cut.Find("[aria-label='Drill through to Supplier']").HasAttribute("checked"));
        Assert.False(cut.Find("[aria-label='Drill through to Payment']").HasAttribute("checked"));
        cut.Find("[aria-label='Drill through to Payment']").Change(true);
        Assert.Equal(["surface.supplier", "surface.payment"], changed!.DrillThroughTargets!);
        // Removing the last target leaves the surface with none, not an empty list.
        cut.Find("[aria-label='Drill through to Supplier']").Change(false);
        Assert.Equal(LayoutAuthoringDraft.Empty, changed);
    }

    [Fact(DisplayName = "layout-bound-3: a capture field picks its control from the registered field controls")]
    public void CaptureFieldPicksItsControlFromTheRegisteredFieldControls()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("amount", "layout.table", new("record_field", "invoice.amount"), Intent: "capture", Capture: new(Required: true)),
            new("notice", "layout.table", new("static", "Enter amounts in GBP"), Intent: "capture"),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue() with { FieldControls = [new("currency", "Currency"), new("text", "Text")] })
            .Add(x => x.ValueChanged, value => changed = value));

        // Only a captured field has a control; static content on a capture surface is offered none.
        Assert.Single(cut.FindAll("[aria-label='Block 2 required']"));
        Assert.Empty(cut.FindAll("[aria-label='Block 2 field control']"));
        var control = cut.Find("[aria-label='Block 1 field control']");
        Assert.Equal(["", "currency", "text"], control.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        control.Change("currency");
        Assert.Equal(blocks[0] with { Capture = new(Required: true, Control: "currency") }, changed!.Blocks[0]);
        // The runtime default stores no control.
        cut.Find("[aria-label='Block 1 field control']").Change("");
        Assert.Equal(blocks[0], changed.Blocks[0]);
    }

    [Fact(DisplayName = "layout-bound-10: a field whose value domain picks its editor offers no authored control")]
    public void ValueDomainFieldOffersNoAuthoredControl()
    {
        LayoutAuthoringBlock[] blocks =
        [
            new("status", "layout.table", new("record_field", "invoice.status"), Intent: "capture"),
            new("reference", "layout.table", new("record_field", "invoice.reference"), Intent: "capture"),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue() with { FieldControls = [new("text", "Text")], ValueDomainFields = ["invoice.status"] }));

        // The value domain's resolver picks the editor; Layout passes that choice through.
        Assert.Empty(cut.FindAll("[aria-label='Block 1 field control']"));
        Assert.Contains("Block 1 control is chosen by its value domain", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[aria-label='Block 2 field control']"));
    }

    [Fact(DisplayName = "layout-auth-20: show_when is authored with the shared guided expression editor and lowered to Rules text (T-724 ruling 39)")]
    public void ShowWhenIsAuthoredWithTheSharedGuidedExpressionEditor()
    {
        LayoutAuthoringDraft? changed = null;
        var guide = new FormulaExpr.Call("==", [new FormulaExpr.Ref("field.status"), new FormulaExpr.Literal("open", ColumnValueType.Text)]);
        var guarded = new LayoutAuthoringBlock("notice", "layout.table", new("static", "Overdue"), ShowWhen: new(Expression: "{\"==\":[{\"var\":\"field.status\"},\"open\"]}"), ShowWhenGuide: guide);
        var unguarded = new LayoutAuthoringBlock("total", "layout.table", new("measure", "invoice.total"));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [guarded, unguarded] })
            .Add(x => x.Catalogue, Catalogue() with { GuardReferences = [new("field.status", "Status", ColumnValueType.Text)] })
            .Add(x => x.ValueChanged, value => changed = value));

        // A block with no guard opens in the guided editor; the raw text box is not the default.
        Assert.Single(cut.FindAll("[aria-label='Block 2 show when expression shape']"));
        Assert.Empty(cut.FindAll("[aria-label='Block 2 show when']"));

        // Editing the guided expression stores the guide and the Rules text the shared engine compiles.
        cut.Find("[aria-label='Block 1 show when argument 2 literal value']").Change("closed");
        var edited = changed!.Blocks[0];
        Assert.Equal(new LayoutAuthoringShowWhen(Expression: "{\"==\":[{\"var\":\"field.status\"},\"closed\"]}"), edited.ShowWhen);
        Assert.Equal(new FormulaExpr.Literal("closed", ColumnValueType.Text), ((FormulaExpr.Call)edited.ShowWhenGuide!).Args[1]);

        // Removing the guard removes both.
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Remove block 1 show when").Click();
        Assert.Equal(new LayoutAuthoringBlock("notice", "layout.table", new("static", "Overdue")), changed.Blocks[0]);
    }

    [Fact(DisplayName = "layout-auth-20: raw Rules text stays available as the escape hatch (T-724 ruling 39)")]
    public void RawRulesTextStaysAvailableAsTheEscapeHatch()
    {
        LayoutAuthoringDraft? changed = null;
        var block = new LayoutAuthoringBlock("notice", "layout.table", new("static", "Flagged"), ShowWhen: new(Expression: "{\"var\":\"field.flagged\"}"), ShowWhenGuide: new FormulaExpr.Ref("field.flagged"));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));
        Assert.Empty(cut.FindAll("[aria-label='Block 1 show when']"));

        cut.Find("[aria-label='Block 1 show when authoring']").Change("raw");
        Assert.Equal("{\"var\":\"field.flagged\"}", cut.Find("[aria-label='Block 1 show when']").GetAttribute("value"));
        cut.Find("[aria-label='Block 1 show when']").Change("{\"!\":[{\"var\":\"field.flagged\"}]}");
        // Raw text is stored verbatim and the guide, which no longer describes it, is dropped.
        Assert.Equal(new LayoutAuthoringBlock("notice", "layout.table", new("static", "Flagged"), ShowWhen: new(Expression: "{\"!\":[{\"var\":\"field.flagged\"}]}")), changed!.Blocks.Single());
    }

    [Fact(DisplayName = "layout-ck-29: show_when cites a catalogue predicate by exact pin, and the guard holds exactly one form")]
    public void ShowWhenCitesACataloguePredicateByExactPin()
    {
        LayoutAuthoringDraft? changed = null;
        var pin = new LayoutAuthoringPredicatePin("invoice.overdue", "1.0.0", new string('a', 64));
        var block = new LayoutAuthoringBlock("notice", "layout.table", new("static", "Overdue"));
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [block with { ShowWhen = new(Expression: "{\"var\":\"field.flagged\"}") }] })
            .Add(x => x.Catalogue, Catalogue() with { Predicates = [new("Overdue", pin)] })
            .Add(x => x.ValueChanged, value => changed = value));

        cut.Find("[aria-label='Block 1 show when authoring']").Change("predicate");
        cut.Find("[aria-label='Block 1 show when predicate']").Change("invoice.overdue@1.0.0");
        // Picking a predicate replaces the expression: the stored guard carries the whole pin and nothing else.
        Assert.Equal(block with { ShowWhen = new(Predicate: pin) }, changed!.Blocks.Single());
        cut.Find("[aria-label='Block 1 show when predicate']").Change("");
        Assert.Equal(block, changed.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-18: a repeating block authored here gets the container admission requires, and matches the shared admitted fixture (T-724 ruling 41)")]
    public void RepeatingBlockAuthoredHereMatchesTheSharedAdmittedFixture()
    {
        var value = LayoutAuthoringDraft.Empty with
        {
            Blocks = [new("lines", "layout.table", new("query", "views.invoice-lines")), new("amount", "layout.table", new("record_field", "line.amount"))],
        };
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, value)
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, next => value = next));
        Assert.Empty(cut.FindAll("[aria-label='Block 1 container']"));

        cut.Find("[aria-label='Block 1 repeats per row']").Change(true);
        cut.Render(parameters => parameters.Add(x => x.Value, value));
        cut.Find("[aria-label='Block 2 parent']").Change("lines");
        cut.Render(parameters => parameters.Add(x => x.Value, value));

        // builder-definitions admits exactly these blocks (LayoutBoundRegisterTests, same fixture).
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "authored-repeating-block.json")))!["blocks"];
        var authored = JsonSerializer.SerializeToNode(value.Blocks, new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
        Assert.True(JsonNode.DeepEquals(fixture, authored), authored!.ToJsonString());

        // The container is offered once a block repeats or has children, and can be changed, never removed.
        var container = cut.Find("[aria-label='Block 1 container']");
        Assert.Equal(["stack", "flow", "areas"], container.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Empty(cut.FindAll("[aria-label='Block 2 container']"));
        container.Change("flow");
        Assert.Equal("flow", value.Blocks[0].Container);
    }

    [Fact(DisplayName = "layout-auth-18: placing a block inside another gives the new parent a default container (T-724 ruling 41)")]
    public void PlacingABlockInsideAnotherGivesTheParentADefaultContainer()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks = [new("group", "layout.table", new("static", "Totals")), new("total", "layout.table", new("measure", "invoice.total"))];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, next => changed = next));

        cut.Find("[aria-label='Block 2 parent']").Change("group");
        Assert.Equal([blocks[0] with { Container = "stack" }, blocks[1] with { ParentId = "group" }], changed!.Blocks);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }

    private static LayoutAuthoringCatalogue Catalogue() => new(
        [new("layout.table", "Table")],
        ["header.center"],
        Bindables: new Dictionary<string, IReadOnlyList<LayoutAuthoringOption>>(StringComparer.Ordinal)
        {
            ["record_field"] = [new("invoice.supplier", "Supplier")],
            ["query"] = [new("views.open-invoices", "Open invoices")],
            ["measure"] = [new("invoice.total", "Invoice total")],
            ["template"] = [new("tpl.remittance", "Remittance")],
        });
}
