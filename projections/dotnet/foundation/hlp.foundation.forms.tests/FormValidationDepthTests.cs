using System.Text.Json;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// F-20 — validation depth on the keystone. Pins the NEW fail-closed admission
/// invariants (all as stable localizable codes):
/// <list type="bullet">
///   <item><description>the page CAP (F-14's <c>MaxPages</c> — the previously
///     untested 51-page rejection, deep-review F2),</description></item>
///   <item><description>an UNPARSEABLE page guard (deep-review F3 — a typo'd
///     guard would admit and silently hide its page forever),</description></item>
///   <item><description>page CHECK bindings (unknown rule id / non-Validate
///     rule),</description></item>
///   <item><description>async-check config (duplicate/empty ids, empty
///     connector/failCode, unknown fields, bad debounce, the count cap),</description></item>
/// </list>
/// plus the positive round-trip: checks + async checks survive the store and the
/// System.Text.Json web-defaults durable path intact, and a check-less overlay
/// stays byte-identical (back-compat).
/// </summary>
public sealed class FormValidationDepthTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ── F2: the page cap (previously uncovered) ────────────────────────────────

    [Fact]
    public void Fifty_one_pages_are_rejected_with_the_too_many_pages_code()
    {
        // 51 sections so every page can be non-empty AND every section assigned.
        var sectionIds = Enumerable.Range(1, FormDefinitionValidation.MaxPages + 1)
            .Select(i => $"sec-{i}").ToArray();
        var pages = sectionIds.Select(id => Page($"page-{id}", id)).ToArray();

        AssertRejected(
            NewForm(pages: pages, sectionIds: sectionIds),
            FormDefinitionCodes.PagesTooManyPages);
    }

    [Fact]
    public async Task Exactly_fifty_pages_are_admitted()
    {
        var sectionIds = Enumerable.Range(1, FormDefinitionValidation.MaxPages)
            .Select(i => $"sec-{i}").ToArray();
        var pages = sectionIds.Select(id => Page($"page-{id}", id)).ToArray();

        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(NewForm(pages: pages, sectionIds: sectionIds)); // must not throw
    }

    // ── F3: unparseable page guard is rejected at the door ────────────────────

    [Fact]
    public void Unparseable_visibleWhen_guard_is_rejected_with_a_stable_code()
        => AssertRejected(
            NewForm(pages: new[]
            {
                Page("page-1", "sec-a"),
                new FormPage(
                    "page-2",
                    InternationalizedText.FromInvariant("Broken guard"),
                    new[] { "sec-b" },
                    VisibleWhen: "not-json{{{"),
            }),
            FormDefinitionCodes.PagesInvalidVisibleWhen);

    // ── Page checks: binding invariants ────────────────────────────────────────

    [Fact]
    public void Page_check_referencing_an_unknown_rule_is_rejected()
        => AssertRejected(
            NewForm(pages: new[]
            {
                Page("page-1", "sec-a"),
                new FormPage(
                    "page-2",
                    InternationalizedText.FromInvariant("Checked"),
                    new[] { "sec-b" },
                    Checks: new[] { "ghost-rule" }),
            }),
            FormDefinitionCodes.PagesUnknownCheck);

    [Fact]
    public void Page_check_referencing_a_non_Validate_rule_is_rejected()
        => AssertRejected(
            NewForm(
                pages: new[]
                {
                    Page("page-1", "sec-a"),
                    new FormPage(
                        "page-2",
                        InternationalizedText.FromInvariant("Checked"),
                        new[] { "sec-b" },
                        Checks: new[] { "vis-rule" }),
                },
                rules: new[] { Rule("vis-rule", RuleActionKind.Visibility) }),
            FormDefinitionCodes.PagesCheckNotValidate);

    [Fact]
    public async Task Page_checks_and_async_checks_round_trip_intact()
    {
        var def = NewForm(
            pages: new[]
            {
                Page("page-1", "sec-a"),
                new FormPage(
                    "page-2",
                    InternationalizedText.FromInvariant("Checked"),
                    new[] { "sec-b" },
                    Checks: new[] { "sod-check" }),
            },
            rules: new[] { Rule("sod-check", RuleActionKind.Validate) },
            asyncChecks: new[]
            {
                new AsyncValidationCheck(
                    "dup-vendor", "vendor-duplicate-lookup", "name", "duplicate-vendor",
                    Inputs: new[] { "employment" }, DebounceMs: 250),
            });

        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await store.RegisterAsync(def);
        var loaded = await store.GetAsync(Tenant, def.Id, def.Version);

        Assert.Equal(new[] { "sod-check" }, loaded.Overlay.Pages![1].Checks);
        var check = Assert.Single(loaded.Overlay.AsyncChecks!);
        Assert.Equal("vendor-duplicate-lookup", check.Connector);
        Assert.Equal("name", check.Field);
        Assert.Equal(250, check.DebounceMs);

        // Durable JSON round-trip (System.Text.Json web defaults).
        var rt = JsonSerializer.SerializeToNode(def, Web).Deserialize<FormDefinition>(Web)!;
        Assert.Equal(new[] { "sod-check" }, rt.Overlay.Pages![1].Checks);
        Assert.Equal("duplicate-vendor", Assert.Single(rt.Overlay.AsyncChecks!).FailCode);
    }

    [Fact]
    public void Checkless_overlay_stays_null_back_compat()
    {
        var def = NewForm(pages: null);
        Assert.Null(def.Overlay.AsyncChecks);
        var rt = JsonSerializer.SerializeToNode(def, Web).Deserialize<FormDefinition>(Web)!;
        Assert.Null(rt.Overlay.AsyncChecks);
        Assert.Null(rt.Overlay.Pages);
    }

    // ── Async-check config invariants ──────────────────────────────────────────

    [Fact]
    public void Duplicate_async_check_id_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("dup", "lookup", "name", "code-a"),
                new AsyncValidationCheck("dup", "lookup", "employment", "code-b"),
            }),
            FormDefinitionCodes.ChecksBadId);

    [Fact]
    public void Empty_async_check_connector_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("c1", "  ", "name", "code-a"),
            }),
            FormDefinitionCodes.ChecksEmptyConnector);

    [Fact]
    public void Empty_async_check_fail_code_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("c1", "lookup", "name", ""),
            }),
            FormDefinitionCodes.ChecksEmptyFailCode);

    [Fact]
    public void Async_check_on_an_undeclared_field_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("c1", "lookup", "ghost-field", "code-a"),
            }),
            FormDefinitionCodes.ChecksUnknownField);

    [Fact]
    public void Async_check_with_an_undeclared_input_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("c1", "lookup", "name", "code-a", Inputs: new[] { "ghost-input" }),
            }),
            FormDefinitionCodes.ChecksUnknownField);

    [Fact]
    public void Async_check_with_a_negative_debounce_is_rejected()
        => AssertRejected(
            NewForm(pages: null, asyncChecks: new[]
            {
                new AsyncValidationCheck("c1", "lookup", "name", "code-a", DebounceMs: -1),
            }),
            FormDefinitionCodes.ChecksBadDebounce);

    [Fact]
    public void Too_many_async_checks_are_rejected()
    {
        var checks = Enumerable.Range(1, FormDefinitionValidation.MaxAsyncChecks + 1)
            .Select(i => new AsyncValidationCheck($"c{i}", "lookup", "name", "code-a"))
            .ToArray();
        AssertRejected(NewForm(pages: null, asyncChecks: checks), FormDefinitionCodes.ChecksTooMany);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void AssertRejected(FormDefinition def, string expectedCode)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var ex = Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(def))
            .GetAwaiter()
            .GetResult();
        Assert.Equal(expectedCode, ex.Code);
    }

    private static FormPage Page(string id, params string[] sections)
        => new(id, InternationalizedText.FromInvariant(id), sections);

    private static RuleDefinition Rule(string id, RuleActionKind action)
        => new(
            Id: id,
            Tier: RuleTier.JsonLogic,
            Scope: action == RuleActionKind.Validate ? RuleScope.Schema : RuleScope.Field,
            ScopeTarget: action == RuleActionKind.Validate ? "" : "name",
            Expression: "{\"!\":{\"==\":[{\"var\":\"name\"},\"\"]}}",
            Action: action);

    private static FormDefinition NewForm(
        IReadOnlyList<FormPage>? pages,
        IReadOnlyList<RuleDefinition>? rules = null,
        IReadOnlyList<AsyncValidationCheck>? asyncChecks = null,
        IReadOnlyList<string>? sectionIds = null)
    {
        FormSection[] sections;
        Dictionary<string, FieldOverlay> fields;

        if (sectionIds is null)
        {
            fields = new[] { "name", "employment", "employer" }
                .ToDictionary(f => f, f => new FieldOverlay(InternationalizedText.FromInvariant(f)));
            sections = new[]
            {
                Section("sec-a", new[] { "name", "employment" }),
                Section("sec-b", new[] { "employer" }),
            };
        }
        else
        {
            // One field per section so an arbitrary page-count fixture stays valid.
            fields = sectionIds.ToDictionary(
                id => $"field-{id}",
                id => new FieldOverlay(InternationalizedText.FromInvariant(id)));
            sections = sectionIds.Select(id => Section(id, new[] { $"field-{id}" })).ToArray();
        }

        return new FormDefinition(
            Id: new FormDefinitionId("validation-depth-test"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:test-validation-depth"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: sections,
                Rules: rules ?? Array.Empty<RuleDefinition>(),
                Pages: pages,
                AsyncChecks: asyncChecks),
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
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
