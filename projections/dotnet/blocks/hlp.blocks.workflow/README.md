# Harborline.Blocks.Workflow

The workflow engine core for Harborline Platform (`hlp.blocks.workflow`).

Two layers in one packable assembly:

- **Typed state-machine runtime** — `IWorkflowDefinition<TState,TTrigger,TContext>`, the
  fluent `WorkflowDefinitionBuilder` (with build-time reachability validation), and the
  per-instance-serialized `InMemoryWorkflowRuntime` (`StartAsync` / `FireAsync` / `GetAsync`).
- **Durable process engine core** — the provider-neutral `IWorkflowStore` seam (atomic
  advance: effect + outcome event + idempotency row + position in one transaction), the
  cross-architecture byte-stable `WorkflowStepKey` idempotency key, the four-trigger
  `WorkflowTriggerDispatcher` with the bounded loop-back iteration cap, the fail-closed
  `WorkflowAdmissionValidator` over the Harborline-owned `capability-authority.json`
  registry (unknown capability ⇒ CP), the two-faced workflow definition store port
  (lenient authoring face; fail-closed re-admitting execution face, implemented in-process
  by `InMemoryWorkflowDefinitionStore`), the effect broker-PEP (`IWorkflowEffectBroker`)
  with confirm-time separation of duties and the override-rate metric, and the typed v1
  step handlers (invoice approval, recurring generation, proposed-CP-action approval).

Effect factories are compiler-contained: the resolve+build surface is `internal`, hosts
register effects through the public `AddWorkflowEffectFactory` delegate seam, and the
broker is the sole `Build` caller.

Distribution: internal-only projection. Public distribution is not authorized.
