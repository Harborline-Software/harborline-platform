using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Localization;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class GuardedControlTests : BunitContext
{
    public GuardedControlTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    private IRenderedComponent<HarborlineGuardedControl> RenderGuard(Action? commit=null,Action<GuardedControlEvent>? observe=null)=>Render<HarborlineGuardedControl>(p=>p.Add(x=>x.Action,"packages.export").Add(x=>x.ClassificationId,"new-record-type").Add(x=>x.Label,"Sign and save").Add(x=>x.OnCommit,()=>commit?.Invoke()).Add(x=>x.OnEvent,e=>observe?.Invoke(e)));
    [Fact] public void RequiresTwoDistinctActivations(){var commits=0;var cut=RenderGuard(()=>commits++);cut.Find("button").Click();Assert.Equal(GuardedControlState.Armed,cut.Instance.State);Assert.Equal(0,commits);cut.Find("button").Click();Assert.Equal(1,commits);Assert.Equal(GuardedControlState.Covered,cut.Instance.State);}
    [Fact] public async Task EscapeRecoversOnce(){var events=new List<GuardedControlEvent>();var cut=RenderGuard(observe:events.Add);cut.Find("button").Click();await cut.Find("button").KeyDownAsync(new KeyboardEventArgs{Key="Escape"});Assert.Equal(GuardedControlState.Covered,cut.Instance.State);Assert.Single(events.OfType<GuardedControlRecovered>());}
    [Fact] public void DisabledReasonIsAssociated(){var cut=Render<HarborlineGuardedControl>(p=>p.Add(x=>x.Action,"x").Add(x=>x.ClassificationId,"c").Add(x=>x.Label,"Delete").Add(x=>x.Disabled,true).Add(x=>x.DescribedBy,"denial").Add(x=>x.OnCommit,()=>{}));var button=cut.Find("button");Assert.True(button.HasAttribute("disabled"));Assert.Equal($"denial {cut.Find(".hl-guarded-control__instruction").Id}",button.GetAttribute("aria-describedby"));}
    [Theory,Trait("ModuleConformance","hlp.ui.guarded-control")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.guarded-control", MemberType = typeof(SharedFixtureBatch))]
    public Task SharedFixtureConforms(string caseId, string? rawFixture) =>
        SharedFixtureBatch.RunAsync(caseId, rawFixture, async () => {
            AssertFixturePrefix("guarded-control.");Assert.NotNull(RenderGuard());
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = JsonDocument.Parse(raw);
            await AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });

    // Revision 2 shared cases run the production component against the fixture's own input and expected
    // values, as the React lane's GuardedControl.fixtures.test.tsx does, so both lanes are held to the same
    // names and descriptions. `disabled` is asserted too, because revision 2 changes what its describedBy
    // carries. The other revision-1 behaviour cases are asserted by name in the native suite, not per case
    // here (the shared runner's case-addressability follow-up, ticket 081). projection-equivalence compares
    // lanes. An unknown case id fails.
    private static readonly string[] Unasserted =
    [
        "guarded-control.initial-covered", "guarded-control.first-activation-arms", "guarded-control.pointer-two-step",
        "guarded-control.keyboard-two-step", "guarded-control.commit-once", "guarded-control.committing-inert",
        "guarded-control.commit-completed", "guarded-control.commit-rejected", "guarded-control.timeout-recovery",
        "guarded-control.escape-recovery", "guarded-control.host-recovery", "guarded-control.recovery-race",
        "guarded-control.became-disabled", "guarded-control.focus", "guarded-control.disposal",
        "guarded-control.authorization-boundary", "guarded-control.localization", "guarded-control.rtl-motion",
        "guarded-control.host-attributes", "guarded-control.invalid-input", "guarded-control.projection-equivalence",
    ];
    private static string[] Strings(JsonElement value) => value.EnumerateArray().Select(v => v.GetString()!).ToArray();
    private IRenderedComponent<HarborlineGuardedControl> Guard(string label, Func<Task> commit, Action<ComponentParameterCollectionBuilder<HarborlineGuardedControl>>? configure = null) =>
        Render<HarborlineGuardedControl>(p => { p.Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type").Add(x => x.Label, label).Add(x => x.OnCommit, commit); configure?.Invoke(p); });
    // One activation arms; a second, with a commit that has not settled, holds the committing state.
    private static void Reach(Func<AngleSharp.Dom.IElement> action, string state)
    {
        if (state == "covered") return;
        action().Click();
        if (state == "committing") _ = action().ClickAsync(new MouseEventArgs());
        Assert.Equal(state, action().GetAttribute("data-guard-state"));
    }
    private IRenderedComponent<HarborlineGuardedControl> RenderVariant(JsonElement variant, Func<Task> commit) => RenderVariant(this, variant, commit);
    private static IRenderedComponent<HarborlineGuardedControl> RenderVariant(BunitContext context, JsonElement variant, Func<Task> commit) =>
        context.Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, variant.GetProperty("locale").GetString()!)
            .Add(x => x.Catalog, variant.GetProperty("catalog").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!))
            .Add(x => x.NumberFormatter, variant.TryGetProperty("numberingSystem", out var system) ? GuardedControlAnnouncementTests.HostFormatter(system.GetString()!) : null)
            .AddChildContent<HarborlineGuardedControl>(guard => guard
                .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
                .Add(x => x.Label, variant.GetProperty("label").GetString()!).Add(x => x.OnCommit, commit)))
        .FindComponent<HarborlineGuardedControl>();
    private async Task AssertCase(string id, JsonElement input, JsonElement expected)
    {
        var pending = new TaskCompletionSource();
        switch (id)
        {
            case "guarded-control.disabled":
            {
                var describedBy = Strings(input.GetProperty("describedBy"));
                var commits = 0;
                var cut = Guard("Delete", () => { commits++; return Task.CompletedTask; }, p => p.Add(x => x.Disabled, input.GetProperty("disabled").GetBoolean()).Add(x => x.DescribedBy, string.Join(' ', describedBy)));
                cut.Find("button").Click();
                Assert.Equal(expected.GetProperty("nativeDisabled").GetBoolean(), cut.Find("button").HasAttribute("disabled"));
                Assert.Equal(expected.GetProperty("state").GetString(), cut.Find("button").GetAttribute("data-guard-state"));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(), commits);
                var ids = (cut.Find("button").GetAttribute("aria-describedby") ?? string.Empty).Split(' ');
                Assert.Equal(expected.GetProperty("descriptionAssociated").GetBoolean(), describedBy.All(ids.Contains));
                break;
            }
            case "guarded-control.name-is-label":
            {
                var states = Strings(input.GetProperty("states"));
                var names = Strings(expected.GetProperty("names"));
                for (var index = 0; index < states.Length; index++)
                {
                    var cut = Guard(input.GetProperty("label").GetString()!, () => pending.Task);
                    Reach(() => cut.Find("button"), states[index]);
                    var name = GuardAccessibility.Name(cut.Find("button"), cut);
                    Assert.Equal(names[index], name);
                    // The countdown is never part of the name, whatever second it shows.
                    if (cut.FindAll("[role=timer]") is { Count: > 0 } timers && timers[0] is var timer)
                        Assert.Equal(expected.GetProperty("timerInName").GetBoolean(), name.Contains(timer.TextContent, StringComparison.Ordinal));
                }
                break;
            }
            case "guarded-control.description-instruction":
            {
                var states = Strings(input.GetProperty("states"));
                var note = input.GetProperty("describedBy");
                var callerId = note.GetProperty("id").GetString()!;
                for (var index = 0; index < states.Length; index++)
                {
                    var host = Render(GuardedControlNamingTests.WithCallerNote(callerId, note.GetProperty("text").GetString()!, input.GetProperty("label").GetString()!, commit: () => pending.Task));
                    Reach(() => host.Find("button"), states[index]);
                    Assert.Equal(Strings(expected.GetProperty("descriptions"))[index], GuardAccessibility.Description(host.Find("button"), host));
                    var instruction = host.FindAll(".hl-guarded-control__instruction") is { Count: > 0 } found ? found[0] : null;
                    var order = (host.Find("button").GetAttribute("aria-describedby") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(token => token == callerId ? "caller" : token == instruction?.Id ? "instruction" : token).ToArray();
                    Assert.Equal(Strings(expected.GetProperty("describedByOrder")), order);
                    Assert.Equal(Strings(expected.GetProperty("instructions"))[index], instruction?.TextContent);
                    Assert.Equal(expected.GetProperty("instructionVisible").GetBoolean(), instruction is not null && instruction.Closest("[aria-hidden=true], [hidden], .hl-visually-hidden") is null);
                }
                break;
            }
            case "guarded-control.description-stable":
            {
                var clock = new GuardedControlAnnouncementTests.ManualClock();
                var cut = Guard(input.GetProperty("label").GetString()!, () => Task.CompletedTask);
                GuardedControlAnnouncementTests.UseClock(cut.Instance, clock);
                var statuses = new List<string>();
                cut.Find("button").Click();
                var elapsed = 0;
                var seconds = input.GetProperty("elapsedSeconds").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                for (var index = 0; index < seconds.Length; index++)
                {
                    for (; elapsed < seconds[index]; elapsed++) await GuardedControlAnnouncementTests.TickAsync(cut, clock);
                    var timer = Strings(expected.GetProperty("timers"))[index];
                    cut.WaitForAssertion(() => Assert.Equal(timer, cut.Find("[role=timer]").TextContent));
                    statuses.Add(cut.Find("[role=status]").TextContent);
                    Assert.Equal(Strings(expected.GetProperty("names"))[index], GuardAccessibility.Name(cut.Find("button"), cut));
                    Assert.Equal(Strings(expected.GetProperty("descriptions"))[index], GuardAccessibility.Description(cut.Find("button"), cut));
                }
                for (; elapsed < 5; elapsed++) await GuardedControlAnnouncementTests.TickAsync(cut, clock);
                cut.WaitForAssertion(() => Assert.Equal("covered", cut.Find("button").GetAttribute("data-guard-state")));
                statuses.Add(cut.Find("[role=status]").TextContent);
                var after = expected.GetProperty("afterWindow");
                Assert.Equal(after.GetProperty("description").GetString(), GuardAccessibility.Description(cut.Find("button"), cut));
                Assert.Equal(after.GetProperty("status").GetString(), cut.Find("[role=status]").TextContent);
                Assert.Equal(Strings(expected.GetProperty("statusSequence")), statuses.Where((value, index) => index == 0 || value != statuses[index - 1]).ToArray());
                break;
            }
            // Every variant asserts the name and the covered and committing descriptions. A description the
            // variant leaves out must be named in knownLimitations with its ticket, or the case fails: the armed
            // description in Arabic and the pseudo-locale is T-1105's deferred duration-localization parity.
            case "guarded-control.naming-localization":
            {
                var variants = input.GetProperty("variants").EnumerateArray().ToArray();
                var wanted = expected.GetProperty("variants").EnumerateArray().ToArray();
                for (var variant = 0; variant < variants.Length; variant++)
                {
                    var given = variants[variant];
                    var want = wanted[variant];
                    var limitations = want.TryGetProperty("knownLimitations", out var named)
                        ? named.EnumerateArray().Where(item => item.GetProperty("ticket").GetString() == "T-1105").Select(item => item.GetProperty("assertion").GetString()).ToArray()
                        : [];
                    foreach (var state in new[] { "covered", "armed", "committing" })
                    {
                        var cut = RenderVariant(given, () => pending.Task);
                        Reach(() => cut.Find("button"), state);
                        var context = $"{given.GetProperty("name").GetString()} {state}";
                        Assert.True(want.GetProperty("direction").GetString() == cut.Find(".hl-guarded-control").GetAttribute("dir"), context);
                        var name = GuardAccessibility.Name(cut.Find("button"), cut);
                        Assert.True(want.GetProperty("names").GetProperty(state).GetString() == name, $"{context} name: {name}");
                        if (want.GetProperty("descriptions").TryGetProperty(state, out var description))
                        {
                            var actual = GuardAccessibility.Description(cut.Find("button"), cut);
                            Assert.True(description.GetString() == actual, $"{context} description: {actual}");
                            Assert.True(description.GetString() == cut.Find(".hl-guarded-control__instruction").TextContent, $"{context} visible instruction");
                        }
                        else Assert.True(limitations.Contains($"descriptions.{state}"), $"{context}: description neither asserted nor a named T-1105 limitation");
                        if (state == "committing")
                        {
                            var loading = cut.Find("button > span:last-child").TextContent;
                            Assert.True(want.GetProperty("loading").GetString() == loading, $"{context} loading: {loading}");
                        }
                    }
                    // Every message takes the same {action} and {duration} (the full window), so the covered
                    // description after a timeout recovery reads exactly as it did before arming.
                    var clock = new GuardedControlAnnouncementTests.ManualClock();
                    var timed = RenderVariant(given, () => Task.CompletedTask);
                    GuardedControlAnnouncementTests.UseClock(timed.Instance, clock);
                    timed.Find("button").Click();
                    for (var second = 0; second < 5; second++) await GuardedControlAnnouncementTests.TickAsync(timed, clock);
                    timed.WaitForAssertion(() => Assert.Equal("covered", timed.Find("button").GetAttribute("data-guard-state")));
                    var afterTimeout = GuardAccessibility.Description(timed.Find("button"), timed);
                    Assert.True(want.GetProperty("afterTimeout").GetProperty("description").GetString() == afterTimeout, $"{given.GetProperty("name").GetString()} after timeout description: {afterTimeout}");
                    var recovered = timed.Find("[role=status]").TextContent;
                    Assert.True(want.GetProperty("afterTimeout").GetProperty("status").GetString() == recovered, $"{given.GetProperty("name").GetString()} recovered status: {recovered}");
                    // Settlement: the completed and rejected statuses. Each runs in its own context, because the
                    // rejected commit's failure is routed to that renderer's unhandled-exception path.
                    foreach (var outcome in new[] { "completed", "rejected" })
                    {
                        using var isolated = new BunitContext();
                        isolated.JSInterop.Mode = JSRuntimeMode.Loose;
                        var operation = new TaskCompletionSource();
                        var settled = RenderVariant(isolated, given, () => operation.Task);
                        settled.Find("button").Click();
                        var commit = settled.Find("button").ClickAsync(new MouseEventArgs());
                        if (outcome == "completed") operation.SetResult();
                        else
                        {
                            operation.SetException(new InvalidOperationException("caller failure"));
                            await isolated.Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(5));
                        }
                        await commit;
                        var status = settled.Find("[role=status]").TextContent;
                        Assert.True(want.GetProperty("statuses").GetProperty(outcome).GetString() == status, $"{given.GetProperty("name").GetString()} {outcome} status: {status}");
                    }
                }
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
        pending.TrySetResult();
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
