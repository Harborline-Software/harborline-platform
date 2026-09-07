using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ErrorCardNativeTests : BunitContext
{
    [Fact]
    public void VariantsMessagesRetryAndHostAttributesPreserveThePublicInterface()
    {
        var activations = 0;
        var page = Render<HarborlineErrorCard>(parameters => parameters
            .Add(component => component.Title, "Page unavailable")
            .Add(component => component.Message, "Check your connection.")
            .Add(component => component.Variant, ErrorCardVariant.Page)
            .Add(component => component.RetryLabel, "Réessayer")
            .Add(component => component.OnRetry, () => activations++)
            .Add(component => component.Class, "consumer")
            .AddUnmatched("lang", "fr-CA")
            .AddUnmatched("dir", "ltr")
            .AddUnmatched("data-case", "native"));

        Assert.Equal("alert", page.Find("[role=alert]").GetAttribute("role"));
        Assert.Equal("Page unavailable", page.Find("h2").TextContent);
        Assert.Equal("Check your connection.", page.Find(".hl-error-card__message").TextContent);
        Assert.Contains("consumer", page.Find("[role=alert]").ClassList);
        Assert.Equal("fr-CA", page.Find("[role=alert]").GetAttribute("lang"));
        page.Find("button").Click();
        Assert.Equal(1, activations);

        var compact = Render<HarborlineErrorCard>(parameters => parameters
            .Add(component => component.Title, "Failed")
            .Add(component => component.Message, string.Empty)
            .Add(component => component.Variant, ErrorCardVariant.Compact));
        Assert.Empty(compact.FindAll("h2"));
        Assert.Empty(compact.FindAll(".hl-error-card__message"));
        Assert.Empty(compact.FindAll("button"));
        Assert.Contains("hl-error-card--compact", compact.Find("[role=alert]").ClassList);
    }

    [Fact]
    public void EmptyTitleFailsClosed()
    {
        var error = Assert.ThrowsAny<Exception>(() => Render<HarborlineErrorCard>());
        Assert.Contains("title-required", error.ToString());
    }
}

public sealed class ErrorCardConformanceTests : BunitContext
{
    [Fact]
    [Trait("ModuleConformance", "hlp.ui.error-card")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw!);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("error-card.", id);

        var expected = fixture.RootElement.GetProperty("expected");
        if (id == "error-card.class-vocabulary")
        {
            // 282 s5: this lane's feedback.css was a hand-written variant of the authority. Nothing
            // but the class spellings ties the two lanes to one stylesheet, so both lanes assert it.
            var card = Render<HarborlineErrorCard>(parameters => parameters
                .Add(component => component.Title, "Unable to load")
                .Add(component => component.Message, "Check your connection.")
                .Add(component => component.OnRetry, () => { }));
            Assert.Equal(Classes(expected, "containerClasses"), card.Find("[role=alert]").ClassList);
            Assert.Equal(Classes(expected, "titleClasses"), card.Find(".hl-error-card__title").ClassList);
            Assert.Equal(Classes(expected, "messageClasses"), card.Find(".hl-error-card__message").ClassList);
            Assert.Equal(Classes(expected, "retryClasses"), card.Find("button").ClassList);
            return;
        }

        var activations = 0;
        var variant = id switch
        {
            "error-card.page" => ErrorCardVariant.Page,
            "error-card.compact" => ErrorCardVariant.Compact,
            _ => ErrorCardVariant.Default,
        };
        var retry = id is "error-card.retry" or "error-card.localized-retry";
        var cut = Render<HarborlineErrorCard>(parameters =>
        {
            parameters.Add(component => component.Title, variant == ErrorCardVariant.Page ? "Page unavailable" : "Unable to load");
            parameters.Add(component => component.Variant, variant);
            if (id == "error-card.message") parameters.Add(component => component.Message, "Check your connection.");
            if (retry)
            {
                parameters.Add(component => component.RetryLabel, id == "error-card.localized-retry" ? "Réessayer" : "Retry");
                parameters.Add(component => component.OnRetry, () => activations++);
            }
            if (id == "error-card.host-attributes")
            {
                parameters.Add(component => component.Class, "consumer");
                parameters.AddUnmatched("dir", "rtl");
                parameters.AddUnmatched("data-case", "shared");
            }
        });

        Assert.Equal("alert", cut.Find("[role=alert]").GetAttribute("role"));
        Assert.Equal(variant == ErrorCardVariant.Page, cut.FindAll("h2").Count == 1);
        Assert.Equal(retry, cut.FindAll("button").Count == 1);
        if (retry)
        {
            cut.Find("button").Click();
            Assert.Equal(1, activations);
        }
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
