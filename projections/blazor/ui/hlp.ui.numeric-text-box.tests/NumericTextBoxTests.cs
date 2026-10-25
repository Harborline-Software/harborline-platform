using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NumericTextBoxTests : BunitContext
{
    [Fact]
    public void ControlledValueFormatsAndStrictCommitRequestsWithoutLocalMutation()
    {
        double? requested = null;
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, 42)
            .Add(component => component.ValueChanged, value => requested = value));
        Assert.Equal("42.00", cut.Find("input").GetAttribute("value"));
        cut.Find("input").Focus(); cut.Find("input").Input("12abc"); cut.Find("input").Blur();
        Assert.Null(requested);
        Assert.Equal("42.00", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void EnterThenBlurCommitsExactlyOnceAndClamps()
    {
        var requests = new List<double?>();
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.DefaultValue, 4).Add(component => component.Min, 2).Add(component => component.Max, 10)
            .Add(component => component.ValueChanged, value => requests.Add(value)));
        cut.Find("input").Focus(); cut.Find("input").Input("20"); cut.Find("input").KeyDown("Enter"); cut.Find("input").Blur();
        Assert.Equal([10d], requests);
    }

    [Fact]
    public void ReadOnlyHidesSpinnersAndNeverEmits()
    {
        var requests = 0;
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters.Add(component => component.Value, 2).Add(component => component.ReadOnly, true).Add(component => component.ValueChanged, _ => requests++));
        Assert.Empty(cut.FindAll("button")); cut.Find("input").Focus(); cut.Find("input").Input("5"); cut.Find("input").Blur(); Assert.Equal(0, requests);
    }

    [Fact]
    public void PseudoEnglishCurrencyUsesEnglishFormattingAcrossHosts()
    {
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, 1234567.89)
            .Add(component => component.Format, "c2")
            .Add(component => component.Currency, "USD")
            .Add(component => component.Locale, "en-XA"));

        Assert.Equal("$1,234,567.89", cut.Find("input").GetAttribute("value"));
    }

    // T-1108. The React lane formats through Intl.NumberFormat, whose default rounding mode is
    // halfExpand: a midpoint rounds away from zero. Every expected string here is what
    // Intl.NumberFormat("en-US") gives for the same value and the React lane's options.
    [Theory]
    [InlineData(1234.5, "c0", 2, "$1,235")]
    [InlineData(-1234.5, "c0", 2, "-$1,235")]
    [InlineData(0.125, "p0", 2, "13%")]
    [InlineData(2.5, null, 0, "3")]
    [InlineData(0.5, null, 0, "1")]
    [InlineData(1.005, "c2", 2, "$1.01")]
    [InlineData(2.675, null, 2, "2.68")]
    public void MidpointsRoundHalfAwayFromZeroLikeIntl(double value, string? format, int decimals, string expected)
    {
        Assert.Equal(expected, Formatted(value, format, decimals));
    }

    // Intl keeps the sign of a negative value that rounds to zero, as this lane's double formatting
    // already did; the half-away-from-zero rounding must not lose it.
    [Theory]
    [InlineData(-0.4, null, 0, "-0")]
    [InlineData(-0.4, "c0", 2, "-$0")]
    [InlineData(-0.0, null, 2, "-0.00")]
    [InlineData(-0.004, "p0", 2, "-0%")]
    public void NegativeValuesRoundingToZeroKeepTheirSignLikeIntl(double value, string? format, int decimals, string expected)
    {
        Assert.Equal(expected, Formatted(value, format, decimals));
    }

    // The rounding starts from the invariant shortest digits, so a culture whose decimal separator is
    // not "." rounds a midpoint the same way. The de-DE strings are Intl.NumberFormat("de-DE"). For ar,
    // the digits and separators depend on the numbering system the ICU data gives it: latn in current
    // CLDR, arab in .NET WebAssembly's ICU 72 (T-1117). The comparison therefore maps arab digits,
    // separators and bidi marks to latn first, and pins only the rounding.
    [Theory]
    [InlineData("de-DE", 1234.5, 0, "1.235")]
    [InlineData("de-DE", -1234.5, 0, "-1.235")]
    [InlineData("de-DE", 2.675, 2, "2,68")]
    [InlineData("de-DE", 1.005, 2, "1,01")]
    [InlineData("ar", 1234.5, 0, "1,235")]
    [InlineData("ar", 2.675, 2, "2.68")]
    public void MidpointsRoundHalfAwayFromZeroInOtherCulturesLikeIntl(string locale, double value, int decimals, string expected)
    {
        Assert.Equal(expected, LatinNumerals(Formatted(value, null, decimals, locale)));
    }

    // T-1108. A spinner starts from current or zero, so an empty value at min 0 is at the bound:
    // Decrement is disabled and neither it nor Arrow Down emits, as in the React lane.
    [Fact]
    public void NullValueAtMinimumDisablesDecrementAndEmitsNothing()
    {
        var requests = new List<double?>();
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, (double?)null)
            .Add(component => component.Min, 0)
            .Add(component => component.ValueChanged, value => requests.Add(value)));

        Assert.True(cut.Find("button[aria-label=Decrement]").HasAttribute("disabled"));
        Assert.False(cut.Find("button[aria-label=Increment]").HasAttribute("disabled"));
        cut.Find("button[aria-label=Decrement]").Click();
        cut.Find("input").KeyDown("ArrowDown");
        Assert.Empty(requests);
    }

    [Fact]
    public void NullValueAtMaximumDisablesIncrementAndEmitsNothing()
    {
        var requests = new List<double?>();
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Max, 0)
            .Add(component => component.ValueChanged, value => requests.Add(value)));

        Assert.True(cut.Find("button[aria-label=Increment]").HasAttribute("disabled"));
        Assert.False(cut.Find("button[aria-label=Decrement]").HasAttribute("disabled"));
        cut.Find("button[aria-label=Increment]").Click();
        cut.Find("input").KeyDown("ArrowUp");
        Assert.Empty(requests);
    }

    // The anchor is current or zero, not "an empty value is at every bound". With zero strictly
    // inside the bounds, both buttons are enabled and each one spins from zero. The value is
    // controlled and stays empty, so every request starts from zero again, as in the React lane.
    [Fact]
    public void NullValueBetweenBoundsSpinsFromTheZeroAnchorBothWays()
    {
        var requests = new List<double?>();
        var cut = Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, (double?)null)
            .Add(component => component.Min, -10)
            .Add(component => component.Max, 10)
            .Add(component => component.ValueChanged, value => requests.Add(value)));

        Assert.False(cut.Find("button[aria-label=Decrement]").HasAttribute("disabled"));
        Assert.False(cut.Find("button[aria-label=Increment]").HasAttribute("disabled"));
        cut.Find("button[aria-label=Decrement]").Click();
        cut.Find("button[aria-label=Increment]").Click();
        cut.Find("input").KeyDown("ArrowDown");
        cut.Find("input").KeyDown("ArrowUp");
        Assert.Equal([-1d, 1d, -1d, 1d], requests);
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.numeric-text-box")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.numeric-text-box", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("numeric-text-box.", fixture.RootElement.GetProperty("id").GetString());
            Assert.Equal("text", Render<HarborlineNumericTextBox>().Find("input").GetAttribute("type"));
            AssertFixtureValues(fixture.RootElement);
        });
    }

    // The value-bearing cases this lane asserts from the fixture's own input. The React lane's
    // conformance suite asserts the same values for the same cases.
    private void AssertFixtureValues(System.Text.Json.JsonElement fixture)
    {
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        switch (fixture.GetProperty("id").GetString())
        {
            case "numeric-text-box.formatting":
            {
                // Intl.NumberFormat("en-US") for the fixture value in each listed format; c0 is a midpoint.
                var intl = new Dictionary<string, string>
                {
                    [""] = "1,234.50", ["c0"] = "$1,235", ["c2"] = "$1,234.50", ["p0"] = "123,450%", ["p2"] = "123,450.00%",
                };
                var value = input.GetProperty("value").GetDouble();
                foreach (var entry in input.GetProperty("formats").EnumerateArray())
                {
                    var format = entry.ValueKind == System.Text.Json.JsonValueKind.Null ? null : entry.GetString();
                    Assert.Equal(intl[format ?? string.Empty], Formatted(value, format, 2));
                }
                break;
            }
            case "numeric-text-box.spinner-null-anchor":
            {
                var requests = new List<double?>();
                var cut = Render<HarborlineNumericTextBox>(parameters => parameters
                    .Add(component => component.Value, (double?)null)
                    .Add(component => component.Min, input.GetProperty("min").GetDouble())
                    .Add(component => component.Step, input.GetProperty("step").GetDouble())
                    .Add(component => component.ValueChanged, value => requests.Add(value)));
                Assert.Equal("increment", input.GetProperty("action").GetString());
                cut.Find("button[aria-label=Increment]").Click();
                Assert.Equal([expected.GetProperty("request").GetDouble()], requests);
                // The zero anchor is at or below min, so Decrement is at its bound.
                Assert.True(cut.Find("button[aria-label=Decrement]").HasAttribute("disabled"));
                cut.Find("button[aria-label=Decrement]").Click();
                Assert.Single(requests);
                break;
            }
            case "numeric-text-box.spinner-bound-disabled":
            {
                var requests = new List<double?>();
                var cut = Render<HarborlineNumericTextBox>(parameters => parameters
                    .Add(component => component.Value, input.GetProperty("value").GetDouble())
                    .Add(component => component.Max, input.GetProperty("max").GetDouble())
                    .Add(component => component.ValueChanged, value => requests.Add(value)));
                Assert.Equal("increment", input.GetProperty("action").GetString());
                Assert.Equal(expected.GetProperty("disabled").GetBoolean(), cut.Find("button[aria-label=Increment]").HasAttribute("disabled"));
                cut.Find("button[aria-label=Increment]").Click();
                Assert.Equal(expected.GetProperty("requests").GetInt32(), requests.Count);
                break;
            }
            case "numeric-text-box.spinner-step":
            {
                var requests = new List<double?>();
                var cut = Render<HarborlineNumericTextBox>(parameters => parameters
                    .Add(component => component.Value, input.GetProperty("value").GetDouble())
                    .Add(component => component.Step, input.GetProperty("step").GetDouble())
                    .Add(component => component.ValueChanged, value => requests.Add(value)));
                foreach (var action in input.GetProperty("actions").EnumerateArray())
                    cut.Find(action.GetString() == "increment" ? "button[aria-label=Increment]" : "button[aria-label=Decrement]").Click();
                Assert.Equal(expected.GetProperty("requests").EnumerateArray().Select(entry => (double?)entry.GetDouble()), requests);
                break;
            }
        }
    }

    private string? Formatted(double value, string? format, int decimals, string locale = "en-US") => Render<HarborlineNumericTextBox>(parameters => parameters
            .Add(component => component.Value, value)
            .Add(component => component.Format, format)
            .Add(component => component.Decimals, decimals)
            .Add(component => component.Currency, "USD")
            .Add(component => component.Locale, locale))
        .Find("input").GetAttribute("value");

    // Arabic-Indic digits and the Arabic decimal and group separators as their latn forms, with
    // the bidi marks dropped (T-1117).
    private static string? LatinNumerals(string? text) => text is null ? null : string.Concat(text
        .Where(character => character is not ('‎' or '‏' or '؜'))
        .Select(character => character switch
        {
            >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
            '٫' => '.',
            '٬' => ',',
            _ => character,
        }));
}
