using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class OccurrenceAdmissionTests
{
    // ADR-0149 decisions 1–3; literal inputs and refusal codes are independent of production symbols.
    private static IReadOnlyList<DefinitionRefusal> Admit(string members)
        => ScheduleDefinitionCatalogue.Admission(new(new("tenant", DefinitionKind.Schedules, "schedule"),
            "schedule@1.0.0", "1.0.0",
            "{\"name\":\"Visit\",\"starts\":{\"kind\":\"form\",\"target_id\":\"visit\"}," + members + "}"), DefinitionAdmissionPhase.Author);

    [Theory]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00Z\"")]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00+02:00\",\"window\":{\"earliest\":\"2026-07-01T10:00:00Z\",\"latest\":\"2026-07-01T11:00:00Z\"}")]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00Z\",\"window\":{\"earliest\":\"2026-07-01T11:00:00Z\",\"latest\":\"2026-07-01T12:00:00Z\"}")]
    [InlineData("\"basis\":{\"kind\":\"completion_offset\",\"offset\":\"P1W\"}")]
    [InlineData("\"basis\":{\"kind\":\"fixed_calendar\",\"rrule\":\"FREQ=DAILY\"},\"window\":{\"earliest\":\"PT0S\",\"latest\":\"P1M\"}")]
    public void LoneDueDateAndOptionalWindowsAdmit(string members) => Assert.Empty(Admit(members));

    [Theory]
    [InlineData("\"due_date\":null", "scheduling.definition.due_date_invalid", "/due_date")]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00\"", "scheduling.definition.due_date_invalid", "/due_date")]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00Z\",\"basis\":{\"kind\":\"completion_offset\",\"offset\":\"P1W\"}", "scheduling.definition.due_date_invalid", "/due_date")]
    [InlineData("\"due_date\":\"2026-07-01T12:00:00Z\",\"window\":{\"earliest\":\"2026-07-01T13:00:00Z\",\"latest\":\"2026-07-01T14:00:00Z\"}", "scheduling.definition.window_invalid", "/window")]
    [InlineData("\"basis\":{\"kind\":\"fixed_calendar\",\"rrule\":\"FREQ=DAILY\"},\"window\":{\"earliest\":\"P6D\",\"latest\":\"P1W\"}", "scheduling.definition.window_invalid", "/window")]
    [InlineData("\"basis\":{\"kind\":\"fixed_calendar\",\"rrule\":\"FREQ=HOURLY\"}", "scheduling.definition.basis_invalid", "/basis/rrule")]
    [InlineData("\"basis\":{\"kind\":\"fixed_calendar\",\"rrule\":\"FREQ=DAILY;EXDATE=20260701\"}", "scheduling.definition.basis_invalid", "/basis/rrule")]
    public void InvalidDueWindowOrRuleRefusesAtAuthoring(string members, string code, string pointer)
        => Assert.Equal([new DefinitionRefusal(code, pointer)], Admit(members));

    [Fact]
    public void ChainWindowBelongsToNodeAndNeverTheSchedule()
    {
        const string basis = "\"basis\":{\"kind\":\"event_anchored_chain\",\"anchor\":\"visit\",\"nodes\":[{\"node_id\":\"a\",\"offset\":\"PT0S\",\"tolerance\":{\"early\":\"PT0S\",\"late\":\"PT1H\"},\"window\":{\"earliest\":\"-P1D\",\"latest\":\"P2D\"}}]}";
        Assert.Empty(Admit(basis));
        Assert.Equal([new DefinitionRefusal("scheduling.definition.window_invalid", "/window")],
            Admit(basis + ",\"window\":{\"earliest\":\"-P1D\",\"latest\":\"P2D\"}"));
        Assert.Equal([new DefinitionRefusal("scheduling.definition.window_invalid", "/basis/nodes/0/window")],
            Admit(basis.Replace("-P1D", "P1D", StringComparison.Ordinal)));
    }
}
