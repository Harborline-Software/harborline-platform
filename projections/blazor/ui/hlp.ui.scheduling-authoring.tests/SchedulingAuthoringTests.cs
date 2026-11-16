using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.SchedulingAuthoring;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// T-491/schedule-editor, the Blazor lane. It reads the same conformance fixture as the React lane and replays the same labelled steps;
// both lanes must emit the same literal body, which is the lane-parity check. Expected bodies are literals written from the
// ScheduleDefinition grammar, and refusal codes and pointers come from the producer's own conformance file, never from the editor
// (T-724 ruling 121).
public sealed class SchedulingAuthoringTests : BunitContext
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures", "fixtures.yaml")))!;
    private static readonly JsonNode RefusalFile = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scheduling-authoring-fixtures", "authoring-refusals.json")))!;
    private static JsonNode Case(string id) => Fixture["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
    private static IReadOnlyList<SchedulingOption> Options(string member) => [.. Fixture["catalogue"]![member]!.AsArray().Select(entry => entry is JsonObject option ? new SchedulingOption(option["id"]!.GetValue<string>(), option["label"]!.GetValue<string>()) : (SchedulingOption)entry!.GetValue<string>())];
    private static readonly SchedulingCatalogue Catalogue = new()
    {
        Types = Options("types"), Forms = Options("forms"), Workflows = Options("workflows"), WorkItems = Options("workItems"), LegalityRuleSets = Options("legalityRuleSets"),
        Backends = [.. Fixture["catalogue"]!["backends"]!.AsArray().Select(entry => new SchedulingBackendOption(entry!["id"]!.GetValue<string>(), entry["label"]!.GetValue<string>(), [.. entry["guarantees"]!.AsArray().Select(guarantee => guarantee!.GetValue<string>())]))],
    };
    private static SchedulingDraft DraftOf(JsonNode draft) => new(draft["identity"]!.GetValue<string>(), draft["versionId"]!.GetValue<string>(), draft["expectedRevision"]?.GetValue<string>(), draft["body"]!.DeepClone().AsObject());
    private static SchedulingDraft Draft(string body, string? revision = "3", string identity = "schedule.inspection") => new(identity, "1.0.0", revision, JsonNode.Parse(body)!.AsObject());

    private IRenderedComponent<HarborlineSchedulingAuthoringEditor> Editor(SchedulingDraft value, List<SchedulingDraftChange> changes, SchedulingDiagnosticSet? diagnostics = null, bool readOnly = false)
        => Render<HarborlineSchedulingAuthoringEditor>(parameters => parameters.Add(component => component.Value, value).Add(component => component.Catalogue, Catalogue).Add(component => component.Diagnostics, diagnostics).Add(component => component.ReadOnly, readOnly).Add(component => component.DraftChanged, (SchedulingDraftChange change) => changes.Add(change)));
    private static IElement Labelled(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string label) => cut.Find($"[aria-label='{label}']");
    private static void Type(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string label, string value) => Labelled(cut, label).Change(value);
    private static IElement Button(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
    private static List<IElement> Listed(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut) => [.. cut.FindAll("ul[aria-label='Refusal list'] li")];
    private static string Announcement(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut) => cut.Find("[role='status'][aria-label='Refusal announcements']").TextContent;
    private static string[] OptionValues(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string select) => [.. cut.FindAll($"select[aria-label='{select}'] option").Select(option => option.GetAttribute("value") ?? "")];
    private static string[] OptionTexts(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string select) => [.. cut.FindAll($"select[aria-label='{select}'] option").Select(option => option.TextContent)];
    private static string? SelectedText(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, string select) { var value = Labelled(cut, select).GetAttribute("value") ?? ""; return cut.FindAll($"select[aria-label='{select}'] option").FirstOrDefault(option => (option.GetAttribute("value") ?? "") == value)?.TextContent; }
    // Replays a fixture case's steps through the rendered editor's labelled controls, exactly as the React lane does.
    private static void Replay(IRenderedComponent<HarborlineSchedulingAuthoringEditor> cut, JsonNode? steps)
    {
        foreach (var step in steps!.AsArray())
        {
            if (step!["set"] is { } set) Type(cut, set.GetValue<string>(), step["value"]!.GetValue<string>());
            else Button(cut, step["click"]!.GetValue<string>()).Click();
        }
    }
    private static void Same(JsonNode? expected, JsonNode? actual) => Assert.True(JsonNode.DeepEquals(expected, actual), actual?.ToJsonString() ?? "null");
    private static SchedulingDiagnosticSet Refused(SchedulingDraft draft, params (string Code, string Pointer)[] diagnostics) => new(draft.Identity, draft.ExpectedRevision, 0, [.. diagnostics.Select(item => new SchedulingDiagnostic(item.Code, item.Pointer))]);

    [Fact(DisplayName = "scheduling-eng-19, scheduling-auth-1, -2, -3, -4, -5, -12, -13: a fixed-calendar Schedule authored from nothing is exactly the fixture body")]
    [Trait("Holds", "scheduling-eng-19"), Trait("Holds", "scheduling-auth-1"), Trait("Holds", "scheduling-auth-2"), Trait("Holds", "scheduling-auth-3"), Trait("Holds", "scheduling-auth-4"), Trait("Holds", "scheduling-auth-5"), Trait("Holds", "scheduling-auth-12"), Trait("Holds", "scheduling-auth-13")]
    public void ScheduleFromNothing()
    {
        var item = Case("scheduling-authoring.schedule-from-nothing"); var changes = new List<SchedulingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        Replay(cut, item["input"]!["steps"]);

        Same(item["expected"]!["body"], changes[^1].Draft.Body);
        Assert.Equal("", changes[^1].Draft.Identity);
    }

    [Fact(DisplayName = "scheduling-eng-19, scheduling-auth-2, -5, -6, -12: an event-anchored chain authored from nothing is exactly the fixture body")]
    [Trait("Holds", "scheduling-eng-19"), Trait("Holds", "scheduling-auth-2"), Trait("Holds", "scheduling-auth-5"), Trait("Holds", "scheduling-auth-6"), Trait("Holds", "scheduling-auth-12")]
    public void ChainFromNothing()
    {
        var item = Case("scheduling-authoring.chain-from-nothing"); var changes = new List<SchedulingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        Replay(cut, item["input"]!["steps"]);

        Same(item["expected"]!["body"], changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-bound-1, scheduling-bound-2, scheduling-bound-3, scheduling-bound-4, scheduling-auth-11: a solver backend and the precedence module are named and parameterised from the editor")]
    [Trait("Holds", "scheduling-bound-1"), Trait("Holds", "scheduling-bound-2"), Trait("Holds", "scheduling-bound-3"), Trait("Holds", "scheduling-bound-4"), Trait("Holds", "scheduling-auth-11")]
    public void SolverAndModule()
    {
        var item = Case("scheduling-authoring.solver-and-module"); var changes = new List<SchedulingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        Replay(cut, item["input"]!["steps"]);

        Same(item["expected"]!["body"], changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-auth-27: the Refusal list shows every refusal with its code and pointer, and marks the fields it can place")]
    [Trait("Holds", "scheduling-auth-27")]
    public void RefusalList()
    {
        var item = Case("scheduling-authoring.refusal-list"); var draft = DraftOf(item["input"]!["draft"]!);
        IReadOnlyList<SchedulingDiagnostic> diagnostics = [.. item["input"]!["diagnostics"]!.AsArray().Select(entry => new SchedulingDiagnostic(entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>()))];
        var cut = Editor(draft, [], new SchedulingDiagnosticSet(draft.Identity, draft.ExpectedRevision, 0, diagnostics));

        Assert.Equal(item["expected"]!["refusalList"]!.AsArray().Select(entry => entry!.GetValue<string>()), Listed(cut).Select(entry => entry.TextContent));
        foreach (var field in item["expected"]!["invalid"]!.AsArray()) Assert.Equal("true", Labelled(cut, field!.GetValue<string>()).GetAttribute("aria-invalid"));
        Assert.Null(Labelled(cut, "Problem class").GetAttribute("aria-invalid"));
    }

    [Fact(DisplayName = "scheduling-auth-27, L487: every refusal case in the scheduling conformance file appears in the Refusal list before publish")]
    [Trait("Holds", "scheduling-auth-27")]
    public void EveryConformanceRefusalIsListed()
    {
        var refused = 0;
        foreach (var item in RefusalFile["cases"]!.AsArray())
        {
            var id = item!["id"]!.GetValue<string>();
            var draft = new SchedulingDraft($"schedule.{id}", "1.0.0", null, item["definition"]!.DeepClone().AsObject());
            var expected = item["refusals"]!.AsArray().Select(entry => (entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>())).ToList();
            var cut = Editor(draft, [], new SchedulingDiagnosticSet(draft.Identity, null, 0, [.. expected.Select(entry => new SchedulingDiagnostic(entry.Item1, entry.Item2))]));

            Assert.True(expected.SequenceEqual(Listed(cut).Select(entry => (entry.GetAttribute("data-code") ?? "", entry.GetAttribute("data-pointer") ?? ""))), id);
            if (expected.Count == 0) Assert.Contains("No refusals.", cut.Markup, StringComparison.Ordinal);
            refused += expected.Count;
        }
        Assert.True(refused > 20);
    }

    [Fact(DisplayName = "scheduling-auth-27: a polite live region announces only the refusals that newly arrived")]
    [Trait("Holds", "scheduling-auth-27")]
    public void LiveRegionAnnouncesNewRefusals()
    {
        var draft = Draft("""{"name":"","starts":{"kind":"form","target_id":"forms.inspection"}}""");
        var cut = Editor(draft, []);
        Assert.Equal("polite", cut.Find("[role='status'][aria-label='Refusal announcements']").GetAttribute("aria-live"));
        Assert.Equal("", Announcement(cut));

        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Refused(draft, ("scheduling.definition.name_required", "/name"))));
        Assert.Equal("1 new refusal: scheduling.definition.name_required at /name", Announcement(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Refused(draft, ("scheduling.definition.name_required", "/name"), ("scheduling.definition.due_date_invalid", "/due_date"))));
        Assert.Equal("1 new refusal: scheduling.definition.due_date_invalid at /due_date", Announcement(cut));
    }

    [Fact(DisplayName = "scheduling-auth-27: refusals for another identity, revision or local generation are stale and never shown")]
    [Trait("Holds", "scheduling-auth-27")]
    public void StaleRefusalsAreHidden()
    {
        var draft = Draft("""{"name":""}""");
        SchedulingDiagnostic[] refusal = [new("scheduling.definition.name_required", "/name")];
        foreach (var stale in new SchedulingDiagnosticSet[] { new("schedule.other", "3", 0, refusal), new("schedule.inspection", "4", 0, refusal), new("schedule.inspection", "3", 1, refusal) })
        {
            var cut = Editor(draft, [], stale);
            Assert.Empty(Listed(cut));
            Assert.Null(Labelled(cut, "Name").GetAttribute("aria-invalid"));
        }
    }

    [Fact(DisplayName = "scheduling-auth-6: each chain node states its scope in text, so scope never rests on colour alone (WCAG 1.4.1)")]
    [Trait("Holds", "scheduling-auth-6")]
    public void ChainScopeIsText()
    {
        var cut = Editor(Draft("""{"basis":{"kind":"event_anchored_chain","anchor":"surgery.completed","nodes":[{"node_id":"first-check","offset":"PT4H","tolerance":{"early":"PT0S","late":"PT0S"}},{"node_id":"second-check","after_node_id":"first-check","offset":"P1D","tolerance":{"early":"PT0S","late":"PT0S"}}]}}"""), []);

        Assert.Contains(cut.FindAll("p"), paragraph => paragraph.TextContent == "Node 1 scope: follows the chain anchor surgery.completed");
        Assert.Contains(cut.FindAll("p"), paragraph => paragraph.TextContent == "Node 2 scope: follows node first-check");
        Assert.Empty(cut.FindAll("[aria-label='Window earliest']"));
    }

    [Fact(DisplayName = "scheduling-auth-6: a node follows only the anchor or an earlier node, and removing a node keeps the others")]
    [Trait("Holds", "scheduling-auth-6")]
    public void NodesFollowEarlierNodes()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"basis":{"kind":"event_anchored_chain","anchor":"a","nodes":[{"node_id":"one","offset":"PT1H","tolerance":{"early":"PT0S","late":"PT0S"}},{"node_id":"two","after_node_id":"one","offset":"PT1H","tolerance":{"early":"PT0S","late":"PT0S"}}]}}"""), changes);

        Assert.Equal([""], OptionValues(cut, "Node 1 follows"));
        Assert.Equal(["", "one"], OptionValues(cut, "Node 2 follows"));
        Type(cut, "Node 2 follows", "");
        Same(JsonNode.Parse("""{"kind":"event_anchored_chain","anchor":"a","nodes":[{"node_id":"one","offset":"PT1H","tolerance":{"early":"PT0S","late":"PT0S"}},{"node_id":"two","offset":"PT1H","tolerance":{"early":"PT0S","late":"PT0S"}}]}"""), changes[^1].Draft.Body["basis"]);
        Button(cut, "Remove node 1").Click();
        Same(JsonNode.Parse("""{"kind":"event_anchored_chain","anchor":"a","nodes":[{"node_id":"two","offset":"PT1H","tolerance":{"early":"PT0S","late":"PT0S"}}]}"""), changes[^1].Draft.Body["basis"]);
    }

    [Fact(DisplayName = "scheduling-bound-6: supply is shown read-only from the shared availability runtime and no edit writes a member for it")]
    [Trait("Holds", "scheduling-bound-6")]
    public void SupplyIsReadOnly()
    {
        var item = Case("scheduling-authoring.schedule-from-nothing"); var changes = new List<SchedulingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        var supply = cut.Find("[role='note'][aria-label='Supply']");

        Assert.Equal("Supply is read from the shared availability runtime, from each Resource's own availability source, set on the Resource in the Booking editor. It cannot be changed here.", supply.TextContent);
        Assert.Empty(supply.QuerySelectorAll("input, select, textarea, button"));
        Replay(cut, item["input"]!["steps"]);
        Assert.Equal(["basis", "legality_ref", "missed_behaviour", "name", "on_type_id", "starts", "tolerance", "window"], changes[^1].Draft.Body.Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "scheduling-bound-7: the editor offers no resource, capability or calendar picker; value-domain resolvers are carried to the Protocol and Constraint editors")]
    [Trait("Holds", "scheduling-bound-7")]
    public void NoValueDomainPickers()
    {
        var cut = Editor(Draft("""{"problem_class":"Workforce","solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{}},"modules":[{"module":"resource_requirements"}]}"""), []);

        Assert.DoesNotContain(cut.FindAll("input, select, textarea").Select(control => control.GetAttribute("aria-label") ?? ""), name => Regex.IsMatch(name, "resource|capabilit|calendar", RegexOptions.IgnoreCase));
    }

    [Fact(DisplayName = "scheduling-auth-11, scheduling-auth-15: a draft from the create action starts at None with a single due date, no solver and no chain chosen")]
    [Trait("Holds", "scheduling-auth-11"), Trait("Holds", "scheduling-auth-15")]
    public void CreateActionDefaults()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(SchedulingDraft.Empty, changes);

        Assert.Equal("None", SelectedText(cut, "Problem class"));
        Assert.Equal("Single due date", SelectedText(cut, "Basis"));
        Assert.NotNull(Labelled(cut, "Due date"));
        Assert.Empty(cut.FindAll("[aria-label='Solver backend']"));
        Assert.Empty(cut.FindAll("[aria-label='Constraint module']"));
        Assert.Empty(cut.FindAll("[aria-label='Chain anchor']"));
        Assert.Empty(changes);
    }

    [Fact(DisplayName = "scheduling-auth-11: choosing None removes problem_class, and the seven classes are offered by their names")]
    [Trait("Holds", "scheduling-auth-11")]
    public void ProblemClassNoneRemovesTheMember()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project"}"""), changes);

        Assert.Equal(["", "Workforce", "Timetabling", "ProtocolAdmission", "Project", "JobShop", "Mobility", "CompositeRecovery"], OptionValues(cut, "Problem class"));
        Type(cut, "Problem class", "");
        Same(new JsonObject(), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-auth-2: a basis replaces the due date, a single due date removes the basis, and a chain removes the window")]
    [Trait("Holds", "scheduling-auth-2")]
    public void BasisReplacesDueDate()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"due_date":"2027-01-01T00:00:00Z","window":{"earliest":"2026-12-25T00:00:00Z","latest":"2027-01-08T00:00:00Z"}}"""), changes);

        Type(cut, "Basis", "completion_offset");
        Same(JsonNode.Parse("""{"basis":{"kind":"completion_offset"},"window":{"earliest":"2026-12-25T00:00:00Z","latest":"2027-01-08T00:00:00Z"}}"""), changes[^1].Draft.Body);
        Type(cut, "Completion offset", "P30D");
        Type(cut, "Basis", "event_anchored_chain");
        Same(JsonNode.Parse("""{"basis":{"kind":"event_anchored_chain"}}"""), changes[^1].Draft.Body);
        Type(cut, "Basis", "");
        Same(new JsonObject(), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-auth-2: a threshold basis takes a reading, a crossing and a decimal value")]
    [Trait("Holds", "scheduling-auth-2")]
    public void ThresholdBasis()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("{}"), changes);
        Type(cut, "Basis", "threshold");
        Type(cut, "Threshold reading", "odometer");
        Type(cut, "Threshold crossing", "above");
        Type(cut, "Threshold value", "10000.5");
        Type(cut, "Threshold value", "not a number");

        Same(JsonNode.Parse("""{"basis":{"kind":"threshold","reading":"odometer","crossing":"above","value":10000.5}}"""), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-auth-3, scheduling-auth-4: emptying both bounds removes the window and the tolerance")]
    [Trait("Holds", "scheduling-auth-3"), Trait("Holds", "scheduling-auth-4")]
    public void EmptyBoundsRemoveWindowAndTolerance()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"window":{"earliest":"-P1D","latest":"P1D"},"tolerance":{"early":"PT0S","late":"PT1H"}}"""), changes);
        Type(cut, "Window earliest", "");
        Same(JsonNode.Parse("""{"latest":"P1D"}"""), changes[^1].Draft.Body["window"]);
        Type(cut, "Window latest", "");
        Type(cut, "Tolerance early", "");
        Type(cut, "Tolerance late", "");

        Same(new JsonObject(), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-auth-12: changing what it starts clears the target, and targets come from the chosen kind")]
    [Trait("Holds", "scheduling-auth-12")]
    public void StartsTargetFollowsKind()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"starts":{"kind":"form","target_id":"forms.inspection"}}"""), changes);
        Type(cut, "Starts", "workflow");

        Same(JsonNode.Parse("""{"kind":"workflow"}"""), changes[^1].Draft.Body["starts"]);
        Assert.Equal(["", "workflows.check"], OptionValues(cut, "Starts target"));
        Type(cut, "Starts", "");
        Same(new JsonObject(), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-bound-4: the guarantee offers only what the chosen backend declares; a value it does not offer is shown, not rewritten")]
    [Trait("Holds", "scheduling-bound-4")]
    public void GuaranteeComesFromTheBackend()
    {
        var cut = Editor(Draft("""{"problem_class":"Project","solver":{"backend_id":"prototype.greedy-first-fit.v1","parameters":{},"guarantee":"optimal_proof"}}"""), []);

        Assert.Equal(["Not set", "optimal_proof (not offered)", "bounded_heuristic"], OptionTexts(cut, "Solver guarantee"));
        Assert.Equal("optimal_proof", Labelled(cut, "Solver guarantee").GetAttribute("value"));
    }

    [Fact(DisplayName = "scheduling-bound-1: changing the backend keeps its parameters, budget and guarantee; Not set removes the solver")]
    [Trait("Holds", "scheduling-bound-1")]
    public void BackendChangeKeepsTheRest()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","solver":{"backend_id":"prototype.greedy-first-fit.v1","parameters":{"seed":1},"guarantee":"bounded_heuristic","work_budget":5}}"""), changes);
        Type(cut, "Solver backend", "prototype.exhaustive-proof.v2");
        Same(JsonNode.Parse("""{"backend_id":"prototype.exhaustive-proof.v2","parameters":{"seed":1},"guarantee":"bounded_heuristic","work_budget":5}"""), changes[^1].Draft.Body["solver"]);
        Type(cut, "Work budget", "");
        Type(cut, "Solver guarantee", "");
        Same(JsonNode.Parse("""{"backend_id":"prototype.exhaustive-proof.v2","parameters":{"seed":1}}"""), changes[^1].Draft.Body["solver"]);
        Type(cut, "Solver backend", "");

        Same(JsonNode.Parse("""{"problem_class":"Project"}"""), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-bound-1: backend parameters accept a JSON object and leave the draft unchanged for anything else")]
    [Trait("Holds", "scheduling-bound-1")]
    public void BackendParametersAreAJsonObject()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{}}}"""), changes);
        Type(cut, "Backend parameters", """{"seed": 7}""");
        Same(JsonNode.Parse("""{"backend_id":"prototype.exhaustive-proof.v2","parameters":{"seed":7}}"""), changes[^1].Draft.Body["solver"]);
        var before = changes.Count;
        foreach (var text in new[] { """{"seed":""", "[1]", "3", "null" }) Type(cut, "Backend parameters", text);

        Assert.Equal(before, changes.Count);
    }

    [Fact(DisplayName = "scheduling-bound-2: a work budget is a whole number; other text leaves the draft unchanged")]
    [Trait("Holds", "scheduling-bound-2")]
    public void WorkBudgetIsWhole()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{}}}"""), changes);
        Type(cut, "Work budget", "12");
        var before = changes.Count;
        Type(cut, "Work budget", "1.5");

        Assert.Equal(before, changes.Count);
        Same(JsonNode.Parse("""{"backend_id":"prototype.exhaustive-proof.v2","parameters":{},"work_budget":12}"""), changes[^1].Draft.Body["solver"]);
    }

    [Fact(DisplayName = "scheduling-bound-2: Int64.MinValue typed as a work budget or a minimum gap is refused as out of range, not thrown")]
    [Trait("Holds", "scheduling-bound-2")]
    public void WholeNumberBelowTheSafeRangeIsRefused()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","solver":{"backend_id":"prototype.exhaustive-proof.v2","parameters":{},"work_budget":10},"modules":[{"module":"precedence","parameters":{"constraints":[{"id":"print-after-layout","beforeActivityId":"layout","afterActivityId":"print","minimumGapSlots":0}]}}]}"""), changes);
        Type(cut, "Work budget", "-9223372036854775808");
        Type(cut, "Precedence 1 minimum gap slots", "-9223372036854775808");

        Assert.Empty(changes);
    }

    [Fact(DisplayName = "scheduling-bound-2: changing the module drops its parameters, other modules are kept, and removing the last constraint removes the parameters")]
    [Trait("Holds", "scheduling-bound-2")]
    public void ModuleEditsKeepOtherModules()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","modules":[{"module":"precedence","parameters":{"constraints":[{"id":"p","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":2}]}},{"module":"no_overlap"}]}"""), changes);
        Button(cut, "Remove precedence constraint 1").Click();
        Same(JsonNode.Parse("""[{"module":"precedence"},{"module":"no_overlap"}]"""), changes[^1].Draft.Body["modules"]);
        Type(cut, "Constraint module", "time_windows");
        Same(JsonNode.Parse("""[{"module":"time_windows"},{"module":"no_overlap"}]"""), changes[^1].Draft.Body["modules"]);
        Type(cut, "Constraint module", "");

        Same(JsonNode.Parse("""[{"module":"no_overlap"}]"""), changes[^1].Draft.Body["modules"]);
    }

    [Fact(DisplayName = "scheduling-bound-2: clearing a precedence minimum gap removes the member from the draft")]
    [Trait("Holds", "scheduling-bound-2")]
    public void ClearedMinimumGapIsRemoved()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"problem_class":"Project","modules":[{"module":"precedence","parameters":{"constraints":[{"id":"p","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":2}]}}]}"""), changes);
        Type(cut, "Precedence 1 minimum gap slots", "");

        Assert.NotEmpty(changes);
        Same(JsonNode.Parse("""[{"module":"precedence","parameters":{"constraints":[{"id":"p","beforeActivityId":"a","afterActivityId":"b"}]}}]"""), changes[^1].Draft.Body["modules"]);
    }

    [Fact(DisplayName = "scheduling-auth-13: the legality rule set is cited by id; one the host does not offer is shown as not offered")]
    [Trait("Holds", "scheduling-auth-13")]
    public void LegalityIsCitedById()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"legality_ref":"legality.retired"}"""), changes);

        Assert.Equal("legality.retired (not offered)", SelectedText(cut, "Legality rule set"));
        Type(cut, "Legality rule set", "");
        Same(new JsonObject(), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-eng-19: unshown members (envelope, grace, escalation) are carried unchanged through every edit")]
    [Trait("Holds", "scheduling-eng-19")]
    public void UnshownMembersAreCarried()
    {
        var changes = new List<SchedulingDraftChange>();
        var cut = Editor(Draft("""{"envelope":{"owner":"fleet"},"grace":"P1D","escalation":{"after":"P2D","to":"role.supervisor"}}"""), changes);
        Type(cut, "Name", "Inspection");

        Same(JsonNode.Parse("""{"envelope":{"owner":"fleet"},"grace":"P1D","escalation":{"after":"P2D","to":"role.supervisor"},"name":"Inspection"}"""), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "scheduling-eng-19: read-only disables every control and emits nothing, including Keep and Discard after a new revision arrives")]
    [Trait("Holds", "scheduling-eng-19")]
    public void ReadOnlyEmitsNothing()
    {
        var changes = new List<SchedulingDraftChange>(); var draft = Draft("""{"name":"Inspection"}""");
        var cut = Editor(draft, changes);
        Type(cut, "Name", "Inspection 2");
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "4" }).Add(component => component.ReadOnly, true));
        var before = changes.Count;

        Assert.All(cut.FindAll("input, select, textarea"), control => Assert.NotNull(control.Closest("fieldset[disabled]")));
        Assert.True(Button(cut, "Keep edits").HasAttribute("disabled"));
        Assert.True(Button(cut, "Discard edits").HasAttribute("disabled"));
        Type(cut, "Name", "Inspection 3");
        Button(cut, "Discard edits").Click();
        Assert.Equal(before, changes.Count);
    }

    [Fact(DisplayName = "scheduling-eng-19: a new revision waits behind Keep or Discard while local edits exist, and replaces a clean draft silently")]
    [Trait("Holds", "scheduling-eng-19")]
    public void IncomingRevisionWaitsOnLocalEdits()
    {
        var changes = new List<SchedulingDraftChange>(); var draft = Draft("""{"name":"Inspection"}""");
        var cut = Editor(draft, changes);
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"Clean"}""", "4")));
        Assert.Equal("Clean", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[role='alert']"));

        Type(cut, "Name", "Mine");
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"Theirs"}""", "5")));
        Assert.Equal("Mine", Labelled(cut, "Name").GetAttribute("value"));
        Button(cut, "Discard edits").Click();
        Assert.Equal("5", changes[^1].Draft.ExpectedRevision);
        Same(JsonNode.Parse("""{"name":"Theirs"}"""), changes[^1].Draft.Body);
        Assert.Equal([2, 3], changes.Select(change => change.Generation));
    }

    [Fact(DisplayName = "scheduling-eng-19: a host draft whose revision is not canonical is refused, never repaired")]
    [Trait("Holds", "scheduling-eng-19")]
    public void NonCanonicalRevisionIsRefused()
    {
        var cut = Editor(Draft("""{"name":"Inspection"}""", "03"), []);

        Assert.Equal("A host draft was not loaded because its revision is not a canonical decimal string.", cut.Find("p[role='status']").TextContent);
        Assert.Empty(cut.FindAll("[aria-label='Name']"));
    }

    [Fact(DisplayName = "scheduling-eng-19: the screen renders the editor inside the host shell")]
    [Trait("Holds", "scheduling-eng-19")]
    public void ScreenUsesTheHostShell()
    {
        RenderFragment<RenderFragment> shell = editor => builder => { builder.OpenElement(0, "main"); builder.AddAttribute(1, "aria-label", "Host shell"); builder.AddContent(2, editor); builder.CloseElement(); };
        var cut = Render<HarborlineSchedulingAuthoringScreen>(parameters => parameters.Add(component => component.Shell, shell).Add(component => component.Value, SchedulingDraft.Empty).Add(component => component.Catalogue, Catalogue));

        Assert.NotNull(cut.Find("main[aria-label='Host shell'] form[aria-label='Schedule definition draft']"));
    }

    // The shared runner's per-case lane (tooling/run-ui-module-shared.mjs): each fixture case, replayed or rendered with its refusals.
    [Theory, Trait("ModuleConformance", "hlp.ui.scheduling-authoring"), Trait("Holds", "scheduling-eng-19")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.scheduling-authoring", MemberType = typeof(SharedFixtureBatch))]
    public void Shared_fixture_conforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () =>
        {
            var raw = SharedFixtureBatch.Current;
            if (string.IsNullOrWhiteSpace(raw)) return;
            var item = JsonNode.Parse(raw)!;
            var draft = DraftOf(item["input"]!["draft"]!);
            var diagnostics = item["input"]!["diagnostics"] is JsonArray list
                ? new SchedulingDiagnosticSet(draft.Identity, draft.ExpectedRevision, 0, [.. list.Select(entry => new SchedulingDiagnostic(entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>()))])
                : null;
            var changes = new List<SchedulingDraftChange>();
            var cut = Editor(draft, changes, diagnostics);
            var expected = item["expected"]!;
            if (item["input"]!["steps"] is { } steps)
            {
                Replay(cut, steps);
                Same(expected["body"], changes[^1].Draft.Body);
            }
            if (expected["refusalList"] is JsonArray refusals) Assert.Equal(refusals.Select(entry => entry!.GetValue<string>()), Listed(cut).Select(entry => entry.TextContent));
            if (expected["invalid"] is JsonArray invalid) foreach (var field in invalid) Assert.Equal("true", Labelled(cut, field!.GetValue<string>()).GetAttribute("aria-invalid"));
        });
    }
}
