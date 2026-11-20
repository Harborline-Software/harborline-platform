using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Durable;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.ExecutionRuntime;
using Harborline.Foundation.Scheduling.Models;

namespace Harborline.Blocks.Scheduling.Planning;

/// <summary>A gate refusal or the completed, persisted Plan.</summary>
public sealed record PlanRunResult(AccessCheck Check, SchedulingPlan? Plan);

/// <summary>Gates planning and records its engine state under the execution identity.</summary>
public sealed class PlanRun(AccessProvider access, RunLifecycle lifecycle, SchedulePlanner planner,
    FileJournalSchedulingStore store, TimeProvider clock)
{
    /// <summary>Registers Scheduling's on-demand run kind, without a due-run handler.</summary>
    public static RunKindRegistration Register(RunKindRegistry registry) => registry.Register(new("plan-run"), "scheduling");

    /// <summary>Checks the principal before starting a run or reading the pinned definition.</summary>
    public async Task<PlanRunResult> PlanAsync(string principal, ScheduleDefinitionPin pin, SchedulingProfile facts,
        string backendId, SolverGuarantee guarantee, int deterministicWorkBudget, DateTimeOffset epoch,
        TimeSpan slotDuration, IReadOnlyDictionary<string, ResourceCapacity> resources,
        RunId? causedBy = null, CancellationToken cancellationToken = default)
    {
        var at = clock.GetUtcNow();
        var decision = await access.CheckWithEvidenceAsync(new(SchedulingPermissions.Plan, principal, pin.Tenant,
            new(pin.Tenant, SchedulingPermissions.ScheduleRecordKind, pin.DefinitionId,
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at), cancellationToken).ConfigureAwait(false);
        if (!decision.Check.Allowed) return new(decision.Check, null);
        var tenant = new TenantId(pin.Tenant);
        var run = await lifecycle.StartAsync(new(new("plan-run"), tenant,
            new(RetryProfileName.ImmediateOrFail, [RetryProfileName.ImmediateOrFail]), CausedBy: causedBy), cancellationToken).ConfigureAwait(false);
        try
        {
            await lifecycle.BeginAttemptAsync(tenant, run.Id, cancellationToken).ConfigureAwait(false);
            var proposal = await planner.PlanAsync(pin, facts, backendId, guarantee, deterministicWorkBudget,
                epoch, slotDuration, resources, cancellationToken).ConfigureAwait(false);
            RequireSuccessfulOutcome(proposal.Status);
            var plan = new SchedulingPlan(run.Id, pin, causedBy, at, facts.FactSetPins, proposal);
            await store.SavePlanAsync(plan, cancellationToken).ConfigureAwait(false);
            // Once durable, finish even if the caller cancels; never report a saved Plan as cancelled.
            await lifecycle.SucceedAsync(tenant, run.Id, CancellationToken.None).ConfigureAwait(false);
            return new(decision.Check, plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await lifecycle.CancelAsync(tenant, run.Id, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (UnsupportedProfileException error)
        {
            var token = error.Message.Split(':', 2)[0];
            var code = token.Length > 0 && token.All(c => c is >= 'A' and <= 'Z' or '_')
                && error.Message.Contains(':', StringComparison.Ordinal) ? token : "PROFILE_UNSUPPORTED";
            await lifecycle.FailAttemptAsync(tenant, run.Id, new(code, false), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await lifecycle.FailAttemptAsync(tenant, run.Id, new("PLAN_FAILED", true), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    internal static RunStatus RequireSuccessfulOutcome(SolveStatus status) => status switch
    {
        SolveStatus.Feasible or SolveStatus.ProvenInfeasible or SolveStatus.Partial or SolveStatus.Indeterminate => RunStatus.Succeeded,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
