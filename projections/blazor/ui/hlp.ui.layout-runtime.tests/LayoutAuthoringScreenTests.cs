using Bunit;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LayoutAuthoringScreenTests : BunitContext
{
    private readonly FakeMedia media = new(false);

    public LayoutAuthoringScreenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMediaQueryObserver>(media);
    }

    // Oracle: DES-0052:183,533 and HarborlineAppLayout.razor:11,23,72; expectations are literals.
    // Named mutations: BZ-second-main wraps the editor in another main; BZ-drop-scroll drops ContentScroll forwarding.
    [Fact(DisplayName = "layout-eng-11: the screen keeps one main around the editor and forwards the declared scroll owner")]
    [Trait("Holds", "layout-eng-11")]
    public void ScreenKeepsOneMainAndForwardsContentScroll()
    {
        var cut = Render<HarborlineLayoutAuthoringScreen>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty)
            .Add(x => x.Catalogue, new LayoutAuthoringCatalogue([new("layout.table", "Table")], [])));

        var main = Assert.Single(cut.FindAll("main"));
        Assert.NotNull(main.QuerySelector("form.hl-layout-authoring"));
        Assert.Equal("main", Assert.Single(cut.FindAll(".hl-app-layout")).GetAttribute("data-content-scroll"));

        cut.Render(parameters => parameters.Add(x => x.ContentScroll, AppContentScroll.Page));

        main = Assert.Single(cut.FindAll("main"));
        Assert.NotNull(main.QuerySelector("form.hl-layout-authoring"));
        Assert.Equal("page", Assert.Single(cut.FindAll(".hl-app-layout")).GetAttribute("data-content-scroll"));
    }

    private sealed class FakeMedia(bool initial) : IMediaQueryObserver
    {
        private Func<MediaQueryChange, ValueTask>? callback;
        private readonly FakeSubscription subscription = new(initial);
        public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
        {
            callback = onChanged;
            subscription.QueryValue = query;
            return new(subscription);
        }
        public async Task SetAsync(bool value)
        {
            subscription.MatchesValue = value;
            if (callback is not null) await callback(new(subscription.Query, value));
        }
        private sealed class FakeSubscription(bool matches) : IMediaQuerySubscription
        {
            public string QueryValue = "";
            public bool MatchesValue = matches;
            public string Query => QueryValue;
            public bool Matches => MatchesValue;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
