using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Scheduling.Tests;

public class ScheduleDurationAdmissionProofTests
{
    [Theory]
    [InlineData("P1W", 6048000000000L)]
    [InlineData("P2W", 12096000000000L)]
    [InlineData("-P1W", -6048000000000L)]
    [InlineData("PT1S", 10000000L)]
    [InlineData("PT0S", 0L)]
    public void scheduling_ck_25_duration_admission_ticks(string value, long ticks)
        => Assert.Equal(ticks, ScheduleDefinitionAdmission.SignedDuration(value)?.Ticks);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" P1D")]
    [InlineData("P1D ")]
    [InlineData("P1W2D")]
    [InlineData("+P1W")]
    public void scheduling_ck_25_duration_admission_rejects_invalid_spelling(string? value)
        => Assert.Null(ScheduleDefinitionAdmission.SignedDuration(value));

    [Theory]
    [InlineData("PT0S")]
    [InlineData("P1W")]
    [InlineData("PT1S")]
    public void scheduling_ck_25_duration_admission_accepts_nonnegative_offset(string offset)
        => Assert.Empty(Refusals(offset));

    [Fact]
    public void scheduling_ck_25_duration_admission_refuses_negative_offset_and_accepts_correction()
    {
        var refusal = Assert.Single(Refusals("-PT1S"));
        Assert.Equal("scheduling.definition.duration_invalid", refusal.Code);
        Assert.Equal("/basis/offset", refusal.Pointer);
        Assert.Empty(Refusals("PT1S"));
    }

    private static List<DefinitionRefusal> Refusals(string offset)
    {
        var definition = new ScheduleDefinition(null, "Schedule", new ScheduleStarts(ScheduleStartKind.Form, "form"), Basis: new CompletionOffsetBasis(offset));
        var refusals = new List<DefinitionRefusal>();
        ScheduleDefinitionAdmission.Generation(definition, refusals);
        return refusals;
    }
}
