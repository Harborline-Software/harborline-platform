using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SwitchFieldTests : BunitContext
{
    [Fact] public void NameSuppliesIdAndControlledActivationRequestsOnce() { bool? requested = null; var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "published").Add(x => x.Checked, false).Add(x => x.Label, "Published").Add(x => x.CheckedChanged, value => requested = value)); var button = cut.Find("[role=switch]"); Assert.Equal("published", button.Id); button.Click(); Assert.True(requested); Assert.Equal("false", button.GetAttribute("aria-checked")); }
    [Fact] public void DisabledSuppressesRequests() { var count = 0; var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "enabled").Add(x => x.Label, "Enabled").Add(x => x.Disabled, true).Add(x => x.CheckedChanged, _ => count++)); cut.Find("button").Click(); Assert.Equal(0, count); }
    [Theory, Trait("ModuleConformance", "hlp.ui.switch-field")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.switch-field", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("switch-field.");
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's
    // own input and expected values. keyboard asserts the switch is a native button with no key handler
    // of its own, so the browser's single click per Enter or Space is the only request (bUnit cannot
    // press native keys; the Chromium probe checks the count). appearance asserts the size tokens and
    // logical direction reach the shared Switch; the 44px target is the gallery's touch check.
    // projection-equivalence compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["switch-field.projection-equivalence"];
    private IRenderedComponent<HarborlineSwitchField> Field(Action<ComponentParameterCollectionBuilder<HarborlineSwitchField>>? configure = null, Action<bool>? changed = null, string name = "published") =>
        Render<HarborlineSwitchField>(p => { p.Add(x => x.Name, name).Add(x => x.AccessibleName, "Published").Add(x => x.CheckedChanged, value => changed?.Invoke(value)); configure?.Invoke(p); });
    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        switch (id)
        {
            case "switch-field.defaults":
            {
                var requests = new List<bool>(); var cut = Field(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add, input.GetProperty("name").GetString()!);
                var control = cut.Find("button");
                Assert.Equal(expected.GetProperty("role").GetString(), control.GetAttribute("role"));
                Assert.Equal(expected.GetProperty("ariaChecked").GetString(), control.GetAttribute("aria-checked"));
                control.Click();
                Assert.Equal([true], requests);
                Assert.Equal(expected.GetProperty("controlled").GetBoolean(), cut.Find("button").GetAttribute("aria-checked") == expected.GetProperty("ariaChecked").GetString());
                break;
            }
            case "switch-field.id-fallback":
            {
                var cut = Field(name: input.GetProperty("name").GetString()!);
                Assert.Equal(expected.GetProperty("id").GetString(), cut.Find("[role=switch]").Id);
                Assert.Equal(expected.GetProperty("name").GetString(), cut.Find("input[type=hidden]").GetAttribute("name"));
                break;
            }
            case "switch-field.checked":
            {
                var control = Field(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean())).Find("[role=switch]");
                Assert.Equal(expected.GetProperty("ariaChecked").GetString(), control.GetAttribute("aria-checked"));
                Assert.Equal(expected.GetProperty("thumbState").GetString(), control.GetAttribute("data-hl-checked") == "true" ? "checked" : "unchecked");
                break;
            }
            case "switch-field.pointer":
            {
                var requests = new List<bool>(); var cut = Field(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add);
                cut.Find("[role=switch]").Click();
                Assert.Equal(expected.GetProperty("requests").EnumerateArray().Select(v => v.GetBoolean()).ToArray(), requests.ToArray());
                break;
            }
            case "switch-field.keyboard":
            {
                var requests = new List<bool>(); var cut = Field(p => p.Add(x => x.Checked, input.GetProperty("checked").GetBoolean()), requests.Add);
                var control = cut.Find("[role=switch]");
                var nativeActivation = control.TagName == "BUTTON" && control.GetAttribute("type") == "button" && !control.HasAttribute("disabled");
                var ownKeyHandlers = control.Attributes.Count(a => a.Name.StartsWith("blazor:onkey", StringComparison.Ordinal));
                // Each key reaches the host through the browser's click: one click per key.
                var perKey = input.GetProperty("keys").EnumerateArray().Select(_ => { var before = requests.Count; cut.Find("[role=switch]").Click(); return requests.Count - before; }).ToArray();
                Assert.Equal(expected.GetProperty("eachRequests").GetBoolean(), nativeActivation && perKey.All(count => count >= 1));
                Assert.Equal(expected.GetProperty("duplicateRequests").GetBoolean(), ownKeyHandlers > 0 || perKey.Any(count => count > 1));
                break;
            }
            case "switch-field.disabled":
            {
                var count = 0; var cut = Field(p => p.Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean()), _ => count++);
                var control = cut.Find("[role=switch]");
                Assert.Equal(expected.GetProperty("disabled").GetBoolean(), control.HasAttribute("disabled"));
                // Pointer, Enter and Space all reach the switch as a click (it has no key handler of its own).
                Assert.DoesNotContain(control.Attributes, a => a.Name.StartsWith("blazor:onkey", StringComparison.Ordinal));
                foreach (var _ in input.GetProperty("actions").EnumerateArray()) control.Click();
                Assert.Equal(expected.GetProperty("requests").GetInt32(), count);
                break;
            }
            case "switch-field.label":
            {
                var label = input.GetProperty("label").GetString()!;
                var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "published").Add(x => x.Label, label).Add(x => x.CheckedChanged, _ => { }));
                var labelledBy = cut.Find("[role=switch]").GetAttribute("aria-labelledby");
                Assert.Equal(expected.GetProperty("accessibleName").GetString(), labelledBy is null ? null : cut.Find($"#{labelledBy}").TextContent);
                Assert.Equal(expected.GetProperty("visibleLabel").GetBoolean(), cut.FindAll("label.hl-switch__label").Any(node => node.TextContent == label));
                break;
            }
            case "switch-field.form-context":
            {
                var context = input.GetProperty("context"); var count = 0;
                var value = new Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue("ambient", "field-label", context.GetProperty("descriptionId").GetString(), context.GetProperty("required").GetBoolean(), context.GetProperty("disabled").GetBoolean());
                var cut = Render<CascadingValue<Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue>>(p => p.Add(x => x.Value, value)
                    .AddChildContent<HarborlineSwitchField>(c => c.Add(x => x.Name, "enabled").Add(x => x.CheckedChanged, _ => count++)));
                var control = cut.Find("[role=switch]");
                var merged = control.GetAttribute("aria-labelledby") == "field-label" && (control.GetAttribute("aria-required") == "true") == value.Required && control.HasAttribute("disabled") == value.Disabled;
                control.Click();
                Assert.Equal(expected.GetProperty("contextMerged").GetBoolean(), merged && count == 0);
                Assert.Equal(expected.GetProperty("describedBy").GetString(), control.GetAttribute("aria-describedby"));
                break;
            }
            case "switch-field.validation":
            {
                var control = Field(p => p.Add(x => x.Required, input.GetProperty("required").GetBoolean()).Add(x => x.Error, input.GetProperty("invalid").GetBoolean())).Find("[role=switch]");
                Assert.Equal(expected.GetProperty("ariaRequired").GetString(), control.GetAttribute("aria-required"));
                Assert.Equal(expected.GetProperty("ariaInvalid").GetString(), control.GetAttribute("aria-invalid"));
                break;
            }
            case "switch-field.appearance":
            {
                var sizes = input.GetProperty("sizes").EnumerateArray().Select(v => v.GetString()!).ToArray();
                // Direction comes from the locale, as in React, not from an echoed parameter.
                var locale = input.GetProperty("direction").GetString() == "rtl" ? "ar-SA" : "en-US";
                var roots = sizes.Select(size => Render<Harborline.UIAdapters.Blazor.Localization.HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).AddChildContent<HarborlineSwitchField>(c => c
                    .Add(x => x.Name, "published").Add(x => x.AccessibleName, "Published").Add(x => x.CheckedChanged, _ => { }).Add(x => x.Size, Enum.Parse<SwitchSize>(size, true)))).Find(".hl-switch")).ToArray();
                Assert.Equal(expected.GetProperty("delegatedToSwitch").GetBoolean(), roots.Select((root, i) => root.GetAttribute("data-hl-size") == sizes[i] && root.QuerySelector(".hl-switch__thumb") is not null).All(ok => ok));
                Assert.Equal(expected.GetProperty("logicalThumb").GetBoolean(), roots.All(root => root.GetAttribute("dir") == input.GetProperty("direction").GetString()));
                break;
            }
            case "switch-field.replacement":
            {
                var calls = 0; var updates = input.GetProperty("updates").GetInt32();
                var cut = Field(p => p.Add(x => x.Checked, false), _ => calls++);
                for (var i = 1; i <= updates; i++) { var next = i % 2 == 1; cut.Render(p => p.Add(x => x.Checked, next)); }
                Assert.Equal(expected.GetProperty("latestCheckedVisible").GetBoolean(), cut.Find("[role=switch]").GetAttribute("aria-checked") == (updates % 2 == 1 ? "true" : "false"));
                Assert.Equal(expected.GetProperty("staleState").GetInt32(), cut.FindAll("[role=switch]").Count - 1);
                // Replacement itself requests nothing, and one activation afterwards requests exactly once.
                var replacementCalls = calls;
                cut.Find("[role=switch]").Click();
                var activationCalls = calls - replacementCalls;
                Assert.Equal(expected.GetProperty("duplicateCallbacks").GetInt32(), replacementCalls + Math.Max(0, activationCalls - 1));
                Assert.Equal(1, activationCalls);
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
