using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Security;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

// T-485 slice 2: the render path decides page visibility through Rules (DES-0016 forms-eng-4,
// forms-run-1; ledger L368). The view carries each page's verdict, never its guard (A9).
public sealed class PageVisibilityRenderTests
{
    private const string TriggerIsYes = "{\"==\":[{\"var\":\"trigger\"},\"yes\"]}";

    [Theory(DisplayName = "forms-eng-4: render decides page visibility through the Rules guard")]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public async Task Render_decides_page_visibility_through_rules(string trigger, bool expected)
    {
        var view = await RenderAsync($$"""{"trigger":"{{trigger}}","amount":1}""", TriggerIsYes);

        var pages = view.Pages.Value!;
        Assert.Equal(["first", "second"], pages.Select(page => page.Id));
        Assert.True(pages[0].Visible);
        Assert.Equal(expected, pages[1].Visible);
        Assert.Equal(["second"], pages[1].Sections);
        Assert.Equal("Second", pages[1].Title.Values["en"]);
    }

    [Fact(DisplayName = "forms-eng-4: render reads computed values before page guards, as submit does")]
    public async Task Render_page_guard_reads_computed_value()
    {
        var view = await RenderAsync("""{"trigger":"no","amount":3}""", "{\">\":[{\"var\":\"total\"},5]}");

        Assert.True(view.Pages.Value![1].Visible);
    }

    [Fact(DisplayName = "forms-eng-4: a page guard that does not evaluate hides the page at render")]
    public async Task Render_fails_closed_on_a_guard_that_does_not_evaluate()
    {
        var view = await RenderAsync("""{"trigger":"yes","amount":1}""", "{\"frobnicate\":[1]}");

        Assert.False(view.Pages.Value![1].Visible);
    }

    [Fact(DisplayName = "forms-eng-4: a pageless form renders no pages")]
    public async Task Render_pageless_form_has_no_pages()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);

        Assert.False(view.Pages.HasValue);
    }

    [Fact(DisplayName = "forms-eng-4: an unbound render evaluates page guards over the empty candidate")]
    public async Task Render_without_instance_evaluates_guards()
    {
        var harness = await CreateAsync(TriggerIsYes, readable: "{}");

        var pages = (await harness.Engine.RenderAsync(harness.Definition.Id, null)).Pages.Value!;

        Assert.True(pages[0].Visible);
        Assert.False(pages[1].Visible);
    }

    [Fact(DisplayName = "forms-run-1: page visibility is evaluated for the caller and never reads a sensitive value")]
    public async Task Render_page_guard_never_reads_a_sensitive_value()
    {
        var view = await RenderAsync("""{"trigger":"no","amount":1,"secret":"x"}""", "{\"==\":[{\"var\":\"secret\"},\"x\"]}");

        var secret = view.Sections.Single(section => section.Id == "second").Fields.Single(field => field.Name == "secret");
        Assert.True(secret.IsReadable);
        Assert.False(view.Pages.Value![1].Visible);
    }

    private static async Task<Harborline.Contracts.Forms.FormView> RenderAsync(string readable, string guard)
    {
        var harness = await CreateAsync(guard, readable);
        using var candidate = JsonDocument.Parse("""{"trigger":"no","amount":1}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, Guid.NewGuid().ToString("N")));
        return await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);
    }

    private static Task<FormEngineOrchestrationTests.Harness> CreateAsync(string guard, string readable) =>
        FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader", FormEnginePermissions.DecryptSensitive],
            schemaJson: """{"type":"object"}""",
            security: new FormEngineOrchestrationTests.RecordingSecurity(JsonDocument.Parse(readable)),
            definitionFactory: (schema, tenant) => Definition(schema, tenant, guard));

    private static State.FormDefinition Definition(string schema, TenantId tenant, string guard)
    {
        var fields = new[] { "trigger", "amount", "total", "secret" }.ToDictionary(
            field => field,
            field => new State.FieldOverlay(
                State.InternationalizedText.FromInvariant(field),
                PiiSensitivity: field == "secret" ? State.PiiSensitivity.Sensitive : State.PiiSensitivity.None),
            StringComparer.Ordinal);
        var access = new State.SectionAccess(
            [Harborline.Contracts.Authorization.RoleReference.Domain("reader")],
            [Harborline.Contracts.Authorization.RoleReference.Domain("reader")]);
        var now = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        return new(
            new("paged-form"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schema),
            new(
                fields,
                [
                    new("first", State.InternationalizedText.FromInvariant("First"), ["trigger", "amount", "total"], access),
                    new("second", State.InternationalizedText.FromInvariant("Second"), ["secret"], access),
                ],
                [new("cmp.total", State.RuleTier.JsonLogic, State.RuleScope.Field, "total", "{\"*\":[{\"var\":\"amount\"},2]}", State.RuleActionKind.Compute)],
                Pages:
                [
                    new("first", State.InternationalizedText.FromInvariant("First"), ["first"]),
                    new("second", State.InternationalizedText.FromInvariant("Second"), ["second"], guard),
                ]),
            null,
            now,
            now);
    }
}
