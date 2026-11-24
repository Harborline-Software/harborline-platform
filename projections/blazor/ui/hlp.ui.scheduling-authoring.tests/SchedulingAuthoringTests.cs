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
    // Oracles: literal host JSON tokens and decimal text; host display is independent of authoring precision limits.
    [Theory(DisplayName = "host numeric display preserves exact decimal tokens")]
    [InlineData("9007199254740993", "9007199254740993")]
    [InlineData("9.007199254740993e15", "9007199254740993")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    public void HostNumericDisplayPreservesExactDecimalTokens(string token, string displayed)
    {
        var body = JsonNode.Parse("""{"basis":{"kind":"threshold","value":TOKEN}}""".Replace("TOKEN", token, StringComparison.Ordinal))!.AsObject();
        var changes = 0;
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, body).Add(x => x.ValueChanged, _ => changes++));
        Assert.Equal(displayed, cut.Find("[aria-label='Threshold']").GetAttribute("value"));
        cut.Find("[aria-label='Threshold']").Change(displayed);
        Assert.Equal(0, changes);
        Assert.Equal(token, body["basis"]!["value"]!.ToJsonString());
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    // Oracle: interface numericInput and literal errors/bodies, through native control events.
    [Theory(DisplayName = "numeric errors stay with their own field")]
    [InlineData("Threshold", "Work budget", "Threshold must be an exact decimal with at most 15 significant digits and scale 28.", "Work budget must be a 32-bit integer in plain decimal notation.", "25", "20")]
    [InlineData("Work budget", "Threshold", "Work budget must be a 32-bit integer in plain decimal notation.", "Threshold must be an exact decimal with at most 15 significant digits and scale 28.", "20", "25")]
    public void NumericErrorsStayWithTheirOwnField(string first, string second, string firstError, string secondError, string firstValid, string secondValid)
    {
        var actual = JsonNode.Parse("""{"basis":{"kind":"threshold","value":12.5},"solver":{"parameters":{},"work_budget":10}}""")!.AsObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));
        cut.Find($"[aria-label='{first}']").Change("abc");
        cut.Find($"[aria-label='{second}']").Change(second == "Threshold" ? "12.5" : "10");
        Assert.Equal(firstError, cut.Find("[role='alert']").TextContent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"basis":{"kind":"threshold","value":12.5},"solver":{"parameters":{},"work_budget":10}}"""), actual));
        cut.Find($"[aria-label='{second}']").Change("bad");
        Assert.Equal(new[] { firstError, secondError }.Order(), cut.FindAll("[role='alert']").Select(alert => alert.TextContent).Order());
        cut.Find($"[aria-label='{first}']").Change(firstValid); cut.Render(p => p.Add(x => x.Value, actual));
        Assert.Equal(secondError, cut.Find("[role='alert']").TextContent);
        cut.Find($"[aria-label='{second}']").Change(secondValid); cut.Render(p => p.Add(x => x.Value, actual));
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"basis":{"kind":"threshold","value":25},"solver":{"parameters":{},"work_budget":20}}"""), actual));
    }

    // Oracle: literal JSON-object errors/body; duplicate labels must not combine control identities.
    [Theory(DisplayName = "parameter errors stay with their own textarea")]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ParameterErrorsStayWithTheirOwnTextarea(int first)
    {
        var actual = JsonNode.Parse("""{"solver":{"parameters":{}},"modules":[{"module":"one","parameters":{}},{"module":"two","parameters":{}}]}""")!.AsObject();
        var catalogue = new ScheduleCatalogue { Modules = [new("one", "Shared", "chain"), new("two", "Shared", "chain")] };
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, catalogue).Add(x => x.ValueChanged, next => actual = next));
        var second = (first + 1) % 3;
        string[] errors = ["Backend parameters must be a JSON object.", "Shared parameters must be a JSON object.", "Shared parameters must be a JSON object."];
        cut.FindAll("textarea")[first].Change("{");
        cut.FindAll("textarea")[second].Change("{}");
        Assert.Equal(errors[first], cut.Find("[role='alert']").TextContent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"solver":{"parameters":{}},"modules":[{"module":"one","parameters":{}},{"module":"two","parameters":{}}]}"""), actual));
        cut.FindAll("textarea")[second].Change("[]");
        Assert.Equal(2, cut.FindAll("[role='alert']").Count);
        cut.FindAll("textarea")[first].Change("{}"); cut.Render(p => p.Add(x => x.Value, actual));
        Assert.Equal(errors[second], cut.Find("[role='alert']").TextContent);
        cut.FindAll("textarea")[second].Change("{}"); cut.Render(p => p.Add(x => x.Value, actual));
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"solver":{"parameters":{}},"modules":[{"module":"one","parameters":{}},{"module":"two","parameters":{}}]}"""), actual));
    }

    // Oracles: interface revision 2 numericInput, literal decimal values and JSON number tokens.
    public static TheoryData<string, decimal> ProducerThresholdWireCases => new()
    {
        { "1e-28", 0.0000000000000000000000000001m },
        { "0.0000000000000000000000000001", 0.0000000000000000000000000001m },
        { "-1e-28", -0.0000000000000000000000000001m },
        { "-0.0000000000000000000000000001", -0.0000000000000000000000000001m },
        { "7.92281625142643e+28", 79228162514264300000000000000m },
        { "79228162514264300000000000000", 79228162514264300000000000000m },
        { "-7.92281625142643e+28", -79228162514264300000000000000m },
        { "-79228162514264300000000000000", -79228162514264300000000000000m },
        { "0", 0m },
        { "1e-7", 0.0000001m },
        { "0.0000001", 0.0000001m },
        { "1e+21", 1000000000000000000000m },
        { "1000000000000000000000", 1000000000000000000000m },
        { "1.23456789012345e+28", 12345678901234500000000000000m },
        { "12345678901234500000000000000", 12345678901234500000000000000m },
        { "1.23456789012345e-14", 0.0000000000000123456789012345m },
        { "0.0000000000000123456789012345", 0.0000000000000123456789012345m },
    };

    [Theory(DisplayName = "scheduling-auth-2: producer codec accepts threshold wire edges as exact decimals")]
    [MemberData(nameof(ProducerThresholdWireCases))]
    public void ProducerAcceptsThresholdWire(string wire, decimal expected)
    {
        var definition = ScheduleDefinitionJson.Deserialize("""{"envelope":null,"name":"Inspection","starts":{"kind":"form","target_id":"forms.inspection"},"basis":{"kind":"threshold","reading":"meter.hours","crossing":"above","value":STAR}}""".Replace("STAR", wire, StringComparison.Ordinal));
        Assert.Equal(expected, Assert.IsType<ThresholdBasis>(definition.Basis).Value);
    }

    // Oracle: optional ScheduleDefinition.Escalation; a present escalation requires both after and to.
    [Theory(DisplayName = "scheduling-auth-5: clearing the last escalation value removes the optional object")]
    [InlineData("""{"after":"PT1H","to":"team.operations"}""", "Escalation after", "Escalation to", """{"name":"Inspection","escalation":{"after":"","to":"team.operations"}}""")]
    [InlineData("""{"after":"PT1H","to":"team.operations"}""", "Escalation to", "Escalation after", """{"name":"Inspection","escalation":{"after":"PT1H","to":""}}""")]
    [InlineData("""{"after":"PT1H"}""", "Escalation after", null, null)]
    [InlineData("""{"to":"team.operations"}""", "Escalation to", null, null)]
    public void RemoveEscalation(string initial, string first, string? last, string? partial)
    {
        JsonObject actual = new() { ["name"] = "Inspection", ["escalation"] = JsonNode.Parse(initial) };
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.ValueChanged, next => actual = next));

        cut.Find($"[aria-label='{first}']").Change(""); cut.Render(p => p.Add(x => x.Value, actual));
        if (last is not null)
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(partial!), actual));
            cut.Find($"[aria-label='{last}']").Change("");
        }

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"name":"Inspection"}"""), actual));
    }

    // Oracle: ScheduleDefinition.Solver is optional; None names no planner (schedule interface revision 12).
    [Fact(DisplayName = "scheduling-bound-1: clearing Backend removes the solver binding for a no-planner Schedule")]
    public void RemoveSolverBinding()
    {
        JsonObject actual = JsonNode.Parse("""{"name":"Inspection","problem_class":"Project","solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{"seed":7},"guarantee":"feasible_proof","work_budget":10}}""")!.AsObject();
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, Catalogue).Add(x => x.ValueChanged, next => actual = next));

        cut.Find("[aria-label='Problem class']").Change("None"); cut.Render(p => p.Add(x => x.Value, actual));
        cut.Find("[aria-label='Backend']").Change("");

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"name":"Inspection","problem_class":"None"}"""), actual));
    }

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
        if (rawFixture is null) { LaneParity(0); LaneParity(1); LaneParity(3); LaneParity(4); LaneParity(5); LaneParity(6); LaneParity(7); LaneParity(8); LaneParity(9); LaneParity(10); LaneParity(11); LaneParity(12); LaneParity(13); LaneParity(14); LaneParity(15); LaneParity(16); LaneParity(17); LaneParity(18); LaneParity(19); LaneParity(20); LaneParity(21); LaneParity(22); LaneParity(23); LaneParity(24); LaneParity(25); LaneParity(26); LaneParity(27); LaneParity(28); LaneParity(29); LaneParity(30); LaneParity(31); LaneParity(32); LaneParity(33); LaneParity(34); LaneParity(35); LaneParity(36); LaneParity(37); LaneParity(38); LaneParity(39); LaneParity(40); LaneParity(41); LaneParity(42); LaneParity(43); LaneParity(44); LaneParity(45); LaneParity(46); LaneParity(47); LaneParity(48); LaneParity(49); LaneParity(50); LaneParity(51); LaneParity(52); LaneParity(53); Module(); Refusals(); return; }
        switch (caseId)
        {
            case "scheduling-authoring.from-empty": LaneParity(0); break;
            case "scheduling-authoring.bound": LaneParity(1); Module(); break;
            case "scheduling-authoring.refusals": Refusals(); break;
            case "scheduling-authoring.remove-node-window": LaneParity(3); break;
            case "scheduling-authoring.remove-tolerance": LaneParity(4); break;
            case "scheduling-authoring.invalid-threshold": LaneParity(5); break;
            case "scheduling-authoring.invalid-work-budget": LaneParity(6); break;
            case "scheduling-authoring.numeric-threshold-rejections": LaneParity(7); break;
            case "scheduling-authoring.numeric-work-budget-rejections": LaneParity(8); break;
            case "scheduling-authoring.numeric-threshold-empty": LaneParity(9); break;
            case "scheduling-authoring.numeric-work-budget-empty": LaneParity(10); break;
            case "scheduling-authoring.numeric-threshold-whitespace": LaneParity(11); break;
            case "scheduling-authoring.numeric-work-budget-whitespace": LaneParity(12); break;
            case "scheduling-authoring.numeric-work-budget-minimum": LaneParity(13); break;
            case "scheduling-authoring.threshold-negative-zero": LaneParity(14); break;
            case "scheduling-authoring.threshold-trailing-zero": LaneParity(15); break;
            case "scheduling-authoring.threshold-leading-point": LaneParity(16); break;
            case "scheduling-authoring.threshold-trailing-point": LaneParity(17); break;
            case "scheduling-authoring.threshold-leading-space": LaneParity(18); break;
            case "scheduling-authoring.threshold-trailing-space": LaneParity(19); break;
            case "scheduling-authoring.threshold-decimal-minimum": LaneParity(20); break;
            case "scheduling-authoring.threshold-decimal-maximum": LaneParity(21); break;
            case "scheduling-authoring.threshold-before-decimal-minimum": LaneParity(22); break;
            case "scheduling-authoring.threshold-after-decimal-maximum": LaneParity(23); break;
            case "scheduling-authoring.threshold-unsafe-integer": LaneParity(24); break;
            case "scheduling-authoring.threshold-minimum-positive": LaneParity(25); break;
            case "scheduling-authoring.threshold-below-scale": LaneParity(26); break;
            case "scheduling-authoring.threshold-precision-boundary": LaneParity(27); break;
            case "scheduling-authoring.threshold-excess-precision": LaneParity(28); break;
            case "scheduling-authoring.threshold-fraction-precision-boundary": LaneParity(29); break;
            case "scheduling-authoring.threshold-display-small": LaneParity(30); break;
            case "scheduling-authoring.threshold-display-large": LaneParity(31); break;
            case "scheduling-authoring.threshold-display-large-precision": LaneParity(32); break;
            case "scheduling-authoring.threshold-display-maximum": LaneParity(33); break;
            case "scheduling-authoring.threshold-display-negative-maximum": LaneParity(34); break;
            case "scheduling-authoring.threshold-display-negative-minimum": LaneParity(35); break;
            case "scheduling-authoring.threshold-host-small": LaneParity(36); break;
            case "scheduling-authoring.threshold-host-large": LaneParity(37); break;
            case "scheduling-authoring.threshold-host-small-precision": LaneParity(38); break;
            case "scheduling-authoring.threshold-host-preserve-scale-29": LaneParity(39); break;
            case "scheduling-authoring.threshold-host-negative-zero": LaneParity(40); break;
            case "scheduling-authoring.threshold-host-trailing-zero": LaneParity(41); break;
            case "scheduling-authoring.threshold-negative-infinity": LaneParity(42); break;
            case "scheduling-authoring.work-budget-negative-infinity": LaneParity(43); break;
            case "scheduling-authoring.threshold-whitespace-only": LaneParity(44); break;
            case "scheduling-authoring.work-budget-whitespace-only": LaneParity(45); break;
            case "scheduling-authoring.threshold-host-exact-integer": LaneParity(46); break;
            case "scheduling-authoring.threshold-host-exact-integer-exponent": LaneParity(47); break;
            case "scheduling-authoring.threshold-host-exact-decimal-maximum": LaneParity(48); break;
            case "scheduling-authoring.threshold-host-exact-fraction": LaneParity(49); break;
            case "scheduling-authoring.remove-threshold-error": LaneParity(50); break;
            case "scheduling-authoring.remove-module-parameter-error": LaneParity(51); break;
            case "scheduling-authoring.backend-parameter-preserves-number-token": LaneParity(52); break;
            case "scheduling-authoring.module-parameter-preserves-number-token": LaneParity(53); break;
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

    // Oracle: literal fixture bodies, optional producer members and the specified numeric-input error text.
    [Theory(DisplayName = "scheduling-auth-1 scheduling-auth-3 scheduling-auth-4 scheduling-auth-5 scheduling-auth-6 scheduling-auth-11 scheduling-auth-12 scheduling-auth-13 scheduling-auth-15 scheduling-eng-19 scheduling-bound-1 scheduling-bound-3 scheduling-bound-4: lane-parity literal body")]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)] [InlineData(21)]
    [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)] [InlineData(27)] [InlineData(28)] [InlineData(29)]
    [InlineData(30)] [InlineData(31)] [InlineData(32)] [InlineData(33)] [InlineData(34)] [InlineData(35)]
    [InlineData(36)] [InlineData(37)] [InlineData(38)] [InlineData(39)]
    [InlineData(40)] [InlineData(41)] [InlineData(42)] [InlineData(43)] [InlineData(44)] [InlineData(45)]
    [InlineData(46)] [InlineData(47)] [InlineData(48)] [InlineData(49)] [InlineData(50)] [InlineData(51)]
    [InlineData(52)] [InlineData(53)]
    public void LaneParity(int index)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures/fixtures.yaml")))!["cases"]![index]!;
        JsonObject actual = fixture["initialJson"] is { } initialJson ? JsonNode.Parse(initialJson.GetValue<string>())!.AsObject() : fixture["initial"]?.DeepClone().AsObject() ?? new();
        void AssertBody()
        {
            if (fixture["expectedJson"] is { } expectedJson) Assert.Equal(expectedJson.GetValue<string>(), actual.ToJsonString());
            else Assert.True(JsonNode.DeepEquals(fixture["expected"], actual), actual.ToJsonString());
        }
        var changes = 0;
        var cut = Render<HarborlineSchedulingAuthoringEditor>(p => p.Add(x => x.Value, actual).Add(x => x.Catalogue, Catalogue)
            .Add(x => x.ValueChanged, next => { changes++; actual = next; }));
        if (fixture["unchangedThresholdEdit"]?.GetValue<bool>() == true)
            Assert.Equal(fixture["expectedThresholdDisplay"]!.GetValue<string>(), cut.Find("[aria-label='Threshold']").GetAttribute("value"));
        foreach (var step in fixture["steps"]!.AsArray())
        {
            var label = step![0]!.GetValue<string>(); var value = step[1]!.GetValue<string>();
            var action = step.AsArray().Count > 2 ? step[2]?.GetValue<string>() : null;
            if (action == "expect-error") { Assert.Equal(value, cut.Find("[role='alert']").TextContent); continue; }
            if (action == "expect-no-errors") { Assert.Empty(cut.FindAll("[role='alert']")); continue; }
            if (action == "toggle") cut.FindAll("label").Single(element => element.TextContent == label).QuerySelector("input")!.Change(value == "true");
            else if (action == "click") cut.FindAll("button").Single(button => button.TextContent == label).Click();
            else cut.Find($"[aria-label='{label}']").Change(value);
            cut.Render(p => p.Add(x => x.Value, actual));
            if (fixture["error"] is { } error)
            {
                Assert.Equal(error.GetValue<string>(), cut.Find("[role='alert']").TextContent);
                AssertBody();
                Assert.Equal(0, changes);
            }
        }
        AssertBody();
        if (fixture["expectedThresholdDisplay"] is { } displayed) {
            var text = displayed.GetValue<string>();
            Assert.Equal(text, cut.Find("[aria-label='Threshold']").GetAttribute("value"));
            if (fixture["unchangedThresholdEdit"]?.GetValue<bool>() == true) Assert.Equal(fixture["expectedChanges"]?.GetValue<int>() ?? 0, changes);
            else {
                cut.Find("[aria-label='Threshold']").Change(text + (text.Contains('.') ? "0" : ".0"));
                cut.Render(p => p.Add(x => x.Value, actual));
            }
            Assert.Empty(cut.FindAll("[role='alert']"));
            Assert.Equal(text, cut.Find("[aria-label='Threshold']").GetAttribute("value"));
            AssertBody();
        }
        if (fixture["expectedThresholdWire"] is JsonArray tokens)
            Assert.Contains(actual["basis"]!["value"]!.ToJsonString(), tokens.Select(token => token!.GetValue<string>()));
        if (fixture["expectedEditedThresholdJson"] is { } editedJson)
        {
            cut.Find("[aria-label='Threshold']").Change("25"); cut.Render(p => p.Add(x => x.Value, actual));
            Assert.Equal("25", cut.Find("[aria-label='Threshold']").GetAttribute("value"));
            Assert.Equal(editedJson.GetValue<string>(), actual.ToJsonString());
            Assert.Empty(cut.FindAll("[role='alert']"));
        }
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
