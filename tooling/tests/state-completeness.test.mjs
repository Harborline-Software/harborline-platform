// assertStateCompleteness could not return PASS at all until 2026-08-25: both branches of its
// verdict returned PARTIAL, so every module whose interface derives a canonical state set was
// permanently non-terminal no matter how many scenarios anyone wrote. The verdict's own note said
// what was missing — "a structural coverage check needs the scenario to declare which state it
// renders, which is a fixture change, not a tool change" — and these pin that change.
//
// The distinction under test throughout: DECLARED is not RENDERED. A scenario's `states` array only
// counts when a story draws that scenario in both lanes, which is what the caller passes in.

import assert from 'node:assert/strict'
import test from 'node:test'

import {fileURLToPath} from 'node:url'
import {dirname, resolve} from 'node:path'

import {collectionProps, declarationBodies, deriveModeSet, deriveStateSet, stateBearingProps, stateCompletenessVerdict, derivedEmptyAndErrorSupport} from '../gates/derive-state-set.mjs'

const platformRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

const derived = states => ({derived: states, react: states, blazor: states, laneAgreement: true, unmapped: []})

test('a derivation that produced nothing is UNBUILT, never a vacuous PASS', () => {
  assert.deepEqual(stateCompletenessVerdict({derived: null, reason: 'no interface'}, [], null)[0], 'UNBUILT')
  assert.deepEqual(stateCompletenessVerdict(derived([]), [], null)[0], 'UNBUILT')
})

test('lanes deriving different state sets FAIL, and the sets are named', () => {
  const [status, note] = stateCompletenessVerdict(
    {derived: ['default'], react: ['default'], blazor: ['default', 'error'], laneAgreement: false, unmapped: []}, [], null)
  assert.equal(status, 'FAIL')
  assert.match(note, /react=/)
  assert.match(note, /blazor=/)
})

test('every derived state declared by a rendered scenario PASSES', () => {
  const [status, note] = stateCompletenessVerdict(derived(['default', 'error']), [], ['default', 'error'])
  assert.equal(status, 'PASS')
  assert.match(note, /drawn in both lanes/)
})

test('extra declared states do not break coverage', () => {
  // A scenario may render more than the type surface names; coverage asks only that the derived set
  // is covered, not that the declaration matches it exactly.
  assert.equal(stateCompletenessVerdict(derived(['default']), [], ['default', 'loading'])[0], 'PASS')
})

test('partial coverage is PARTIAL and names exactly what is unrendered', () => {
  const [status, note] = stateCompletenessVerdict(derived(['default', 'error', 'warning']), [], ['default'])
  assert.equal(status, 'PARTIAL')
  assert.match(note, /"error"/)
  assert.match(note, /"warning"/)
  assert.doesNotMatch(note, /leaving \[\]/)
})

test('no declaration at all falls back to the weaker name match, and says it is weaker', () => {
  // The fallback must not be mistaken for the structural check. An absent declaration and a
  // declaration of nothing are different claims, which is why the caller passes null rather than [].
  const [status, note] = stateCompletenessVerdict(derived(['default']), ['x.default default'], null)
  assert.equal(status, 'PARTIAL')
  assert.match(note, /substring match/)
})

test('a declaration on a scenario no lane draws cannot reach the verdict', () => {
  // Enforced by the caller: renderedStates is built only from scenarios drawn in BOTH lanes, so an
  // undrawn scenario contributes nothing and the verdict sees null. This asserts the contract that
  // makes that meaningful — passing [] must NOT read as full coverage.
  const [status] = stateCompletenessVerdict(derived(['default']), [], [])
  assert.notEqual(status, 'PASS', 'an empty coverage set must never satisfy a non-empty derived set')
})

// `hlp.ui.chat` draws a designed empty state at Chat.tsx:130. The shared vocabulary scanner now
// requires its declaration to use the canonical `empty` name, and the state derivation must see it.
test('a designed empty state named empty is support, not absence', () => {
  const support = derivedEmptyAndErrorSupport(platformRoot, 'hlp.ui.chat')
  assert.equal(support.read, true, 'chat sources must be readable for this to mean anything')
  assert.equal(support.supportsEmpty, true)
  assert.ok(support.props.includes('empty'))
})

// The canary: a module with no such prop must still derive as unsupported, or the widened pattern is
// matching everything and the gate has stopped discriminating.
test('canonical empty matching does not make every module claim an empty state', () => {
  const support = derivedEmptyAndErrorSupport(platformRoot, 'hlp.ui.separator')
  assert.equal(support.supportsEmpty, false)
})

// Both gates used to report a truth as a falsehood. assertEmptyAndErrorStates said chart "expresses
// neither an empty nor an error state; nothing to render", and assertStateCompleteness said app-shell
// "expresses no canonical state" -- while chart falls back to a 0-1 axis on an empty series and
// app-shell declares collapsed/expanded/endPanelOpen/active*. Nothing passed falsely, but the note is
// the evidence a reviewer reads, and a gate that records a false reason is worse than one that
// records none. These pin the notes, not just the verdicts.
test('a collection-bearing component is told its empty state is undesigned, not absent', () => {
  const props = collectionProps(platformRoot, 'hlp.ui.chart').props
  assert.ok(props.includes('slices'), `chart must derive a collection prop, got ${JSON.stringify(props)}`)
})

test('a container with nothing to be empty is distinguished from one that has a collection', () => {
  assert.deepEqual(collectionProps(platformRoot, 'hlp.ui.separator').props, [])
})

test('state outside the canonical vocabulary is named in the note, not reported as statelessness', () => {
  const outside = stateBearingProps(platformRoot, 'hlp.ui.app-shell').props
  // The four disclosure props a caller can actually control. `endPanelOpen` and `mobileNavOpen` are
  // here because the pattern matches camelCase WORDS: a name-prefix match found neither, since
  // neither name begins with its state word.
  for (const expected of ['collapsed', 'endPanelOpen', 'endPanelExpanded', 'mobileNavOpen', 'activeItemId']) {
    assert.ok(outside.includes(expected), `app-shell declares ${expected}; got ${JSON.stringify(outside)}`)
  }
  // NOT `expanded`. AppShell.tsx:146 declares it on an inline type for the private EndPanel helper,
  // which is not the module's surface -- reading raw source text credited app-shell with a prop no
  // caller can pass. Only `interface`/`type` bodies are read now.
  assert.ok(!outside.includes('expanded'), 'expanded belongs to the private EndPanel helper')
  const [status, note] = stateCompletenessVerdict(derived([]), [], null, outside)
  assert.equal(status, 'UNBUILT')
  assert.match(note, /neither axis can name/)
  assert.match(note, /collapsed/)
})

// An object-literal ENTRY is not a declaration. ConversationList.tsx:230 spells
// `empty: t('ai.conversations.empty')` -- a value in an object, not an interface member -- and the
// old raw-source scan credited the module with declaring it.
test('an object-literal entry is not read as a declared prop', () => {
  const bodies = declarationBodies("const labels = {\n  empty: t('x'),\n}\nexport interface P {\n  real?: string\n}\n")
  assert.equal(bodies.length, 1, 'only the interface body is a declaration')
  assert.match(bodies[0], /real/)
  assert.ok(!bodies[0].includes('empty'), 'the object literal must not be read')
})

// The Blazor lane is read too. Before this, these helpers read React only while deriveStateSet read
// both, so their notes were claims about one lane presented as claims about the module.
test('the Blazor lane is read, and its spelling is reconciled with React', () => {
  const collapsible = stateBearingProps(platformRoot, 'hlp.ui.collapsible')
  assert.ok(collapsible.blazor !== null, 'HarborlineCollapsible.razor declares [Parameter] bool? Open')
  assert.ok(collapsible.blazor.includes('open'), `Blazor spells it Open; got ${JSON.stringify(collapsible.blazor)}`)
  assert.ok(collapsible.react.includes('open'), 'React spells it open')
})

// `[Parameter]` is what makes a Blazor property public. Without requiring it, action-menu's
// `public bool IsOpen {get; private set;}` -- internal state -- read as a controllable mode and
// produced a false lane disagreement against React, which holds the same thing in useState.
// The attribute list form `[Parameter, EditorRequired]` counts: missing it produced a second one.
test('internal Blazor state is not mistaken for a controllable parameter', () => {
  assert.equal(deriveModeSet(platformRoot, 'hlp.ui.action-menu').laneAgreement, true)
  assert.equal(deriveModeSet(platformRoot, 'hlp.ui.switch-field').laneAgreement, true)
})

// The MODE axis itself. A boolean disclosure prop means BOTH renderings are owed, not one.
test('a boolean disclosure prop owes both renderings', () => {
  assert.deepEqual(deriveModeSet(platformRoot, 'hlp.ui.collapsible').derived, ['collapsed', 'expanded'])
})

// The canary for that axis: a collection named `collapsedGroups` is not a mode, and a keybinding
// named `openSearchShortcut` is not one either. Both matched an earlier substring attempt.
test('a collection or a keybinding is not a mode state', () => {
  assert.deepEqual(deriveModeSet(platformRoot, 'hlp.ui.separator').derived, [])
  const grid = stateBearingProps(platformRoot, 'hlp.ui.data-grid').props
  assert.ok(!grid.includes('collapsedGroups'), 'collapsedGroups is a string[] of ids, not a state')
  const shell = stateBearingProps(platformRoot, 'hlp.ui.app-shell').props
  assert.ok(!shell.includes('openSearchShortcut'), 'openSearchShortcut is a keybinding string')
})

// Both axes are checked, and neither can be satisfied by the other. This is the whole reason mode is
// a separate axis rather than more words in CANONICAL.
test('covering the status axis does not satisfy the mode axis', () => {
  const status = derived(['error'])
  const mode = {derived: ['collapsed', 'expanded'], react: null, blazor: null, laneAgreement: null}
  const [drawnError, note] = stateCompletenessVerdict(status, [], ['error'], [], mode)
  assert.equal(drawnError, 'PARTIAL', 'error alone must not pass a module that also has a mode')
  assert.match(note, /mode/)
  const [both] = stateCompletenessVerdict(status, [], ['error', 'collapsed', 'expanded'], [], mode)
  assert.equal(both, 'PASS')
})

// A real cross-lane divergence must still FAIL, and the note must name what diverged.
//
// The fixture is synthetic on purpose. This test used to read hlp.ui.user-menu, which genuinely
// diverged -- open/defaultOpen/onOpenChange in React against a `private bool open` in Blazor. That
// made the module's defect the test's fixture, so repairing the module (ticket 133) broke a test of
// the READER while the reader was working perfectly. A behaviour test must not depend on a defect
// surviving in the tree; the tree is supposed to stop containing defects.
test('a genuine lane divergence in mode state FAILS', () => {
  const diverging = {
    derived: ['collapsed', 'expanded'], react: ['collapsed', 'expanded'], blazor: [],
    laneAgreement: false, unmapped: [],
  }
  const [status, note] = stateCompletenessVerdict(derived([]), [], null, [], diverging)
  assert.equal(status, 'FAIL')
  assert.match(note, /different mode states/)
  assert.match(note, /collapsed/)
})

// And the module that used to BE that fixture now agrees. This is the regression guard the test
// above can no longer be, and it fails if the Blazor disclosure parameters are ever taken away.
test('hlp.ui.user-menu publishes the same disclosure API in both lanes', () => {
  const userMenu = deriveModeSet(platformRoot, 'hlp.ui.user-menu')
  assert.equal(userMenu.laneAgreement, true)
  assert.deepEqual(userMenu.derived, ['collapsed', 'expanded'])
  assert.deepEqual(userMenu.blazor, ['collapsed', 'expanded'])
})

// The canary. Without it the branch above could report every module as carrying unnameable state,
// which would read as thorough while discriminating nothing.
test('a genuinely stateless module keeps the plain note', () => {
  assert.deepEqual(stateBearingProps(platformRoot, 'hlp.ui.separator').props, [])
  const [, note] = stateCompletenessVerdict(derived([]), [], null, [])
  assert.match(note, /declares no state-bearing property/)
})

// hlp.ui.chip declares ChipThemeColor as a multi-line union with leading pipes. Requiring the first
// literal to follow `=` directly derived an EMPTY React set against a full Blazor enum and reported
// a lane DISAGREEMENT for a module whose lanes agree exactly. It stayed hidden because an empty
// derivation short-circuited to UNBUILT before the agreement check ever ran; adding the mode axis
// moved chip past that early return and exposed it.
test('a multi-line union with leading pipes is read, in both lanes', () => {
  const chip = deriveStateSet(platformRoot, 'hlp.ui.chip')
  assert.deepEqual(chip.react, ['default', 'error', 'success', 'warning'])
  assert.equal(chip.laneAgreement, true, 'chip lanes declare the same states and must not read as divergent')
})
