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

import type { RecordStandingReference, RoleVocabulary } from './authorization.js'
import type { ActionClassification, WorkflowContractVersion, WorkflowDefinition, WorkflowGateRole } from './workflow.js'

/** Stable, locale-independent admission-violation codes (mirror WorkflowAdmissionCodes.cs). */
export const WORKFLOW_ADMISSION_CODES = {
  actionUnclassified: 'workflow.admission.action_unclassified',
  unknownCapability: 'workflow.admission.unknown_capability',
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
  invalidStanding: 'workflow.admission.invalid_standing',
  envelopeRequired: 'workflow.admission.envelope_required',
  envelopeMismatch: 'workflow.admission.envelope_mismatch',
  envelopeInvalid: 'workflow.admission.envelope_invalid',
  contractMissing: 'definition.contract.missing',
  contractOutOfWindow: 'definition.contract.out_of_window',
  gateOnAutonomousStep: 'workflow.admission.gate_on_autonomous_step',
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

/** The application-contract version and the oldest major release it admits (DefinitionContractWindow's mirror). */
export interface WorkflowContractWindow {
  major: number
  minor: number
  oldestMajor: number
}

/** The sealed record-relative vocabulary (ADR 0041, DES-0019 workflows-ck-15). */
export const WORKFLOW_SEALED_ROLES = Object.freeze({
  vocabulary: 'sys.roles',
  names: Object.freeze(['Anyone', 'Author', 'Reviewer', 'Approver']),
})

const KNOWN_ROLE_VOCABULARIES: readonly string[] = [WORKFLOW_SEALED_ROLES.vocabulary, 'sys.platform-roles', 'tax.roles']

function roleAdmits(role: WorkflowGateRole, roleVocabulary?: RoleVocabulary): boolean {
  if (!KNOWN_ROLE_VOCABULARIES.includes(role.vocabulary) || role.name.trim() === '') return false
  if (role.vocabulary === 'sys.roles') return WORKFLOW_SEALED_ROLES.names.includes(role.name)
  return roleVocabulary === undefined || roleVocabulary.resolve(role) !== undefined
}

/** A bare name only: standings every one of which must hold can narrow and nothing else. */
function isStandingName(standing: RecordStandingReference): boolean {
  return /^[A-Za-z][A-Za-z0-9._-]*$/.test(standing.name)
}

function contractRefusal(declared: WorkflowContractVersion | undefined, window: WorkflowContractWindow): WorkflowAdmissionCode | undefined {
  if (declared === undefined || declared.major < 0 || declared.minor < 0) return WORKFLOW_ADMISSION_CODES.contractMissing
  const outside = declared.major > window.major
    || declared.major < window.oldestMajor
    || (declared.major === window.major && declared.minor > window.minor)
    || (window.major === 0 && declared.major === 0 && declared.minor !== window.minor)
  return outside ? WORKFLOW_ADMISSION_CODES.contractOutOfWindow : undefined
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
  /** True iff the registry has a row for the capability; admission refuses one it has not (T-525 item 2). */
  isRegistered(capabilityRef: string): boolean
}

export function createWorkflowAuthorityResolver(registry: Readonly<Record<string, ActionClassification>>): WorkflowAuthorityResolver {
  for (const [capabilityRef, classification] of Object.entries(registry)) {
    if (classification !== 'CP' && classification !== 'AP')
      throw new TypeError(`unknown-closed-value: ActionClassification (${capabilityRef}=${String(classification)})`)
  }
  const snapshot = Object.freeze({...registry})
  return Object.freeze({
    authorityOf: (capabilityRef: string) => snapshot[capabilityRef] ?? 'CP',
    isRegistered: (capabilityRef: string) => Object.hasOwn(snapshot, capabilityRef),
  })
}

/** Validate a WorkflowDefinition; returns the surviving violations (empty means admissible). */
export function validateWorkflowAdmission(
  def: WorkflowDefinition,
  authorityResolver: WorkflowAuthorityResolver,
  roleVocabulary?: RoleVocabulary,
  contractWindow?: WorkflowContractWindow,
): WorkflowAdmissionResult {
  const v: WorkflowAdmissionViolation[] = []
  const add = (code: WorkflowAdmissionCode, message: string, locator: string) =>
    v.push({ code, message, locator })

  // -- envelope --
  if (def.envelope === undefined) {
    if (contractWindow) add(WORKFLOW_ADMISSION_CODES.envelopeRequired, 'definition carries no envelope.', '/envelope')
  } else {
    const envelope = def.envelope
    if (envelope.identity !== def.key || envelope.version !== def.version || envelope.tenant !== def.tenant)
      add(WORKFLOW_ADMISSION_CODES.envelopeMismatch, 'envelope does not name this definition.', '/envelope')
    if (envelope.provenance === null || typeof envelope.provenance !== 'object' || Array.isArray(envelope.provenance))
      add(WORKFLOW_ADMISSION_CODES.envelopeInvalid, 'envelope provenance is not an object.', '/envelope/provenance')
    if (envelope.retentionClass.trim() === '')
      add(WORKFLOW_ADMISSION_CODES.envelopeInvalid, 'envelope retention class is blank.', '/envelope/retentionClass')
    envelope.requires.forEach((requirement, index) => {
      if (requirement.capability.trim() === '')
        add(WORKFLOW_ADMISSION_CODES.envelopeInvalid, 'envelope requirement names no capability.', `/envelope/requires/${index}`)
    })
    const refusal = contractWindow && contractRefusal(envelope.contract, contractWindow)
    if (refusal) add(refusal, 'envelope contract is absent or outside the window.', '/envelope/contract')
  }

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
    if (t.requiredRoles?.some(role => !roleAdmits(role, roleVocabulary)))
      add(WORKFLOW_ADMISSION_CODES.unknownRoleReference, `transition '${t.id}' requires an unknown role.`, t.id)
    if (t.requiredStandings?.some(standing => !isStandingName(standing)))
      add(WORKFLOW_ADMISSION_CODES.invalidStanding, `transition '${t.id}' requires a malformed standing.`, t.id)
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
    if (a.requiredRoles?.some(role => !roleAdmits(role, roleVocabulary)))
      add(WORKFLOW_ADMISSION_CODES.unknownRoleReference, `action '${a.id}' requires an unknown role.`, a.id)
    if (a.requiredStandings?.some(standing => !isStandingName(standing)))
      add(WORKFLOW_ADMISSION_CODES.invalidStanding, `action '${a.id}' requires a malformed standing.`, a.id)
  }

  // T-1176 Q1 (owner ruling 2026-11-07): a role or standing gate where no human acts is refused; the engine
  // principal holds none (ADR-0051). "No human acts" is the CP fence's own test, so the two rules agree.
  for (const t of def.transitions)
    if (((t.requiredRoles?.length ?? 0) > 0 || (t.requiredStandings?.length ?? 0) > 0) && !isHumanAction(t.on, triggerKind))
      add(WORKFLOW_ADMISSION_CODES.gateOnAutonomousStep, `transition '${t.id}' has no human actor but declares a role or standing gate.`, t.id)
  for (const a of def.actions)
    if (((a.requiredRoles?.length ?? 0) > 0 || (a.requiredStandings?.length ?? 0) > 0) && firesWithoutHumanGate(a, def, transitionById, triggerKind))
      add(WORKFLOW_ADMISSION_CODES.gateOnAutonomousStep, `action '${a.id}' fires with no human actor but declares a role or standing gate.`, a.id)

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

      // An unregistered capability is refused, never derived to CP (T-525 item 2; mirrors the
      // .NET WorkflowAdmissionCodes.UnknownCapability check).
      if (!authorityResolver.isRegistered(a.capabilityRef)) {
        add(
          WORKFLOW_ADMISSION_CODES.unknownCapability,
          `action '${a.id}' names capability '${a.capabilityRef}', which the authority registry does not recognize.`,
          a.id,
        )
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
