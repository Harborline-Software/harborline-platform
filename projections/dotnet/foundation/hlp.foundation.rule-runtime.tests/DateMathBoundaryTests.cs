using Harborline.Foundation.RuleEngine.Evaluation;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

public sealed class DateMathBoundaryTests
{
    [Theory]
    [InlineData(1969, 12, 31, -1L)]
    [InlineData(1970, 1, 1, 0L)]
    [InlineData(1970, 1, 2, 1L)]
    [InlineData(1970, 3, 1, 59L)]
    [InlineData(2000, 1, 1, 10957L)]
    public void EpochDay_matches_literal_Unix_epoch_anchors(int year, int month, int day, long expected)
    {
        Assert.Equal(expected, DateMath.EpochDay(year, month, day));
    }

    [Theory]
    [InlineData(-1L, 1969, 12, 31)]
    [InlineData(0L, 1970, 1, 1)]
    [InlineData(1L, 1970, 1, 2)]
    [InlineData(59L, 1970, 3, 1)]
    [InlineData(10957L, 2000, 1, 1)]
    public void FromEpochDay_matches_literal_civil_date_anchors(long day, int year, int month, int dayOfMonth)
    {
        Assert.Equal((year, month, dayOfMonth), DateMath.FromEpochDay(day));
    }

    [Theory]
    [InlineData("1899-12-31", 60L, "1900-03-01")]
    [InlineData("1999-12-31", 60L, "2000-02-29")]
    [InlineData("2099-12-31", 60L, "2100-03-01")]
    public void Add_days_observes_Gregorian_century_leap_rules(string date, long days, string expected)
    {
        Assert.Equal(expected, DateMath.Add(date, days, "day"));
    }

    [Theory]
    [InlineData("1900-01-31", 1L, "1900-02-28")]
    [InlineData("2000-01-31", 1L, "2000-02-29")]
    [InlineData("2100-01-31", 1L, "2100-02-28")]
    public void Add_months_clamps_to_the_century_February_end(string date, long months, string expected)
    {
        Assert.Equal(expected, DateMath.Add(date, months, "month"));
    }

    [Theory]
    [InlineData("2000-02-29", 1L, "2001-02-28")]
    [InlineData("2000-02-29", 4L, "2004-02-29")]
    public void Add_years_clamps_leap_day_only_for_nonleap_destinations(string date, long years, string expected)
    {
        Assert.Equal(expected, DateMath.Add(date, years, "year"));
    }

    [Theory]
    [InlineData("1900-03-01", "1900-02-28", 1L)]
    [InlineData("2000-03-01", "2000-02-28", 2L)]
    [InlineData("2100-03-01", "2100-02-28", 1L)]
    [InlineData("2000-02-28", "2000-03-01", -2L)]
    public void DiffDays_counts_century_leap_days_and_preserves_direction(string date, string other, long expected)
    {
        Assert.Equal(expected, DateMath.DiffDays(date, other));
    }
}
