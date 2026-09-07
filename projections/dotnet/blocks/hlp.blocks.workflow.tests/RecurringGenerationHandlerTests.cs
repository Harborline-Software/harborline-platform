using System.Text.Json;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 slice 2 — Handler B (recurring generation on the <c>schedule</c> trigger) UNIT coverage at the
/// blocks-workflow seam (no financial dependency — the generation effect comes from a fake
/// <see cref="IRecurringGenerationContext"/>). Asserts the AP autonomous generate branch (one effect per
/// occurrence step), the occurrence-keyed step id, and the CP period-close-park-by-name invariant.
/// </summary>
public sealed class RecurringGenerationHandlerTests
{
    private sealed class FakeContext : IRecurringGenerationContext
    {
        public List<DateOnly> EffectsBuiltFor { get; } = new();
        public bool ReturnNullEffect { get; init; }

        public WorkflowEffect? BuildGenerationEffect(
            WorkflowInstanceRecord instance, DateOnly occurrenceDate, WorkflowStepKey generateStepKey)
        {
            EffectsBuiltFor.Add(occurrenceDate);
            return ReturnNullEffect ? null : new WorkflowEffect((_, _) => Task.CompletedTask);
        }
    }

    private static WorkflowInstanceRecord Instance() => new()
    {
        Id = "inst-B",
        TenantId = "t",
        DefinitionKey = RecurringGenerationSteps.DefinitionKey,
        DefinitionVersion = "v1",
        CurrentStep = RecurringGenerationSteps.GenerateStep(new DateOnly(2026, 7, 1)),
        Status = WorkflowStatus.Running,
    };

    [Fact(DisplayName = "Handler B: a schedule tick on generate@{date} advances (AP) and stages the occurrence's effect; the event records the occurrence date")]
    public async Task ScheduleTick_GeneratesOccurrence()
    {
        var ctx = new FakeContext();
        var handler = new RecurringGenerationHandler(ctx);
        var step = RecurringGenerationSteps.GenerateStep(new DateOnly(2026, 7, 1));

        var outcome = await handler.DecideAsync(
            Instance(), WorkflowTrigger.For(WorkflowTriggerKind.Schedule, "inst-B", step));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal(WorkflowStatus.Running, outcome.NextStatus);    // long-lived recurring instance
        Assert.Equal(step, outcome.NextStep);                        // stays on this occurrence's step
        Assert.NotNull(outcome.Effect);                              // the occurrence's JE effect is staged
        Assert.Equal(new[] { new DateOnly(2026, 7, 1) }, ctx.EffectsBuiltFor);

        using var data = JsonDocument.Parse(outcome.EventDataJson);
        Assert.Equal("2026-07-01", data.RootElement.GetProperty("occurrenceDate").GetString());
        Assert.True(data.RootElement.GetProperty("generated").GetBoolean());
    }

    [Fact(DisplayName = "Handler B: each occurrence is a DISTINCT generate@{date} step → a distinct idempotency key, so distinct occurrences both advance")]
    public void GenerateStep_IsDistinctPerOccurrence()
    {
        var jul = RecurringGenerationSteps.GenerateStep(new DateOnly(2026, 7, 1));
        var aug = RecurringGenerationSteps.GenerateStep(new DateOnly(2026, 8, 1));
        Assert.NotEqual(jul, aug);
        Assert.True(RecurringGenerationSteps.IsGenerateStep(jul));
        Assert.True(RecurringGenerationSteps.IsGenerateStep(aug));

        // The engine idempotency key is per (instance, iteration, step) — distinct occurrence ⇒ distinct key.
        var keyJul = new WorkflowStepKey("inst-B", 0, jul);
        var keyAug = new WorkflowStepKey("inst-B", 0, aug);
        Assert.NotEqual(keyJul.Value, keyAug.Value);
    }

    [Fact(DisplayName = "Handler B: nothing due for an occurrence → a pure advance (null effect), no double-post risk")]
    public async Task NothingDue_PureAdvance()
    {
        var ctx = new FakeContext { ReturnNullEffect = true };
        var handler = new RecurringGenerationHandler(ctx);
        var step = RecurringGenerationSteps.GenerateStep(new DateOnly(2026, 7, 1));

        var outcome = await handler.DecideAsync(
            Instance(), WorkflowTrigger.For(WorkflowTriggerKind.Schedule, "inst-B", step));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Null(outcome.Effect);
        using var data = JsonDocument.Parse(outcome.EventDataJson);
        Assert.False(data.RootElement.GetProperty("generated").GetBoolean());
    }

    [Fact(DisplayName = "Handler B (CP-park BY NAME): the period-close step NEVER auto-executes — it parks on a human-task (the recurring handler's CP-park gate)")]
    public async Task PeriodClose_ParksHumanTask()
    {
        var ctx = new FakeContext();
        var handler = new RecurringGenerationHandler(ctx);

        var outcome = await handler.DecideAsync(
            Instance(), WorkflowTrigger.For(
                WorkflowTriggerKind.Schedule, "inst-B", RecurringGenerationSteps.PeriodClose));

        // CP-park invariant: a trigger for the period-close step PARKS — it does NOT auto-advance.
        Assert.Equal(WorkflowStepOutcomeKind.Park, outcome.Kind);
        Assert.Equal(RecurringGenerationSteps.PeriodClose, outcome.NextStep);
        Assert.Null(outcome.Effect);
        Assert.Empty(ctx.EffectsBuiltFor);       // no generation effect on a period-close park
    }
}
