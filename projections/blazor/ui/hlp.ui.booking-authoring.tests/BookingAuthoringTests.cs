using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.BookingAuthoring;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// T-606/member-editors, the Blazor lane. It reads the same conformance fixture as the React lane; expected bodies are literals written
// from the Booking grammar, never captured from an editor (T-724 ruling 121), and hlp.blocks.calendar.tests admits the same bodies
// through BookingDefinitionAdmission.
public sealed class BookingAuthoringTests : BunitContext
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "booking-authoring-fixtures", "fixtures.yaml")))!;
    private static JsonNode Case(string id) => Fixture["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
    private static IReadOnlyList<BookingOption> Options(string member) => [.. Fixture["catalogue"]![member]!.AsArray().Select(entry => entry is JsonObject option ? new BookingOption(option["id"]!.GetValue<string>(), option["label"]!.GetValue<string>()) : (BookingOption)entry!.GetValue<string>())];
    private static readonly BookingCatalogue Catalogue = new() { ResourceTypes = Options("resourceTypes"), BookableTypes = Options("bookableTypes"), Resources = Options("resources"), MaintenanceSources = Options("maintenanceSources") };
    private static BookingDraft DraftOf(JsonNode draft) => new(draft["identity"]!.GetValue<string>(), draft["versionId"]!.GetValue<string>(), draft["expectedRevision"]?.GetValue<string>(), draft["body"]!.DeepClone().AsObject());
    private static BookingDraft Resource(string body, string revision = "3") { var parsed = JsonNode.Parse(body)!.AsObject(); parsed["kind"] = "resource"; return new("resource.room", "1.0.0", revision, parsed); }
    private static BookingDraft Bookable(string body) { var parsed = JsonNode.Parse(body)!.AsObject(); parsed["kind"] = "bookable"; return new("bookable.x", "1.0.0", null, parsed); }

    private IRenderedComponent<HarborlineBookingAuthoringEditor> Editor(BookingDraft value, List<BookingDraftChange> changes, BookingDiagnosticSet? diagnostics = null, bool readOnly = false, BookingCatalogue? catalogue = null)
        => Render<HarborlineBookingAuthoringEditor>(parameters => parameters.Add(component => component.Value, value).Add(component => component.Catalogue, catalogue ?? Catalogue).Add(component => component.Diagnostics, diagnostics).Add(component => component.ReadOnly, readOnly).Add(component => component.DraftChanged, (BookingDraftChange change) => changes.Add(change)));
    private static IElement Labelled(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string label) => cut.Find($"[aria-label='{label}']");
    private static void Type(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string label, string value) => Labelled(cut, label).Change(value);
    private static void Check(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string label, bool on) => Labelled(cut, label).Change(on);
    private static IElement Button(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
    private static string[] Items(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string list) => [.. cut.FindAll($"ul[aria-label='{list}'] li").Select(item => item.TextContent)];
    // Replays a fixture case's steps through the rendered editor's labelled controls, exactly as the React lane does.
    private static void Replay(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, JsonNode? steps)
    {
        foreach (var step in steps!.AsArray())
        {
            if (step!["set"] is { } set) Type(cut, set.GetValue<string>(), step["value"]!.GetValue<string>());
            else if (step["check"] is { } check) Check(cut, check.GetValue<string>(), Labelled(cut, check.GetValue<string>()).HasAttribute("checked") is false);
            else Button(cut, step["click"]!.GetValue<string>()).Click();
        }
    }
    private static void Same(JsonNode? expected, JsonNode actual) => Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());

    [Fact(DisplayName = "booking-eng-20, booking-auth-1, -2, -3, -5: a Resource authored from nothing is exactly the fixture body, envelope carried unchanged")]
    [Trait("Holds", "booking-eng-20"), Trait("Holds", "booking-auth-1"), Trait("Holds", "booking-auth-2"), Trait("Holds", "booking-auth-3"), Trait("Holds", "booking-auth-5")]
    public void ResourceFromNothing()
    {
        var item = Case("booking-authoring.resource-from-nothing"); var changes = new List<BookingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        Replay(cut, item["input"]!["steps"]);

        Same(item["expected"]!["body"], changes[^1].Draft.Body);
        Assert.Equal("", changes[^1].Draft.Identity);
    }

    [Fact(DisplayName = "booking-eng-20, booking-auth-6, -7, -10: a Bookable authored from nothing is exactly the fixture body")]
    [Trait("Holds", "booking-eng-20"), Trait("Holds", "booking-auth-6"), Trait("Holds", "booking-auth-7"), Trait("Holds", "booking-auth-10")]
    public void BookableFromNothing()
    {
        var item = Case("booking-authoring.bookable-from-nothing"); var changes = new List<BookingDraftChange>();
        var cut = Editor(DraftOf(item["input"]!["draft"]!), changes);
        Replay(cut, item["input"]!["steps"]);

        Same(item["expected"]!["body"], changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "booking-eng-20: admission refusals render by field, and a pointer the editor shows no field for is listed with its pointer")]
    [Trait("Holds", "booking-eng-20")]
    public void RefusalsRenderByField()
    {
        var item = Case("booking-authoring.refusals-by-field"); var draft = DraftOf(item["input"]!["draft"]!);
        IReadOnlyList<BookingDiagnostic> diagnostics = [.. item["input"]!["diagnostics"]!.AsArray().Select(entry => new BookingDiagnostic(entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>()))];
        var cut = Editor(draft, [], new BookingDiagnosticSet(draft.Identity, draft.ExpectedRevision, 0, diagnostics));

        foreach (var (field, codes) in item["expected"]!["byField"]!.AsObject())
        {
            Assert.Equal(codes!.AsArray().Select(code => code!.GetValue<string>()), Items(cut, $"{field} refusals"));
            Assert.Equal("true", Labelled(cut, field).GetAttribute("aria-invalid"));
        }
        Assert.Equal(item["expected"]!["unplaced"]!.AsArray().Select(entry => entry!.GetValue<string>()), Items(cut, "Other refusals"));
    }

    [Fact(DisplayName = "booking-eng-20: refusals for another identity, revision or local generation are stale and never shown")]
    [Trait("Holds", "booking-eng-20")]
    public void StaleRefusalsAreNotShown()
    {
        IReadOnlyList<BookingDiagnostic> refusal = [new("booking.definition.name_required", "/name")];
        foreach (var stale in new[] { new BookingDiagnosticSet("resource.other", "3", 0, refusal), new BookingDiagnosticSet("resource.room", "4", 0, refusal), new BookingDiagnosticSet("resource.room", "3", 1, refusal) })
        {
            var cut = Editor(Resource("""{"name":""}"""), [], stale);
            Assert.Empty(Items(cut, "Name refusals"));
            Assert.Null(Labelled(cut, "Name").GetAttribute("aria-invalid"));
        }
        Assert.Equal(["booking.definition.name_required"], Items(Editor(Resource("""{"name":""}"""), [], new BookingDiagnosticSet("resource.room", "3", 0, refusal)), "Name refusals"));
    }

    [Fact(DisplayName = "read-only disables every control and emits nothing, including Keep and Discard after a new revision arrives")]
    [Trait("Holds", "booking-eng-20")]
    public void ReadOnlyEmitsNothing()
    {
        var changes = new List<BookingDraftChange>(); var draft = Resource("""{"name":"Room"}""");
        var cut = Editor(draft, changes);
        Type(cut, "Name", "Room 2");
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "4" }).Add(component => component.ReadOnly, true));
        var before = changes.Count;

        Assert.True(cut.Find("fieldset.hl-booking-authoring__read-only").HasAttribute("disabled"));
        Assert.All(cut.FindAll("input, select"), control => Assert.NotNull(control.Closest("fieldset[disabled]")));
        Assert.True(Button(cut, "Keep edits").HasAttribute("disabled"));
        Assert.True(Button(cut, "Discard edits").HasAttribute("disabled"));
        Type(cut, "Name", "Room 3");
        Button(cut, "Discard edits").Click();
        Assert.Equal(before, changes.Count);
    }

    [Fact(DisplayName = "a new identity or revision never silently overwrites local edits: Keep keeps them, Discard loads the host draft")]
    [Trait("Holds", "booking-eng-20")]
    public void DiscardLoadsTheHostDraft()
    {
        var changes = new List<BookingDraftChange>(); var draft = Resource("""{"name":"Room"}""");
        var cut = Editor(draft, changes);
        Type(cut, "Name", "Local name");
        cut.Render(parameters => parameters.Add(component => component.Value, Resource("""{"name":"Host name"}""", "4")));

        Assert.Contains("A different draft or revision arrived while you have local edits.", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
        Assert.Equal("Local name", Labelled(cut, "Name").GetAttribute("value"));
        Button(cut, "Discard edits").Click();
        Assert.Equal("Host name", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Equal("4", changes[^1].Draft.ExpectedRevision);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact(DisplayName = "a clean draft is replaced by a new revision without asking, and Keep leaves local edits in place")]
    [Trait("Holds", "booking-eng-20")]
    public void CleanReplacementAndKeep()
    {
        var cut = Editor(Resource("""{"name":"Room"}"""), []);
        cut.Render(parameters => parameters.Add(component => component.Value, Resource("""{"name":"Replaced"}""", "4")));
        Assert.Equal("Replaced", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[role='alert']"));

        Type(cut, "Name", "Mine");
        cut.Render(parameters => parameters.Add(component => component.Value, Resource("""{"name":"Room"}""", "5")));
        Button(cut, "Keep edits").Click();
        Assert.Equal("Mine", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact(DisplayName = "a host draft whose revision is not a canonical decimal string is refused, never repaired")]
    [Trait("Holds", "booking-eng-20")]
    public void NonCanonicalRevisionIsRefused()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("{}", "03"), changes);
        Assert.Contains("revision is not a canonical decimal string", cut.Find("[role='status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[aria-label='Name']"));
        Assert.Empty(changes);
    }

    [Fact(DisplayName = "booking-auth-1: options take either shape, a current value the host does not offer is shown, and no offer shows a fallback note")]
    [Trait("Holds", "booking-auth-1")]
    public void OptionShapesAndNotOffered()
    {
        var cut = Editor(Resource("""{"from_type_id":"type.retired"}"""), []);
        var options = cut.FindAll("select[aria-label='Resource record type'] option").Select(option => (option.GetAttribute("value"), option.TextContent)).ToArray();
        Assert.Contains(("type.room", "Room"), options);
        Assert.Contains(("type.nurse", "type.nurse"), options);
        Assert.Contains(("type.retired", "type.retired (not offered)"), options);
        Assert.Equal("type.retired", Labelled(cut, "Resource record type").GetAttribute("value"));
    }

    [Fact(DisplayName = "an empty option list shows its fallback note instead of choices")]
    [Trait("Holds", "booking-auth-1")]
    public void FallbackNotes()
    {
        var cut = Editor(Resource("{}"), [], catalogue: new BookingCatalogue());
        var notes = cut.FindAll(".hl-booking-authoring__note").Select(note => note.TextContent).ToArray();
        Assert.Contains("No resource record types offered.", notes);
        Assert.Contains(notes, note => note.StartsWith("No maintenance sources offered", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "booking-auth-2: choosing exclusive capacity removes the pool size; pool keeps it")]
    [Trait("Holds", "booking-auth-2")]
    public void ExclusiveRemovesPoolSize()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"capacity_kind":"pool","pool_size":3}"""), changes);
        Type(cut, "Capacity", "exclusive");
        Same(JsonNode.Parse("""{"capacity_kind":"exclusive","kind":"resource"}"""), changes[^1].Draft.Body);
        Assert.Empty(cut.FindAll("[aria-label='Pool size']"));
    }

    [Fact(DisplayName = "booking-auth-3: an emptied buffer removes the member; text that is not a number leaves the draft unchanged")]
    [Trait("Holds", "booking-auth-3")]
    public void BufferNumbers()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"setup_minutes":10}"""), changes);
        Type(cut, "Setup minutes", "");
        Same(JsonNode.Parse("""{"kind":"resource"}"""), changes[^1].Draft.Body);
        var before = changes.Count;
        Type(cut, "Cleanup minutes", "ten");
        Assert.Equal(before, changes.Count);
    }

    [Fact(DisplayName = "booking-auth-5: maintenance is chosen from the record state the host offers, and unchecking the last source removes the member")]
    [Trait("Holds", "booking-auth-5")]
    public void MaintenanceSources()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"maintenance_windows":["field.under_repair"]}"""), changes);
        Check(cut, "Maintenance from Out of service", true);
        Same(JsonNode.Parse("""["field.under_repair","field.out_of_service"]"""), changes[^1].Draft.Body["maintenance_windows"]!);
        Check(cut, "Maintenance from field.under_repair", false); Check(cut, "Maintenance from Out of service", false);
        Same(JsonNode.Parse("""{"kind":"resource"}"""), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "booking-auth-6: a duration is edited in place and removed, and an invalid duration is not added")]
    [Trait("Holds", "booking-auth-6")]
    public void Durations()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable("""{"duration_intervals_minutes":[30,60]}"""), changes);
        Type(cut, "Duration 2 minutes", "45");
        Same(JsonNode.Parse("[30,45]"), changes[^1].Draft.Body["duration_intervals_minutes"]!);
        Button(cut, "Remove duration 1").Click();
        Same(JsonNode.Parse("[45]"), changes[^1].Draft.Body["duration_intervals_minutes"]!);
        var before = changes.Count;
        Type(cut, "New duration minutes", ""); Button(cut, "Add duration").Click();
        Assert.Equal(before, changes.Count);
    }

    [Fact(DisplayName = "booking-auth-7: required resources keep the order they were chosen, and a required id the host does not offer is shown")]
    [Trait("Holds", "booking-auth-7")]
    public void RequiredResourcesOrder()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable("""{"requires":["pump"]}"""), changes);
        Assert.Contains("pump (not offered)", cut.FindAll(".hl-booking-authoring__note").Select(note => note.TextContent));
        Check(cut, "Required resource instructor", true); Check(cut, "Required resource Training room", true);
        Same(JsonNode.Parse("""["pump","instructor","room"]"""), changes[^1].Draft.Body["requires"]!);
    }

    [Fact(DisplayName = "booking-auth-10: the waitlist is only a flag; clearing an absent flag writes nothing and clearing true writes false")]
    [Trait("Holds", "booking-auth-10")]
    public void WaitlistFlag()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable("""{"waitlist":true}"""), changes);
        Check(cut, "Waitlist", false);
        Same(JsonValue.Create(false), changes[^1].Draft.Body["waitlist"]!);
        Assert.False(Labelled(cut, "Waitlist").HasAttribute("checked"));

        var absent = new List<BookingDraftChange>();
        Check(Editor(Bookable("{}"), absent), "Waitlist", false);
        Same(JsonNode.Parse("""{"kind":"bookable"}"""), absent[^1].Draft.Body);
    }

    [Fact(DisplayName = "members the editor does not author are carried unchanged through an edit")]
    [Trait("Holds", "booking-eng-20")]
    public void UnauthoredMembersSurvive()
    {
        const string body = """{"envelope":{"cascade_layer":"domain_package"},"book_gate":[{"capability":"training.book"}],"eligibility_expression":"x == 1","require_all":true,"future_member":{"a":1}}""";
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable(body), changes);
        Type(cut, "Name", "Renamed");
        var expected = JsonNode.Parse(body)!.AsObject(); expected["kind"] = "bookable"; expected["name"] = "Renamed";
        Same(expected, changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "booking-eng-20: the screen renders the editor inside the host-supplied shell")]
    [Trait("Holds", "booking-eng-20")]
    public void ScreenUsesTheHostShell()
    {
        RenderFragment<RenderFragment> shell = editor => builder => { builder.OpenElement(0, "main"); builder.AddAttribute(1, "aria-label", "Host shell"); builder.AddContent(2, editor); builder.CloseElement(); };
        var cut = Render<HarborlineBookingAuthoringScreen>(parameters => parameters.Add(component => component.Shell, shell).Add(component => component.Value, BookingDraft.Empty).Add(component => component.Catalogue, Catalogue));
        Assert.NotNull(cut.Find("main[aria-label='Host shell'] form[aria-label='Booking definition draft']"));
    }

    private static BookingDiagnosticSet Refused(BookingDraft draft, params (string Code, string Pointer)[] diagnostics) => new(draft.Identity, draft.ExpectedRevision, 0, [.. diagnostics.Select(item => new BookingDiagnostic(item.Code, item.Pointer))]);
    private static string[] OptionTexts(IRenderedComponent<HarborlineBookingAuthoringEditor> cut, string select) => [.. cut.FindAll($"select[aria-label='{select}'] option").Select(option => option.TextContent)];

    [Fact(DisplayName = "booking-eng-20: the revision grammar admits only \"0\" or [1-9][0-9]* up to Int64.MaxValue, compared as text")]
    [Trait("Holds", "booking-eng-20")]
    public void RevisionGrammar()
    {
        // Literals from the canonical revision rule shared with Records and Rules (ADR 0096 option and revision shapes).
        (string? Text, bool Canonical)[] table = [("0", true), ("7", true), ("10", true), ("9223372036854775806", true), ("9223372036854775807", true), ("999999999999999999", true),
            ("9223372036854775808", false), ("10000000000000000000", false), ("00", false), ("01", false), ("", false), ("1a", false), (" 1", false), ("-1", false), ("/", false), (":", false), (null, false)];
        Assert.Equal(table.Select(row => row.Canonical), table.Select(row => BookingRevisionText.IsCanonical(row.Text)));
        Assert.Equal([true, true, false], new[] { null, "3", "03" }.Select(BookingRevisionText.IsAcceptable));
    }

    [Fact(DisplayName = "booking-eng-20: every Resource pointer renders under its own field, and a Bookable member on a Resource is listed with its pointer")]
    [Trait("Holds", "booking-eng-20")]
    public void ResourcePointersByField()
    {
        (string Pointer, string Field)[] pointers = [("/name", "Name"), ("/from_type_id", "Resource record type"), ("/capacity_kind", "Capacity"), ("/pool_size", "Pool size"), ("/setup_minutes", "Setup minutes"), ("/cleanup_minutes", "Cleanup minutes"), ("/maintenance_windows/0", "Maintenance"), ("/availability_from", "Availability source")];
        var draft = Resource("""{"capacity_kind":"pool"}""");
        var cut = Editor(draft, [], Refused(draft, [.. pointers.Select(item => ($"code{item.Pointer}", item.Pointer)), ("code.on", "/on_type_id")]));
        foreach (var (pointer, field) in pointers) Assert.Equal([$"code{pointer}"], Items(cut, $"{field} refusals"));
        foreach (var field in new[] { "Name", "Resource record type", "Capacity", "Pool size", "Setup minutes", "Cleanup minutes", "Availability source" }) Assert.Equal("true", Labelled(cut, field).GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.FindAll("fieldset").Single(set => set.QuerySelector("legend")!.TextContent == "Maintenance").GetAttribute("aria-invalid"));
        Assert.Equal(["code.on at /on_type_id"], Items(cut, "Other refusals"));
    }

    [Fact(DisplayName = "booking-eng-20: every Bookable pointer renders under its own field, and a Resource member on a Bookable is listed with its pointer")]
    [Trait("Holds", "booking-eng-20")]
    public void BookablePointersByField()
    {
        (string Pointer, string Field)[] pointers = [("/name", "Name"), ("/on_type_id", "Offered against record type"), ("/duration_intervals_minutes/1", "Durations"), ("/requires/0", "Required resources"), ("/waitlist", "Waitlist")];
        var draft = Bookable("{}") with { ExpectedRevision = "2" };
        var cut = Editor(draft, [], Refused(draft, [.. pointers.Select(item => ($"code{item.Pointer}", item.Pointer)), ("code.pool", "/pool_size"), ("code.root", "")]));
        foreach (var (pointer, field) in pointers) Assert.Equal([$"code{pointer}"], Items(cut, $"{field} refusals"));
        foreach (var legend in new[] { "Durations", "Required resources" }) Assert.Equal("true", cut.FindAll("fieldset").Single(set => set.QuerySelector("legend")!.TextContent == legend).GetAttribute("aria-invalid"));
        Assert.Equal("true", Labelled(cut, "Waitlist").GetAttribute("aria-invalid"));
        Assert.Equal(["code.pool at /pool_size", "code.root at "], Items(cut, "Other refusals"));
    }

    [Fact(DisplayName = "booking-eng-20: with nothing refused no field is marked invalid and no refusal list renders")]
    [Trait("Holds", "booking-eng-20")]
    public void NothingRefused()
    {
        var draft = Resource("""{"capacity_kind":"pool","pool_size":2}""");
        var cut = Editor(draft, [], Refused(draft));
        foreach (var field in new[] { "Name", "Resource record type", "Capacity", "Pool size", "Setup minutes", "Availability source" }) Assert.False(Labelled(cut, field).HasAttribute("aria-invalid"), field);
        Assert.Empty(cut.FindAll("ul"));
    }

    [Fact(DisplayName = "booking-eng-20: each local change reports the next local generation, Discard reports one more, and a discarded draft is clean again")]
    [Trait("Holds", "booking-eng-20")]
    public void Generations()
    {
        var changes = new List<BookingDraftChange>(); var draft = Resource("""{"name":"Room"}""");
        var cut = Editor(draft, changes);
        Type(cut, "Name", "A"); Type(cut, "Name", "B");
        Assert.Equal([1, 2], changes.Select(change => change.Generation));
        Assert.Equal("2", cut.Find("form").GetAttribute("data-generation"));
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "4" }));
        Button(cut, "Discard edits").Click();
        Assert.Equal([1, 2, 3], changes.Select(change => change.Generation));
        cut.Render(parameters => parameters.Add(component => component.Value, Resource("""{"name":"Five"}""", "5")));
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Equal("Five", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Equal("4", cut.Find("form").GetAttribute("data-generation"));
    }

    [Fact(DisplayName = "read-only ignores edits and Keep, so a waiting draft stays waiting")]
    [Trait("Holds", "booking-eng-20")]
    public void ReadOnlyIgnoresKeep()
    {
        var changes = new List<BookingDraftChange>(); var draft = Resource("""{"name":"Room"}""");
        var cut = Editor(draft, changes);
        Type(cut, "Name", "Mine");
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "4" }).Add(component => component.ReadOnly, true));
        Type(cut, "Name", "Ignored");
        Button(cut, "Keep edits").Click();
        Assert.Single(changes);
        Assert.NotEmpty(cut.FindAll("[role='alert']"));
        Assert.Equal("", cut.Find("form").GetAttribute("data-read-only"));
    }

    [Fact(DisplayName = "a later host draft with a non-canonical revision is ignored: the current draft stays and nothing is emitted")]
    [Trait("Holds", "booking-eng-20")]
    public void LaterNonCanonicalRevisionIgnored()
    {
        var changes = new List<BookingDraftChange>(); var draft = Resource("""{"name":"Room"}""");
        var cut = Editor(draft, changes);
        cut.Render(parameters => parameters.Add(component => component.Value, Resource("""{"name":"Bad"}""", "04")));
        Assert.Equal("Room", Labelled(cut, "Name").GetAttribute("value"));
        Assert.Equal("A host draft was not loaded because its revision is not a canonical decimal string.", cut.Find("p[role='status']").TextContent);
        Assert.Empty(changes);
    }

    [Fact(DisplayName = "the header shows unassigned identity, version and revision, and offers a kind only until one is chosen")]
    [Trait("Holds", "booking-eng-20")]
    public void HeaderAndKind()
    {
        var cut = Editor(BookingDraft.Empty, []);
        Assert.Equal(("Not yet assigned", "Not yet assigned", "None"), (Labelled(cut, "Identity").TextContent, Labelled(cut, "Version").TextContent, Labelled(cut, "Expected revision").TextContent));
        Assert.Equal(["Choose a definition kind", "Resource", "Bookable"], OptionTexts(cut, "Definition kind"));
        Type(cut, "Definition kind", "bookable");
        Assert.Equal(["Resource", "Bookable"], OptionTexts(cut, "Definition kind"));
    }

    [Fact(DisplayName = "booking-auth-7: clearing the last required resource retains the required empty array")]
    [Trait("Holds", "booking-auth-7")]
    public void ClearLastRequiredResource()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable("""{"requires":["room"]}"""), changes);
        Check(cut, "Required resource Training room", false);
        Same(JsonNode.Parse("[]"), changes[^1].Draft.Body["requires"]!);
    }

    [Fact(DisplayName = "booking-eng-20: an established kind cannot be changed and its fields stay intact")]
    [Trait("Holds", "booking-eng-20")]
    public void EstablishedKindIsFixed()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"from_type_id":"type.room","availability_from":"base-hours"}"""), changes);
        Assert.True(Labelled(cut, "Definition kind").HasAttribute("disabled"));
        Type(cut, "Definition kind", "bookable");
        Assert.Empty(changes);
        Assert.Equal("base-hours", Labelled(cut, "Availability source").GetAttribute("value"));
    }

    [Fact(DisplayName = "an unknown kind is shown as not a Booking kind and is never rewritten")]
    [Trait("Holds", "booking-eng-20")]
    public void UnknownKind()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(new BookingDraft("x", "1", null, JsonNode.Parse("""{"kind":"resource_pool"}""")!.AsObject()), changes);
        Assert.Equal(["resource_pool (not a Booking kind)", "Resource", "Bookable"], OptionTexts(cut, "Definition kind"));
        Assert.Empty(cut.FindAll("[aria-label='Name']"));
        Type(cut, "Definition kind", "resource_pool");
        Assert.Empty(changes);
    }

    [Fact(DisplayName = "booking-auth-2: clearing capacity removes it and the pool size; choosing pool shows an empty pool size; a stray pool size is still shown")]
    [Trait("Holds", "booking-auth-2")]
    public void CapacityEdges()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"capacity_kind":"pool","pool_size":3}"""), changes);
        Type(cut, "Capacity", "");
        Same(JsonNode.Parse("""{"kind":"resource"}"""), changes[^1].Draft.Body);
        Type(cut, "Capacity", "pool");
        Same(JsonNode.Parse("""{"kind":"resource","capacity_kind":"pool"}"""), changes[^1].Draft.Body);
        Assert.Equal("", Labelled(cut, "Pool size").GetAttribute("value"));
        var stray = Editor(Resource("""{"pool_size":3}"""), []);
        Assert.Equal("3", Labelled(stray, "Pool size").GetAttribute("value"));
        Assert.Equal(["Not set (exclusive)", "exclusive", "pool"], OptionTexts(stray, "Capacity"));
    }

    [Fact(DisplayName = "booking-auth-2: a capacity outside the grammar is shown as not a current spelling")]
    [Trait("Holds", "booking-auth-2")]
    public void UnknownCapacity()
    {
        var cut = Editor(Resource("""{"capacity_kind":"scalar"}"""), []);
        Assert.Equal("scalar", Labelled(cut, "Capacity").GetAttribute("value"));
        Assert.Contains("scalar (not a current spelling)", OptionTexts(cut, "Capacity"));
    }

    [Fact(DisplayName = "booking-auth-6: an empty list shows its note, labels count from one, an invalid edit is a no-op and removing the last duration removes the member")]
    [Trait("Holds", "booking-auth-6")]
    public void DurationEdges()
    {
        Assert.Contains("No durations authored.", Editor(Bookable("{}"), []).FindAll(".hl-booking-authoring__note").Select(note => note.TextContent));
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Bookable("""{"duration_intervals_minutes":[30,60]}"""), changes);
        Assert.DoesNotContain("No durations authored.", cut.FindAll(".hl-booking-authoring__note").Select(note => note.TextContent));
        Assert.Equal("30", Labelled(cut, "Duration 1 minutes").GetAttribute("value"));
        Assert.NotNull(Button(cut, "Remove duration 2"));
        Type(cut, "Duration 1 minutes", "x");
        Assert.Empty(changes);
        Button(cut, "Remove duration 2").Click(); Button(cut, "Remove duration 1").Click();
        Same(JsonNode.Parse("""{"kind":"bookable"}"""), changes[^1].Draft.Body);
        Type(cut, "New duration minutes", "45"); Button(cut, "Add duration").Click();
        Same(JsonNode.Parse("""{"kind":"bookable","duration_intervals_minutes":[45]}"""), changes[^1].Draft.Body);
        Assert.Equal("", Labelled(cut, "New duration minutes").GetAttribute("value"));
    }

    [Fact(DisplayName = "emptying the name writes an empty name; emptying the availability source removes it")]
    [Trait("Holds", "booking-eng-20")]
    public void NameAndAvailability()
    {
        var changes = new List<BookingDraftChange>();
        var cut = Editor(Resource("""{"name":"Room","availability_from":"supply.base-hours"}"""), changes);
        Type(cut, "Name", "");
        Same(JsonNode.Parse("""{"kind":"resource","name":"","availability_from":"supply.base-hours"}"""), changes[^1].Draft.Body);
        Type(cut, "Availability source", "");
        Same(JsonNode.Parse("""{"kind":"resource","name":""}"""), changes[^1].Draft.Body);
    }

    [Fact(DisplayName = "booking-auth-1, -7: a not-offered note appears only for chosen ids the host does not offer, and an empty choice is never one")]
    [Trait("Holds", "booking-auth-1"), Trait("Holds", "booking-auth-7")]
    public void NotOfferedNotes()
    {
        var cut = Editor(Bookable("""{"on_type_id":"","requires":["pump","room"]}"""), []);
        Assert.Equal(["pump (not offered)"], cut.FindAll("p, option").Select(node => node.TextContent).Where(text => text.EndsWith("(not offered)", StringComparison.Ordinal)));
        Assert.True(Labelled(cut, "Required resource Training room").HasAttribute("checked"));
        Assert.False(Labelled(cut, "Required resource instructor").HasAttribute("checked"));
    }

    // The shared runner's per-case lane (tooling/run-ui-module-shared.mjs): each fixture case, replayed or rendered with its refusals.
    [Theory, Trait("ModuleConformance", "hlp.ui.booking-authoring"), Trait("Holds", "booking-eng-20")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.booking-authoring", MemberType = typeof(SharedFixtureBatch))]
    public void Shared_fixture_conforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () =>
        {
            var raw = SharedFixtureBatch.Current;
            if (string.IsNullOrWhiteSpace(raw)) return;
            var item = JsonNode.Parse(raw)!;
            var draft = DraftOf(item["input"]!["draft"]!);
            var diagnostics = item["input"]!["diagnostics"] is JsonArray list
                ? new BookingDiagnosticSet(draft.Identity, draft.ExpectedRevision, 0, [.. list.Select(entry => new BookingDiagnostic(entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>()))])
                : null;
            var changes = new List<BookingDraftChange>();
            var cut = Editor(draft, changes, diagnostics);
            var expected = item["expected"]!;
            if (item["input"]!["steps"] is { } steps)
            {
                Replay(cut, steps);
                Same(expected["body"], changes[^1].Draft.Body);
            }
            if (expected["byField"] is JsonObject byField)
                foreach (var (field, codes) in byField) Assert.Equal(codes!.AsArray().Select(code => code!.GetValue<string>()), Items(cut, $"{field} refusals"));
            if (expected["unplaced"] is JsonArray unplaced) Assert.Equal(unplaced.Select(entry => entry!.GetValue<string>()), Items(cut, "Other refusals"));
        });
    }
}
