using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class SchedulingExpansionBudgetLiteralProofTests
{
    [Fact(DisplayName = "scheduling-eng-4 recurrence-expansion-eng-10 budget-object: exact allowance and refusal")]
    public void ExactAllowanceAndRefusal()
    {
        var budget = new RruleExpansionBudget("FREQ=DAILY", new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3), 11);
        Assert.Equal(10, budget.CandidatesExamined);
        Assert.Null(Record.Exception(() => budget.Charge()));
        Assert.Equal(11, budget.CandidatesExamined);
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() => budget.Charge());
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Message);
        Assert.Equal("FREQ=DAILY", refusal.RecurrenceId);
        Assert.Equal(new DateOnly(2026, 1, 1), refusal.Anchor);
        Assert.Equal(new DateOnly(2026, 1, 2), refusal.RequestedRangeStart);
        Assert.Equal(new DateOnly(2026, 1, 3), refusal.RequestedRangeEnd);
        Assert.Equal(11, refusal.CandidateLimit);
        Assert.Equal(12, refusal.CandidatesExamined);
        Assert.Null(refusal.LastEvaluatedOccurrence);
        Assert.Equal("None", refusal.Bound?.ToString());
    }

    [Fact(DisplayName = "scheduling-eng-4 recurrence-expansion-eng-10 budget-object: input exhaustion before classification")]
    public void InputExhaustion()
    {
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() => new RruleExpansionBudget(
            "FREQ=DAILY", new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3), 9));
        Assert.Equal(9, refusal.CandidateLimit);
        Assert.Equal(10, refusal.CandidatesExamined);
        Assert.Null(refusal.Bound);
        Assert.Null(refusal.LastEvaluatedOccurrence);
    }

    [Theory(DisplayName = "scheduling-eng-4 recurrence-expansion-eng-10 budget-object: bound classification")]
    [InlineData("FREQ=DAILY;COUNT=2", "Count")]
    [InlineData("FREQ=DAILY;UNTIL=20260103", "Until")]
    [InlineData("FREQ=DAILY", "None")]
    public void BoundClassification(string rule, string expected)
    {
        var budget = new RruleExpansionBudget(rule, new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3), 100);
        Assert.Equal(expected, budget.Bound?.ToString());
    }
}
