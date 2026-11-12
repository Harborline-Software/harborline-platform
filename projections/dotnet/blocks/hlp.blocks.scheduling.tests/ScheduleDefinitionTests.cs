using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Scheduling.Tests;

/// <summary>
/// T-491/typed-definition, DES-0022 section 2. Oracles: wire literals from DES-0004 section Schedule and the DES-0022
/// inventory, the problem-class names from ADR 0048, and the seed's own Schedulable trait in the platform export.
/// </summary>
public sealed class ScheduleDefinitionTests
{
    private const string Tenant = "tenant-a";
    private const string Id = "inspection.quarterly";
    private const string Instants = """{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-31T00:00:00+02:00"}""";
    private const string Offsets = """{"earliest":"-P1D","latest":"P2D"}""";
    private const string Form = """{"kind":"form","target_id":"forms.inspection"}""";

    private readonly InMemoryVersionedDefinitionStore _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
    });

    private ScheduleDefinitionCatalogue Catalogue => new(_store);

    private static DefinitionKey Key => new(Tenant, DefinitionKind.Schedules, Id);

    private static string Envelope => """
        {"identity":"inspection.quarterly","tenant":"tenant-a","cascade_layer":"domain_package","provenance":{"pack":"general"},"retention_class":"standard","legal_hold":true,"requires":[{"capability":"platform.scheduling"}],"contract":{"major":1,"minor":0}}
        """;

    // A body with an envelope, a name and a form, plus the given members.
    private static string Body(string members)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse($$"""{"envelope":{{Envelope}},"name":"Quarterly","starts":{{Form}},{{members}}}""")!;
        if (body["basis"] is null) body["due_date"] = "2027-01-01T00:00:00Z";
        return body.ToJsonString();
    }

    private async Task<IReadOnlyList<DefinitionRefusal>> RefusalsAsync(string body)
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            _store.SaveDraftAsync(new DefinitionDocument(Key, Id + "@1.0.0", "1.0.0", body), 0, "draft").AsTask());
        return refused.Refusals;
    }

    private async Task<ScheduleDefinitionRevision> RoundTripAsync(string body)
    {
        var saved = await _store.SaveDraftAsync(new DefinitionDocument(Key, Id + "@1.0.0", "1.0.0", body), 0, "draft");
        await _store.PublishAsync(Key, saved.Document.VersionId, 1, "publish");
        return await Catalogue.ResolveAsync(new(Tenant, Id, Id + "@1.0.0"));
    }

    [Fact(DisplayName = "scheduling-ck-1: ScheduleDefinition is content kind 11 under the Scheduling pillar")]
    public void ContentKindIsEleven()
        => Assert.Equal(new PackContentKindEntry("ScheduleDefinition", 11, "Scheduling", PackContentKindStatus.Shipped), ScheduleDefinitionCatalogue.ContentKind);

    [Fact(DisplayName = "L1445, scheduling-ck-10, scheduling-auth-11: a due date and a form are valid, default to no problem class and name no solver")]
    public async Task DueDateAndFormIsValidAndNamesNoSolver()
    {
        var resolved = (await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}}"""))).Definition;

        Assert.Equal(ScheduleProblemClass.None, resolved.ProblemClass);
        Assert.Null(resolved.Basis);
        Assert.Null(resolved.Modules);
        Assert.Contains("\"problem_class\":\"None\"", ScheduleDefinitionJson.Serialize(resolved), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "scheduling-ck-10, scheduling-auth-11: the seven Harborline problem classes round-trip")]
    [InlineData("Workforce", ScheduleProblemClass.Workforce)]
    [InlineData("Timetabling", ScheduleProblemClass.Timetabling)]
    [InlineData("ProtocolAdmission", ScheduleProblemClass.ProtocolAdmission)]
    [InlineData("Project", ScheduleProblemClass.Project)]
    [InlineData("JobShop", ScheduleProblemClass.JobShop)]
    [InlineData("Mobility", ScheduleProblemClass.Mobility)]
    [InlineData("CompositeRecovery", ScheduleProblemClass.CompositeRecovery)]
    public async Task SevenProblemClassesRoundTrip(string literal, ScheduleProblemClass expected)
    {
        var resolved = await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"problem_class":"{{literal}}","modules":[{"module":"no_overlap"}]"""));
        Assert.Equal(expected, resolved.Definition.ProblemClass);
    }

    [Theory(DisplayName = "scheduling-ck-10: a problem class outside the seven, or a number, refuses")]
    [InlineData("\"Astrology\"")]
    [InlineData("\"none\"")]
    [InlineData("1")]
    public async Task UnknownProblemClassRefuses(string literal)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"problem_class":{{literal}}""")));

    [Theory(DisplayName = "scheduling-ck-4, scheduling-auth-2: each of the four generators round-trips with its own members")]
    [InlineData("""{"kind":"fixed_calendar","rrule":"FREQ=DAILY"}""", typeof(FixedCalendarBasis))]
    [InlineData("""{"kind":"completion_offset","offset":"P90D"}""", typeof(CompletionOffsetBasis))]
    [InlineData("""{"kind":"threshold","reading":"hours_run","crossing":"above","value":500.5}""", typeof(ThresholdBasis))]
    public async Task EachGeneratorRoundTrips(string basis, Type expected)
    {
        var resolved = await RoundTripAsync(Body("\"" + $$"""basis":{{basis}},"window":{{Offsets}}"""));
        Assert.IsType(expected, resolved.Definition.Basis);
        Assert.Contains(basis, ScheduleDefinitionJson.Serialize(resolved.Definition), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "scheduling-ck-4, scheduling-auth-2: a generator outside the four, none named, or two at once refuses")]
    [InlineData("""{"kind":"weekly","rrule":"x"}""")]
    [InlineData("""{"rrule":"x"}""")]
    [InlineData("""{"kind":"fixed_calendar","rrule":"x","offset":"P1D"}""")]
    [InlineData("""[{"kind":"fixed_calendar","rrule":"x"},{"kind":"completion_offset","offset":"P1D"}]""")]
    public async Task OnlyOneOfTheFourGeneratorsIsAdmitted(string basis)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")], await RefusalsAsync(Body("\"" + $$"""basis":{{basis}},"window":{{Offsets}}""")));

    [Theory(DisplayName = "scheduling-ck-4: a generator missing what it needs refuses at its member")]
    [InlineData("""{"kind":"fixed_calendar"}""", "scheduling.definition.basis_invalid", "/basis/rrule")]
    [InlineData("""{"kind":"fixed_calendar","rrule":" "}""", "scheduling.definition.basis_invalid", "/basis/rrule")]
    [InlineData("""{"kind":"completion_offset"}""", "scheduling.definition.duration_invalid", "/basis/offset")]
    [InlineData("""{"kind":"completion_offset","offset":"-P1D"}""", "scheduling.definition.duration_invalid", "/basis/offset")]
    [InlineData("""{"kind":"threshold","reading":"","crossing":"below","value":1}""", "scheduling.definition.basis_invalid", "/basis/reading")]
    public async Task IncompleteGeneratorRefuses(string basis, string code, string pointer)
        => Assert.Equal([new DefinitionRefusal(code, pointer)], await RefusalsAsync(Body("\"" + $$"""basis":{{basis}},"window":{{Offsets}}""")));

    [Theory(DisplayName = "scheduling-ck-4: a duration may be whole weeks, PnW, as RFC 5545 section 3.3.6 writes them")]
    [InlineData("P1W")]
    [InlineData("P0W")]
    [InlineData("P52W")]
    public async Task WeekDurationIsAdmitted(string offset)
        => Assert.Equal(offset, Assert.IsType<CompletionOffsetBasis>((await RoundTripAsync(Body("\"" + $$"""basis":{"kind":"completion_offset","offset":"{{offset}}"},"window":{{Offsets}}"""))).Definition.Basis).Offset);

    [Theory(DisplayName = "scheduling-ck-5: ordered bounds that exclude due refuse under ADR-0149")]
    [InlineData("""{"earliest":"P6D","latest":"P1W"}""")]
    [InlineData("""{"earliest":"P1W","latest":"P8D"}""")]
    [InlineData("""{"earliest":"-P1W","latest":"-P6D"}""")]
    public async Task WindowExcludingDueRefuses(string window)
        => Assert.Equal([new DefinitionRefusal("scheduling.definition.window_invalid", "/window")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"fixed_calendar","rrule":"FREQ=DAILY"},"window":{{window}}""")));

    [Theory(DisplayName = "scheduling-ck-5: a week that is not seven days past the other bound does not order")]
    [InlineData("""{"earliest":"P1W","latest":"P7D"}""")]
    [InlineData("""{"earliest":"P8D","latest":"P1W"}""")]
    [InlineData("""{"earliest":"-P6D","latest":"-P1W"}""")]
    public async Task WeekOutOfOrderRefuses(string window)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.WindowInvalid, "/window")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"fixed_calendar","rrule":"FREQ=DAILY"},"window":{{window}}""")));

    [Theory(DisplayName = "scheduling-ck-4: a duration has one unpadded spelling, and a week stands alone")]
    [InlineData(" P1D")]
    [InlineData("P1D ")]
    [InlineData(" P1W ")]
    [InlineData("""\tP1D""")]
    [InlineData("""P1D\n""")]
    [InlineData("P1W2D")]
    [InlineData("P1.5W")]
    [InlineData("+P1W")]
    [InlineData("-P1W")]
    [InlineData("p1w")]
    [InlineData("P1600000W")]
    public async Task UnadmittedDurationSpellingRefuses(string offset)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.DurationInvalid, "/basis/offset")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"completion_offset","offset":"{{offset}}"},"window":{{Offsets}}""")));

    [Fact(DisplayName = "scheduling-ck-5: a padded window bound refuses")]
    public async Task PaddedWindowBoundRefuses()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.WindowInvalid, "/window")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"fixed_calendar","rrule":"FREQ=DAILY"},"window":{"earliest":" P1D","latest":"P2D"}""")));

    [Fact(DisplayName = "scheduling-ck-4, scheduling-auth-6: a chain has one anchor; nodes carry an offset and their own tolerance, and need no window")]
    public async Task ChainNodesCarryOffsetAndTolerance()
    {
        var resolved = await RoundTripAsync(Body(""""
            "basis":{"kind":"event_anchored_chain","anchor":"event.delivery","nodes":[
              {"node_id":"a","offset":"P1D","tolerance":{"early":"PT0S","late":"P1D"}},
              {"node_id":"b","after_node_id":"a","offset":"P2D","tolerance":{"early":"P1D","late":"P3D"}}]}
            """"));

        var chain = Assert.IsType<EventAnchoredChainBasis>(resolved.Definition.Basis);
        Assert.Equal("event.delivery", chain.Anchor);
        Assert.Equal(new ScheduleChainNode("b", "a", "P2D", new("P1D", "P3D")), chain.Nodes[1]);
        Assert.Null(chain.Nodes[0].AfterNodeId);
    }

    [Theory(DisplayName = "scheduling-auth-6, L449: a node cannot leave the chain; it follows the anchor or an earlier node")]
    [InlineData("""{"node_id":"a","after_node_id":"event.other","offset":"P1D","tolerance":{"early":"P0D","late":"P0D"}}""", "/basis/nodes/0/after_node_id")]
    [InlineData("""{"node_id":"a","after_node_id":"a","offset":"P1D","tolerance":{"early":"P0D","late":"P0D"}}""", "/basis/nodes/0/after_node_id")]
    [InlineData("""{"node_id":"","offset":"P1D","tolerance":{"early":"P0D","late":"P0D"}}""", "/basis/nodes/0/node_id")]
    [InlineData("""{"node_id":"a","offset":"P1D"}""", "/basis/nodes/0/tolerance")]
    public async Task ChainNodeRefusals(string node, string pointer)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BasisInvalid, pointer)],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"event_anchored_chain","anchor":"e","nodes":[{{node}}]}""")));

    [Fact(DisplayName = "scheduling-auth-6: a repeated node id, an empty chain and a missing anchor refuse")]
    public async Task ChainShapeRefusals()
    {
        const string Node = """{"node_id":"a","offset":"P1D","tolerance":{"early":"P0D","late":"P0D"}}""";
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BasisInvalid, "/basis/nodes/1/node_id")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"event_anchored_chain","anchor":"e","nodes":[{{Node}},{{Node}}]}""")));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BasisInvalid, "/basis/nodes")],
            await RefusalsAsync(Body("\"" + """basis":{"kind":"event_anchored_chain","anchor":"e","nodes":[]}""")));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BasisInvalid, "/basis/anchor")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"event_anchored_chain","nodes":[{{Node}}]}""")));
    }

    [Fact(DisplayName = "scheduling-ck-5, scheduling-auth-3: the window keeps earliest and latest apart")]
    public async Task WindowRoundTrips()
    {
        var window = (await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}}"""))).Definition.Window;
        Assert.Equal(new ScheduleWindow("2027-01-01T00:00:00Z", "2027-01-31T00:00:00+02:00"), window);
    }

    [Theory(DisplayName = "scheduling-ck-5, scheduling-auth-3, L483: a single point, a reversed window or the wrong form for the basis refuses")]
    [InlineData("""{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-01T00:00:00Z"}""")]
    [InlineData("""{"earliest":"2027-01-02T00:00:00Z","latest":"2027-01-01T00:00:00Z"}""")]
    [InlineData("""{"earliest":"2027-01-01T00:00:00Z"}""")]
    [InlineData("""{"earliest":"2027-01-01T00:00:00","latest":"2027-01-02T00:00:00"}""")]
    [InlineData("""{"earliest":"-P1D","latest":"P1D"}""")]
    public async Task SinglePointWindowRefuses(string window)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.WindowInvalid, "/window")], await RefusalsAsync(Body("\"" + $$"""window":{{window}}""")));

    [Theory(DisplayName = "scheduling-ck-5: with a generator the bounds are durations from the due point, in order")]
    [InlineData("""{"earliest":"P1D","latest":"P1D"}""")]
    [InlineData("""{"earliest":"P2D","latest":"P1D"}""")]
    [InlineData("""{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-02T00:00:00Z"}""")]
    public async Task GeneratedWindowRefuses(string window)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.WindowInvalid, "/window")],
            await RefusalsAsync(Body("\"" + $$"""basis":{"kind":"fixed_calendar","rrule":"FREQ=DAILY"},"window":{{window}}""")));

    [Fact(DisplayName = "scheduling-ck-5: a schedule without a basis requires its own due instant")]
    public async Task LoneDueDateIsRequired()
        => Assert.Equal([new DefinitionRefusal("scheduling.definition.due_date_invalid", "/due_date")],
            await RefusalsAsync($$"""{"envelope":{{Envelope}},"name":"n","starts":{{Form}}}"""));

    [Fact(DisplayName = "scheduling-ck-6, scheduling-ck-7, scheduling-ck-8, scheduling-ck-9, scheduling-auth-4, scheduling-auth-5: tolerance, grace, missed behaviour and escalation are separate members")]
    public async Task ToleranceGraceMissedAndEscalationAreSeparate()
    {
        var definition = (await RoundTripAsync(Body($$"""
            "due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"tolerance":{"early":"PT1H","late":"PT2H"},"grace":"P3D","missed_behaviour":"window_closed","escalation":{"after":"P4D","to":"roles.supervisor"}
            """))).Definition;

        Assert.Equal(new ScheduleTolerance("PT1H", "PT2H"), definition.Tolerance);
        Assert.Equal("P3D", definition.Grace);
        Assert.Equal(ScheduleMissedBehaviour.WindowClosed, definition.MissedBehaviour);
        Assert.Equal(new ScheduleEscalation("P4D", "roles.supervisor"), definition.Escalation);
    }

    [Fact(DisplayName = "scheduling-ck-8, scheduling-auth-5: record and re-plan is the other missed-behaviour kind")]
    public async Task RecordAndReplanRoundTrips()
    {
        var resolved = await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"missed_behaviour":"record_and_replan""" + "\""));
        Assert.Equal(ScheduleMissedBehaviour.RecordAndReplan, resolved.Definition.MissedBehaviour);
        Assert.Contains("\"missed_behaviour\":\"record_and_replan\"", ScheduleDefinitionJson.Serialize(resolved.Definition), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "scheduling-ck-6, scheduling-ck-7, scheduling-ck-9: durations are non-negative ISO 8601, and an escalation names who it goes to")]
    [InlineData("""
        "tolerance":{"early":"soon","late":"P1D"}
        """, "scheduling.definition.duration_invalid", "/tolerance/early")]
    [InlineData("""
        "tolerance":{"early":"P1D","late":"-P1D"}
        """, "scheduling.definition.duration_invalid", "/tolerance/late")]
    [InlineData("""
        "grace":"-P1D"
        """, "scheduling.definition.duration_invalid", "/grace")]
    [InlineData("""
        "grace":"3 days"
        """, "scheduling.definition.duration_invalid", "/grace")]
    [InlineData("""
        "escalation":{"after":"nope","to":"r"}
        """, "scheduling.definition.duration_invalid", "/escalation/after")]
    [InlineData("""
        "escalation":{"after":"P1D","to":" "}
        """, "scheduling.definition.escalation_invalid", "/escalation/to")]
    public async Task MemberRefusals(string members, string code, string pointer)
        => Assert.Equal([new DefinitionRefusal(code, pointer)], await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},{{members.Trim()}}""")));

    [Fact(DisplayName = "scheduling-ck-8: a missed behaviour outside the two kinds refuses")]
    public async Task UnknownMissedBehaviourRefuses()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"missed_behaviour":"ignore""" + "\"")));

    [Fact(DisplayName = "scheduling-ck-11: admitted modules round-trip with their typed parameters")]
    public async Task ModulesRoundTrip()
    {
        var definition = (await RoundTripAsync(Body($$"""
            "due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"problem_class":"Workforce","modules":[{"module":"precedence","parameters":{"constraints":[{"id":"after","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":11}]} },{"module":"no_overlap"}]
            """))).Definition;

        Assert.Equal(["precedence", "no_overlap"], definition.Modules!.Select(module => module.Module));
        Assert.Equal(11, definition.Modules![0].Parameters!.Value.GetProperty("constraints")[0].GetProperty("minimumGapSlots").GetInt32());
        Assert.Null(definition.Modules![1].Parameters);
    }

    [Fact(DisplayName = "scheduling-ck-11, L1149: None names no planner, so modules with no problem class refuse")]
    public async Task ModulesNeedAProblemClass()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.ModulesWithoutProblemClass, "/modules")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"modules":[{"module":"no_overlap"}]""")));

    [Fact(DisplayName = "scheduling-ck-11: a module with no name refuses")]
    public async Task BlankModuleRefuses()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.ModuleInvalid, "/modules/0/module")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"problem_class":"Project","modules":[{"module":" "}]""")));

    [Fact(DisplayName = "scheduling-ck-12, scheduling-auth-13: the legality rule set is cited by reference, verbatim and uninterpreted")]
    public async Task LegalityRefIsACitation()
    {
        var resolved = await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"legality_ref":"rules.eu-561@2026""" + "\""));
        Assert.Equal("rules.eu-561@2026", resolved.Definition.LegalityRef);
    }

    [Fact(DisplayName = "scheduling-ck-12: a blank citation refuses")]
    public async Task BlankLegalityRefRefuses()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.LegalityRefInvalid, "/legality_ref")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"legality_ref":" """ + "\"")));

    [Theory(DisplayName = "scheduling-ck-13, scheduling-auth-1, scheduling-auth-12: a schedule names its type and starts a form, workflow or work item")]
    [InlineData("form", ScheduleStartKind.Form)]
    [InlineData("workflow", ScheduleStartKind.Workflow)]
    [InlineData("work_item", ScheduleStartKind.WorkItem)]
    public async Task StartsAndOnTypeRoundTrip(string kind, ScheduleStartKind expected)
    {
        var definition = (await RoundTripAsync($$"""
            {"envelope":{{Envelope}},"name":"Quarterly","on_type_id":"types.boiler","starts":{"kind":"{{kind}}","target_id":"targets.a"},"due_date":"2027-01-01T00:00:00Z","window":{{Instants}}}
            """)).Definition;

        Assert.Equal("Quarterly", definition.Name);
        Assert.Equal("types.boiler", definition.OnTypeId);
        Assert.Equal(new ScheduleStarts(expected, "targets.a"), definition.Starts);
    }

    [Theory(DisplayName = "scheduling-ck-13, scheduling-auth-1, scheduling-auth-12: a schedule with no name or nothing to start refuses")]
    [InlineData("""{"starts":{"kind":"form","target_id":"f"},"due_date":"2027-01-01T00:00:00Z","window":{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-02T00:00:00Z"}}""", "scheduling.definition.name_required", "/name")]
    [InlineData("""{"name":"n","due_date":"2027-01-01T00:00:00Z","window":{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-02T00:00:00Z"}}""", "scheduling.definition.starts_required", "/starts")]
    [InlineData("""{"name":"n","starts":{"kind":"form","target_id":" "},"due_date":"2027-01-01T00:00:00Z","window":{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-02T00:00:00Z"}}""", "scheduling.definition.starts_required", "/starts")]
    public async Task NameAndStartsAreRequired(string body, string code, string pointer)
        => Assert.Equal([new DefinitionRefusal(code, pointer)], await RefusalsAsync(body));

    [Fact(DisplayName = "scheduling-ck-13: a schedule never does the work itself, and a body member it does not define refuses")]
    public async Task AnUnknownMemberRefuses()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")],
            await RefusalsAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}},"action":"run""" + "\"")));

    [Fact(DisplayName = "scheduling-ck-19: every envelope member round-trips, and the version is the document's")]
    public async Task EnvelopeRoundTrips()
    {
        var envelope = (await RoundTripAsync(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}}"""))).Definition.Envelope!;

        Assert.Equal(Id, envelope.Identity);
        Assert.Equal("1.0.0", envelope.Version);
        Assert.Equal(Tenant, envelope.Tenant);
        Assert.Equal(LayoutCascadeLayer.DomainPackage, envelope.CascadeLayer);
        Assert.Equal("general", envelope.Provenance!.Value.GetProperty("pack").GetString());
        Assert.Equal("standard", envelope.RetentionClass);
        Assert.True(envelope.LegalHold);
        Assert.Equal("platform.scheduling", Assert.Single(envelope.Requires).Capability);
        Assert.Equal(new DefinitionContractVersion(1, 0), envelope.Contract);
    }

    [Fact(DisplayName = "scheduling-ck-19: the catalogue stores no envelope version, so a restored draft publishes under its own")]
    public async Task RestoredDraftPublishesUnderItsOwnVersion()
    {
        var definition = ScheduleDefinitionJson.Deserialize(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}}"""));
        var withVersion = definition with { Envelope = definition.Envelope! with { Version = "1.0.0" } };
        var saved = await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", withVersion, 0, "draft");
        Assert.DoesNotContain("\"version\"", saved.Revision.Document.BodyJson, StringComparison.Ordinal);
        await Catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish");

        await Catalogue.RestoreAsDraftAsync(Tenant, Id, "1.0.0", "1.1.0", 2, "restore");
        var published = await Catalogue.PublishAsync(Tenant, Id, "1.1.0", 3, "publish-restored");

        Assert.Equal("1.1.0", published.Definition.Envelope!.Version);
    }

    [Fact(DisplayName = "scheduling-ck-19: a save whose envelope version disagrees refuses")]
    public async Task EnvelopeVersionMismatchRefuses()
    {
        var definition = ScheduleDefinitionJson.Deserialize(Body("\"" + $$"""due_date":"2027-01-01T00:00:00Z","window":{{Instants}}"""));
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            definition with { Envelope = definition.Envelope! with { Version = "9.9.9" } }, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionMismatch, "/envelope/version")], refused.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-19: a draft may omit the envelope; publishing without one refuses")]
    public async Task EnvelopeIsRequiredPastAuthoring()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", new(null, "n", new(ScheduleStartKind.Form, "f"),
            DueDate: "2027-01-01T00:00:00Z", Window: new("2027-01-01T00:00:00Z", "2027-01-02T00:00:00Z")), 0, "draft");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([new DefinitionRefusal("definition.envelope_required", "/envelope")], refused.Refusals);
    }

    [Theory(DisplayName = "scheduling-ck-19: an envelope for another definition or tenant refuses")]
    [InlineData("other.schedule", "tenant-a")]
    [InlineData("inspection.quarterly", "tenant-b")]
    public async Task EnvelopeCoordinatesMustAgree(string identity, string tenant)
    {
        var refusals = await RefusalsAsync($$"""
            {"envelope":{"identity":"{{identity}}","tenant":"{{tenant}}","cascade_layer":"tenant_configuration","provenance":{},"retention_class":"r","legal_hold":false,"requires":[],"contract":{"major":1,"minor":0} },"name":"n","starts":{{Form}},"due_date":"2027-01-01T00:00:00Z","window":{{Instants}}}
            """);
        Assert.Equal([new DefinitionRefusal("definition.envelope_mismatch", "/envelope")], refusals);
    }

    [Theory(DisplayName = "scheduling-ck-19: provenance, retention class and requires are required members of a present envelope")]
    [InlineData("""
        "retention_class":"r","requires":[]
        """, "/envelope/provenance")]
    [InlineData("""
        "provenance":[],"retention_class":"r","requires":[]
        """, "/envelope/provenance")]
    [InlineData("""
        "provenance":{},"requires":[]
        """, "/envelope/retention_class")]
    [InlineData("""
        "provenance":{},"retention_class":"r"
        """, "/envelope/requires")]
    public async Task EnvelopeMembersAreRequired(string members, string pointer)
    {
        var refusals = await RefusalsAsync($$"""
            {"envelope":{"identity":"{{Id}}","tenant":"{{Tenant}}","cascade_layer":"tenant_configuration",{{members.Trim()}},"legal_hold":false,"contract":{"major":1,"minor":0} },"name":"n","starts":{{Form}},"due_date":"2027-01-01T00:00:00Z","window":{{Instants}}}
            """);
        Assert.Equal([new DefinitionRefusal("scheduling.definition.envelope_invalid", pointer)], refusals);
    }

    [Fact(DisplayName = "scheduling-ck-19, T-572: an envelope with no contract refuses")]
    public async Task EnvelopeContractIsRequired()
    {
        var refusals = await RefusalsAsync($$"""
            {"envelope":{"identity":"{{Id}}","tenant":"{{Tenant}}","cascade_layer":"tenant_configuration","provenance":{},"retention_class":"r","legal_hold":false,"requires":[] },"name":"n","starts":{{Form}},"due_date":"2027-01-01T00:00:00Z","window":{{Instants}}}
            """);
        Assert.Equal("/envelope/contract", Assert.Single(refusals).Pointer);
    }

    [Theory(DisplayName = "scheduling-ck-4: a threshold missing its crossing or value refuses, not reading as above zero")]
    [InlineData("""{"kind":"threshold","reading":"hours_run"}""")]
    [InlineData("""{"kind":"threshold","reading":"hours_run","crossing":"above"}""")]
    [InlineData("""{"kind":"threshold","reading":"hours_run","value":5}""")]
    public async Task ThresholdNeedsCrossingAndValue(string basis)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")],
            await RefusalsAsync(Body("\"basis\":" + basis + ",\"window\":" + Offsets)));

    [Fact(DisplayName = "scheduling-ck-13: starts with no kind refuses, not reading as a form")]
    public async Task StartsNeedsAKind()
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")],
            await RefusalsAsync("{\"name\":\"n\",\"starts\":{\"target_id\":\"f\"},\"window\":" + Instants + "}"));

    [Theory(DisplayName = "scheduling-ck-19: an envelope with no cascade layer or legal hold refuses, not reading as the default")]
    [InlineData("\"legal_hold\":false")]
    [InlineData("\"cascade_layer\":\"tenant_configuration\"")]
    public async Task EnvelopeNeedsCascadeLayerAndLegalHold(string member)
        => Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")], await RefusalsAsync(
            "{\"envelope\":{\"identity\":\"" + Id + "\",\"tenant\":\"" + Tenant + "\"," + member
            + ",\"provenance\":{},\"retention_class\":\"r\",\"requires\":[],\"contract\":{\"major\":1,\"minor\":0}},\"name\":\"n\",\"starts\":" + Form + ",\"due_date\":\"2027-01-01T00:00:00Z\",\"window\":" + Instants + "}"));

    [Theory(DisplayName = "scheduling-ck-4, scheduling-ck-11: a null list entry refuses at its index")]
    [InlineData("\"basis\":{\"kind\":\"event_anchored_chain\",\"anchor\":\"e\",\"nodes\":[null]}", "scheduling.definition.basis_invalid", "/basis/nodes/0")]
    [InlineData("\"window\":{\"earliest\":\"2027-01-01T00:00:00Z\",\"latest\":\"2027-01-02T00:00:00Z\"},\"problem_class\":\"Project\",\"modules\":[null]", "scheduling.definition.module_invalid", "/modules/0")]
    public async Task NullListEntryRefuses(string members, string code, string pointer)
        => Assert.Equal([new DefinitionRefusal(code, pointer)], await RefusalsAsync(Body(members)));

    [Theory(DisplayName = "scheduling-ck-19: a null or blank required capability refuses at its index")]
    [InlineData("null")]
    [InlineData("{\"capability\":\" \"}")]
    public async Task EnvelopeRequirementRefuses(string requirement)
        => Assert.Equal([new DefinitionRefusal("scheduling.definition.envelope_invalid", "/envelope/requires/0")], await RefusalsAsync(
            "{\"envelope\":{\"identity\":\"" + Id + "\",\"tenant\":\"" + Tenant + "\",\"cascade_layer\":\"tenant_configuration\",\"legal_hold\":false"
            + ",\"provenance\":{},\"retention_class\":\"r\",\"requires\":[" + requirement + "],\"contract\":{\"major\":1,\"minor\":0}},\"name\":\"n\",\"starts\":" + Form + ",\"due_date\":\"2027-01-01T00:00:00Z\",\"window\":" + Instants + "}"));

    [Fact(DisplayName = "scheduling-ck-22: the sealed Schedulable trait and its three slots agree with the seed, and only it admits a type to planning")]
    public void SchedulableTraitAgreesWithTheSeed()
    {
        Assert.Equal("platform.trait.schedulable", SchedulableTrait.Id);
        Assert.Equal(["work_calendar", "qualification_set", "capacity_slots"], SchedulableTrait.Slots);

        using var export = JsonDocument.Parse(PlatformPackageSeed.Export());
        var seeded = export.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-4")
            .GetProperty("content").GetProperty("payload").GetProperty("members").EnumerateArray()
            .Single(member => member.GetProperty("id").GetString() == SchedulableTrait.Id);
        Assert.True(seeded.GetProperty("sealedTrait").GetBoolean());
        Assert.Equal(SchedulableTrait.Slots, seeded.GetProperty("slots").EnumerateArray().Select(slot => slot.GetString()));

        Assert.True(SchedulableTrait.AdmitsToPlanning(["platform.trait.bookable-resource", "platform.trait.schedulable"]));
        Assert.False(SchedulableTrait.AdmitsToPlanning(["platform.trait.bookable-resource"]));
        Assert.Throws<ArgumentNullException>(() => SchedulableTrait.AdmitsToPlanning(null!));
    }

    [Fact(DisplayName = "the catalogue requires a definition to save")]
    public async Task SaveRequiresADefinition()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", null!, 0, "draft").AsTask());
}
