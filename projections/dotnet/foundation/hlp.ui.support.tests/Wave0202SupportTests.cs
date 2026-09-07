using Harborline.Foundation.Builder;
using Harborline.Foundation.Responsive;
using Harborline.Foundation.Theming;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class Wave0202SupportTests
{
    [Fact]
    public void ToneMappingsUseOnlyAuthoredSemanticVariables()
    {
        foreach (var tone in Enum.GetValues<LensTone>())
        {
            var style = ToneStyles.Resolve(tone);
            Assert.All(new[] { style.Swatch, style.Border, style.SoftBackground, style.Text },
                value => Assert.Matches("^var\\(--color-[a-z-]+\\)$", value));
        }
        Assert.Equal(ToneStyles.Resolve(LensTone.Muted), ToneStyles.Resolve(LensTone.SensitivityNone));
        Assert.Equal("var(--color-secondary)", ToneStyles.Resolve(LensTone.Info).Swatch);
    }

    [Fact]
    public void FormFactorPolicyPreservesShortLandscapeAndCapabilityRules()
    {
        var shortLandscape = FormFactorPolicy.Resolve(new(DesktopWidth: true, Landscape: true, ShortHeight: true));
        Assert.Equal(FormFactorMode.Phone, shortLandscape.Mode);
        Assert.False(shortLandscape.CanShowMasterDetail);
        Assert.True(shortLandscape.TouchSizing);

        var hybrid = FormFactorPolicy.Resolve(new(DesktopWidth: true, AnyCoarsePointer: true, AnyFinePointer: true));
        Assert.Equal(FormFactorMode.Desktop, hybrid.Mode);
        Assert.True(hybrid.TouchSizing);
        Assert.True(hybrid.ShowHoverAffordance);

        // Master-detail requires the shared rail query, not merely a non-phone mode:
        // the 768x550 class is tablet mode yet below the rail's 600px floor (ticket 154).
        Assert.False(FormFactorPolicy.Resolve(new()).CanShowMasterDetail);
        Assert.True(FormFactorPolicy.Resolve(new(MasterDetailRail: true)).CanShowMasterDetail);
        Assert.False(FormFactorPolicy.Resolve(new(PhoneWidth: true, MasterDetailRail: true)).CanShowMasterDetail);
    }

    [Fact]
    public void BreakpointIdentityPreservesTheHeldDockException()
    {
        Assert.Equal("(max-width: 767px)", BreakpointQueries.Phone);
        Assert.Equal("(min-width: 768px) and (min-height: 600px)", BreakpointQueries.CanShowRail);
        Assert.Equal("(min-width: 1280px)", BreakpointQueries.Dock);
        Assert.Equal(FormFactorQueries.Phone, BreakpointQueries.Phone);
        Assert.Equal(FormFactorQueries.MasterDetailRail, BreakpointQueries.CanShowRail);
    }

    [Fact]
    public void RailThresholdsExceedThePhoneAndShortHeightBands()
    {
        // Ticket 154 review: the constant-bound gates (FormFactorQueries / BreakpointQueries /
        // the Blazor components) equal the full policy (mode != phone AND rail) only because the
        // rail's floors clear the phone-width and short-height ceilings. Pin that arithmetic so
        // retuning phone/shortHeight/rail breaks a NAMED test instead of silently splitting the
        // policy from the raw-query gates.
        Assert.True(QueryBound(FormFactorQueries.MasterDetailRail, "min-width") > QueryBound(FormFactorQueries.Phone, "max-width"));
        Assert.True(QueryBound(FormFactorQueries.MasterDetailRail, "min-height") > QueryBound(FormFactorQueries.ShortHeight, "max-height"));
    }

    private static int QueryBound(string query, string dimension)
    {
        var match = System.Text.RegularExpressions.Regex.Match(query, $@"\({dimension}:\s*(\d+)px\)");
        Assert.True(match.Success, $"query '{query}' carries no {dimension} bound");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.tone-style")]
    public void ToneStyleSharedFixturesRemainBoundToThePublicDotnetSurface() =>
        AssertSharedFixture("tone-style.", "hlp.ui.tone-style");

    [Fact, Trait("ModuleConformance", "hlp.ui.use-can-show-master-detail")]
    public void FormFactorSharedFixturesRemainBoundToThePublicDotnetSurface()
    {
        using var fixture = Fixture.Read("form-factor.");
        if (fixture is null) return;
        var root = fixture.RootElement;
        string id = root.GetProperty("id").GetString()!;
        Assert.Contains(id, Fixture.CaseIds("hlp.ui.use-can-show-master-detail"));
        Assert.Equal("Harborline.Foundation", typeof(FormFactorPolicy).Assembly.GetName().Name);

        // Ticket 154 review: EXECUTE the fixture — drive the case's inputs through
        // FormFactorPolicy.Resolve and compare against its expected values, so deleting a
        // policy conjunct fails this lane instead of leaving everything green.
        var input = root.GetProperty("input");
        var expected = root.GetProperty("expected");
        switch (id)
        {
            case "form-factor.phone-width":
            case "form-factor.tablet-residual":
            case "form-factor.desktop-width":
            case "form-factor.short-landscape-fold":
                AssertResolvedShape(FormFactorPolicy.Resolve(ParseSignals(input)), expected);
                break;
            case "form-factor.master-detail":
                AssertCaseValues(input, expected, resolved => resolved.CanShowMasterDetail);
                break;
            case "form-factor.touch-sizing":
                AssertCaseValues(input, expected, resolved => resolved.TouchSizing);
                break;
            case "form-factor.hover-affordance":
                AssertCaseValues(input, expected, resolved => resolved.ShowHoverAffordance);
                break;
            case "form-factor.split-builder":
            {
                Assert.Equal(FormFactorQueries.CanSplitBuilderPanes, input.GetProperty("query").GetString());
                var resolved = FormFactorPolicy.Resolve(new(CanSplitBuilderPanes: input.GetProperty("matches").GetBoolean()));
                Assert.Equal(expected.GetProperty("value").GetBoolean(), resolved.CanSplitBuilderPanes);
                break;
            }
            case "form-factor.ssr-fail-closed":
            {
                // No browser: every signal defaults false — resolving the default record IS the SSR answer.
                var resolved = FormFactorPolicy.Resolve(new());
                Assert.Equal(expected.GetProperty("mode").GetString(), resolved.Mode.ToString().ToLowerInvariant());
                Assert.Equal(expected.GetProperty("masterDetail").GetBoolean(), resolved.CanShowMasterDetail);
                Assert.Equal(expected.GetProperty("touchSizing").GetBoolean(), resolved.TouchSizing);
                Assert.Equal(expected.GetProperty("hoverAffordance").GetBoolean(), resolved.ShowHoverAffordance);
                Assert.Equal(expected.GetProperty("splitBuilder").GetBoolean(), resolved.CanSplitBuilderPanes);
                break;
            }
            case "form-factor.projection-equivalence":
            {
                // Totality over every Boolean signal combination (the TS suite iterates the same mask;
                // outcome equivalence is proven by the value-carrying cases above resolving identically).
                for (int mask = 0; mask < 256; mask++)
                {
                    _ = FormFactorPolicy.Resolve(new(
                        PhoneWidth: (mask & 1) != 0, DesktopWidth: (mask & 2) != 0, Landscape: (mask & 4) != 0,
                        ShortHeight: (mask & 8) != 0, AnyCoarsePointer: (mask & 16) != 0, AnyFinePointer: (mask & 32) != 0,
                        Hover: (mask & 64) != 0, MasterDetailRail: (mask & 128) != 0));
                }
                Assert.True(expected.GetProperty("typescriptEqualsDotnet").GetBoolean());
                break;
            }
            default:
                Assert.Fail($"form-factor fixture '{id}' has no dotnet executor — add one before extending the fixture set.");
                break;
        }
    }

    private static FormFactorSignals ParseSignals(System.Text.Json.JsonElement input) => new(
        PhoneWidth: ReadFlag(input, "phoneWidth"),
        DesktopWidth: ReadFlag(input, "desktopWidth"),
        Landscape: ReadFlag(input, "landscape"),
        ShortHeight: ReadFlag(input, "shortHeight"),
        AnyCoarsePointer: ReadFlag(input, "anyCoarse"),
        AnyFinePointer: ReadFlag(input, "anyFine"),
        Hover: ReadFlag(input, "hover"),
        MasterDetailRail: ReadFlag(input, "masterDetailRail"));

    private static bool ReadFlag(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.GetBoolean();

    private static void AssertResolvedShape(ResolvedFormFactor resolved, System.Text.Json.JsonElement expected)
    {
        Assert.Equal(expected.GetProperty("mode").GetString(), resolved.Mode.ToString().ToLowerInvariant());
        Assert.Equal(expected.GetProperty("orientation").GetString(), resolved.Orientation.ToString().ToLowerInvariant());
        Assert.Equal(expected.GetProperty("heightClass").GetString(), resolved.HeightClass.ToString().ToLowerInvariant());
    }

    private static void AssertCaseValues(
        System.Text.Json.JsonElement input,
        System.Text.Json.JsonElement expected,
        Func<ResolvedFormFactor, bool> read)
    {
        var actual = input.GetProperty("cases").EnumerateArray()
            .Select(signals => read(FormFactorPolicy.Resolve(ParseSignals(signals))))
            .ToArray();
        var wanted = expected.GetProperty("values").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
        Assert.Equal(wanted, actual);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.use-is-mobile")]
    public void BreakpointSharedFixturesRemainBoundToThePublicDotnetSurface() =>
        AssertSharedFixture("breakpoint.", "hlp.ui.use-is-mobile");

    private static void AssertSharedFixture(string prefix, string moduleId)
    {
        using var fixture = Fixture.Read(prefix);
        if (fixture is null) return;
        Assert.Contains(fixture.RootElement.GetProperty("id").GetString(), Fixture.CaseIds(moduleId));
        Assert.Equal("Harborline.Foundation", typeof(FormFactorPolicy).Assembly.GetName().Name);
    }
}
