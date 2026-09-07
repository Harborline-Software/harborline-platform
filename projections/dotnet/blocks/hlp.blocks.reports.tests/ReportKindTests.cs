using System;
using System.Linq;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ReportKindTests
{
    [Fact]
    public void ReportKind_HasAtLeastFiveMvpMembers()
    {
        var members = Enum.GetValues<ReportKind>();
        Assert.True(members.Length >= 5, $"Expected ≥5 MVP cartridge kinds, got {members.Length}.");
        Assert.Contains(ReportKind.TrialBalance, members);
        Assert.Contains(ReportKind.ArAgingSummary, members);
        Assert.Contains(ReportKind.ApAgingSummary, members);
        Assert.Contains(ReportKind.ProfitAndLossByProperty, members);
        Assert.Contains(ReportKind.RentRoll, members);
    }
    // These become persisted report-definition config keys in ticket 073
    // and MUST NOT change silently.
    [Theory]
    [InlineData(ReportKind.TrialBalance, "trial-balance")]
    [InlineData(ReportKind.ArAgingSummary, "ar-aging-summary")]
    [InlineData(ReportKind.ApAgingSummary, "ap-aging-summary")]
    [InlineData(ReportKind.ProfitAndLossByProperty, "profit-and-loss-by-property")]
    [InlineData(ReportKind.RentRoll, "rent-roll")]
    [InlineData(ReportKind.BalanceSheet, "balance-sheet")]
    [InlineData(ReportKind.ProfitAndLoss, "profit-and-loss")]
    public void ToKebab_PersistedTicket073Key_IsPinned(ReportKind kind, string expected)
        => Assert.Equal(expected, kind.ToKebab());

    [Fact]
    public void ToKebab_PersistedTicket073Keys_AreUnique()
    {
        var members = Enum.GetValues<ReportKind>();
        Assert.Equal(members.Length,
            members.Select(k => k.ToKebab()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ReportKindExtensions_ToKebab_AllMembersMapped()
    {
        foreach (var k in Enum.GetValues<ReportKind>())
        {
            // Must not throw.
            var kebab = k.ToKebab();
            Assert.False(string.IsNullOrEmpty(kebab), $"{k} produced empty kebab string.");
        }
    }

    [Fact]
    public void ReportKindExtensions_ToKebab_ProducesLowercaseKebabIdentifiers()
    {
        foreach (var k in Enum.GetValues<ReportKind>())
        {
            var kebab = k.ToKebab();
            Assert.Equal(kebab.ToLowerInvariant(), kebab);
            Assert.DoesNotContain(' ', kebab);
            Assert.DoesNotContain('_', kebab);
            // Must contain only [a-z0-9-]
            Assert.All(kebab, c => Assert.True(
                (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-',
                $"{k} kebab '{kebab}' contains invalid char '{c}'"));
        }
    }
}
