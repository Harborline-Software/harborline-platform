using System.Text.Json;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// F-14 — pages/steps + wizard settings. Tests the wizard-page grain on the
/// keystone: a paged definition round-trips through the store AND through
/// System.Text.Json web defaults (the durable path) intact; a pageless
/// definition is unchanged (back-compat — <c>Pages</c>/<c>Wizard</c> stay null);
/// and the fail-closed page invariants REJECT a bad grain with a stable,
/// localizable code (duplicate page ids, empty pages, a page referencing a
/// missing section, an unassigned section, a double-assigned section, a blank
/// <c>VisibleWhen</c> guard).
/// </summary>
public sealed class FormPageValidationTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ── round-trip + back-compat ───────────────────────────────────────────────

    [Fact]
    public async Task Paged_definition_round_trips_through_the_store_intact()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var def = PagedForm();

        await store.RegisterAsync(def); // exercises the fail-closed page invariants
        var loaded = await store.GetAsync(Tenant, def.Id, def.Version);

        Assert.NotNull(loaded.Overlay.Pages);
        Assert.Equal(2, loaded.Overlay.Pages!.Count);
        Assert.Equal(new[] { "sec-a" }, loaded.Overlay.Pages[0].Sections);
        Assert.Equal(new[] { "sec-b" }, loaded.Overlay.Pages[1].Sections);
        Assert.Equal("{\"==\":[{\"var\":\"employment\"},\"employed\"]}", loaded.Overlay.Pages[1].VisibleWhen);
        Assert.NotNull(loaded.Overlay.Wizard);
        Assert.True(loaded.Overlay.Wizard!.Review);
        Assert.True(loaded.Overlay.Wizard.Confirmation);
        Assert.Equal("https://example.test/done", loaded.Overlay.Wizard.OnSuccess?.RedirectUrl);
    }

    [Fact]
    public void Paged_definition_round_trips_through_System_Text_Json_web_defaults()
    {
        var def = PagedForm();

        var node = JsonSerializer.SerializeToNode(def, Web);
        var rt = node.Deserialize<FormDefinition>(Web)!;

        Assert.Equal(2, rt.Overlay.Pages!.Count);
        Assert.Equal("page-2", rt.Overlay.Pages[1].Id);
        Assert.Equal("{\"==\":[{\"var\":\"employment\"},\"employed\"]}", rt.Overlay.Pages[1].VisibleWhen);
        Assert.Equal("formDone", rt.Overlay.Wizard!.OnSuccess?.HostCallback);
    }

    [Fact]
    public async Task Pageless_definition_is_unchanged_back_compat()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var flat = PagelessForm();

        Assert.Null(flat.Overlay.Pages);
        Assert.Null(flat.Overlay.Wizard);

        await store.RegisterAsync(flat);
        var loaded = await store.GetAsync(Tenant, flat.Id, flat.Version);
        Assert.Null(loaded.Overlay.Pages);
        Assert.Null(loaded.Overlay.Wizard);

        // Durable round-trip keeps both null (semantically identical to pre-F-14).
        var rt = JsonSerializer.SerializeToNode(flat, Web).Deserialize<FormDefinition>(Web)!;
        Assert.Null(rt.Overlay.Pages);
        Assert.Null(rt.Overlay.Wizard);
    }

    // ── fail-closed page invariants ────────────────────────────────────────────

    [Fact]
    public void Duplicate_page_id_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                Page("dup", "sec-a"),
                Page("dup", "sec-b")),
            FormDefinitionCodes.PagesDuplicatePageId);

    [Fact]
    public void Empty_page_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                Page("page-1", "sec-a", "sec-b"),
                new FormPage("page-2", InternationalizedText.FromInvariant("Empty"), Array.Empty<string>())),
            FormDefinitionCodes.PagesEmptyPage);

    [Fact]
    public void Page_referencing_a_missing_section_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                Page("page-1", "sec-a", "sec-b"),
                Page("page-2", "sec-ghost")),
            FormDefinitionCodes.PagesUnknownSection);

    [Fact]
    public void Unassigned_section_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(Page("page-1", "sec-a")), // sec-b is on no page
            FormDefinitionCodes.PagesUnassignedSection);

    [Fact]
    public void Double_assigned_section_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                Page("page-1", "sec-a", "sec-b"),
                Page("page-2", "sec-b")),
            FormDefinitionCodes.PagesDuplicateSectionAssignment);

    [Fact]
    public void Blank_visibleWhen_guard_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                Page("page-1", "sec-a"),
                new FormPage(
                    "page-2",
                    InternationalizedText.FromInvariant("Blank guard"),
                    new[] { "sec-b" },
                    VisibleWhen: "   ")),
            FormDefinitionCodes.PagesEmptyVisibleWhen);

    [Fact]
    public void Empty_page_id_is_rejected_with_a_stable_code()
        => AssertRejected(
            Pages(
                new FormPage(" ", InternationalizedText.FromInvariant("Anon"), new[] { "sec-a", "sec-b" })),
            FormDefinitionCodes.PagesEmptyPageId);

    private static void AssertRejected(FormDefinition def, string expectedCode)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var ex = Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(def))
            .GetAwaiter()
            .GetResult();
        Assert.Equal(expectedCode, ex.Code);
    }

    // ── fixtures ───────────────────────────────────────────────────────────────

    private static FormPage Page(string id, params string[] sections)
        => new(id, InternationalizedText.FromInvariant(id), sections);

    private static FormDefinition Pages(params FormPage[] pages)
        => NewForm(pages: pages, wizard: null);

    private static FormDefinition PagedForm()
        => NewForm(
            pages: new[]
            {
                Page("page-1", "sec-a"),
                new FormPage(
                    "page-2",
                    InternationalizedText.FromInvariant("Work"),
                    new[] { "sec-b" },
                    VisibleWhen: "{\"==\":[{\"var\":\"employment\"},\"employed\"]}"),
            },
            wizard: new WizardSettings(
                Review: true,
                Confirmation: true,
                ConfirmationMessage: InternationalizedText.FromInvariant("Thanks!"),
                OnSuccess: new OnSuccessConfig("https://example.test/done", "formDone")));

    private static FormDefinition PagelessForm() => NewForm(pages: null, wizard: null);

    private static FormDefinition NewForm(IReadOnlyList<FormPage>? pages, WizardSettings? wizard)
    {
        var fields = new[] { "name", "employment", "employer" };
        var sections = new[]
        {
            Section("sec-a", new[] { "name", "employment" }),
            Section("sec-b", new[] { "employer" }),
        };
        return new FormDefinition(
            Id: new FormDefinitionId("wizard-test"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:test-wizard"),
            Overlay: new HarborlineOverlay(
                Fields: fields.ToDictionary(f => f, f => new FieldOverlay(InternationalizedText.FromInvariant(f))),
                Sections: sections,
                Rules: Array.Empty<RuleDefinition>(),
                Pages: pages,
                Wizard: wizard),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
    }

    private static FormSection Section(string id, IReadOnlyList<string> fields)
        => new(
            id,
            InternationalizedText.FromInvariant(id),
            Fields: fields,
            Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") }));

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
