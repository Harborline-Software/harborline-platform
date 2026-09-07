using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ConfirmDialogTests : BunitContext
{
    public ConfirmDialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact(DisplayName = "confirm-dialog.controlled-open")]
    public void ControlledOpenRequestsOnly()
    {
        var requests = new List<bool>();
        var cut = RenderConfirm(open: false, openChanged: requests.Add);
        Assert.Empty(cut.FindAll("[role='dialog']"));

        cut.Render(parameters => BaseParameters(parameters, true, requests.Add));
        Assert.Single(cut.FindAll("[role='dialog']"));
        cut.Find(".hl-confirm-dialog__cancel").Click();

        Assert.Equal([false], requests);
        Assert.Single(cut.FindAll("[role='dialog']"));
    }

    [Fact(DisplayName = "confirm-dialog.confirm-invokes-once-then-closes")]
    public void ConfirmInvokesOnceThenCloses()
    {
        var order = new List<string>();
        var cut = RenderConfirm(
            confirm: () => order.Add("confirm"),
            openChanged: next => { Assert.False(next); order.Add("close"); });

        cut.Find(".hl-confirm-dialog__confirm").Click();

        Assert.Equal(["confirm", "close"], order);
    }

    [Fact(DisplayName = "confirm-dialog.cancel-closes-without-confirm")]
    public void CancelClosesWithoutConfirm()
    {
        var confirms = 0;
        var requests = new List<bool>();
        var cut = RenderConfirm(confirm: () => confirms++, openChanged: requests.Add);

        cut.Find(".hl-confirm-dialog__cancel").Click();

        Assert.Equal(0, confirms);
        Assert.Equal([false], requests);
    }

    [Fact(DisplayName = "confirm-dialog.localized-labels")]
    public void LocalizedLabelsResolveFromCatalog()
    {
        var cut = RenderConfirm(catalog: new Dictionary<string, string>
        {
            ["common.confirm"] = "Proceed",
            ["common.cancel"] = "Return",
        });

        Assert.Equal("Return", cut.Find(".hl-confirm-dialog__cancel").TextContent);
        Assert.Equal("Proceed", cut.Find(".hl-confirm-dialog__confirm").TextContent);
    }

    [Fact(DisplayName = "confirm-dialog.label-override")]
    public void LabelOverridesWinOverCatalog()
    {
        var cut = RenderConfirm(
            confirmLabel: "Delete forever",
            cancelLabel: "Keep",
            catalog: new Dictionary<string, string>
            {
                ["common.confirm"] = "Proceed",
                ["common.cancel"] = "Return",
            });

        Assert.Equal("Keep", cut.Find(".hl-confirm-dialog__cancel").TextContent);
        Assert.Equal("Delete forever", cut.Find(".hl-confirm-dialog__confirm").TextContent);
        Assert.DoesNotContain("Proceed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "confirm-dialog.key-echo-fallback")]
    public void KeyEchoFallsBackToSharedDefaults()
    {
        var cut = RenderConfirm(catalog: new Dictionary<string, string>
        {
            ["common.confirm"] = "common.confirm",
            ["common.cancel"] = "common.cancel",
        });

        Assert.Equal("Cancel", cut.Find(".hl-confirm-dialog__cancel").TextContent);
        Assert.Equal("Confirm", cut.Find(".hl-confirm-dialog__confirm").TextContent);
        Assert.DoesNotContain("common.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "confirm-dialog.destructive-emphasis")]
    public void DestructiveChangesOnlyEmphasis()
    {
        var cut = RenderConfirm();
        var defaultActions = ActionLabels(cut);
        Assert.DoesNotContain("hl-confirm-dialog__confirm--destructive", cut.Find(".hl-confirm-dialog__confirm").ClassList);

        cut.Render(parameters => BaseParameters(parameters, true, variant: ConfirmDialogVariant.Destructive));

        var destructive = cut.Find(".hl-confirm-dialog__confirm");
        Assert.Contains("hl-confirm-dialog__confirm--destructive", destructive.ClassList);
        Assert.Equal("button", destructive.LocalName);
        Assert.Equal(defaultActions, ActionLabels(cut));
    }

    [Fact(DisplayName = "confirm-dialog.dismissal-passthrough")]
    public async Task DismissalPolicyPassesThrough()
    {
        var requests = new List<bool>();
        var cut = RenderConfirm(closeOnOverlayClick: false, closeOnEscape: false, openChanged: requests.Add);
        var shell = cut.FindComponent<HarborlineDialog>();

        Assert.False(shell.Instance.CloseOnOverlayClick);
        Assert.False(shell.Instance.CloseOnEscape);
        await cut.Find(".hl-dialog__overlay").TriggerEventAsync("onpointerdown", new PointerEventArgs());
        await shell.Instance.DismissFromJavaScriptAsync();
        Assert.Empty(requests);
    }

    [Fact(DisplayName = "confirm-dialog.composes-dialog")]
    public void ComposesDialogShell()
    {
        var cut = RenderConfirm();

        Assert.Single(cut.FindComponents<HarborlineDialog>());
        Assert.Single(cut.FindAll("[role='dialog']"));
        Assert.Equal("true", cut.Find("[role='dialog']").GetAttribute("aria-modal"));
        Assert.Empty(cut.FindAll(".hl-confirm-dialog[role]"));
    }

    [Fact(DisplayName = "confirm-dialog.long-content-reflow")]
    public void LongContentUsesReflowHooks()
    {
        var title = new string('T', 220);
        var description = new string('D', 900);
        var label = "A deliberately expanded confirmation label that must wrap safely at narrow widths";
        var cut = RenderConfirm(title: title, description: description, confirmLabel: label);

        Assert.Equal(title, cut.Find(".hl-dialog__title").TextContent);
        Assert.Equal(description, cut.Find(".hl-dialog__description").TextContent);
        Assert.Equal(label, cut.Find(".hl-confirm-dialog__confirm").TextContent);
        Assert.Equal(2, cut.FindAll(".hl-dialog__footer button").Count);
    }

    [Fact(DisplayName = "confirm-dialog.locale-rtl")]
    public void LocaleRtlFlowsFromProviderAndPreservesActionOrder()
    {
        var cut = RenderConfirm(locale: "ar", direction: "rtl", catalog: new Dictionary<string, string>
        {
            ["common.confirm"] = "تأكيد",
            ["common.cancel"] = "إلغاء",
        });

        Assert.Equal("rtl", cut.Find("[role='dialog']").GetAttribute("dir"));
        Assert.Empty(cut.FindAll(".hl-dialog__footer[dir]"));
        Assert.Equal(["إلغاء", "تأكيد"], ActionLabels(cut));
    }

    [Fact(DisplayName = "confirm-dialog.replacement")]
    public void ReplacementRemovesStaleContent()
    {
        var cut = RenderConfirm(title: "First title", description: "First description", confirmLabel: "First action");

        cut.Render(parameters => BaseParameters(parameters, true, title: "Second title", description: "Second description", confirmLabel: "Second action"));

        Assert.Contains("Second title", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Second description", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Second action", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("First", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "confirm-dialog.projection-equivalence")]
    public void ProjectionEquivalentObservableSurface()
    {
        var cut = RenderConfirm(confirmLabel: "Confirm", cancelLabel: "Cancel");

        Assert.Single(cut.FindAll("[role='dialog']"));
        Assert.Equal(["Cancel", "Confirm"], ActionLabels(cut));
        Assert.Equal(2, cut.FindAll(".hl-dialog__footer button[type='button']").Count);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.confirm-dialog")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("confirm-dialog.", fixture.RootElement.GetProperty("id").GetString());
        var cut = RenderConfirm();
        Assert.Single(cut.FindAll("[role='dialog']"));
        Assert.Equal(["Cancel", "Confirm"], ActionLabels(cut));

        if (fixture.RootElement.GetProperty("id").GetString() != "confirm-dialog.action-classes") return;

        // The spelling of these classes IS the parity: the React lane asserts the same fixture row,
        // so a lane-only class or a dropped modifier turns both suites red instead of hiding in a
        // hand-written lane stylesheet (ticket 282).
        var expected = fixture.RootElement.GetProperty("expected");
        Assert.Equal(Classes(expected, "cancelClasses"), cut.Find(".hl-confirm-dialog__cancel").ClassList);
        Assert.Equal(Classes(expected, "confirmClasses"), cut.Find(".hl-confirm-dialog__confirm").ClassList);

        var destructive = RenderConfirm(variant: ConfirmDialogVariant.Destructive);
        Assert.Equal(Classes(expected, "destructiveConfirmClasses"), destructive.Find(".hl-confirm-dialog__confirm").ClassList);
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

    private IRenderedComponent<HarborlineConfirmDialog> RenderConfirm(
        bool open = true,
        string title = "Delete the record?",
        string description = "This cannot be undone.",
        string? confirmLabel = null,
        string? cancelLabel = null,
        ConfirmDialogVariant variant = ConfirmDialogVariant.Default,
        bool closeOnOverlayClick = true,
        bool closeOnEscape = true,
        string locale = "en-US",
        string? direction = "ltr",
        IReadOnlyDictionary<string, string>? catalog = null,
        Action? confirm = null,
        Action<bool>? openChanged = null)
    {
        var provider = Render<HarborlineLocaleProvider>(parameters => parameters
            .Add(component => component.Locale, locale)
            .Add(component => component.Direction, direction)
            .Add(component => component.Catalog, catalog)
            .AddChildContent<HarborlineConfirmDialog>(dialog => BaseParameters(
                dialog, open, openChanged, confirm, title, description, confirmLabel, cancelLabel,
                variant, closeOnOverlayClick, closeOnEscape)));
        return provider.FindComponent<HarborlineConfirmDialog>();
    }

    private static ComponentParameterCollectionBuilder<HarborlineConfirmDialog> BaseParameters(
        ComponentParameterCollectionBuilder<HarborlineConfirmDialog> parameters,
        bool open,
        Action<bool>? openChanged = null,
        Action? confirm = null,
        string title = "Delete the record?",
        string description = "This cannot be undone.",
        string? confirmLabel = null,
        string? cancelLabel = null,
        ConfirmDialogVariant variant = ConfirmDialogVariant.Default,
        bool closeOnOverlayClick = true,
        bool closeOnEscape = true) => parameters
            .Add(component => component.Open, open)
            .Add(component => component.OpenChanged, next => openChanged?.Invoke(next))
            .Add(component => component.Title, title)
            .Add(component => component.Description, description)
            .Add(component => component.ConfirmLabel, confirmLabel)
            .Add(component => component.CancelLabel, cancelLabel)
            .Add(component => component.OnConfirm, () => confirm?.Invoke())
            .Add(component => component.Variant, variant)
            .Add(component => component.CloseOnOverlayClick, closeOnOverlayClick)
            .Add(component => component.CloseOnEscape, closeOnEscape);

    private static string[] ActionLabels(IRenderedComponent<HarborlineConfirmDialog> cut) =>
        cut.FindAll(".hl-dialog__footer button").Select(button => button.TextContent).ToArray();
}
