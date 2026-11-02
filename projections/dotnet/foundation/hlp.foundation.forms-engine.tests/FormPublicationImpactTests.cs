using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>Independent literal ADR 0099 decision 7 oracles; these are not execution evidence.</summary>
public sealed class FormPublicationImpactTests
{
    [Fact]
    public void Existing_five_member_construction_and_deconstruction_do_not_invent_provenance()
    {
        var assessment = new FormPublicationAssessment(null, FormPublicationCompatibility.ProposalEligible, [], null, null);
        var (basis, compatibility, surfaces, proposal, reason) = assessment;
        Assert.Null(basis);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, compatibility);
        Assert.Empty(surfaces);
        Assert.Null(proposal);
        Assert.Null(reason);
        Assert.True(assessment.IsComplete);
        Assert.Null(assessment.Inputs);
        Assert.Null(assessment.FreshnessLimit);
    }

    [Fact]
    public async Task Positive_capture_fidelity_probe_retains_the_full_unpersisted_Draft()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var candidate = Rich(fixture.Candidate) with
        {
            Lineage = new(new("form.parent"), new(1, 4, 0)),
            Owner = new("user", "author-17"),
            CreatedAt = DateTimeOffset.Parse("2026-10-18T09:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-10-18T10:00:00Z"),
        };
        var store = new InMemoryFormDefinitionStore();
        var basis = fixture.Basis with { NoPredecessor = true, Predecessor = null, PredecessorEnvironment = null };
        var result = await new FormPublicationImpactAssessment().AssessAsync(candidate, store,
            fixture.Context with { Basis = basis, Surfaces = new ImpactFixture.Source(_ =>
                ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-a", []))) });

        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.Empty(result.Surfaces);
        Assert.Null(result.Proposal);
        var inputs = Assert.IsType<FormPublicationAssessedInputs>(result.Inputs);
        AssertRich(inputs.CandidateFormJson);
        var body = JsonNode.Parse(inputs.CandidateFormJson)!;
        Assert.Equal("form.customer", body["Id"]!.GetValue<string>());
        Assert.Equal("2.2.0", body["Version"]!.GetValue<string>());
        Assert.Equal("tenant-a", body["Tenant"]!.GetValue<string>());
        Assert.Equal(0, body["Status"]!.GetValue<int>());
        Assert.Equal("user", body["Owner"]!["Scheme"]!.GetValue<string>());
        Assert.Equal("author-17", body["Owner"]!["Value"]!.GetValue<string>());
        Assert.Equal("form.parent", body["Lineage"]!["ParentDefinitionId"]!.GetValue<string>());
        Assert.Equal("1.4.0", body["Lineage"]!["ParentVersion"]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T09:00:00Z"), body["CreatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T10:00:00Z"), body["UpdatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Null(inputs.PredecessorFormJson);
        Assert.Null(inputs.PredecessorSchema);
        Assert.Equal("""{"type":"object","properties":{"name":{"type":"string"}}}""", inputs.CandidateSchema.JsonSchemaText);
        AssertEnvironment(result.Basis!.CandidateEnvironment.Declaration!);
        Assert.Equal("Historical inputs observed before publication; the committed Form transition and current Layout membership are not guaranteed. Reassess before acting.", result.FreshnessLimit);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => store.GetAsync(new("tenant-a"), new("form.customer"), new(2, 2, 0)).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comparison_uses_captured_bytes_when_one_original_graph_changes_during_its_await(bool mutatePredecessor)
    {
        var fixture = await ImpactFixture.CreateAsync(configure: Rich);
        var candidate = Rich(fixture.Candidate);
        var previous = await fixture.Store.GetAsync(new("tenant-a"), new("form.customer"), new(2, 1, 0));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask Pause(CancellationToken token)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        var schemas = new SchemaOverride(ImpactFixture.Schema) { BeforeRead = mutatePredecessor ? Pause : null };
        var store = new ReadGateStore(fixture.Store, mutatePredecessor ? null : Pause);
        var blocks = new[] { "form-a" };
        var rows = new[] { new FormPublishedSurface("tenant-a", "surface.customer", "surface-opaque-a", "1.0.0", "digest-a", blocks) };
        var source = new ImpactFixture.Source(_ => ValueTask.FromResult<FormPublishedSurfaceRead>(
            new FormPublishedSurfaceRead.Complete("tenant-a", rows)), observed =>
        {
            AssertEnvironment(observed.CandidateEnvironment.Declaration!);
            AssertEnvironment(observed.PredecessorEnvironment!.Declaration!);
            AssertFrozen(observed.CandidateEnvironment.Declaration!);
            AssertFrozen(observed.PredecessorEnvironment.Declaration!);
        });
        var pending = new FormPublicationImpactAssessment().AssessAsync(candidate, store,
            fixture.Context with { Schemas = schemas, Surfaces = source }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Mutate(mutatePredecessor ? previous : candidate);
        var originalEnvironment = (mutatePredecessor ? fixture.Basis.PredecessorEnvironment! : fixture.Basis.CandidateEnvironment).Declaration!;
        MutateEnvironment(originalEnvironment);
        release.SetResult();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        AssertRich(result.Inputs!.CandidateFormJson);
        AssertRich(result.Inputs.PredecessorFormJson!);
        Mutate(mutatePredecessor ? candidate : previous);
        MutateEnvironment((mutatePredecessor ? fixture.Basis.CandidateEnvironment : fixture.Basis.PredecessorEnvironment!).Declaration!);
        blocks[0] = "changed-after-return";
        rows[0] = rows[0] with { VersionId = "changed-after-return" };
        AssertRich(result.Inputs.CandidateFormJson);
        AssertRich(result.Inputs.PredecessorFormJson!);
        AssertEnvironment(result.Basis!.CandidateEnvironment.Declaration!);
        AssertEnvironment(result.Basis.PredecessorEnvironment!.Declaration!);
        AssertFrozen(result.Basis.CandidateEnvironment.Declaration!);
        Assert.Equal("surface-opaque-a", Assert.Single(result.Surfaces).VersionId);
        Assert.Equal(new[] { "form-a" }, result.Surfaces[0].BlockIds);
        Assert.Throws<NotSupportedException>(() => ((IList<FormPublishedSurface>)result.Surfaces)[0] = rows[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Surfaces[0].BlockIds)[0] = "changed");
        Assert.Same(result.Surfaces, result.Proposal!.Surfaces);
    }

    [Fact]
    public async Task Later_lifecycle_changes_do_not_replace_the_observed_predecessor_body()
    {
        var clock = new TestClock { Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z") };
        var fixture = await ImpactFixture.CreateAsync(clock: clock);
        var result = await fixture.AssessAsync();
        clock.Now = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var deprecated = await fixture.Store.DeprecateAsync(new("tenant-a"), new("form.customer"), new(2, 1, 0));
        Assert.Equal(FormDefinitionStatus.Deprecated, deprecated.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-19T12:00:00Z"), deprecated.UpdatedAt);
        Assert.True(result.IsComplete);
        var body = JsonNode.Parse(result.Inputs!.PredecessorFormJson!)!;
        Assert.Equal("2.1.0", body["Version"]!.GetValue<string>());
        Assert.Equal(1, body["Status"]!.GetValue<int>());
        Assert.Equal(DateTimeOffset.UnixEpoch, body["CreatedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T12:00:00Z"), body["UpdatedAt"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task Separate_actual_schema_responses_are_retained_even_when_their_ids_match()
    {
        var fixture = await ImpactFixture.CreateAsync();
        fixture = fixture with { Store = new InMemoryFormDefinitionStore(), Candidate = fixture.Candidate with
            { SchemaRef = new("fixture-shared-schema-id") } };
        await fixture.Store.RegisterAndPublishAsync(fixture.Candidate with { Version = new(2, 1, 0) });
        const string candidateText = " { \"type\":\"object\", \"properties\":{\"name\":{\"type\":\"number\"}} } ";
        const string predecessorText = "\n{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}\n";
        var schemas = new SchemaOverride("{}") { Answers = new Queue<string>([candidateText, predecessorText]) };
        var result = await new FormPublicationImpactAssessment().AssessAsync(fixture.Candidate, fixture.Store,
            fixture.Context with { Schemas = schemas });
        schemas.Text = "{\"type\":\"object\",\"properties\":{}}";
        Assert.Equal("{\"type\":\"object\",\"properties\":{}}", (await schemas.GetAsync(new("fixture-shared-schema-id")))!.JsonSchemaText);
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
        Assert.Equal(candidateText, result.Inputs!.CandidateSchema.JsonSchemaText);
        Assert.Equal(predecessorText, result.Inputs.PredecessorSchema!.JsonSchemaText);
        Assert.Equal("fixture-shared-schema-id", result.Inputs.CandidateSchema.Id.Value);
        Assert.Equal("fixture-shared-schema-id", result.Inputs.PredecessorSchema.Id.Value);
    }

    [Fact]
    public async Task An_unserializable_input_is_unavailable_without_partial_evidence()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var items = new List<FormItem>();
        items.Add(FormItem.OfGroup("cycle", items));
        var candidate = fixture.Candidate with { Overlay = fixture.Candidate.Overlay with
            { Sections = [new("main", InternationalizedText.FromInvariant("Main"), [], new([], []), Items: items)] } };
        var result = await fixture.AssessAsync(candidate);
        Assert.Equal("assessment_input_unavailable", result.UnavailableReason);
        Assert.Null(result.Inputs);
        Assert.Null(result.FreshnessLimit);
        Assert.Null(result.Compatibility);
        Assert.Null(result.Proposal);
        Assert.Empty(result.Surfaces);
    }

    [Fact]
    public async Task Optional_addition_proposes_exact_pins_without_applying_anything()
    {
        var fixture = await ImpactFixture.CreateAsync(
            """{"type":"object","properties":{"name":{"type":"string"},"nickname":{"type":"string","minLength":2,"enum":["ab","cd"]}}}""");
        var result = await fixture.AssessAsync();
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.Equal("f-old-x9", result.Proposal!.Predecessor.VersionId);
        Assert.Equal("f-new-y8", result.Proposal.Candidate.VersionId);
        Assert.Equal("surface-opaque-a", Assert.Single(result.Proposal.Surfaces).VersionId);
        Assert.Equal("2.1.0", (await fixture.Store.GetCurrentPublishedAsync(fixture.Candidate.Tenant,
            fixture.Candidate.Id))!.Version.ToString()); // The assessor cannot publish even the Form.
    }

    [Fact]
    public async Task Presentation_metadata_alone_is_separately_proposal_eligible()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var next = fixture.Candidate with { Overlay = fixture.Candidate.Overlay with
            { Title = InternationalizedText.FromInvariant("Customer details") } };
        var result = await fixture.AssessAsync(next);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.NotNull(result.Proposal);
    }

    [Fact]
    public async Task A_simple_unconstrained_optional_default_is_not_confused_with_an_unusable_default()
    {
        var fixture = await ImpactFixture.CreateAsync(
            """{"type":"object","properties":{"name":{"type":"string"},"nickname":{"type":"string","default":"Anonymous"}}}""");
        var result = await fixture.AssessAsync();
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.NotNull(result.Proposal);
    }

    [Fact]
    public async Task An_optional_field_can_join_an_existing_section_with_its_own_metadata()
    {
        var fixture = await ImpactFixture.CreateAsync(
            """{"type":"object","properties":{"name":{"type":"string"},"nickname":{"type":"string"}}}""",
            withPagesAndSections: true);
        var next = fixture.Candidate with
        {
            Overlay = fixture.Candidate.Overlay with
            {
                Fields = new Dictionary<string, FieldOverlay>(fixture.Candidate.Overlay.Fields)
                { ["nickname"] = new(InternationalizedText.FromInvariant("Nickname")) },
                Sections = [fixture.Candidate.Overlay.Sections[0] with { Fields = ["name", "nickname"] }],
            },
            Authoring = new(new Dictionary<string, FormFieldAuthoringMetadata>
            { ["nickname"] = new("text", false) }),
        };
        var result = await fixture.AssessAsync(next);
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.Equal("f-old-x9", result.Proposal!.Predecessor.VersionId);
        Assert.Equal("f-new-y8", result.Proposal.Candidate.VersionId);
        Assert.Equal("surface-opaque-a", Assert.Single(result.Proposal.Surfaces).VersionId);

        var removedOldMember = next with { Overlay = next.Overlay with
            { Sections = [next.Overlay.Sections[0] with { Fields = ["nickname"] }] } };
        var negative = await fixture.AssessAsync(removedOldMember);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, negative.Compatibility);
        Assert.Null(negative.Proposal);
    }

    [Theory]
    [InlineData("read-role")]
    [InlineData("write-role")]
    [InlineData("pii")]
    [InlineData("config")]
    [InlineData("authoring-required")]
    [InlineData("authoring-validation")]
    public async Task New_metadata_for_an_existing_schema_field_is_not_an_optional_field_addition(string change)
    {
        // The predecessor legitimately has no per-field metadata for its existing schema property "name".
        var fixture = await ImpactFixture.CreateAsync(withoutExistingFieldMetadata: true);
        var next = fixture.Candidate;
        if (change.StartsWith("authoring-", StringComparison.Ordinal))
            next = next with { Authoring = new(new Dictionary<string, FormFieldAuthoringMetadata>
            {
                ["name"] = change == "authoring-required" ? new("text", true)
                    : new("text", false, [new("minLength", "2")]),
            }) };
        else
        {
            var overlay = new FieldOverlay(InternationalizedText.FromInvariant("Name"));
            overlay = change switch
            {
                "read-role" => overlay with { FieldReadRoles = [RoleReference.Domain("reviewer")] },
                "write-role" => overlay with { FieldWriteRoles = [RoleReference.Domain("author")] },
                "pii" => overlay with { PiiSensitivity = PiiSensitivity.Sensitive },
                _ => overlay with { Config = new(CurrencyCode: "USD") },
            };
            next = next with { Overlay = next.Overlay with { Fields = new Dictionary<string, FieldOverlay> { ["name"] = overlay } } };
        }
        var result = await fixture.AssessAsync(next);
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
        var unchanged = await fixture.AssessAsync();
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, unchanged.Compatibility);
        Assert.NotNull(unchanged.Proposal);
    }

    [Fact]
    public async Task A_malformed_overlay_map_is_not_normalized_as_absent_authoring_metadata()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var result = await fixture.AssessAsync(fixture.Candidate with
            { Overlay = fixture.Candidate.Overlay with { Fields = null! } });
        Assert.False(result.IsComplete);
        Assert.Null(result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Theory]
    [InlineData("section-title")]
    [InlineData("section-layout")]
    [InlineData("section-placement")]
    [InlineData("page-title")]
    public async Task Declared_section_and_page_presentation_metadata_is_eligible(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true);
        var overlay = fixture.Candidate.Overlay;
        var section = overlay.Sections[0];
        var page = overlay.Pages![0];
        if (change == "section-title") section = section with { Title = InternationalizedText.FromInvariant("Customer details") };
        if (change == "section-layout") section = section with { Layout = new SectionLayout(SectionLayoutKind.Grid) };
        if (change == "section-placement") section = section with { FieldPlacement = new Dictionary<string, FieldPlacement> { ["name"] = new() } };
        if (change == "page-title") page = page with { Title = InternationalizedText.FromInvariant("About the customer") };
        var result = await fixture.AssessAsync(fixture.Candidate with { Overlay = overlay with { Sections = [section], Pages = [page] } });
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.NotNull(result.Proposal);
    }

    [Theory]
    [InlineData("page-guard")]
    [InlineData("section-access")]
    public async Task Presentation_comparison_keeps_guards_and_access_semantic(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true);
        var overlay = fixture.Candidate.Overlay;
        if (change == "page-guard") overlay = overlay with { Pages = [overlay.Pages![0] with { VisibleWhen = "false" }] };
        else overlay = overlay with { Sections = [overlay.Sections[0] with { Access =
            new SectionAccess([RoleReference.Domain("reviewer")], [RoleReference.Domain("author")]) }] };
        var result = await fixture.AssessAsync(fixture.Candidate with { Overlay = overlay });
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("redirect")]
    public async Task Wizard_post_success_behavior_is_not_presentation_metadata(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true);
        var wizard = fixture.Candidate.Overlay.Wizard!;
        var behavior = change == "callback" ? new OnSuccessConfig(HostCallback: "notify.customer-b")
            : new OnSuccessConfig(RedirectUrl: "/customers/other", HostCallback: "notify.customer-a");
        var result = await fixture.AssessAsync(fixture.Candidate with { Overlay = fixture.Candidate.Overlay with
            { Wizard = wizard with { OnSuccess = behavior } } });
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("chrome")]
    public async Task Wizard_confirmation_text_and_chrome_remain_presentation_metadata(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true);
        var wizard = fixture.Candidate.Overlay.Wizard!;
        wizard = change == "message" ? wizard with { ConfirmationMessage = InternationalizedText.FromInvariant("Customer saved") }
            : wizard with { Review = true, Confirmation = false };
        var result = await fixture.AssessAsync(fixture.Candidate with { Overlay = fixture.Candidate.Overlay with { Wizard = wizard } });
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.NotNull(result.Proposal);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("layout")]
    [InlineData("placement")]
    [InlineData("table")]
    public async Task Nested_item_presentation_metadata_is_eligible_without_changing_its_structure(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true, withNestedItems: true);
        var section = fixture.Candidate.Overlay.Sections[0];
        var group = section.Items![0];
        if (change == "title") group = group with { Title = InternationalizedText.FromInvariant("Customer aliases") };
        if (change == "layout") group = group with { Layout = new SectionLayout(SectionLayoutKind.Grid) };
        if (change == "placement") group = group with { Placement = new Dictionary<string, FieldPlacement> { ["aliases"] = new(Width: "full") } };
        if (change == "table") group = group with { Items = [group.Items![0] with
            { Table = new(new Dictionary<string, CollectionColumn> { ["name"] = new(Width: "1/2") }) }] };
        var next = fixture.Candidate with { Overlay = fixture.Candidate.Overlay with { Sections = [section with { Items = [group] }] } };
        var result = await fixture.AssessAsync(next);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Compatibility);
        Assert.NotNull(result.Proposal);
    }

    [Theory]
    [InlineData("cardinality")]
    [InlineData("identity")]
    public async Task Nested_item_semantics_are_retained_by_the_presentation_projection(string change)
    {
        var fixture = await ImpactFixture.CreateAsync(withPagesAndSections: true, withNestedItems: true);
        var section = fixture.Candidate.Overlay.Sections[0];
        var group = section.Items![0];
        var collection = group.Items![0];
        collection = change == "cardinality" ? collection with { Cardinality = new(1, 3) } : collection with { Key = "other_aliases" };
        group = group with { Items = [collection] };
        var result = await fixture.AssessAsync(fixture.Candidate with { Overlay = fixture.Candidate.Overlay with
            { Sections = [section with { Items = [group] }] } });
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("removed")]
    [InlineData("type")]
    [InlineData("rule")]
    [InlineData("environment")]
    public async Task Each_second_row_change_requires_author_resolution(string change)
    {
        var schema = change switch
        {
            "identity" => """{"type":"object","properties":{"legal_name":{"type":"string"}}}""",
            "removed" => """{"type":"object","properties":{}}""",
            "type" => """{"type":"object","properties":{"name":{"type":"number"}}}""",
            _ => ImpactFixture.Schema,
        };
        var fixture = await ImpactFixture.CreateAsync(schema);
        var next = fixture.Candidate;
        var basis = fixture.Basis;
        if (change == "rule") next = next with { Overlay = next.Overlay with { Rules =
            [new("rule.changed", RuleTier.JsonLogic, RuleScope.Schema, "", "true", RuleActionKind.Validate)] } };
        if (change == "environment") basis = basis with { CandidateEnvironment =
            new(ImpactFixture.Environment() with { TimeZone = "Europe/London" }, "env-source/revision-b") };
        var result = await fixture.AssessAsync(next, basis);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task New_required_field_without_default_blocks_even_with_another_author_change()
    {
        var fixture = await ImpactFixture.CreateAsync(
            """{"type":"object","properties":{"name":{"type":"number"},"tax_id":{"type":"string"}},"required":["tax_id"]}""");
        var result = await fixture.AssessAsync();
        Assert.Equal(FormPublicationCompatibility.Block, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task A_required_field_with_a_usable_default_needs_author_resolution_but_does_not_block()
    {
        var fixture = await ImpactFixture.CreateAsync(
            """{"type":"object","properties":{"name":{"type":"string"},"nickname":{"type":"string","default":"Anonymous"}},"required":["nickname"]}""");
        var result = await fixture.AssessAsync();
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task Adding_the_first_record_type_target_is_an_author_change_not_a_captured_target_change()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var result = await fixture.AssessAsync(fixture.Candidate with { RecordTypeId = "asset.customer" });
        Assert.True(result.IsComplete);
        Assert.Equal(FormPublicationCompatibility.AuthorResolution, result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task Captured_record_type_change_uses_the_permitted_block_branch()
    {
        var fixture = await ImpactFixture.CreateAsync(recordType: "asset.customer");
        var result = await fixture.AssessAsync(fixture.Candidate with { RecordTypeId = "asset.vendor" });
        Assert.Equal(FormPublicationCompatibility.Block, result.Compatibility);
        Assert.Null(result.Proposal); // No fabricated migration decision.
    }

    [Fact]
    public async Task A_known_but_unadmitted_environment_is_unavailable_even_when_both_declarations_match()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var declaration = ImpactFixture.Environment() with { Grammar = "unsupported/v1" };
        var basis = fixture.Basis with
        {
            CandidateEnvironment = new(declaration, "environment/known-invalid-next"),
            PredecessorEnvironment = new(declaration, "environment/known-invalid-old"),
        };
        var result = await fixture.AssessAsync(basis: basis);
        Assert.False(result.IsComplete);
        Assert.Null(result.Compatibility);
        Assert.Null(result.Proposal);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, (await fixture.AssessAsync()).Compatibility);
    }

    [Theory]
    [InlineData("unknown-environment")]
    [InlineData("missing-provenance")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-id")]
    [InlineData("wrong-version")]
    [InlineData("empty-opaque")]
    [InlineData("same-opaque")]
    [InlineData("false-first")]
    public async Task Missing_or_mismatched_basis_never_becomes_eligible(string fault)
    {
        var fixture = await ImpactFixture.CreateAsync();
        var basis = fixture.Basis;
        basis = fault switch
        {
            "unknown-environment" => basis with { PredecessorEnvironment = new(null, null) },
            "missing-provenance" => basis with { CandidateEnvironment = new(ImpactFixture.Environment(), "") },
            "wrong-tenant" => basis with { Candidate = basis.Candidate with { Tenant = new("tenant-b") } },
            "wrong-id" => basis with { Candidate = basis.Candidate with { DefinitionId = new("form.other") } },
            "wrong-version" => basis with { Predecessor = basis.Predecessor! with { Version = new(9, 0, 0) } },
            "empty-opaque" => basis with { Candidate = basis.Candidate with { VersionId = "" } },
            "same-opaque" => basis with { Candidate = basis.Candidate with { VersionId = "f-old-x9" } },
            _ => basis with { NoPredecessor = true, Predecessor = null },
        };
        var result = await fixture.AssessAsync(basis: basis);
        Assert.False(result.IsComplete);
        Assert.Null(result.Compatibility);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task An_explicit_first_publication_is_not_a_failed_predecessor_lookup()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var emptyStore = new InMemoryFormDefinitionStore();
        var basis = fixture.Basis with { NoPredecessor = true, Predecessor = null, PredecessorEnvironment = null };
        var source = new ImpactFixture.Source(_ => ValueTask.FromResult<FormPublishedSurfaceRead>(
            new FormPublishedSurfaceRead.Complete("tenant-a", [])));
        var context = new FormPublicationAssessmentContext(basis, fixture.Schemas, source);
        var first = await new FormPublicationImpactAssessment().AssessAsync(fixture.Candidate, emptyStore, context);
        Assert.True(first.IsComplete);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, first.Compatibility);
        Assert.Empty(first.Surfaces);
        Assert.Null(first.Proposal);
        var failedLookup = await new FormPublicationImpactAssessment().AssessAsync(fixture.Candidate, emptyStore,
            context with { Basis = fixture.Basis });
        Assert.False(failedLookup.IsComplete);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"name":{"$ref":"#/$defs/name"}}}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string"}},"allOf":[]}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string"}},"required":"name"}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","minLength":"bad"}}}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","enum":[]}}}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","title":false}}}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","default":42}}}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string"}},"required":null}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string"}},"additionalProperties":null}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","default":"x","enum":["y"]}},"required":["name"]}""")]
    [InlineData("""{"type":"object","properties":{"name":{"type":"string","default":"x","const":"y"}},"required":["name"]}""")]
    public async Task Unsupported_schema_never_implies_compatibility(string schema)
    {
        // A literal lookup port supplies unsupported schema text; no parser failure is equated with empty fields.
        var fixture = await ImpactFixture.CreateAsync();
        var source = new SchemaOverride(schema);
        var result = await new FormPublicationImpactAssessment().AssessAsync(fixture.Candidate, fixture.Store,
            fixture.Context with { Schemas = source });
        Assert.False(result.IsComplete);
        Assert.Null(result.Proposal);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("null")]
    [InlineData("tenant-poison")]
    [InlineData("duplicate")]
    [InlineData("exception")]
    [InlineData("noncaller-cancellation")]
    public async Task Failed_partial_or_malformed_census_is_not_complete_empty(string fault)
    {
        var fixture = await ImpactFixture.CreateAsync();
        var row = new FormPublishedSurface("tenant-a", "surface.customer", "surface-opaque-a", "1.0.0", "digest-a", ["form-a"]);
        var source = new ImpactFixture.Source(_ => fault switch
        {
            "null" => ValueTask.FromResult<FormPublishedSurfaceRead>(null!),
            "tenant-poison" => ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-b", [])),
            "duplicate" => ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-a", [row, row])),
            "exception" => throw new IOException("private provider text"),
            "noncaller-cancellation" => throw new OperationCanceledException(),
            _ => ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Unavailable("partial")),
        });
        var result = await new FormPublicationImpactAssessment().AssessAsync(fixture.Candidate, fixture.Store,
            fixture.Context with { Surfaces = source });
        Assert.False(result.IsComplete);
        Assert.Null(result.Compatibility);
        Assert.Null(result.Proposal);
        Assert.DoesNotContain("private provider text", result.UnavailableReason!);
    }

    [Fact]
    public async Task Genuine_caller_cancellation_propagates_from_the_census()
    {
        var fixture = await ImpactFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var source = new ImpactFixture.Source(token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return default; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FormPublicationImpactAssessment()
            .AssessAsync(fixture.Candidate, fixture.Store, fixture.Context with { Surfaces = source }, cancellation.Token).AsTask());
    }

    private sealed class SchemaOverride(string text) : ISchemaRegistry
    {
        public string Text { get; set; } = text;
        public Queue<string>? Answers { get; init; }
        public Func<CancellationToken, ValueTask>? BeforeRead { get; init; }
        public async ValueTask<Schema?> GetAsync(Harborline.Kernel.SchemaValidation.SchemaId id, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead(cancellationToken);
            return new(id, Answers is { Count: > 0 } ? Answers.Dequeue() : Text, [], [], "fixture-content-address");
        }
        public ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<Harborline.Kernel.SchemaValidation.SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SchemaValidationResult> ValidateAsync(Harborline.Kernel.SchemaValidation.SchemaId id, ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static FormDefinition Rich(FormDefinition definition) => definition with
    {
        Overlay = definition.Overlay with
        {
            Fields = new Dictionary<string, FieldOverlay> { ["name"] = new(new("en", new Dictionary<string, string>
                { ["en"] = "Name", ["fr"] = "Nom" })) },
            Rules = new List<RuleDefinition> { new("name.valid", RuleTier.JsonLogic, RuleScope.Schema, "", "true", RuleActionKind.Validate) },
            Sections = new List<FormSection> { new("main", InternationalizedText.FromInvariant("Main"), ["name"],
                new([RoleReference.Domain("author")], [RoleReference.Domain("author")]),
                Items: new List<FormItem> { FormItem.OfGroup("customer", new List<FormItem>
                    { FormItem.OfCollection("aliases", new List<FormItem> { FormItem.OfField("name") }, new(0, 3)) }) }) },
            Pages = new List<FormPage> { new("details", InternationalizedText.FromInvariant("Details"), ["main"], VisibleWhen: "true") },
        },
        Authoring = new(new Dictionary<string, FormFieldAuthoringMetadata>
            { ["name"] = new("radio", false, [new("minLength", "2")], ["Alice", "Bob"]) }),
    };

    private static void Mutate(FormDefinition definition)
    {
        ((Dictionary<string, string>)definition.Overlay.Fields["name"].Label.Values)["fr"] = "Changed";
        ((Dictionary<string, FieldOverlay>)definition.Overlay.Fields)["name"] = definition.Overlay.Fields["name"] with { PiiSensitivity = PiiSensitivity.Sensitive };
        ((List<RuleDefinition>)definition.Overlay.Rules)[0] = definition.Overlay.Rules[0] with { Expression = "false" };
        ((List<FormPage>)definition.Overlay.Pages!)[0] = definition.Overlay.Pages![0] with { VisibleWhen = "false" };
        var children = (List<FormItem>)definition.Overlay.Sections[0].Items![0].Items!;
        children[0] = children[0] with { Cardinality = new(1, 8) };
        ((string[])definition.Authoring!.Fields["name"].Options!)[0] = "Changed";
    }

    private static void AssertRich(string json)
    {
        var body = JsonNode.Parse(json)!;
        Assert.Equal("tax.roles", body["SubmitGate"]!["role"]!["vocabulary"]!.GetValue<string>());
        Assert.Equal("author", body["SubmitGate"]!["role"]!["name"]!.GetValue<string>());
        Assert.Equal("Nom", body["Overlay"]!["Fields"]!["name"]!["Label"]!["Values"]!["fr"]!.GetValue<string>());
        Assert.Equal(0, body["Overlay"]!["Fields"]!["name"]!["PiiSensitivity"]!.GetValue<int>());
        Assert.Equal("true", body["Overlay"]!["Rules"]![0]!["Expression"]!.GetValue<string>());
        Assert.Equal("true", body["Overlay"]!["Pages"]![0]!["VisibleWhen"]!.GetValue<string>());
        Assert.Equal("aliases", body["Overlay"]!["Sections"]![0]!["Items"]![0]!["Items"]![0]!["Key"]!.GetValue<string>());
        Assert.Equal(0, body["Overlay"]!["Sections"]![0]!["Items"]![0]!["Items"]![0]!["Cardinality"]!["Min"]!.GetValue<int>());
        Assert.Equal(3, body["Overlay"]!["Sections"]![0]!["Items"]![0]!["Items"]![0]!["Cardinality"]!["Max"]!.GetValue<int>());
        Assert.Equal("name", body["Overlay"]!["Sections"]![0]!["Items"]![0]!["Items"]![0]!["Items"]![0]!["Key"]!.GetValue<string>());
        Assert.Equal("radio", body["Authoring"]!["Fields"]!["name"]!["Type"]!.GetValue<string>());
        Assert.False(body["Authoring"]!["Fields"]!["name"]!["Required"]!.GetValue<bool>());
        Assert.Equal("minLength", body["Authoring"]!["Fields"]!["name"]!["Validations"]![0]!["Code"]!.GetValue<string>());
        Assert.Equal("2", body["Authoring"]!["Fields"]!["name"]!["Validations"]![0]!["Param"]!.GetValue<string>());
        Assert.Equal(new[] { "Alice", "Bob" }, body["Authoring"]!["Fields"]!["name"]!["Options"]!.AsArray().Select(value => value!.GetValue<string>()));
    }

    private static void AssertEnvironment(BorrowerEnvironmentDeclaration declaration)
    {
        Assert.Equal("forms-ck-13", declaration.Borrower);
        Assert.Equal("harborline-jsonlogic/v1", declaration.Grammar);
        Assert.Equal("form-submission-candidate", Assert.Single(declaration.Variables).Value);
        Assert.Equal("candidate", Assert.Single(declaration.Variables).Key);
        Assert.Equal(new[] { "==" }, declaration.Operations);
        Assert.Equal(new[] { "field-read" }, declaration.Effects);
        Assert.Equal("missing-field-reads-null", declaration.MissingValues);
        Assert.Equal("evaluated-at", declaration.TimeSource);
        Assert.Equal("utc", declaration.TimeZone);
        Assert.Equal("deterministic-over-pinned-clock-and-candidate", declaration.Replay);
        Assert.Equal(6, declaration.Phases.Count);
        Assert.False(declaration.Phases[EvaluationPhase.AuthoringValidation]);
        Assert.False(declaration.Phases[EvaluationPhase.PublishValidation]);
        Assert.True(declaration.Phases[EvaluationPhase.Render]);
        Assert.True(declaration.Phases[EvaluationPhase.Submission]);
        Assert.False(declaration.Phases[EvaluationPhase.Run]);
        Assert.False(declaration.Phases[EvaluationPhase.SignOff]);
    }

    private static void MutateEnvironment(BorrowerEnvironmentDeclaration declaration)
    {
        ((Dictionary<string, string>)declaration.Variables)["candidate"] = "mutated";
        ((Dictionary<EvaluationPhase, bool>)declaration.Phases)[EvaluationPhase.Render] = false;
        ((IList<string>)declaration.Operations)[0] = "!=";
        ((IList<string>)declaration.Effects)[0] = "mutated";
    }

    private static void AssertFrozen(BorrowerEnvironmentDeclaration declaration)
    {
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)declaration.Variables)["candidate"] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<EvaluationPhase, bool>)declaration.Phases)[EvaluationPhase.Render] = false);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)declaration.Operations)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)declaration.Effects)[0] = "changed");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReadGateStore(IFormDefinitionStore inner, Func<CancellationToken, ValueTask>? beforeCurrent) : IFormDefinitionStore
    {
        public async ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default)
        {
            if (beforeCurrent is not null) await beforeCurrent(ct);
            return await inner.GetCurrentPublishedAsync(tenant, id, ct);
        }
        public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => inner.GetAsync(tenant, id, version, ct);
        public ValueTask<FormDefinition> RegisterAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> CreateAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> DeprecateAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> WithdrawAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RestorePackProjectionAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<FormDefinition> ListByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<FormDefinition> ListCurrentPublishedByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

internal sealed record ImpactFixture(InMemoryFormDefinitionStore Store, InMemorySchemaRegistry Schemas,
    FormDefinition Candidate, FormPublicationBasis Basis)
{
    internal const string Schema = """{"type":"object","properties":{"name":{"type":"string"}}}""";
    internal FormPublicationAssessmentContext Context => new(Basis, Schemas, new Source(_ => ValueTask.FromResult<FormPublishedSurfaceRead>(
        new FormPublishedSurfaceRead.Complete("tenant-a", [new("tenant-a", "surface.customer", "surface-opaque-a", "1.0.0", "digest-a", ["form-a"])]))));

    internal ValueTask<FormPublicationAssessment> AssessAsync(FormDefinition? candidate = null, FormPublicationBasis? basis = null) =>
        new FormPublicationImpactAssessment().AssessAsync(candidate ?? Candidate, Store, Context with { Basis = basis ?? Basis });

    internal static async Task<ImpactFixture> CreateAsync(string schema = Schema, string? recordType = null,
        bool withPagesAndSections = false, bool withNestedItems = false, bool withoutExistingFieldMetadata = false,
        Func<FormDefinition, FormDefinition>? configure = null, TimeProvider? clock = null)
    {
        var schemas = new InMemorySchemaRegistry();
        var previousSchema = await schemas.RegisterAsync(Schema);
        var nextSchema = await schemas.RegisterAsync(schema);
        var store = new InMemoryFormDefinitionStore(clock ?? TimeProvider.System, recordTypePolicies: new RecordTypes());
        var old = Definition(previousSchema.Id.Value, new(2, 1, 0)) with { RecordTypeId = recordType };
        if (withoutExistingFieldMetadata) old = old with
        {
            Overlay = old.Overlay with { Fields = new Dictionary<string, FieldOverlay>() },
            Authoring = new(new Dictionary<string, FormFieldAuthoringMetadata>()),
        };
        if (withPagesAndSections) old = old with { Overlay = old.Overlay with
        {
            Sections = [new("main", InternationalizedText.FromInvariant("Main"), ["name"],
                new SectionAccess([RoleReference.Domain("author")], [RoleReference.Domain("author")]))],
            Pages = [new("details", InternationalizedText.FromInvariant("Details"), ["main"], VisibleWhen: "true")],
            Wizard = new(OnSuccess: new(HostCallback: "notify.customer-a")),
        } };
        if (withNestedItems) old = old with { Overlay = old.Overlay with { Sections = [old.Overlay.Sections[0] with
        {
            Items = [FormItem.OfGroup("customer", [FormItem.OfCollection("aliases", [FormItem.OfField("name")], new(0, 3))])],
        }] } };
        if (configure is not null) old = configure(old);
        await store.RegisterAndPublishAsync(old);
        var next = old with { SchemaRef = new Harborline.Foundation.Assets.Common.SchemaId(nextSchema.Id.Value), Version = new(2, 2, 0) };
        var basis = new FormPublicationBasis(new(next.Tenant, next.Id, next.Version, "f-new-y8", "writer/candidate-y8"),
            new(old.Tenant, old.Id, old.Version, "f-old-x9", "writer/predecessor-x9"), false,
            new(Environment(), "environment/revision-a"), new(Environment(), "environment/revision-a"));
        return new(store, schemas, next, basis);
    }

    internal static FormDefinition Definition(string schema, SemanticVersion version) => new(new("form.customer"), version,
        FormDefinitionStatus.Draft, new("tenant-a"), IdentityRef.System, new Harborline.Foundation.Assets.Common.SchemaId(schema),
        new(new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("Name")) }, [], []),
        null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        SubmitGate: new(Role: RoleReference.Domain("author")));

    internal static BorrowerEnvironmentDeclaration Environment() => new("forms-ck-13", "harborline-jsonlogic/v1",
        new Dictionary<string, string> { ["candidate"] = "form-submission-candidate" }, new[] { "==" }, new[] { "field-read" },
        "missing-field-reads-null", "evaluated-at", "utc", new Dictionary<EvaluationPhase, bool>
        {
            [EvaluationPhase.AuthoringValidation] = false, [EvaluationPhase.PublishValidation] = false,
            [EvaluationPhase.Render] = true, [EvaluationPhase.Submission] = true,
            [EvaluationPhase.Run] = false, [EvaluationPhase.SignOff] = false,
        }, "deterministic-over-pinned-clock-and-candidate");

    internal sealed class Source(Func<CancellationToken, ValueTask<FormPublishedSurfaceRead>> read,
        Action<FormPublicationBasis>? inspect = null) : IFormPublishedSurfaceSource
    {
        public ValueTask<FormPublishedSurfaceRead> ReadAsync(FormPublicationBasis basis, CancellationToken cancellationToken = default)
        {
            inspect?.Invoke(basis);
            return read(cancellationToken);
        }
    }

    private sealed class RecordTypes : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RecordTypeCreationGate?>(recordTypeId is "asset.customer" or "asset.vendor" ? RecordTypeCreationGate.Role : null);
    }
}
