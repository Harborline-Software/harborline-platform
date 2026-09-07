/**
 * Workflow admission validator (WF-KEY — the AUTHORING-TIME face of the security fence).
 *
 * A faithful TS port of the canonical .NET
 * `packages/blocks-workflow/src/durable/WorkflowAdmissionValidator.cs` (ADR 0140 / the
 * ADR 0135 A1 R-1 gate). The .NET validator is the publish-time / load-time gate; THIS
 * one runs in the builder so the canvas refuses the very graphs R-1 would refuse — at
 * DESIGN time (a red edge + diagnostic), which is strictly stronger than catching it at
 * load time. The two MUST agree; the same two load-bearing invariants are fail-closed:
 *
 *   (1) an action that is NOT explicitly CP or AP ⇒ refuse (never default-allow);
 *   (2) a CP action must be HUMAN-GATED AT THE POINT OF FIRING — it may fire ONLY on a
 *       HumanAction transition, OR on entering a non-initial state whose EVERY incoming
 *       transition is a HumanAction (the approve→post-JE pattern: the human approve IS the
 *       gating transition). STRICTER than "a human is somewhere upstream": a single upstream
 *       approval does NOT license a later autonomous tick — nor an autonomous back-edge
 *       re-entry — to fire the CP without a human gating that firing.
 *
 * Plus reachability + referential integrity (BFS-throw-at-load skeleton). Structural —
 * it reasons over the state/transition/trigger/action graph, never over runtime data.
 */

import type { RoleVocabulary } from './authorization.js'
import type { ActionClassification, WorkflowDefinition } from './workflow.js'

/** Stable, locale-independent admission-violation codes (mirror WorkflowAdmissionCodes.cs). */
export const WORKFLOW_ADMISSION_CODES = {
  actionUnclassified: 'workflow.admission.action_unclassified',
  classificationMismatch: 'workflow.admission.classification_mismatch',
  cpReachableWithoutHumanTask: 'workflow.admission.cp_reachable_without_human_task',
  initialStateMissing: 'workflow.admission.initial_state_missing',
  deadEndState: 'workflow.admission.dead_end_state',
  terminalHasOutgoing: 'workflow.admission.terminal_has_outgoing',
  unreachableState: 'workflow.admission.unreachable_state',
  danglingReference: 'workflow.admission.dangling_reference',
  actionBindingInvalid: 'workflow.admission.action_binding_invalid',
  duplicateId: 'workflow.admission.duplicate_id',
  unknownRoleReference: 'workflow.admission.unknown_role_reference',
} as const

export type WorkflowAdmissionCode =
  (typeof WORKFLOW_ADMISSION_CODES)[keyof typeof WORKFLOW_ADMISSION_CODES]

export interface WorkflowAdmissionViolation {
  code: WorkflowAdmissionCode
  message: string
  /** The offending id (state / transition / action), for the canvas to highlight. */
  locator: string
}

export interface WorkflowAdmissionResult {
  isValid: boolean
  violations: WorkflowAdmissionViolation[]
}

/** BFS over the state graph from the initial state, traversing ALL transitions. */
function reachableStates(def: WorkflowDefinition): Set<string> {
  const visited = new Set<string>([def.initialState])
  const queue: string[] = [def.initialState]
  while (queue.length > 0) {
    const state = queue.shift()!
    for (const t of def.transitions) {
      if (t.from !== state) continue
      if (!visited.has(t.to)) {
        visited.add(t.to)
        queue.push(t.to)
      }
    }
  }
  return visited
}

export interface WorkflowAuthorityResolver {
  authorityOf(capabilityRef: string): ActionClassification
}

export function createWorkflowAuthorityResolver(registry: Readonly<Record<string, ActionClassification>>): WorkflowAuthorityResolver {
  for (const [capabilityRef, classification] of Object.entries(registry)) {
    if (classification !== 'CP' && classification !== 'AP')
      throw new TypeError(`unknown-closed-value: ActionClassification (${capabilityRef}=${String(classification)})`)
  }
  const snapshot = Object.freeze({...registry})
  return Object.freeze({authorityOf: (capabilityRef: string) => snapshot[capabilityRef] ?? 'CP'})
}

/** Validate a WorkflowDefinition; returns the surviving violations (empty means admissible). */
export function validateWorkflowAdmission(
  def: WorkflowDefinition,
  authorityResolver: WorkflowAuthorityResolver,
  roleVocabulary?: RoleVocabulary,
): WorkflowAdmissionResult {
  const v: WorkflowAdmissionViolation[] = []
  const add = (code: WorkflowAdmissionCode, message: string, locator: string) =>
    v.push({ code, message, locator })

  // -- indices + duplicate detection --
  const stateIds = new Set<string>()
  for (const s of def.states) {
    if (stateIds.has(s.id)) add(WORKFLOW_ADMISSION_CODES.duplicateId, `duplicate state id '${s.id}'.`, s.id)
    stateIds.add(s.id)
  }
  const triggerKind = new Map<string, string>()
  for (const t of def.triggers) {
    if (triggerKind.has(t.id)) add(WORKFLOW_ADMISSION_CODES.duplicateId, `duplicate trigger id '${t.id}'.`, t.id)
    triggerKind.set(t.id, t.kind)
  }
  const transitionById = new Map<string, WorkflowDefinition['transitions'][number]>()
  for (const transition of def.transitions) {
    if (transitionById.has(transition.id)) add(WORKFLOW_ADMISSION_CODES.duplicateId, `duplicate transition id '${transition.id}'.`, transition.id)
    transitionById.set(transition.id, transition)
  }
  const actionIds = new Set<string>()
  for (const action of def.actions) {
    if (actionIds.has(action.id)) add(WORKFLOW_ADMISSION_CODES.duplicateId, `duplicate action id '${action.id}'.`, action.id)
    actionIds.add(action.id)
  }
  const guardIds = new Set<string>()
  for (const guard of def.guards) {
    if (guardIds.has(guard.id)) add(WORKFLOW_ADMISSION_CODES.duplicateId, `duplicate guard id '${guard.id}'.`, guard.id)
    guardIds.add(guard.id)
  }

  // -- referential integrity --
  if (!stateIds.has(def.initialState))
    add(WORKFLOW_ADMISSION_CODES.initialStateMissing, `initialState '${def.initialState}' is not a defined state.`, def.initialState)

  for (const t of def.transitions) {
    if (!stateIds.has(t.from)) add(WORKFLOW_ADMISSION_CODES.danglingReference, `transition '${t.id}' from-state '${t.from}' does not exist.`, t.id)
    if (!stateIds.has(t.to)) add(WORKFLOW_ADMISSION_CODES.danglingReference, `transition '${t.id}' to-state '${t.to}' does not exist.`, t.id)
    if (!triggerKind.has(t.on)) add(WORKFLOW_ADMISSION_CODES.danglingReference, `transition '${t.id}' trigger '${t.on}' does not exist.`, t.id)
    if (t.guard && !guardIds.has(t.guard)) add(WORKFLOW_ADMISSION_CODES.danglingReference, `transition '${t.id}' guard '${t.guard}' does not exist.`, t.id)
    if (roleVocabulary && t.requiredRoles?.some(role => roleVocabulary.resolve(role) === undefined))
      add(WORKFLOW_ADMISSION_CODES.unknownRoleReference, `transition '${t.id}' requires an unknown role.`, t.id)
  }

  for (const a of def.actions) {
    const hasState = 'state' in a.on
    const hasTransition = 'transition' in a.on
    if (hasState === hasTransition)
      add(WORKFLOW_ADMISSION_CODES.actionBindingInvalid, `action '${a.id}' must name exactly one of state / transition.`, a.id)
    if (hasState && !stateIds.has((a.on as { state: string }).state))
      add(WORKFLOW_ADMISSION_CODES.danglingReference, `action '${a.id}' state does not exist.`, a.id)
    if (hasTransition && !transitionById.has((a.on as { transition: string }).transition))
      add(WORKFLOW_ADMISSION_CODES.danglingReference, `action '${a.id}' transition does not exist.`, a.id)
    if (a.condition && !guardIds.has(a.condition))
      add(WORKFLOW_ADMISSION_CODES.danglingReference, `action '${a.id}' condition '${a.condition}' does not exist.`, a.id)
    if (roleVocabulary && a.requiredRoles?.some(role => roleVocabulary.resolve(role) === undefined))
      add(WORKFLOW_ADMISSION_CODES.unknownRoleReference, `action '${a.id}' requires an unknown role.`, a.id)
  }

  // -- reachability + the two security invariants (only once initial is valid) --
  if (stateIds.has(def.initialState)) {
    const reachable = reachableStates(def)
    const hasOutgoing = new Set(def.transitions.map((t) => t.from))

    for (const s of def.states) {
      if (!reachable.has(s.id)) {
        add(WORKFLOW_ADMISSION_CODES.unreachableState, `state '${s.id}' is not reachable from '${def.initialState}'.`, s.id)
        continue
      }
      if (s.kind !== 'Terminal' && !hasOutgoing.has(s.id))
        add(WORKFLOW_ADMISSION_CODES.deadEndState, `state '${s.id}' is reachable but is neither Terminal nor has an outgoing transition.`, s.id)
      if (s.kind === 'Terminal' && hasOutgoing.has(s.id))
        add(WORKFLOW_ADMISSION_CODES.terminalHasOutgoing, `state '${s.id}' is Terminal but has an outgoing transition (Terminal => no outgoing).`, s.id)
    }

    for (const a of def.actions) {
      if (a.classification !== 'CP' && a.classification !== 'AP') {
        add(WORKFLOW_ADMISSION_CODES.actionUnclassified, `action '${a.id}' (${a.kind} via '${a.capabilityRef}') declares no CP/AP classification.`, a.id)
        continue
      }

      // Registry-DERIVED authority (ADR 0143, red-team Chain 1+5 / F2): the class is DERIVED
      // from the canonical capability→authority registry (`authorityOf`, unknown ⇒ CP
      // fail-closed) — the SAME single source the .NET WorkflowAdmissionValidator consults.
      // The author-declared `classification` is a NON-authoritative assertion that MUST equal
      // the derived class: a mismatch is refused (a CP capability cannot be laundered to AP),
      // and an unregistered capability derives to CP so it cannot be smuggled past the
      // human-gate as AP. The human-gate fence then keys off the DERIVED class, never the label.
      const derived = authorityResolver.authorityOf(a.capabilityRef)
      if (a.classification !== derived) {
        add(
          WORKFLOW_ADMISSION_CODES.classificationMismatch,
          `action '${a.id}' declares '${a.classification}' but capability '${a.capabilityRef}' resolves to ` +
            `'${derived}' in the authority registry — the declared class must match the derived class ` +
            `(an unknown capability derives to CP).`,
          a.id,
        )
        continue
      }

      if (derived === 'CP' && firesWithoutHumanGate(a, def, transitionById, triggerKind))
        add(
          WORKFLOW_ADMISSION_CODES.cpReachableWithoutHumanTask,
          `CP action '${a.id}' can fire without a human-task gating the firing. A CP action may fire ONLY ` +
            `on a HumanAction transition, or on entering a non-initial state whose every incoming transition ` +
            `is a HumanAction.`,
          a.id,
        )
    }
  }

  return { isValid: v.length === 0, violations: v }
}

/** A trigger id resolves to a HumanAction (fail-closed: an unknown/dangling id is NOT human). */
function isHumanAction(triggerId: string, triggerKind: Map<string, string>): boolean {
  return triggerKind.get(triggerId) === 'HumanAction'
}

/**
 * Does this CP action's fire-point fail the "park before firing" property — can it fire WITHOUT a
 * HumanAction immediately gating the firing? (true => refuse.) A LOCAL structural property of the
 * fire-point, strictly stronger than "a human is somewhere upstream":
 *   - OnTransition T => SAFE only if T's own trigger is a HumanAction (the human IS the gate).
 *   - OnState S      => SAFE only if S is non-initial AND has >=1 incoming AND EVERY incoming
 *     transition is a HumanAction (the only way to ENTER S — and fire the on-enter CP — is a human).
 */
function firesWithoutHumanGate(
  action: WorkflowDefinition['actions'][number],
  def: WorkflowDefinition,
  transitionById: Map<string, WorkflowDefinition['transitions'][number]>,
  triggerKind: Map<string, string>,
): boolean {
  if ('transition' in action.on) {
    const t = transitionById.get(action.on.transition)
    if (!t) return false // dangling — already refused; not a CP-fence violation here
    return !isHumanAction(t.on, triggerKind)
  }
  if ('state' in action.on) {
    const stateId = action.on.state
    if (stateId === def.initialState) return true // entered autonomously on instantiation
    const incoming = def.transitions.filter((t) => t.to === stateId)
    return incoming.length === 0 || incoming.some((t) => !isHumanAction(t.on, triggerKind))
  }
  return false
}
