// The vertical-pair canary, in its own module so a self-test can build the SAME context the CLI
// builds. It lived inside run-vertical-pair.mjs against a hand-written context object, and when
// assertEmptyAndErrorStates started reading `ctx.emptyErrorSupport` that object did not have it:
// `--canary` threw at gate-rows.mjs before its first assertion, so every row below -- including the
// design-review rows ticket 138 added -- was dead, and nothing was red. A canary that cannot run is
// the failure this ticket family is about.

import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

import {referenceSurface, reviewVerdict, rollUp} from './design-review.mjs'
import {CASES, runCase} from './design-surface-cases.mjs'
import {firstIntrinsicTag, renderDigest} from './render-digest.mjs'

// A real module, because the context is built by the real builder. Which one does not matter: every
// field the canary reasons about is overridden below.
export const CANARY_MODULE = 'hlp.ui.toaster'

// ONE source for the context SHAPE -- contextFor, the same function the report uses. Only the
// values the canary reasons about are overridden; every other field, including one a gate starts
// reading tomorrow, arrives from the real builder. A second hand-written shape is what broke.
export function canaryContexts({contextFor}) {
  const base = {
    ...contextFor(CANARY_MODULE),
    // A declared not-applicable disposition beats every computed verdict, VOID included. The canary
    // must not inherit a real module's profile, or a disposition recorded there could silence the
    // very rows it exists to prove.
    qualityProfile: null,
    catalog: {scenarios: [{sourceQualityCaseIds: ['x.quality.visual-parity']}]},
    quality: null,
    conformanceCases: 1,
    gallery: true,
    tokens: {directLiterals: 0, moduleLocal: 0},
    stateSet: {derived: ['default'], react: ['default'], blazor: ['default'], laneAgreement: true, unmapped: []},
    declaredSurfaces: ['canary.default default'],
    designReview: null,
    reviewRevision: 'c'.repeat(64),
    reviewSurface: null,
    focus: {moduleId: 'canary', verdict: 'PASS', findings: []},
    declares: {states: false, empty: false, error: false, content: false},
    renders: {empty: false, error: false, content: false},
  }
  return {
    // The real scanner shape: {scenarioId, timing[], unclassified[], async[]}, with `async` carrying
    // a lane. One lane only is the failure -- both lanes doing the same async thing is symmetric and
    // therefore not a divergence risk.
    failing: {...base, determinism: [{moduleId: CANARY_MODULE, scenarioId: 'canary.async', timing: [], unclassified: [], async: [{lane: 'react', construct: 'Promise'}]}]},
    clean: {...base, determinism: []},
  }
}

// The classes of file the derived surface must reach, each one a shape ticket 138 found it blind to.
// A class here with no matching file is a failure, not a skip: "the derivation stopped producing
// .razor paths" is the defect, and a canary that shrugs at an empty class cannot see it.
// One class per LANE, not one per shape: a single "a gallery story" row is satisfied by the Blazor
// story after the React one is dropped, so it cannot see half the derivation go missing.
export const SURFACE_CLASSES = [
  ['a React projection source (.tsx)', file => /^projections\/react\/.+\.tsx$/.test(file)],
  ['a Blazor projection template (.razor)', file => /^projections\/blazor\/.+\.razor$/.test(file)],
  ['a React gallery story', file => /^gallery\/projections\/react\/.+\.stories\.tsx$/.test(file)],
  ['a Blazor gallery story', file => /^gallery\/projections\/blazor\/.+\.stories\.razor$/.test(file)],
  ['a lane stylesheet', file => /^projections\/(react|blazor)\/.+\.css$/.test(file)],
]

// Returns one string per failure; an empty array means the canary is healthy.
export function runCanary(model) {
  const {VOIDED_BY_DETERMINISM, verdictOf, gateRows} = model
  const {failing, clean} = canaryContexts(model)
  const failures = []
  // Goes through gateRows -- the same function the report uses -- so disabling the real propagation
  // fails this canary rather than leaving it green against a private copy of the rule.
  const apply = ctx => Object.fromEntries(gateRows(ctx)
    .filter(r => VOIDED_BY_DETERMINISM.includes(r.id))
    .map(r => [r.id, r.status]))
  if (verdictOf('assertDeterminism')(failing)[0] !== 'FAIL') failures.push('an unguarded async construct must FAIL assertDeterminism')
  if (verdictOf('assertDeterminism')(clean)[0] !== 'PASS') failures.push('a lane with no timing construct must PASS assertDeterminism')
  const voidedVerdicts = apply(failing)
  const cleanVerdicts = apply(clean)
  for (const id of VOIDED_BY_DETERMINISM) {
    if (voidedVerdicts[id] !== 'VOID') failures.push(`${id} must be VOID when determinism fails, got ${voidedVerdicts[id]}`)
    if (cleanVerdicts[id] === 'VOID') failures.push(`${id} must NOT be VOID when determinism passes`)
    if (cleanVerdicts[id] !== 'PASS') failures.push(`${id} must PASS on a clean lane, got ${cleanVerdicts[id]}`)
  }
  // Ruling 2: a design-review veto must propagate to the group. There is no human verdict on any
  // real module and the tooling will not write one, so the propagation is proven here against
  // synthetic records instead of a fabricated approval.
  const revision = 'a'.repeat(64)
  const approved = {schemaVersion: 1, verdict: 'approved', reviewer: 'canary', recordedAt: '2026-08-24', reference: {revision}}
  const expired = {...approved, reference: {revision: 'b'.repeat(64)}}
  // Ticket 138: the same propagation, proven for a verdict bound to a per-file surface -- and the
  // refusal must NAME the file that moved, or nobody can act on it.
  const surface = Object.fromEntries(['scenarios.json', 'style.css', 'catalog.json'].map(f => [f, 'a'.repeat(64)]))
  const boundApproved = {...approved, reference: {revision, surface}}
  const boundStale = {...surface, 'style.css': 'b'.repeat(64)}
  const greenSiblings = [
    {id: 'assertTokenAdherence', status: 'PASS'},
    {id: 'assertStateCompleteness', status: 'PASS'},
    {id: 'assertFocusQuality', status: 'PASS'},
    {id: 'assertResponsiveQuality', status: 'PASS'},
    {id: 'assertEmptyAndErrorStates', status: 'PASS'},
    {id: 'assertContentResilience', status: 'PASS'},
  ]
  const withReview = (record, against) => {
    const [status] = reviewVerdict({record, revision, surface: against})
    return rollUp([...greenSiblings, {id: 'assertDesignReview', status}])[0]
  }
  // Ticket 138 slice 6: the LEGACY shape -- a record whose only reference is the two-file digest --
  // is kept here on purpose, and it must now be REFUSED. Every record on disk was migrated onto a
  // bound surface, so this row is the only live proof left that the refusal works; delete it and
  // the reopening of the blind spot would be silent. The refusal must also SAY so, because "FAIL"
  // with no reason reads as a review problem rather than a record that binds nothing.
  const [legacyStatus, legacyNote] = reviewVerdict({record: approved, revision, surface: {}})
  if (legacyStatus !== 'FAIL') failures.push(`a verdict that binds no surface must be refused, got ${legacyStatus}`)
  if (!/binds no surface/.test(legacyNote)) failures.push(`the refusal of an unbound verdict must say so, got "${legacyNote}"`)
  if (reviewVerdict({record: expired, revision, surface: {}})[0] !== 'FAIL') failures.push('a verdict whose reference moved must FAIL, not read as absent')
  if (reviewVerdict({record: null, revision})[0] !== 'UNBUILT') failures.push('no record at all must be UNBUILT, not FAIL')
  if (reviewVerdict({record: boundApproved, revision, surface})[0] !== 'PASS') failures.push('a surface-bound approval whose files are unchanged must PASS')
  const [staleStatus, staleNote] = reviewVerdict({record: boundApproved, revision, surface: boundStale})
  if (staleStatus !== 'FAIL') failures.push('a surface-bound verdict whose file changed must FAIL')
  if (!staleNote.includes('style.css')) failures.push(`an expired surface-bound verdict must name the changed file, got "${staleNote}"`)
  if (staleNote.includes('scenarios.json')) failures.push(`an expired surface-bound verdict must name ONLY the changed file, got "${staleNote}"`)
  // Ticket 138 slice 3: the surface is DERIVED, and a derivation can quietly stop reaching a whole
  // class of file -- which is exactly the defect the ticket opened with, a verdict blind to .tsx,
  // .razor and story edits. So these rows run against the REAL derived surface of a real module and
  // prove, one class at a time, that the class is in it AND that moving it expires the verdict by
  // name. Drop a class from the derivation and the matching row goes red.
  const derived = referenceSurface(model.platformRoot, CANARY_MODULE)
  for (const [label, owns] of SURFACE_CLASSES) {
    const file = Object.keys(derived ?? {}).find(owns)
    if (!file) {
      failures.push(`the derived design surface of ${CANARY_MODULE} contains ${label.replace(/^an? /, 'no ')}`)
      continue
    }
    const bound = {...approved, reference: {revision, surface: derived}}
    const [status, note] = reviewVerdict({record: bound, revision, surface: {...derived, [file]: 'f'.repeat(64)}})
    if (status !== 'FAIL') failures.push(`changing ${label} (${file}) must expire the verdict, got ${status}`)
    else if (!note.includes(file)) failures.push(`expiring on ${label} must name ${file}, got "${note}"`)
    if (reviewVerdict({record: bound, revision, surface: derived})[0] !== 'PASS') {
      failures.push(`the unchanged derived surface of ${CANARY_MODULE} must still PASS`)
    }
  }
  // Ticket 138 slice 3, the other half: the reach above is only safe because a render-CARRYING file
  // is hashed over a normalised form. Revert that to raw bytes and this row goes red -- which is the
  // regression to fear, because raw bytes look correct and only misbehave on the edits nobody in a
  // gate run is watching for.
  const tsx = Object.keys(derived ?? {}).find(file => file.endsWith('.tsx') && file.startsWith('projections/'))
  if (!tsx) failures.push(`the derived design surface of ${CANARY_MODULE} contains no React projection source`)
  else {
    const source = readFileSync(resolve(model.platformRoot, tsx), 'utf8')
    // Comments only. Reindentation is deliberately NOT used here: a template literal's content is
    // preserved by the normaliser (it can reach the render), so reindenting a file that embeds code
    // in a template literal is not a neutral edit, and a canary must assert something that is true
    // of every module rather than of the one it happens to run on.
    const neutral = `// ticket 138 slice 3: a comment is not an appearance.
${source}
// nor is this one.
`
    if (neutral === source) failures.push(`the neutral edit changed nothing in ${tsx}`)
    if (renderDigest(tsx, neutral) !== derived[tsx]) failures.push(`a comment-only edit to ${tsx} must not move its digest`)
    // The markup edit is an ELEMENT NAME swap, not an attribute rename. An attribute name is kept by
    // the protected-name path, so a `className=` edit passes whatever the normaliser does to JSX and
    // could not see review 1's blocker (JSX names and text lexing as local identifiers). Swapping the
    // element the component renders is red unless the parser's verbatim-range path is present.
    const tag = firstIntrinsicTag(source, tsx)
    if (!tag) failures.push(`${tsx} renders no intrinsic element, so the markup edit cannot be made`)
    else {
      const swapped = source.replaceAll(`<${tag}`, '<article').replaceAll(`</${tag}>`, '</article>')
      if (swapped === source) failures.push(`the markup edit changed nothing in ${tsx}`)
      if (renderDigest(tsx, swapped) === derived[tsx]) {
        failures.push(`swapping the <${tag}> elements of ${tsx} MUST move its digest`)
      }
    }
  }
  if (withReview(boundApproved, surface) !== 'PASS') failures.push(`six green siblings plus a current review must roll up to PASS, got ${withReview(boundApproved, surface)}`)
  if (withReview(boundApproved, boundStale) !== 'FAIL') failures.push(`an EXPIRED review must take the whole group off green, got ${withReview(boundApproved, boundStale)}`)
  // Ticket 138 slice 4: the OTHER half of the rule, and the half a hash-everything surface would
  // get wrong. A behaviour-neutral edit to a real file of a real module must leave the verdict
  // standing; a detector that expires on a comment is the false-positive stream HLP-0008 rejected.
  for (const id of ['comment-only', 'rename-private-helper']) {
    const kase = CASES.find(c => c.id === id)
    if (!kase) { failures.push(`neutral-edit case ${id} has vanished from the case table`); continue }
    const {status, note, file} = runCase(kase)
    if (status !== 'PASS') failures.push(`a ${id} edit to ${file} must NOT expire the verdict, got ${status}: ${note}`)
  }
  if (withReview(null, surface) !== 'UNBUILT') failures.push(`an absent review must leave the group UNBUILT, got ${withReview(null, surface)}`)
  return failures
}

// The rows this canary exercises, named, so the run can SHOW that a given row ran. A canary that
// prints only "OK" cannot answer "was the surface-bound design-review row exercised?".
export const CANARY_ROWS = [
  'assertDeterminism: an unguarded async construct FAILs; a clean lane PASSes',
  'assertVisualParity + assertFunctionalParity: VOID when determinism fails, PASS when it does not',
  'assertDesignReview: a verdict that binds no surface is REFUSED, and the refusal says so (ticket 138 slice 6)',
  'assertDesignReview: a verdict whose reference moved FAILs rather than reading as absent',
  'assertDesignReview: no record at all is UNBUILT',
  'assertDesignReview: a surface-bound approval whose files are unchanged PASSes (ticket 138 slice 1)',
  'assertDesignReview: a surface-bound verdict whose file changed FAILs and names ONLY that file (ticket 138 slice 1)',
  ...SURFACE_CLASSES.map(([label]) => `assertDesignReview: the derived surface reaches ${label}, and moving it expires the verdict by name (ticket 138 slice 3)`),
  'assertDesignReview: a comment-only edit to a real projection source moves no digest, and swapping its rendered element does (ticket 138 slice 3)',
  'assertDesignReview: a comment-only edit to a real projection does NOT expire the verdict (ticket 138 slice 4)',
  'assertDesignReview: renaming a private helper in a real projection does NOT expire the verdict (ticket 138 slice 4)',
  'assertDesignQuality: an EXPIRED review takes a fully green group off green; an absent one leaves it UNBUILT',
]
