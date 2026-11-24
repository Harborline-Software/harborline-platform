using System.Text.Json.Nodes;
using System.Text.Json;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.SchedulingAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SchedulingAuthoringTests : BunitContext
{
    // Oracle: literal word separator and supplied target; the pointer and target remain separate text tokens.
    [Fact(DisplayName = "scheduling-eng-19: long refusal targets retain the separator after their pointer")]
    public void LongRefusalTargetSeparator()
    {
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, new JsonObject())
            .Add(x => x.Refusals, new ScheduleRefusal[] { new("scheduling.definition.name_required", "/name",
                Target: "ResourceTargetABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZ") }));

        Assert.Equal(" ResourceTargetABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZ", cut.Find("li span").TextContent);
    }

    [Fact(DisplayName = "scheduling-eng-19: repeated module labels retain unique control identities and exactly one own label")]
    public void RepeatedModuleLabels()
    {
        var catalogue = new ScheduleCatalogue { Modules = [new("one", "Shared", "chain"), new("two", "Shared", "chain"), new("three", "Backend", "chain")] };
        var body = JsonNode.Parse("""{"solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{},"guarantee":"feasible_proof","work_budget":10},"modules":[{"module":"one","parameters":{}},{"module":"two","parameters":{}},{"module":"three","parameters":{}}],"basis":{"kind":"event_anchored_chain","nodes":[{"node_id":"same"},{"node_id":"same"}]}}""")!.AsObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, body).Add(x => x.Catalogue, catalogue));
        // Oracle: HTML ID uniqueness/label association and literal supplied labels, not FieldId.
        var ids = cut.FindAll("[id]").Select(element => element.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        var controls = cut.FindAll("textarea");
        Assert.Equal(4, controls.Count);
        var names = new[] { "Backend parameters", "Shared parameters", "Shared parameters", "Backend parameters" };
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var ownLabel = Assert.Single(cut.FindAll("label"), label => label.GetAttribute("for") == control.Id);
            Assert.Equal(names[index], ownLabel.ChildNodes[0].TextContent);
            Assert.Same(ownLabel, control.Closest("label"));
        }
    }
    // Oracle: HTML label element and WAI explicit-label guidance, not another renderer's output.
    [Theory(DisplayName = "scheduling-eng-19: gallery scenarios have direct labels linked to controls")]
    [InlineData("scheduling-authoring.from-empty")]
    [InlineData("scheduling-authoring.bound")]
    [InlineData("scheduling-authoring.refusals")]
    public void GalleryLabels(string scenario)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures/fixtures.yaml")))!;
        var body = scenario == "scheduling-authoring.bound" ? fixture["cases"]![1]!["expected"]!.DeepClone().AsObject() : new JsonObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, body).Add(x => x.Catalogue, Catalogue)
            .Add(x => x.Refusals, scenario.EndsWith(".refusals", StringComparison.Ordinal) ? new ScheduleRefusal[] { new("scheduling.definition.name_required", "/name") } : []));
        foreach (var title in new[] { "Validity window", "On-time tolerance" })
        {
            var group = cut.FindAll("fieldset").Single(field => field.Children.FirstOrDefault()?.TextContent == title);
            Assert.Equal(new[] { "LABEL", "LABEL" }, group.Children.Where(child => child.TagName != "LEGEND").Select(child => child.TagName));
        }
        foreach (var control in cut.FindAll("input,select,textarea"))
        {
            var label = control.Closest("label");
            Assert.NotNull(label);
            Assert.False(string.IsNullOrEmpty(control.Id));
            Assert.Equal(control.Id, label.GetAttribute("for"));
            Assert.Same(control, cut.FindAll("[id]").Single(element => element.Id == label.GetAttribute("for")));
        }
    }
    [Fact(DisplayName = "scheduling-auth-2 scheduling-auth-3: review R3 removes the optional window after choosing a chain")]
    public void RemovesValidityWindowForChain()
    {
        JsonObject actual = JsonNode.Parse("""{"name":"Inspection","basis":{"kind":"completion_offset","offset":"P2D"},"window":{"earliest":"PT0S","latest":"P1D"}}""")!.AsObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));
        cut.Find("[aria-label='Basis']").Change("event_anchored_chain"); cut.Render(p => p.Add(x => x.Value, actual));
        cut.FindAll("button").Single(button => button.TextContent == "Remove validity window").Click(); cut.Render(p => p.Add(x => x.Value, actual));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"name":"Inspection","basis":{"kind":"event_anchored_chain"}}"""), actual));
        Assert.Equal("", cut.Find("[aria-label='Window earliest']").GetAttribute("value"));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent == "Remove validity window");
    }
    // Oracle: slice lines 28/38, producer grammar and literal no-planner/solver bodies.
    [Theory(DisplayName = "scheduling-bound-1: first solver edit materialises empty parameters")]
    [InlineData("Backend", "prototype.exhaustive-proof.v2", "backend_id")]
    [InlineData("Guarantee", "feasible_proof", "guarantee")]
    [InlineData("Work budget", "10", "work_budget")]
    public void EmptySolverParameters(string label, string typed, string member)
    {
        JsonObject actual = label == "Guarantee" ? JsonNode.Parse("""{"solver":{"backend_id":"prototype.exhaustive-proof.v2"}}""")!.AsObject() : new();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, Catalogue).Add(x => x.ValueChanged, next => actual = next));
        cut.Find($"[aria-label='{label}']").Change(typed);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{}"), actual["solver"]!["parameters"]));
        Assert.Equal(typed, actual["solver"]![member]!.ToString());
    }
    [Fact(DisplayName = "scheduling-bound-1: untouched parameters blur leaves no solver")]
    public void UntouchedParameters()
    {
        var changes = 0;
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, new JsonObject()).Add(x => x.ValueChanged, _ => changes++));
        // bUnit refuses dispatch when there is no blur handler; a native blur then does nothing.
        try { cut.Find("[aria-label='Backend parameters']").Blur(); }
        catch (MissingEventHandlerException) { }
        Assert.Equal(0, changes);
    }
    [Fact(DisplayName = "scheduling-bound-4: negative work budget remains available for server refusal")]
    public void NegativeWorkBudget()
    {
        JsonObject actual = JsonNode.Parse("""{"solver":{"parameters":{},"work_budget":10}}""")!.AsObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));
        cut.Find("[aria-label='Work budget']").Change("-1");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"solver":{"parameters":{},"work_budget":-1}}"""), actual));
    }
    [Theory, Trait("ModuleConformance", "hlp.ui.scheduling-authoring")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.scheduling-authoring", MemberType = typeof(SharedFixtureBatch))]
    public void Shared(string caseId, string? rawFixture) => SharedFixtureBatch.Run(caseId, rawFixture, () =>
    {
        if (rawFixture is null) { LaneParity(0); LaneParity(1); Module(); Refusals(); return; }
        switch (caseId)
        {
            case "scheduling-authoring.from-empty": LaneParity(0); break;
            case "scheduling-authoring.bound": LaneParity(1); Module(); break;
            case "scheduling-authoring.refusals": Refusals(); break;
            default: Assert.Fail("Unknown Scheduling fixture: " + caseId); break;
        }
    });
    private static ScheduleCatalogue Catalogue => new()
    {
        Types = [new("types.asset", "Asset")], Legality = [new("legality.safety", "Safety rules")],
        Backends = [new("prototype.exhaustive-proof.v2", "Prototype", ["feasible_proof"])],
        Modules = [new("precedence", "Precedence", "chain")]
    };

    [Fact(DisplayName = "scheduling-auth-15: requests a new Schedule without replacing the current body")]
    public void Create()
    {
        JsonObject? created = null;
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, new JsonObject { ["name"] = "Existing" })
            .Add(x => x.CreateRequested, next => created = next));
        cut.FindAll("button").Single(button => button.TextContent == "Create Schedule").Click();
        Assert.NotNull(created); Assert.Empty(created);
        Assert.Equal("Existing", cut.Find("[aria-label='Schedule name']").GetAttribute("value"));
    }

    [Theory(DisplayName = "scheduling-auth-2: all generator editors preserve host metadata")]
    [InlineData("fixed_calendar", "Recurrence rule", "FREQ=DAILY", "rrule")]
    [InlineData("threshold", "Reading", "meter.hours", "reading")]
    public void OtherGenerators(string kind, string label, string value, string member)
    {
        JsonObject actual = new() { ["due_date"] = "2027-01-01T00:00:00Z", ["envelope"] = new JsonObject { ["identity"] = "inspection" } };
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));
        cut.Find("[aria-label='Basis']").Change(kind); cut.Render(p => p.Add(x => x.Value, actual));
        cut.Find($"[aria-label='{label}']").Change(value);
        Assert.False(actual.ContainsKey("due_date"));
        Assert.Equal("inspection", actual["envelope"]!["identity"]!.GetValue<string>());
        Assert.Equal(kind, actual["basis"]!["kind"]!.GetValue<string>());
        Assert.Equal(value, actual["basis"]![member]!.GetValue<string>());
    }

    [Theory(DisplayName = "scheduling-auth-1 scheduling-auth-3 scheduling-auth-4 scheduling-auth-5 scheduling-auth-11 scheduling-auth-12 scheduling-auth-13 scheduling-auth-15 scheduling-eng-19 scheduling-bound-1 scheduling-bound-3 scheduling-bound-4: lane-parity literal body")]
    [InlineData(0)] [InlineData(1)]
    public void LaneParity(int index)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures/fixtures.yaml")))!["cases"]![index]!;
        JsonObject actual = new();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, Catalogue)
            .Add(x => x.ValueChanged, next => actual = next));
        foreach (var step in fixture["steps"]!.AsArray())
        {
            var label = step![0]!.GetValue<string>(); var value = step[1]!.GetValue<string>();
            cut.Find($"[aria-label='{label}']").Change(value);
            cut.Render(p => p.Add(x => x.Value, actual));
        }
        Assert.True(JsonNode.DeepEquals(fixture["expected"], actual), actual.ToJsonString());
    }

    [Fact(DisplayName = "scheduling-bound-6: supply is read-only and no availability member is written")]
    public void AvailabilityReadOnly()
    {
        JsonObject original = new(); JsonObject? changed = null;
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, original).Add(x => x.ValueChanged, next => changed = next));
        Assert.Contains("shared availability runtime, from each Resource's own availability source", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[aria-label='Availability source']"));
        cut.Find("[aria-label='Schedule name']").Change("Inspection");
        Assert.Equal("{\"name\":\"Inspection\"}", changed!.ToJsonString());
        Assert.Empty(original);
    }

    [Fact(DisplayName = "scheduling-auth-2 scheduling-auth-6: authors chain nodes with independent tolerance")]
    public void Chain()
    {
        JsonObject actual = new();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));
        void Change(string label, string value) { cut.Find($"[aria-label='{label}']").Change(value); cut.Render(p => p.Add(x => x.Value, actual)); }
        Change("Basis", "event_anchored_chain"); Change("Chain anchor", "event.install");
        cut.FindAll("button").Single(button => button.TextContent == "Add node").Click(); cut.Render(p => p.Add(x => x.Value, actual));
        Change("Node 1 id", "check"); Change("Node 1 offset", "P1D"); Change("Node 1 tolerance early", "PT1H"); Change("Node 1 tolerance late", "PT2H");
        var expected = JsonNode.Parse("""{"basis":{"kind":"event_anchored_chain","anchor":"event.install","nodes":[{"node_id":"check","after_node_id":null,"offset":"P1D","tolerance":{"early":"PT1H","late":"PT2H"}}]}}""");
        Assert.True(JsonNode.DeepEquals(expected, actual));
        Change("Basis", "completion_offset"); Change("Completion offset", "P2D");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"basis":{"kind":"completion_offset","offset":"P2D"}}"""), actual));
    }

    [Fact(DisplayName = "scheduling-bound-2 scheduling-bound-7: explicit admitted module binding and parameters")]
    public void Module()
    {
        JsonObject actual = new();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, Catalogue).Add(x => x.ValueChanged, next => actual = next));
        Assert.False(cut.Find("input[type=checkbox]").HasAttribute("checked"));
        Assert.Contains("explicit opt-in", cut.Markup, StringComparison.Ordinal);
        cut.Find("input[type=checkbox]").Change(true); cut.Render(p => p.Add(x => x.Value, actual));
        cut.Find("[aria-label='Precedence parameters']").Change("{\"constraints\":[{\"id\":\"after\",\"beforeActivityId\":\"a\",\"afterActivityId\":\"b\",\"minimumGapSlots\":1}]}"); cut.Render(p => p.Add(x => x.Value, actual));
        cut.Find("[aria-label='Backend parameters']").Change("{\"seed\":7}");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"modules":[{"module":"precedence","parameters":{"constraints":[{"id":"after","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":1}]}}],"solver":{"parameters":{"seed":7}}}"""), actual));
        var authored = actual["modules"]![0]!;
        Assert.True(ConstraintModuleAdmission.IsAdmitted(new ScheduleConstraintModule(
            authored["module"]!.GetValue<string>(), JsonSerializer.SerializeToElement(authored["parameters"]))));
    }

    [Fact(DisplayName = "scheduling-auth-27: every conformance refusal is displayed before publish")]
    public void Refusals()
    {
        var corpus = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures/authoring-refusals.json")))!;
        foreach (var entry in corpus["cases"]!.AsArray())
        {
            var refusals = entry!["refusals"]!.AsArray().Select(row => new ScheduleRefusal(row!["code"]!.GetValue<string>(), row["pointer"]!.GetValue<string>(), row["detail"]?.DeepClone(), row["target"]?.GetValue<string>())).ToArray();
            var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Refusals, refusals));
            Assert.Equal(refusals.Length, cut.FindAll("li").Count);
            // Compare each rendered row to the independently supplied corpus, including generator payloads.
            var items = cut.FindAll("li");
            for (var index = 0; index < refusals.Length; index++)
            {
                var expected = entry["refusals"]![index]!;
                Assert.Equal(expected["code"]!.GetValue<string>(), items[index].QuerySelectorAll("code")[0].TextContent);
                Assert.Equal(expected["pointer"]!.GetValue<string>(), items[index].QuerySelectorAll("code")[1].TextContent);
                if (expected["target"] is not null) Assert.Equal(expected["target"]!.GetValue<string>(), items[index].QuerySelector("span")!.TextContent.Trim());
                if (expected["detail"] is not null) Assert.True(JsonNode.DeepEquals(expected["detail"], JsonNode.Parse(items[index].QuerySelector("pre")!.TextContent)));
            }
            Assert.Equal("true", cut.Find("[aria-live='polite']").GetAttribute("aria-atomic"));
            Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent == "Publish");
            cut.Dispose();
        }
    }
}
