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
