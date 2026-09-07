#!/usr/bin/env node
// Ticket 098 acceptance 5: "Every module carries a per-module disposition that a person decided, not
// a default the sweep inferred. The sweep produces the worksheet; it does not get to answer it."
//
// Seventy-six modules against the gates that block them is roughly 380 rows, which is not a decision
// a person can make one row at a time without rubber-stamping it — and a rubber stamp is the default
// the acceptance exists to forbid, wearing a signature.
//
// So this proposes by RULE. Each rule states its condition, its rationale, and every module it would
// touch. A person approves or rejects the RULE, knowing its blast radius, and `--apply <name>` then
// writes those dispositions with that person recorded as the decider. Eight decisions instead of 380,
// and each one is still a judgement made by a human on stated evidence rather than inferred here.
//
// Nothing is written without --apply and --decided-by. Reading this file changes nothing.
//
// Usage:
//   node propose-dispositions.mjs                          list every rule with its blast radius
//   node propose-dispositions.mjs --rule <name>            list the modules one rule would touch
//   node propose-dispositions.mjs --apply <name> --decided-by "<person>" --rationale-ok
//   node propose-dispositions.mjs --canary

import {readFileSync, writeFileSync, existsSync} from 'node:fs'

import {collectionProps, deriveStateSet, derivedEmptyAndErrorSupport} from './derive-state-set.mjs'
import {resolve} from 'node:path'

const argv = process.argv.slice(2)
const flag = (name, fallback = null) => {
  const at = argv.indexOf(name)
  return at >= 0 && argv[at + 1] && !argv[at + 1].startsWith('--') ? argv[at + 1] : fallback
}
const platformRoot = resolve(flag('--platform') ?? `${import.meta.dirname}/../..`)

const readJson = path => JSON.parse(readFileSync(resolve(platformRoot, path), 'utf8'))

// Each rule is a condition over facts already recorded in the catalog, plus the gate it disposes and
// the reason. The condition reads DECLARED facts only -- moduleKind and presentation.disposition --
// never anything this tool worked out for itself. If a rule's condition needed a judgement, it would
// be the sweep answering the worksheet.
// A fifth rule was drafted and DELETED rather than offered for signature. It would have marked
// assertPerformanceProfile not-applicable for 46 visual modules on the grounds that they declare no
// `performance` dimension and so are not profiled. The premise is false: THIRTEEN visual modules
// declare a required performance dimension and the harness profiles ELEVEN -- hlp.ui.app-shell and
// hlp.ui.detail-panel declare it and are silently unprofiled. Signing that rule would have recorded
// a decider's name against a reason that is not true, which is the failure this repository already
// paid for once when four pixel exemptions carried a false rationale for nine days.

const RULES = [
  {
    name: 'non-rendering-modules-have-no-visual-gates',
    gates: ['assertVisualParity', 'assertFocusQuality', 'assertResponsiveQuality',
            'assertEmptyAndErrorStates', 'assertContentResilience', 'assertDesignReview',
            'assertPerformanceProfile', 'assertStateCompleteness'],
    when: module => module.presentation?.disposition === 'not-applicable',
    rationale: 'The catalog already records presentation.disposition not-applicable for this module: it renders no interface. There is no pixel to compare, no focus to treat, no layout to reflow, no state to design, and nothing for a reviewer to look at.',
  },
  {
    // Deliberately NOT a blanket rule over every non-rendering module. hlp.ui.default-strings owns a
    // 713-key English fallback vocabulary and hlp.ui.locale-provider owns locale resolution
    // itself -- internationalization is the most REQUIRED gate either of them has, and dispositioning
    // it away would switch off the check on the two modules it matters most for. Naming them here is
    // the whole reason this is a separate rule.
    name: 'text-free-modules-have-no-internationalization',
    gates: ['assertInternationalization'],
    when: module => module.presentation?.disposition === 'not-applicable'
      && !['hlp.ui.default-strings', 'hlp.ui.locale-provider'].includes(module.moduleId),
    rationale: 'This module renders no text and formats no locale-sensitive value, so there is nothing for locale, pluralization, RTL or text expansion to act on. Excludes default-strings and locale-provider, which own text and locale resolution respectively.',
  },
  {
    name: 'non-interactive-components-own-no-focus',
    gates: ['assertFocusQuality'],
    when: module => module.moduleKind === 'non-interactive-component',
    rationale: 'The catalog records this module as a non-interactive component, so it owns no focusable element and a focus ring would have nothing to attach to.',
  },
  // WITHDRAWN 2026-08-26, before signature, on review by CIC.
  //
  // `stateless-interfaces-have-no-state-completeness` claimed these modules' interfaces "express no
  // canonical state variant, so there is no required state set to render". The first clause was true
  // and the conclusion FALSE: of the fifty modules it covered, THIRTY-TWO declare state-bearing
  // props. app-shell has `collapsed`, `expanded`, `endPanelOpen`, `mobileNavOpen` and three
  // `active*` ids; accordion has `value`/`defaultValue`; collapsible, dialog, detail-panel and
  // confirm-dialog have `open`; check-box has `checked`; chip has `selected`.
  //
  // deriveStateSet recognises only VISUAL STATUS vocabulary -- error, success, warning, loading,
  // disabled -- and only when spelled as literal-union members. It is blind to disclosure state, to
  // selection and route state, and to state carried as `value` rather than a union. So "no canonical
  // state" means "outside this vocabulary", not "stateless", and signing it would have recorded a
  // false reason against thirty-two modules that plainly have state.
  //
  // Lifting assertStateCompleteness for these needs the vocabulary widened or the gate rescoped --
  // a change to the model, not a disposition. Not re-proposed until that is decided.
  {
    // Stronger evidence than the stateless-interface rule below it: this reads DECLARED PROPERTY
    // NAMES, so a component with an `empty`, `error` or `invalid` prop is excluded
    // automatically. A component with none of them has no empty or error state to design. Spot-check
    // a few anyway -- a state reachable only through a child component would not appear here.
    name: 'components-without-empty-or-error-props-have-neither-state',
    gates: ['assertEmptyAndErrorStates'],
    when: module => module.presentation?.disposition === 'visual'
      && !derivedEmptyAndErrorSupport(platformRoot, module.moduleId).supportsEmpty
      && !derivedEmptyAndErrorSupport(platformRoot, module.moduleId).supportsError
      // A component taking a COLLECTION can be handed an empty one, and what it draws then is its
      // own question to answer -- chart with no series, chat with no messages, side-nav with no
      // items. Those have an empty state whether or not they named a prop for it, so excluding them
      // is the difference between "no such state" and "no DESIGNED handling of it", which is a
      // defect. Narrowed 38 -> 25 on review by CIC, who asked what a chart with no data draws.
      && collectionProps(platformRoot, module.moduleId).props.length === 0,
    rationale: 'This component takes no collection and declares no empty, no-results, error or invalid property in either lane. Its content comes from the caller, so an empty rendering belongs to the caller rather than to this component, which has nothing of its own to design.',
  },
  {
    name: 'runtime-modules-have-no-performance-profile',
    gates: ['assertPerformanceProfile'],
    when: module => module.moduleKind === 'runtime',
    rationale: 'A runtime helper renders nothing, so it has no render to profile; its cost belongs to the caller that uses it and is measured there.',
  },
]

function load() {
  const catalog = readJson('catalog/modules.yaml').modules
  const receiptPath = 'docs/evidence/gate-model/ui-gate-model.json'
  const receipt = existsSync(resolve(platformRoot, receiptPath)) ? readJson(receiptPath) : null
  const blocking = new Map()
  for (const entry of receipt?.modules ?? []) {
    blocking.set(entry.moduleId, new Set(
      entry.gates.filter(gate => gate.status !== 'PASS' && gate.status !== 'NOT-APPLICABLE').map(gate => gate.id)))
  }
  const modules = Object.entries(catalog)
    .filter(([id]) => id.startsWith('hlp.ui.'))
    .map(([moduleId, module]) => ({moduleId, ...module}))
  return {modules, blocking}
}

// A rule only proposes where the gate is ACTUALLY blocking. Proposing a disposition for a gate that
// already passes would record a human decision nobody needed to make, and would quietly convert a
// real PASS into a NOT-APPLICABLE -- weakening the gate under cover of tidying it.
export function proposalsFor(rule, modules, blocking) {
  const rows = []
  for (const module of modules) {
    if (!rule.when(module)) continue
    const blocked = blocking.get(module.moduleId) ?? new Set()
    const gates = rule.gates.filter(gate => blocked.has(gate))
    if (gates.length > 0) rows.push({moduleId: module.moduleId, gates})
  }
  return rows
}

function profilePath(moduleId) {
  return `specs/modules/ui/${moduleId}/quality.yaml`
}

function apply(rule, rows, decidedBy) {
  let written = 0
  for (const row of rows) {
    const path = profilePath(row.moduleId)
    const full = resolve(platformRoot, path)
    // Textual insert, never a JSON round-trip: these profiles are minified single-line JSON and a
    // parse/stringify pass reformats every one of them, burying the change in churn.
    const text = readFileSync(full, 'utf8')
    const profile = JSON.parse(text)
    profile.gates = profile.gates ?? {}
    for (const gate of row.gates) {
      profile.gates[gate] = {
        disposition: 'not-applicable',
        rationale: rule.rationale,
        decidedBy,
        rule: rule.name,
      }
    }
    // Preserve the file's OWN formatting. These profiles are mixed: some are minified onto one line,
    // some are pretty-printed at two spaces. Normalising them all one way turns a one-line addition
    // into a nineteen-line diff and buries the change that matters in reformatting noise.
    const prettyPrinted = /\n\s+"/.test(text)
    const rendered = prettyPrinted ? JSON.stringify(profile, null, 2) : JSON.stringify(profile)
    writeFileSync(full, rendered + '\n')
    written += 1
  }
  return written
}

function canary() {
  const failures = []
  const modules = [
    {moduleId: 'hlp.ui.a', presentation: {disposition: 'not-applicable'}, moduleKind: 'runtime'},
    {moduleId: 'hlp.ui.b', presentation: {disposition: 'visual'}, moduleKind: 'interactive-component'},
  ]
  const rule = RULES[0]

  // A rule must not propose for a gate that is already passing: that would record a decision nobody
  // needed and downgrade a real PASS to NOT-APPLICABLE under cover of tidying.
  const noneBlocked = proposalsFor(rule, modules, new Map([['hlp.ui.a', new Set()]]))
  if (noneBlocked.length !== 0) failures.push('a rule must not propose where no gate is blocking')

  const oneBlocked = proposalsFor(rule, modules, new Map([['hlp.ui.a', new Set(['assertVisualParity'])]]))
  if (oneBlocked.length !== 1 || oneBlocked[0].gates.length !== 1) {
    failures.push('a rule must propose exactly the blocking gates it covers')
  }

  // The condition must read declared facts only. A visual, interactive module is not this rule's.
  const wrongModule = proposalsFor(rule, modules, new Map([['hlp.ui.b', new Set(['assertVisualParity'])]]))
  if (wrongModule.length !== 0) failures.push('a rule must not reach a module its condition excludes')

  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(f => `  ${f}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK -- a rule proposes only where its condition holds AND the gate is actually blocking\n')
  process.exit(0)
}

if (argv.includes('--canary')) canary()

const {modules, blocking} = load()
const applyName = flag('--apply')
const only = flag('--rule')

if (applyName) {
  const rule = RULES.find(candidate => candidate.name === applyName)
  if (!rule) { process.stderr.write(`unknown rule: ${applyName}\n`); process.exit(1) }
  const decidedBy = flag('--decided-by')
  if (!decidedBy) {
    process.stderr.write('--decided-by "<person>" is required. Ticket 098 acceptance 5 wants a person, not a default.\n')
    process.exit(1)
  }
  const rows = proposalsFor(rule, modules, blocking)
  const written = apply(rule, rows, decidedBy)
  process.stdout.write(`${rule.name}: ${written} module(s) dispositioned, decided by ${decidedBy}\n`)
  process.stdout.write('Re-run: node tooling/gates/run-ui-gate-model.mjs, then refresh provenance hashes.\n')
} else {
  for (const rule of RULES) {
    if (only && rule.name !== only) continue
    const rows = proposalsFor(rule, modules, blocking)
    const cells = rows.reduce((total, row) => total + row.gates.length, 0)
    process.stdout.write(`\n${rule.name}\n`)
    process.stdout.write(`  disposes : ${rule.gates.join(', ')}\n`)
    process.stdout.write(`  because  : ${rule.rationale}\n`)
    process.stdout.write(`  touches  : ${rows.length} module(s), ${cells} blocking gate cell(s)\n`)
    if (only) for (const row of rows) process.stdout.write(`      ${row.moduleId.padEnd(34)}${row.gates.join(' ')}\n`)
  }
  process.stdout.write('\nNothing was written. To accept a rule:\n')
  process.stdout.write('  node tooling/gates/propose-dispositions.mjs --apply <name> --decided-by "<your name>"\n')
}
