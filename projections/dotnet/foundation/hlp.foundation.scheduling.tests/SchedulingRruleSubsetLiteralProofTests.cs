using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class SchedulingRruleSubsetLiteralProofTests
{
    [Fact(DisplayName = "scheduling-eng-4 recurrence-expansion-ck-1 subset-admission: repeated frequency refuses")]
    public void RepeatedFrequency() => Assert.Equal(
        "RRULE component 'FREQ' is given more than once.",
        Assert.Throws<FormatException>(() => InMemoryRruleExpansionService.ValidateRule("FREQ=DAILY;FREQ=WEEKLY")).Message);

    [Fact(DisplayName = "scheduling-eng-4 recurrence-expansion-ck-1 subset-admission: conflicting bounds refuse")]
    public void ConflictingBounds() => Assert.Equal(
        "RRULE components 'COUNT' and 'UNTIL' must not occur in the same rule.",
        Assert.Throws<FormatException>(() => InMemoryRruleExpansionService.ValidateRule("FREQ=DAILY;COUNT=2;UNTIL=20260103")).Message);

    [Fact(DisplayName = "scheduling-eng-4 recurrence-expansion-ck-1 subset-admission: weekly month day refuses")]
    public void WeeklyMonthDay() => Assert.Equal(
        "RRULE component 'BYMONTHDAY' is not applicable with FREQ=WEEKLY.",
        Assert.Throws<NotSupportedException>(() => InMemoryRruleExpansionService.ValidateRule("FREQ=WEEKLY;BYMONTHDAY=15")).Message);

    [Theory(DisplayName = "scheduling-eng-4 recurrence-expansion-ck-1 subset-admission: valid controls admit")]
    [InlineData("FREQ=DAILY;COUNT=2")]
    [InlineData("FREQ=DAILY;UNTIL=20260103")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;COUNT=2")]
    public void ValidControls(string rule) => InMemoryRruleExpansionService.ValidateRule(rule);
}
