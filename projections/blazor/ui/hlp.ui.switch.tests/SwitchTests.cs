using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SwitchTests : BunitContext
{
    [Fact] public void ControlledActivationRequestsOnceAndKeepsState(){var values=new List<bool>();var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.Checked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.CheckedChanged,v=>values.Add(v)));cut.Find("[role=switch]").Click();Assert.Equal([true],values);Assert.Equal("false",cut.Find("[role=switch]").GetAttribute("aria-checked"));}
    [Fact] public void UncontrolledStateAndFormValueTrack(){var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.DefaultChecked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.Name,"alerts"));cut.Find("[role=switch]").Click();Assert.Equal("true",cut.Find("[role=switch]").GetAttribute("aria-checked"));Assert.Equal("on",cut.Find("input[type=hidden]").GetAttribute("value"));}
    [Fact] public void StateCopyNeverRenamesControl(){var cut=Render<HarborlineSwitch>(p=>p.Add(x=>x.DefaultChecked,false).Add(x=>x.AccessibleName,"Alerts").Add(x=>x.OnText,"Enabled").Add(x=>x.OffText,"Disabled"));Assert.Equal("Alerts",cut.Find("[role=switch]").GetAttribute("aria-label"));cut.Find("[role=switch]").Click();Assert.Equal("Alerts",cut.Find("[role=switch]").GetAttribute("aria-label"));Assert.Equal("Enabled",cut.Find(".hl-switch__state").TextContent);}
    [Theory,Trait("ModuleConformance","hlp.ui.switch")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.switch", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("switch.");
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's
    // own input and expected values. keyboard and disabled assert the switch is a native button with no key
    // handler of its own, so the browser's single click per Enter or Space is the only request (bUnit cannot
    // press native keys; the Chromium probe checks the count). visible-label clicks the element the label's
    // for targets, as the browser forwards label activation. rtl asserts logical direction and checked markers
    // (pixel geometry is the gallery's check). touch-target flips the coarse-pointer query through a fake
    // observer and asserts the data-hl-touch marker plus the stylesheet's 44px rule keyed on it.
    // projection-equivalence compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["switch.projection-equivalence"];
    private static string RepositoryFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, relative))) return File.ReadAllText(Path.Combine(directory.FullName, relative));
        throw new FileNotFoundException(relative);
    }
    private IRenderedComponent<HarborlineSwitch> Switch(Action<ComponentParameterCollectionBuilder<HarborlineSwitch>>? configure = null, Action<bool>? changed = null) =>
        Render<HarborlineSwitch>(p => { p.Add(x => x.AccessibleName, "Email alerts"); if (changed is not null) p.Add(x => x.CheckedChanged, value => changed(value)); configure?.Invoke(p); });
    private static bool[] Bools(System.Text.Json.JsonElement value) => value.EnumerateArray().Select(v => v.GetBoolean()).ToArray();
    private static string[] Strings(System.Text.Json.JsonElement value) => value.EnumerateArray().Select(v => v.GetString()!).ToArray();
    private static bool OwnKeyHandlers(AngleSharp.Dom.IElement control) => control.Attributes.Any(a => a.Name.StartsWith("blazor:onkey", StringComparison.Ordinal));
    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        switch (id)
        {
            case "switch.uncontrolled":
            {
                var requests = new List<bool>(); var cut = Switch(p => p.Add(x => x.DefaultChecked, input.GetProperty("defaultChecked").GetBoolean()), requests.Add);
                var states = new List<bool> { cut.Find("[role=switch]").GetAttribute("aria-checked") == "true" };
                for (var i = 0; i < input.GetProperty("activations").GetInt32(); i++) { cut.Find("[role=switch]").Click(); states.Add(cut.Find("[role=switch]").GetAttribute("aria-checked") == "true"); }
                Assert.Equal(Bools(expected.GetProperty("states")), states.ToArray());
                Assert.Equal(Bools(expected.GetProperty("requestedValues")), requests.ToArray());
                break;
            }
            case "switch.controlled":
            {
                var requests = new List<bool>(); var cut = Switch(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add);
                if (input.GetProperty("activate").GetBoolean()) cut.Find("[role=switch]").Click();
                // Controlled: the request is made and the rendered state waits for the caller.
                Assert.Equal(expected.GetProperty("renderedChecked").GetBoolean(), cut.Find("[role=switch]").GetAttribute("aria-checked") == "true");
                Assert.Equal(Bools(expected.GetProperty("requestedValues")), requests.ToArray());
                break;
            }
            case "switch.pointer":
            {
                var requests = new List<bool>(); var cut = Switch(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add);
                if (input.GetProperty("pointer").GetString() == "click") cut.Find("[role=switch]").Click();
                Assert.Equal(Bools(expected.GetProperty("requestedValues")), requests.ToArray());
                Assert.Equal(expected.GetProperty("requestCount").GetInt32(), requests.Count);
                break;
            }
            case "switch.keyboard":
            {
                var requests = new List<bool>(); var cut = Switch(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add);
                var control = cut.Find("[role=switch]");
                var nativeActivation = control.TagName == "BUTTON" && control.GetAttribute("type") == "button" && !control.HasAttribute("disabled");
                // Each key reaches the host through the browser's click: one click per key.
                var perKey = input.GetProperty("keys").EnumerateArray().Select(_ => { var before = requests.Count; cut.Find("[role=switch]").Click(); return requests.Count - before; }).ToArray();
                Assert.Equal(Bools(expected.GetProperty("requestedValues")), requests.ToArray());
                Assert.Equal(expected.GetProperty("oneRequestPerKey").GetBoolean(), nativeActivation && !OwnKeyHandlers(control) && perKey.All(count => count == 1));
                break;
            }
            case "switch.disabled":
            {
                var count = 0; var cut = Switch(p => p.Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean()), _ => count++);
                var control = cut.Find("[role=switch]");
                Assert.Equal(expected.GetProperty("nativeDisabled").GetBoolean(), control.HasAttribute("disabled"));
                // Pointer, Enter and Space all reach the switch as a click (it has no key handler of its own).
                Assert.False(OwnKeyHandlers(control));
                foreach (var _ in input.GetProperty("methods").EnumerateArray()) control.Click();
                Assert.Equal(expected.GetProperty("requestCount").GetInt32(), count);
                break;
            }
            case "switch.stable-name":
            {
                var name = input.GetProperty("accessibleName").GetString();
                var states = Bools(input.GetProperty("states"));
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, name).Add(x => x.Checked, states[0]));
                var names = new List<string?>(); var checkedStates = new List<bool>();
                foreach (var state in states)
                {
                    cut.Render(p => p.Add(x => x.Checked, state));
                    var control = cut.Find("[role=switch]");
                    names.Add(control.GetAttribute("aria-labelledby") is null ? control.GetAttribute("aria-label") : null);
                    checkedStates.Add(control.GetAttribute("aria-checked") == "true");
                }
                Assert.Equal(Strings(expected.GetProperty("names")), names.ToArray());
                Assert.Equal(Bools(expected.GetProperty("checkedStates")), checkedStates.ToArray());
                break;
            }
            case "switch.visible-label":
            {
                var label = input.GetProperty("label").GetString()!; var requests = new List<bool>();
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.Label, label).Add(x => x.Checked, false).Add(x => x.CheckedChanged, value => requests.Add(value)));
                var control = cut.Find("[role=switch]"); var element = cut.Find("label.hl-switch__label");
                var associated = element.TextContent == label && element.GetAttribute("for") == control.Id && control.GetAttribute("aria-labelledby") == element.Id;
                Assert.Equal(expected.GetProperty("associated").GetBoolean(), associated);
                if (input.GetProperty("activateLabel").GetBoolean()) cut.Find($"#{element.GetAttribute("for")}").Click();
                Assert.Equal(Bools(expected.GetProperty("requestedValues")), requests.ToArray());
                break;
            }
            case "switch.state-copy":
            {
                var states = Bools(input.GetProperty("states"));
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Alerts").Add(x => x.Checked, states[0]).Add(x => x.OnText, input.GetProperty("onText").GetString()).Add(x => x.OffText, input.GetProperty("offText").GetString()));
                var names = new List<string?>(); var visible = new List<string>();
                foreach (var state in states)
                {
                    cut.Render(p => p.Add(x => x.Checked, state));
                    names.Add(cut.Find("[role=switch]").GetAttribute("aria-label"));
                    visible.Add(cut.Find(".hl-switch__state").TextContent);
                }
                Assert.Equal(Strings(expected.GetProperty("visibleText")), visible.ToArray());
                // The state copy is aria-hidden and never becomes the name.
                Assert.Equal(expected.GetProperty("accessibleNameUnchanged").GetBoolean(), names.All(name => name == "Alerts") && cut.Find(".hl-switch__state").GetAttribute("aria-hidden") == "true");
                break;
            }
            case "switch.description":
            {
                var description = input.GetProperty("description").GetString()!;
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.Label, input.GetProperty("label").GetString()).Add(x => x.Description, description));
                var element = cut.Find(".hl-switch__description");
                Assert.Equal(expected.GetProperty("descriptionVisible").GetBoolean(), element.TextContent == description && element.Closest("[aria-hidden=true]") is null);
                Assert.Equal(expected.GetProperty("describedByIncludesDescription").GetBoolean(), (cut.Find("[role=switch]").GetAttribute("aria-describedby") ?? "").Split(' ').Contains(element.Id));
                break;
            }
            case "switch.form-field":
            {
                var required = input.GetProperty("required").GetBoolean(); var disabled = input.GetProperty("disabled").GetBoolean();
                var value = new Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue("ambient", "field-label", $"{input.GetProperty("hintId").GetString()} {input.GetProperty("errorId").GetString()}", required, disabled);
                AngleSharp.Dom.IElement Field(Action<ComponentParameterCollectionBuilder<HarborlineSwitch>> local) =>
                    Render<CascadingValue<Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue>>(p => p.Add(x => x.Value, value).AddChildContent<HarborlineSwitch>(local)).Find("[role=switch]");
                var ambient = Field(_ => { });
                var applied = ambient.GetAttribute("aria-labelledby") == value.LabelId && (ambient.GetAttribute("aria-required") == "true") == required && ambient.HasAttribute("disabled") == disabled;
                Assert.Equal(expected.GetProperty("ambientMetadataApplied").GetBoolean(), applied);
                Assert.Equal(expected.GetProperty("describedBy").GetString(), ambient.GetAttribute("aria-describedby"));
                var local = Field(p => p.Add(x => x.Disabled, !disabled).Add(x => x.Required, !required));
                Assert.Equal(expected.GetProperty("localPrecedence").GetBoolean(), local.HasAttribute("aria-required") == !required && local.HasAttribute("disabled") == !disabled);
                break;
            }
            case "switch.validation":
            {
                var cut = Switch(p => p.Add(x => x.Error, input.GetProperty("error").GetBoolean()).Add(x => x.Required, input.GetProperty("required").GetBoolean()));
                var control = cut.Find("[role=switch]");
                Assert.Equal(expected.GetProperty("ariaInvalid").GetString(), control.GetAttribute("aria-invalid"));
                Assert.Equal(expected.GetProperty("ariaRequired").GetString(), control.GetAttribute("aria-required"));
                // The non-color signal: a visible "!" indicator beside the double-bordered control.
                Assert.Equal(expected.GetProperty("nonColorSignal").GetBoolean(), cut.FindAll(".hl-switch__error-indicator").Any(node => node.TextContent == "!") && control.ClassList.Contains("hl-switch__control--invalid"));
                break;
            }
            case "switch.form-value":
            {
                var name = input.GetProperty("name").GetString()!; var states = Bools(input.GetProperty("states"));
                var cut = Switch(p => p.Add(x => x.Name, name).Add(x => x.Checked, states[0]));
                var values = states.Select(state => { cut.Render(p => p.Add(x => x.Checked, state)); var fields = cut.FindAll($"input[name={name}]"); Assert.Single(fields); return fields[0].GetAttribute("value"); }).ToArray();
                Assert.Equal(Strings(expected.GetProperty("values")), values);
                break;
            }
            case "switch.touch-target":
            {
                var media = new LiveMedia();
                Services.AddSingleton<IMediaQueryObserver>(media);
                var size = Enum.Parse<SwitchSize>(input.GetProperty("size").GetString()!, ignoreCase: true);
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Alerts").Add(x => x.Size, size));
                (string? Touch, string? Size) Tokens() { var root = cut.Find(".hl-switch"); return (root.GetAttribute("data-hl-touch"), root.GetAttribute("data-hl-size")); }
                var fine = Tokens();
                cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.AnyCoarsePointer, input.GetProperty("coarsePointer").GetBoolean())).GetAwaiter().GetResult();
                cut.WaitForAssertion(() => Assert.Equal("true", Tokens().Touch));
                var coarse = Tokens();
                Assert.Null(fine.Touch);
                // The visible pill keeps its size token; the marker only switches on the invisible ::before target.
                Assert.Equal(expected.GetProperty("visiblePillUnchanged").GetBoolean(), fine.Size == coarse.Size && coarse.Size == input.GetProperty("size").GetString());
                var rule = System.Text.RegularExpressions.Regex.Match(RepositoryFile("projections/blazor/ui/hlp.ui.switch/wwwroot/switch.css"), @"\.hl-switch\[data-hl-touch='true'\] \.hl-switch__control::before\{min-inline-size:(\d+)px;min-block-size:(\d+)px\}");
                var minimum = expected.GetProperty("minimumTargetCssPixels").GetInt32();
                Assert.Equal((minimum, minimum), (int.Parse(rule.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(rule.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
                break;
            }
            case "switch.sizes":
            {
                var rendered = Strings(input.GetProperty("values")).Select(size =>
                {
                    var cut = Switch(p => p.Add(x => x.Checked, true).Add(x => x.Size, Enum.Parse<SwitchSize>(size, true)));
                    var control = cut.Find("[role=switch]");
                    return (Size: cut.Find(".hl-switch").GetAttribute("data-hl-size"), Semantics: string.Join("|", control.GetAttribute("role"), control.GetAttribute("aria-checked"), control.GetAttribute("aria-label"), control.GetAttribute("type")));
                }).ToArray();
                Assert.Equal(Strings(expected.GetProperty("normalized")), rendered.Select(value => value.Size).ToArray());
                Assert.Equal(expected.GetProperty("semanticsEqual").GetBoolean(), rendered.All(value => value.Semantics == rendered[0].Semantics));
                break;
            }
            case "switch.rtl":
            {
                var isChecked = input.GetProperty("checked").GetBoolean();
                // Direction comes from the locale, as in React, not from an echoed parameter.
                (string? Dir, string? Checked, bool Thumb, string? Classes) Markers(string locale)
                {
                    var root = Render<Harborline.UIAdapters.Blazor.Localization.HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).AddChildContent<HarborlineSwitch>(c => c.Add(x => x.AccessibleName, "Alerts").Add(x => x.Checked, isChecked))).Find(".hl-switch");
                    var control = root.QuerySelector("[role=switch]")!;
                    return (root.GetAttribute("dir"), control.GetAttribute("data-hl-checked"), root.QuerySelector(".hl-switch__thumb") is not null, control.GetAttribute("class"));
                }
                var ltr = Markers("en-US");
                var rtl = Markers(input.GetProperty("direction").GetString() == "rtl" ? "ar-SA" : "en-US");
                // Logical inset-inline-start in the stylesheet puts the checked thumb at the end of the direction.
                Assert.Equal(expected.GetProperty("thumbAtLogicalEnd").GetBoolean(), rtl.Dir == input.GetProperty("direction").GetString() && rtl.Checked == (isChecked ? "true" : null) && rtl.Thumb);
                Assert.Equal(expected.GetProperty("borderStable").GetBoolean(), rtl.Classes == ltr.Classes && ltr.Dir != rtl.Dir);
                break;
            }
            case "switch.state-copy-catalog":
            {
                var host = input.GetProperty("hostCatalog");
                var entries = host.GetProperty("entries").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!);
                var caller = input.GetProperty("callerText");
                string[] Copy(bool inHost, bool withCaller) => input.GetProperty("states").EnumerateArray().Select(state =>
                {
                    void Configure(ComponentParameterCollectionBuilder<HarborlineSwitch> p)
                    {
                        p.Add(x => x.AccessibleName, "Alerts").Add(x => x.Checked, state.GetBoolean());
                        if (withCaller) p.Add(x => x.OffText, caller.GetProperty("offText").GetString()).Add(x => x.OnText, caller.GetProperty("onText").GetString());
                    }
                    return inHost
                        ? Render<Harborline.UIAdapters.Blazor.Localization.HarborlineLocaleProvider>(p => p.Add(x => x.Locale, host.GetProperty("locale").GetString()).Add(x => x.Catalog, entries).AddChildContent<HarborlineSwitch>(Configure)).Find(".hl-switch__state").TextContent
                        : Render<HarborlineSwitch>(Configure).Find(".hl-switch__state").TextContent;
                }).ToArray();
                Assert.Equal(Strings(expected.GetProperty("defaultCatalog")), Copy(false, false));
                Assert.Equal(Strings(expected.GetProperty("hostCatalog")), Copy(true, false));
                Assert.Equal(Strings(expected.GetProperty("callerText")), Copy(true, true));
                break;
            }
            case "switch.label-precedence":
            {
                var cut = Render<HarborlineSwitch>(p => p.Add(x => x.Label, input.GetProperty("label").GetString()).Add(x => x.AccessibleName, input.GetProperty("accessibleName").GetString()));
                // The accessible name computation: aria-labelledby wins over aria-label.
                var control = cut.Find("[role=switch]");
                var labelledBy = control.GetAttribute("aria-labelledby");
                Assert.Equal(expected.GetProperty("accessibleName").GetString(), labelledBy is null ? control.GetAttribute("aria-label") : cut.Find($"#{labelledBy}").TextContent);
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}

    // Touch sizing beyond the fixture: the observer is shared, every form-factor signal is followed, a
    // disposed switch ignores later changes, and a failing observer leaves the pointer-sized switch.
    private sealed class LiveMedia : IMediaQueryObserver
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        private readonly Dictionary<string, Func<MediaQueryChange, ValueTask>> callbacks = new(StringComparer.Ordinal);
        public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new Microsoft.JSInterop.JSException("media-query module unavailable");
            callbacks[query] = onChanged;
            return ValueTask.FromResult<IMediaQuerySubscription>(new Live(query));
        }
        public Task SetAsync(string query, bool matches) => callbacks[query](new(query, matches)).AsTask();
        private sealed record Live(string Query) : IMediaQuerySubscription { public bool Matches => false; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    [Fact]
    public void SwitchesSharingAnObserverSubscribeToTheFormFactorQueriesOnce()
    {
        var media = new LiveMedia();
        Services.AddSingleton<IMediaQueryObserver>(media);
        Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "One"));
        Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Two"));
        Assert.Equal(4, media.Calls);
    }

    [Fact]
    public async Task PhoneWidthShortLandscapeAndCoarsePointerEachTurnOnTheTouchTarget()
    {
        var media = new LiveMedia();
        Services.AddSingleton<IMediaQueryObserver>(media);
        var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Alerts"));
        string? Marker() => cut.Find(".hl-switch").GetAttribute("data-hl-touch");
        Assert.Null(Marker());
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.Phone, true));
        cut.WaitForAssertion(() => Assert.Equal("true", Marker()));
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.Phone, false));
        cut.WaitForAssertion(() => Assert.Null(Marker()));
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.Landscape, true));
        Assert.Null(Marker());
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.ShortHeight, true));
        cut.WaitForAssertion(() => Assert.Equal("true", Marker()));
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.ShortHeight, false));
        cut.WaitForAssertion(() => Assert.Null(Marker()));
        await cut.InvokeAsync(() => media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.AnyCoarsePointer, true));
        cut.WaitForAssertion(() => Assert.Equal("true", Marker()));
        // The visible pill keeps its size token throughout.
        Assert.Equal("md", cut.Find(".hl-switch").GetAttribute("data-hl-size"));
    }

    [Fact]
    public async Task ADisposedSwitchIgnoresLaterFormFactorChanges()
    {
        var media = new LiveMedia();
        Services.AddSingleton<IMediaQueryObserver>(media);
        var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Alerts"));
        await DisposeComponentsAsync();
        await media.SetAsync(Harborline.Foundation.Responsive.FormFactorQueries.AnyCoarsePointer, true);
        Assert.Equal(4, media.Calls);
    }

    [Fact]
    public void AFailingObserverLeavesThePointerSizedSwitch()
    {
        Services.AddSingleton<IMediaQueryObserver>(new LiveMedia { Fail = true });
        var cut = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Alerts"));
        Assert.Null(cut.Find(".hl-switch").GetAttribute("data-hl-touch"));
        Assert.Equal("Alerts", cut.Find("[role=switch]").GetAttribute("aria-label"));
    }
}
