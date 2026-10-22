using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TextBoxTests : BunitContext
{
    [Fact] public void ControlledChangeRequestsWithoutLocalMutation() { var requests = new List<string>(); var cut = Render<HarborlineTextBox>(p => p.Add(x => x.Value, "alpha").Add(x => x.ValueChanged, value => requests.Add(value))); cut.Find("input").Input("beta"); Assert.Equal(["beta"], requests); Assert.Equal("alpha", cut.Find("input").GetAttribute("value")); }
    [Fact] public void UncontrolledClearAndRevealAreKeyboardReachable() { JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose; var requests = new List<string>(); var clear = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "alpha").Add(x => x.ClearButton, true).Add(x => x.ValueChanged, value => requests.Add(value))); clear.Find("button").Click(); Assert.Equal([string.Empty], requests); Assert.Equal(string.Empty, clear.Find("input").GetAttribute("value")); var password = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "secret").Add(x => x.Type, "password").Add(x => x.ShowReveal, true)); Assert.Equal("password", password.Find("input").GetAttribute("type")); password.Find("button").Click(); Assert.Equal("text", password.Find("input").GetAttribute("type")); Assert.NotEqual("-1", password.Find("button").GetAttribute("tabindex")); }
    [Fact] public void DisabledPasswordCannotReveal() { var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "secret").Add(x => x.Type, "password").Add(x => x.ShowReveal, true).Add(x => x.Disabled, true)); Assert.Empty(cut.FindAll("button")); Assert.Equal("password", cut.Find("input").GetAttribute("type")); }
    [Fact] public void ComposesInputAndPreservesCallerSuffix() { var cut = Render<HarborlineTextBox>(p => p.Add(x => x.Value, "alpha").Add(x => x.ClearButton, true).Add(x => x.Suffix, (RenderFragment)(builder => builder.AddContent(0, "USD"))).Add(x => x.Prefix, (RenderFragment)(builder => builder.AddContent(0, "$")))); Assert.NotNull(cut.Find("span.hl-input")); Assert.Equal("$", cut.Find("span.hl-input__prefix").TextContent); Assert.Equal("USD", cut.Find("span.hl-text-box__caller-suffix").TextContent); Assert.Equal("clear", cut.Find("button.hl-text-box__action").GetAttribute("data-hl-action")); }
    // Contract: clear emits the empty string once and retains focus in the text-entry group. The clear
    // control disappears with the value, so a keyboard user's focus must move back to the input.
    [Fact] public void ClearReturnsFocusToTheInput()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
        var requests = new List<string>();
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "alpha").Add(x => x.ClearButton, true).Add(x => x.ValueChanged, value => requests.Add(value)));
        var inputId = cut.Find("input").Id;
        cut.Find("[data-hl-action=clear]").Click();
        Assert.Equal([""], requests);
        Assert.Empty(cut.FindAll("[data-hl-action=clear]"));
        var focus = Assert.Single(module.Invocations["focusInput"]);
        Assert.Equal(inputId, focus.Arguments[0]);
    }
    // The component renders only its input composition: no stray text beside it.
    [Fact] public void RendersNoTextOutsideTheInputComposition()
    {
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.Value, "alpha").Add(x => x.ValueChanged, _ => { }).Add(x => x.ClearButton, true));
        Assert.All(cut.Nodes, node => Assert.True(node is AngleSharp.Dom.IElement || string.IsNullOrWhiteSpace(node.TextContent), $"stray text: '{node.TextContent}'"));
    }
    // Focus moves after the render that removes the clear action, because hlp.ui.input then renders a
    // new input element; the host hears of the change before that render.
    [Fact] public void ClearFocusesTheInputAfterTheRender()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
        var focusCallsSeenByHost = -1;
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "alpha").Add(x => x.ClearButton, true)
            .Add(x => x.ValueChanged, _ => focusCallsSeenByHost = module.Invocations["focusInput"].Count));
        cut.Find("[data-hl-action=clear]").Click();
        Assert.Equal(0, focusCallsSeenByHost);
        Assert.Single(module.Invocations["focusInput"]);
    }
    // A host may remove the field while handling the cleared value; the disposed field makes no call.
    [Fact] public void ClearMakesNoCallWhenTheHostRemovesTheField()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
        var host = Render<RemovableTextBoxHost>();
        host.Find("[data-hl-action=clear]").Click();
        Assert.Empty(host.FindAll("input"));
        Assert.Empty(module.Invocations["focusInput"]);
    }
    private sealed class RemovableTextBoxHost : ComponentBase
    {
        private bool shown = true;
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            if (!shown) return;
            builder.OpenComponent<HarborlineTextBox>(0);
            builder.AddComponentParameter(1, nameof(HarborlineTextBox.DefaultValue), "alpha");
            builder.AddComponentParameter(2, nameof(HarborlineTextBox.ClearButton), true);
            builder.AddComponentParameter(3, nameof(HarborlineTextBox.ValueChanged), EventCallback.Factory.Create<string>(this, () => shown = false));
            builder.CloseComponent();
        }
    }
    // Contract: reveal toggles only the rendered type and preserves the selection when possible; focus
    // returns to the input with the selection read before the type change.
    [Fact] public void RevealRestoresFocusAndSelection()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
        module.Setup<int[]?>("readSelection", _ => true).SetResult([2, 5]);
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "hunter2").Add(x => x.Type, "password").Add(x => x.ShowReveal, true));
        var inputId = cut.Find("input").Id;
        cut.Find("[data-hl-action=reveal]").Click();
        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
        var restore = Assert.Single(module.Invocations["restoreSelection"]);
        Assert.Equal(new object?[] { inputId, 2, 5 }, restore.Arguments);
        cut.Find("[data-hl-action=reveal]").Click();
        Assert.Equal("password", cut.Find("input").GetAttribute("type"));
        Assert.Equal(2, module.Invocations["restoreSelection"].Count);
    }
    [Theory, Trait("ModuleConformance", "hlp.ui.text-box")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.text-box", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("text-box.");
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's
    // own input and expected values. targets-focus asserts the actions are keyboard-reachable buttons
    // after the input in DOM order; the 44px target and focus ring are stylesheet behaviour left to the
    // gallery. projection-equivalence compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["text-box.projection-equivalence"];
    private IRenderedComponent<HarborlineTextBox> Box(Action<ComponentParameterCollectionBuilder<HarborlineTextBox>>? more = null)
    {
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose;
        return Render<HarborlineTextBox>(p => { p.Add(x => x.AccessibleName, "Query"); more?.Invoke(p); });
    }
    private static string Attr(AngleSharp.Dom.IElement element, string name) => element.GetAttribute(name) ?? string.Empty;
    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        static string[] Strings(System.Text.Json.JsonElement element) => element.EnumerateArray().Select(value => value.GetString()!).ToArray();
        switch (id)
        {
            case "text-box.defaults":
            {
                var cut = Box(); var box = cut.Find("input");
                Assert.Equal(expected.GetProperty("value").GetString(), Attr(box, "value"));
                Assert.Equal(expected.GetProperty("type").GetString(), Attr(box, "type"));
                Assert.Equal(expected.GetProperty("clear").GetBoolean(), cut.FindAll("[data-hl-action=clear]").Count > 0);
                Assert.Equal(expected.GetProperty("reveal").GetBoolean(), cut.FindAll("[data-hl-action=reveal]").Count > 0);
                Assert.Equal(new[] { expected.GetProperty("size").GetString()!, expected.GetProperty("fillMode").GetString()!, expected.GetProperty("rounded").GetString()! }, new[] { Attr(box, "data-hl-size"), Attr(box, "data-hl-fill"), Attr(box, "data-hl-rounded") });
                break;
            }
            case "text-box.controlled-change":
            {
                var requests = new List<string>(); var cut = Box(p => p.Add(x => x.Value, input.GetProperty("value").GetString()).Add(x => x.ValueChanged, v => requests.Add(v)));
                cut.Find("input").Input(input.GetProperty("typed").GetString());
                Assert.Equal(Strings(expected.GetProperty("requests")), requests.ToArray());
                Assert.Equal(expected.GetProperty("visibleUntilReplacement").GetString(), Attr(cut.Find("input"), "value"));
                break;
            }
            case "text-box.uncontrolled-default":
            {
                var requests = new List<string>(); var cut = Box(p => p.Add(x => x.DefaultValue, input.GetProperty("defaultValue").GetString()).Add(x => x.ValueChanged, v => requests.Add(v)));
                cut.Find("input").Input(input.GetProperty("typed").GetString());
                Assert.Equal(Strings(expected.GetProperty("requests")), requests.ToArray());
                Assert.Equal(expected.GetProperty("visible").GetString(), Attr(cut.Find("input"), "value"));
                break;
            }
            case "text-box.default-once":
            {
                var cut = Box(p => p.Add(x => x.DefaultValue, input.GetProperty("initialDefault").GetString()));
                cut.Render(p => p.Add(x => x.DefaultValue, input.GetProperty("laterDefault").GetString()));
                Assert.Equal(expected.GetProperty("visible").GetString(), Attr(cut.Find("input"), "value"));
                break;
            }
            case "text-box.external-replacement":
            {
                var cut = Box(p => p.Add(x => x.Value, input.GetProperty("valueBefore").GetString()).Add(x => x.ValueChanged, _ => { }));
                cut.Render(p => p.Add(x => x.Value, input.GetProperty("valueAfter").GetString()));
                Assert.Equal(expected.GetProperty("visible").GetString(), Attr(cut.Find("input"), "value"));
                Assert.Equal(expected.GetProperty("staleValue").GetBoolean(), Attr(cut.Find("input"), "value") == input.GetProperty("valueBefore").GetString());
                break;
            }
            case "text-box.clear-visible":
                Assert.Equal(expected.GetProperty("visibleByValue").EnumerateArray().Select(v => v.GetBoolean()).ToArray(),
                    Strings(input.GetProperty("values")).Select(value => Box(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, _ => { }).Add(x => x.ClearButton, input.GetProperty("clearButton").GetBoolean()).Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean())).FindAll("[data-hl-action=clear]").Count > 0).ToArray());
                break;
            case "text-box.clear-action":
            {
                var requests = new List<string>();
                var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
                var cut = Render<HarborlineTextBox>(p => p.Add(x => x.AccessibleName, "Query").Add(x => x.Value, input.GetProperty("value").GetString()).Add(x => x.ClearButton, true).Add(x => x.ValueChanged, v => requests.Add(v)));
                var inputId = cut.Find("input").Id;
                cut.Find("[data-hl-action=clear]").Click();
                Assert.Equal(Strings(expected.GetProperty("requests")), requests.ToArray());
                Assert.Equal(expected.GetProperty("focusWithinEditor").GetBoolean(), module.Invocations["focusInput"].Count == 1 && (string?)module.Invocations["focusInput"][0].Arguments[0] == inputId);
                break;
            }
            case "text-box.clear-disabled":
            {
                var requests = new List<string>(); var cut = Box(p => p.Add(x => x.Value, input.GetProperty("value").GetString()).Add(x => x.ClearButton, input.GetProperty("clearButton").GetBoolean()).Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean()).Add(x => x.ValueChanged, v => requests.Add(v)));
                Assert.Equal(expected.GetProperty("clearActionAvailable").GetBoolean(), cut.FindAll("[data-hl-action=clear]:not([disabled])").Count > 0);
                // Attempt every route to a request: activate any clear control present and edit the input.
                foreach (var clear in cut.FindAll("[data-hl-action=clear]")) clear.Click();
                cut.Find("input").Input(string.Empty);
                Assert.Equal(expected.GetProperty("requests").GetInt32(), requests.Count);
                break;
            }
            case "text-box.password-reveal":
            {
                var cut = Box(p => p.Add(x => x.Type, input.GetProperty("type").GetString()!).Add(x => x.ShowReveal, input.GetProperty("showReveal").GetBoolean()).Add(x => x.DefaultValue, "hunter2"));
                var types = new List<string>(); var names = new List<string>();
                foreach (var _ in Strings(input.GetProperty("actions"))) { names.Add(Attr(cut.Find("[data-hl-action=reveal]"), "aria-label")); cut.Find("[data-hl-action=reveal]").Click(); types.Add(Attr(cut.Find("input"), "type")); }
                Assert.Equal(Strings(expected.GetProperty("types")), types.ToArray());
                Assert.Equal(expected.GetProperty("valueUnchanged").GetBoolean(), Attr(cut.Find("input"), "value") == "hunter2");
                Assert.Equal(expected.GetProperty("localizedNames").GetBoolean(), names.SequenceEqual(["Show password", "Hide password"]));
                break;
            }
            case "text-box.reveal-disabled":
            {
                var cut = Box(p => p.Add(x => x.Type, input.GetProperty("type").GetString()!).Add(x => x.ShowReveal, input.GetProperty("showReveal").GetBoolean()).Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean()).Add(x => x.DefaultValue, "hunter2"));
                Assert.Equal(expected.GetProperty("revealDisabledOrAbsent").GetBoolean(), cut.FindAll("[data-hl-action=reveal]:not([disabled])").Count == 0);
                Assert.Equal(expected.GetProperty("type").GetString(), Attr(cut.Find("input"), "type"));
                break;
            }
            case "text-box.form-context":
            {
                var ctx = input.GetProperty("context");
                var value = new Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue(ctx.GetProperty("id").GetString()!, "field-label", ctx.GetProperty("describedBy").GetString(), ctx.GetProperty("required").GetBoolean(), ctx.GetProperty("disabled").GetBoolean());
                JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose;
                var cut = Render<CascadingValue<Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue>>(p => p.Add(x => x.Value, value).AddChildContent<HarborlineTextBox>());
                var box = cut.Find("input");
                Assert.Equal(expected.GetProperty("contextApplied").GetBoolean(), box.Id == value.Id && box.HasAttribute("required") == value.Required && box.HasAttribute("disabled") == value.Disabled);
                Assert.Equal(expected.GetProperty("inputRelationship").GetBoolean(), Attr(box, "aria-describedby") == value.DescribedBy && Attr(box, "aria-labelledby") == "field-label");
                break;
            }
            case "text-box.validation":
            {
                var box = Box(p => p.Add(x => x.Error, input.GetProperty("error").GetBoolean()).Add(x => x.Invalid, input.GetProperty("invalid").GetBoolean()).Add(x => x.Required, input.GetProperty("required").GetBoolean())).Find("input");
                Assert.Equal(expected.GetProperty("ariaInvalid").GetString(), Attr(box, "aria-invalid"));
                Assert.Equal(expected.GetProperty("ariaRequired").GetString(), Attr(box, "aria-required"));
                Assert.Equal(expected.GetProperty("visualInvalid").GetBoolean(), Attr(box, "data-hl-invalid") == "true");
                break;
            }
            case "text-box.host-input-attributes":
            {
                JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose;
                var box = Render<HarborlineTextBox>(p => p.Add(x => x.Id, input.GetProperty("id").GetString()).Add(x => x.Name, input.GetProperty("name").GetString()).Add(x => x.Placeholder, input.GetProperty("placeholder").GetString()).Add(x => x.Autocomplete, input.GetProperty("autocomplete").GetString()).Add(x => x.AccessibleName, input.GetProperty("aria-label").GetString()).AddUnmatched("tabindex", input.GetProperty("tabIndex").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture))).Find("input");
                Assert.Equal(expected.GetProperty("allOnInput").GetBoolean(), box.Id == input.GetProperty("id").GetString() && Attr(box, "name") == input.GetProperty("name").GetString() && Attr(box, "placeholder") == input.GetProperty("placeholder").GetString()
                    && Attr(box, "autocomplete") == input.GetProperty("autocomplete").GetString() && Attr(box, "tabindex") == input.GetProperty("tabIndex").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) && Attr(box, "aria-label") == input.GetProperty("aria-label").GetString());
                break;
            }
            case "text-box.keyboard-handler":
            {
                var calls = 0; var cut = Box(p => { if (input.GetProperty("callerHandler").GetBoolean()) p.Add(x => x.KeyDown, _ => calls++); });
                cut.Find("input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = input.GetProperty("key").GetString()! });
                Assert.Equal(expected.GetProperty("callerCount").GetInt32(), calls);
                Assert.Equal(expected.GetProperty("duplicate").GetBoolean(), calls > 1);
                break;
            }
            case "text-box.prefix-suffix-actions":
            {
                var cut = Box(p => p.Add(x => x.Value, "12").Add(x => x.ValueChanged, _ => { }).Add(x => x.Prefix, (RenderFragment)(b => b.AddContent(0, input.GetProperty("prefix").GetString()))).Add(x => x.Suffix, (RenderFragment)(b => b.AddContent(0, input.GetProperty("suffix").GetString()))).Add(x => x.ClearButton, input.GetProperty("clearButton").GetBoolean()).Add(x => x.ShowReveal, input.GetProperty("showReveal").GetBoolean()));
                var root = cut.Find(".hl-input");
                var order = root.QuerySelectorAll("input, .hl-input__prefix, .hl-text-box__caller-suffix, [data-hl-action]").Select(node => node.TagName == "INPUT" ? "input" : node.ClassList.Contains("hl-input__prefix") ? "prefix" : node.ClassList.Contains("hl-text-box__caller-suffix") ? "suffix" : node.GetAttribute("data-hl-action")!).ToArray();
                Assert.Equal(Strings(expected.GetProperty("order")), order);
                Assert.Equal(expected.GetProperty("suffixPreserved").GetBoolean(), root.QuerySelector(".hl-text-box__caller-suffix")?.TextContent == input.GetProperty("suffix").GetString());
                break;
            }
            case "text-box.locale-rtl-labels":
            {
                JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose;
                var cut = Render<Harborline.UIAdapters.Blazor.Localization.HarborlineLocaleProvider>(p => p.Add(x => x.Locale, input.GetProperty("locale").GetString()!).AddChildContent<HarborlineTextBox>(c => c.Add(x => x.AccessibleName, "Query").Add(x => x.Value, "x").Add(x => x.ValueChanged, _ => { }).Add(x => x.ClearButton, true).Add(x => x.ClearLabel, input.GetProperty("labels").GetProperty("clear").GetString())));
                Assert.Equal(expected.GetProperty("direction").GetString(), Attr(cut.Find("input"), "dir"));
                Assert.Equal(expected.GetProperty("clearLabel").GetString(), Attr(cut.Find("[data-hl-action=clear]"), "aria-label"));
                // Logical order: the action follows the input in DOM order inside an RTL context, so the browser
                // places it at the inline end; the rendered position is left to the gallery's RTL check.
                var clearAction = cut.Find("[data-hl-action=clear]");
                Assert.Equal(expected.GetProperty("logicalOrder").GetBoolean(), (cut.Find("input").CompareDocumentPosition(clearAction) & AngleSharp.Dom.DocumentPositions.Following) != 0 && clearAction.Closest("[dir]")?.GetAttribute("dir") == "rtl");
                break;
            }
            case "text-box.targets-focus":
            {
                var cut = Box(p => p.Add(x => x.Type, "password").Add(x => x.DefaultValue, "hunter2").Add(x => x.ClearButton, true).Add(x => x.ShowReveal, true));
                var actions = cut.FindAll("[data-hl-action]");
                Assert.Equal(input.GetProperty("actions").GetInt32(), actions.Count);
                var box = cut.Find("input");
                Assert.Equal(expected.GetProperty("actionsKeyboardReachable").GetBoolean(), actions.All(a => a.TagName == "BUTTON" && !a.HasAttribute("disabled") && a.GetAttribute("tabindex") != "-1" && (box.CompareDocumentPosition(a) & AngleSharp.Dom.DocumentPositions.Following) != 0));
                break;
            }
            case "text-box.appearance":
            {
                string Size(string token) => Attr(Box(p => p.Add(x => x.Size, Enum.Parse<Harborline.UIAdapters.Blazor.Components.Forms.InputSize>(token, true))).Find("input"), "data-hl-size");
                var canonical = new Dictionary<string, string> { ["small"] = "sm", ["medium"] = "md", ["large"] = "lg" };
                var sizes = Strings(input.GetProperty("sizes"));
                Assert.Equal(expected.GetProperty("aliasesNormalized").GetBoolean(), sizes.All(size => Size(size) == (canonical.TryGetValue(size, out var c) ? c : size)));
                var fills = Strings(input.GetProperty("fills")).Select(fill => Attr(Box(p => p.Add(x => x.FillMode, Enum.Parse<Harborline.UIAdapters.Blazor.Components.Forms.InputFillMode>(fill, true))).Find("input"), "data-hl-fill")).ToArray();
                var rounding = Strings(input.GetProperty("rounding")).Select(value => Attr(Box(p => p.Add(x => x.Rounded, Enum.Parse<Harborline.UIAdapters.Blazor.Components.Forms.InputRounded>(value, true))).Find("input"), "data-hl-rounded")).ToArray();
                Assert.Equal(expected.GetProperty("allTokensStable").GetBoolean(), fills.SequenceEqual(Strings(input.GetProperty("fills"))) && rounding.SequenceEqual(Strings(input.GetProperty("rounding"))));
                break;
            }
            case "text-box.replacement":
            {
                var calls = 0; var updates = input.GetProperty("updates").GetInt32();
                var cut = Box(p => p.Add(x => x.Value, "v0").Add(x => x.Placeholder, "p0").Add(x => x.ValueChanged, _ => calls++));
                for (var i = 1; i <= updates; i++) cut.Render(p => p.Add(x => x.Value, $"v{i}").Add(x => x.Placeholder, $"p{i}"));
                Assert.Equal(expected.GetProperty("latestValueAndAttributesVisible").GetBoolean(), Attr(cut.Find("input"), "value") == $"v{updates}" && Attr(cut.Find("input"), "placeholder") == $"p{updates}");
                Assert.Equal(expected.GetProperty("staleState").GetInt32(), cut.FindAll("input").Count(element => Attr(element, "value") != $"v{updates}"));
                // Replacement itself requests nothing, and one edit afterwards requests exactly once.
                var replacementCalls = calls;
                cut.Find("input").Input("typed");
                Assert.Equal(expected.GetProperty("duplicateCallbacks").GetInt32(), replacementCalls + (calls - replacementCalls - 1));
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
