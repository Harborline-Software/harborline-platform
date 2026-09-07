using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class EmptyStateNativeTestsTierCPerformance : BunitContext
{
    private const int MaximumFixtureTextLength = 4_096;
    private const int UpdateCycles = 96;
    private const int MaximumRenderedElements = 8;
    private const int MaximumStructuralMarkupLength = 1_024;

    [Fact]
    public void MaximumSizeStateChangesKeepMarkupAndActionWorkBounded()
    {
        var activations = new int[UpdateCycles];
        var cut = Render<HarborlineEmptyState>(parameters => parameters
            .Add(component => component.Title, FixtureText("title-0"))
            .Add(component => component.Description, FixtureText("description-0"))
            .Add(component => component.Variant, EmptyStateVariant.Positive)
            .Add(component => component.ActionLabel, "Continue")
            .Add(component => component.OnAction, () => activations[0]++));

        for (var cycle = 0; cycle < UpdateCycles; cycle++)
        {
            var currentCycle = cycle;
            cut.Render(parameters => parameters
                .Add(component => component.Title, FixtureText($"title-{currentCycle}"))
                .Add(component => component.Description, FixtureText($"description-{currentCycle}"))
                .Add(component => component.Variant, (currentCycle % 3) switch
                {
                    0 => EmptyStateVariant.Positive,
                    1 => EmptyStateVariant.Actionable,
                    _ => EmptyStateVariant.Informational,
                })
                .Add(component => component.ActionLabel, "Continue")
                .Add(component => component.OnAction, () => activations[currentCycle]++));

            Assert.Single(cut.FindAll(".hl-empty-state"));
            Assert.Single(cut.FindAll(".hl-empty-state__icon"));
            Assert.Single(cut.FindAll(".hl-empty-state__title"));
            Assert.Single(cut.FindAll(".hl-empty-state__description"));
            Assert.Single(cut.FindAll(".hl-empty-state__action"));
            var descendantCount = cut.Find(".hl-empty-state").QuerySelectorAll("*").Length;
            Assert.True(descendantCount <= MaximumRenderedElements - 1,
                $"Expected at most {MaximumRenderedElements - 1} descendants, found {descendantCount}: "
                + string.Join(", ", cut.Find(".hl-empty-state").QuerySelectorAll("*").Select(element => element.TagName)));
            Assert.True(cut.Markup.Length <=
                (MaximumFixtureTextLength * 2) + MaximumStructuralMarkupLength);
        }

        cut.Find("button").Click();

        Assert.All(activations[..^1], count => Assert.Equal(0, count));
        Assert.Equal(1, activations[^1]);
        Assert.Empty(cut.FindAll("[role=status]"));
        Assert.Empty(cut.FindAll("[aria-live]"));
    }

    private static string FixtureText(string prefix) =>
        prefix.PadRight(MaximumFixtureTextLength, 'x')[..MaximumFixtureTextLength];
}
