# Harborline.Blocks.Workflow.Interpreter

The general declarative workflow interpreter for Harborline Platform
(`hlp.blocks.workflow-interpreter`).

`DeclarativeWorkflowInterpreter` is the dispatcher's fallback when no typed
`IWorkflowStepHandler` matches an instance's definition key. It executes ANY authored +
admitted + persisted `WorkflowDefinition`:

1. **Load** the instance's pinned definition through the re-validating
   `IWorkflowDefinitionExecutionStore` (never the lenient authoring face) — a
   now-inadmissible definition throws at load and never executes.
2. **Traverse** from the current state: autonomous triggers walk the single autonomous
   outgoing edge per hop until a park point, an effecting action, or a terminal state;
   human-action resumes select the outgoing HumanAction transition bound to the decision
   verb via the `decision:<verb>` guard sentinel (`send-back` is a bounded loop-back).
3. **Reach effects only through the broker** — an AP action builds autonomously; a CP
   action builds only through the SoD-gated confirm path, with the confirmer resolved
   server-side via `IWorkflowConfirmationContext`, never from the payload.

This is a separate assembly from the engine core by design: it composes only the public
broker/catalog seams and cannot name the internal effect-factory surface, so broker-only
effect reach is a compile-time fact.

Distribution: internal-only projection. Public distribution is not authorized.

## Decision instant (T-1176/platform-gate-instant)

Composition requires the host's kernel `TimeProvider`; there is no wall-clock fallback.
The verified-authority `DecideAsync` overload reads it once per act and carries
`WorkflowGateIntent.DecidedAt` through the transition and effecting action gates.
Answers must bind that instant. Payload `decidedAt` and `captured_at` cannot supply it.
The broker's confirmation evidence and the store's event timestamps remain separate.

Hand-mutation evidence, 2026-11-10 (both mutations restored before the passing run):

- Replaced the action gate's `decidedAt` argument with `_clock.GetUtcNow()`.
  `Kernel_clock_instant_is_shared_by_transition_and_action_once_per_act_despite_payload_time`
  failed: expected `2026-11-10T12:34:56+00:00`, received `2026-11-10T12:34:57+00:00`.
  Its independent oracle is that literal instant, with `[1, 1]` clock-read counts
  observed at the two ports, before the broker stamps its own evidence.
- Removed `&& DecidedAt == other.DecidedAt` from `WorkflowGateIntent.Binds`.
  `An_answer_at_a_different_decision_instant_is_unbound_before_any_effect`
  failed: expected `WorkflowGateRefusedException`, no exception thrown.
  Its oracle is the slice's literal refusal code `workflow.gate.answer_unbound`
  for an answer changed by one tick, with zero confirmation effects.

Both tests live in `DeclarativeWorkflowInterpreterTests.cs` in the interpreter test
project. Each mutation was run with `dotnet test` and a `FullyQualifiedName` filter
selecting its named test; each exited 1 with one failed test. These are hand-mutation
proofs, not a Stryker score claim. Carried role-gate tests name `workflows-auth-10`
and `workflows-eng-13` through `Holds` traits.
