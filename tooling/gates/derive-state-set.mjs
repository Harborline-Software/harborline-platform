// Vendored from harborline-control/tools/derive-state-set.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 ruling 8: the required state set is DERIVED from the component interface -- the TS and
// C# types, not a schema file and not a hand-written list. `assertStateCompleteness` had no
// derivation at all; it could only ask whether a module happened to declare a scenario whose surface
// contained the word "state", which is a spelling check, not a completeness check.
//
// What is derivable is which of the model's canonical states this component's own type surface can
// EXPRESS. A badge whose only variant union is default|secondary|info|success|warning|danger cannot
// express "disabled" or "loading", and demanding a disabled scenario for it would be noise. A toaster
// whose variant union includes "loading" can, and must.

import {existsSync, readFileSync, readdirSync} from 'node:fs'
import {resolve} from 'node:path'

// From ticket 098's assertStateCompleteness row, verbatim.
const CANONICAL = ['default', 'hover', 'active', 'focus', 'disabled', 'selected', 'loading', 'success', 'warning', 'error', 'read-only']

// Synonyms a real type surface uses for a canonical state. Closed on purpose: an unrecognised union
// member is reported, never silently mapped, because a silent map is how a derivation becomes a
// guess. `danger` is the one this pair actually needed.
const SYNONYMS = {
  danger: 'error',
  destructive: 'error',
  information: 'default',
  info: 'default',
  readonly: 'read-only',
  disable: 'disabled',
  busy: 'loading',
  pending: 'loading',
}

// hover / active / focus are NOT derived, and the attempt to derive them was removed after it
// produced two false failures in a row: it read hlp.ui.badge's "extends HTMLAttributes<HTMLSpanElement>"
// as interactivity (a badge is a span), and then read hlp.ui.toaster's React lane as non-interactive
// because that lane expresses its actions through a nested ToastAction type while the Blazor lane
// uses @onclick in markup. Both were detector artifacts reported as lane disagreements.
//
// Interactivity is not reliably derivable from a regex across TypeScript and Razor, and a
// completeness gate that manufactures findings is worse than one that admits its scope. This
// derives ONLY what the type surface states -- enum and union members. Pseudo-states are CSS
// concerns and belong to assertFocusQuality, which is separately UNBUILT and honestly so.
// Reads every non-test .ts/.tsx in the module's src, the way blazorSurface reads every .cs/.razor in
// the module directory. Reading only src/<Name>.tsx was the asymmetry that made hlp.ui.schema-form
// FAIL on ticket 381: its rule value state union lives in SchemaForm.types.ts, the Blazor lane
// declares the same three members in SchemaFormTypes.cs, and one reader saw a lane the other did not
// -- reported as a lane DISAGREEMENT, which is the false-failure mode this file warns about twice
// above.
function reactSurface(platformRoot, moduleId) {
  const path = resolve(platformRoot, `projections/react/ui/${moduleId}/src`)
  if (!existsSync(path)) return null
  let source = ''
  for (const entry of readdirSync(path, {withFileTypes: true})) {
    if (entry.isFile() && /\.tsx?$/.test(entry.name) && !/\.test\.|test-setup/.test(entry.name)) {
      source += readFileSync(resolve(path, entry.name), 'utf8')
    }
  }
  const members = new Set()
  // The leading-pipe multi-line form counts. hlp.ui.chip declares ChipThemeColor as `=\n  | 'base'\n
  // | 'primary' ...`, and requiring the first literal to follow `=` directly derived an EMPTY React
  // set against a full Blazor enum -- reported as a lane DISAGREEMENT for a module whose lanes agree
  // exactly. It stayed hidden while an empty derivation short-circuited to UNBUILT before the
  // agreement check; adding the mode axis moved chip past that early return and exposed it.
  for (const match of source.matchAll(/export type \w+\s*=\s*\|?\s*((?:'[^']*'\s*\|\s*)*'[^']*')/g)) {
    for (const literal of match[1].matchAll(/'([^']*)'/g)) members.add(literal[1])
  }
  // No union at all is NOT an empty state set: it means this reader found nothing to read, which is
  // what blazorSurface says with its sawEnum check. hlp.ui.schema-form holds its rule value state
  // vocabulary in @harborline-software/rule-engine and @harborline-platform/hlp.ui.form-view -- the
  // Blazor lane must mirror those contracts locally (SchemaFormRuleValueState, SchemaFormVisibility)
  // because C# has no structural import of a union -- so the React lane declared nothing of its own
  // and the module FAILed with react=[] blazor=["error","loading"]. Both lanes draw and test both
  // states (schema-form.submit-blocked in each), so that was a reader gap reported as a lane
  // disagreement: exactly the false failure this file warns about twice above (ticket 381).
  return members.size > 0 ? {members: [...members], path} : null
}

// Reads every .cs and .razor in the module directory rather than a hardcoded file list. The first
// draft looked only at ToastTypes.cs / Types.cs / Enums.cs, missed hlp.ui.badge's BadgeTypes.cs, and
// derived an EMPTY set for the Blazor lane -- which then reported a lane DISAGREEMENT. A reader that
// cannot find a lane's types must say so; deriving nothing and calling it a difference is a false
// failure, and a false failure in a completeness gate is worse than no gate.
function blazorSurface(platformRoot, moduleId) {
  const dir = resolve(platformRoot, `projections/blazor/ui/${moduleId}`)
  if (!existsSync(dir)) return null
  const members = new Set()
  let sawEnum = false
  for (const entry of readdirSync(dir, {withFileTypes: true})) {
    if (!entry.isFile() || !/\.(cs|razor)$/.test(entry.name)) continue
    const source = readFileSync(resolve(dir, entry.name), 'utf8')
    for (const enumBlock of source.matchAll(/enum\s+\w+\s*\{([^}]*)\}/g)) {
      sawEnum = true
      for (const member of enumBlock[1].split(',')) {
        const name = member.trim().split(/\s|=/)[0]
        if (name) members.add(name.toLowerCase())
      }
    }
  }
  return sawEnum ? {members: [...members]} : null
}

function statesFrom(surface) {
  if (!surface) return null
  const states = new Set()
  const unmapped = []
  for (const raw of surface.members) {
    const member = raw.toLowerCase()
    const mapped = SYNONYMS[member] ?? member
    if (CANONICAL.includes(mapped)) states.add(mapped)
    else unmapped.push(raw)
  }
  return {states: [...states].sort(), unmapped: [...new Set(unmapped)].sort()}
}

export function deriveStateSet(platformRoot, moduleId) {
  const react = statesFrom(reactSurface(platformRoot, moduleId))
  const blazor = statesFrom(blazorSurface(platformRoot, moduleId))
  if (!react && !blazor) return {derived: null, reason: 'no component interface found in either lane'}
  const laneAgreement = react && blazor
    ? JSON.stringify(react.states) === JSON.stringify(blazor.states)
    : null
  return {
    derived: (react ?? blazor).states,
    react: react?.states ?? null,
    blazor: blazor?.states ?? null,
    laneAgreement,
    unmapped: [...new Set([...(react?.unmapped ?? []), ...(blazor?.unmapped ?? [])])].sort(),
  }
}

// ---------------------------------------------------------------------------
// Declaration reading, BOTH lanes.
//
// Two defects fixed here on 2026-08-26, both found by CIC reading the notes these produce:
//
// 1. Scanning raw source text for `name:` matched object-literal ENTRIES as well as property
//    DECLARATIONS. ConversationList.tsx:230 spells `empty: t('ai.conversations.empty')` -- a value,
//    not an interface member -- so a component could be credited with a prop it never declares.
//    These now read only the bodies of `interface X { ... }` and `type X = { ... }`.
//
// 2. The earlier helpers read the REACT lane only while deriveStateSet read both and reported
//    laneAgreement, so their notes were claims about one lane presented as claims about the module.
//    Both lanes are read now, and disagreement is reported rather than averaged away.
export function declarationBodies(source) {
  const bodies = []
  const opener = /(?:export\s+)?(?:interface\s+\w+(?:<[^>]*>)?(?:\s+extends\s+[^{]+?)?|type\s+\w+(?:<[^>]*>)?\s*=[^={;]*?)\s*\{/g
  for (const match of source.matchAll(opener)) {
    let depth = 1
    let index = match.index + match[0].length
    const start = index
    while (index < source.length && depth > 0) {
      const character = source[index]
      if (character === '{') depth += 1
      else if (character === '}') depth -= 1
      index += 1
    }
    if (depth === 0) bodies.push(source.slice(start, index - 1))
  }
  return bodies
}

const MEMBER = /^[ \t]*(?:readonly[ \t]+)?([A-Za-z][A-Za-z0-9]*)\??[ \t]*:[ \t]*([^\n;,]+)/gm

export function reactProps(platformRoot, moduleId) {
  const directory = resolve(platformRoot, `projections/react/ui/${moduleId}/src`)
  if (!existsSync(directory)) return null
  const props = []
  for (const entry of readdirSync(directory, {withFileTypes: true})) {
    if (!entry.isFile() || !/\.tsx?$/.test(entry.name)) continue
    for (const body of declarationBodies(readFileSync(resolve(directory, entry.name), 'utf8'))) {
      for (const member of body.matchAll(MEMBER)) props.push({name: member[1], type: member[2].trim(), parameter: true})
    }
  }
  return props.length > 0 ? props : null
}

// The C# half. Blazor declares the same surface two ways: `[Parameter] public T Name { get; set; }`
// and positional records -- hlp.ui.chart's ChartTypes.cs:5 is
// `record LineChartSeries(string Name, IReadOnlyList<double?> Values)`. Both are read.
// Non-visual UI contracts use the same C# vocabulary from their dotnet foundation projection, so
// that directory is the fallback when a module has no Blazor adapter.
//
// Returns null rather than [] when nothing is found, for the reason blazorSurface above gives: a
// reader that cannot find a lane's types must say so, because deriving nothing and calling it a lane
// DIFFERENCE is a false failure, and a false failure in a completeness gate is worse than no gate.
const CS_TYPE = String.raw`[A-Za-z_][A-Za-z0-9_.]*(?:<[^<>]*(?:<[^<>]*>[^<>]*)*>)?(?:\?|\[\])*`

export function blazorProps(platformRoot, moduleId) {
  const directory = [
    resolve(platformRoot, `projections/blazor/ui/${moduleId}`),
    resolve(platformRoot, `projections/dotnet/foundation/${moduleId}`),
  ].find(candidate => existsSync(candidate))
  if (!directory) return null
  const property = new RegExp(String.raw`public\s+(?:required\s+|virtual\s+|override\s+|sealed\s+)*(${CS_TYPE})\s+([A-Za-z_]\w*)\s*\{\s*get\s*;`, 'g')
  const positional = /record\s+(?:sealed\s+|class\s+|struct\s+)*\w+\s*\(([^)]*)\)/g
  const parameter = new RegExp(String.raw`^(${CS_TYPE})\s+([A-Za-z_]\w*)(?:\s*=\s*.+)?$`)
  const props = []
  for (const entry of readdirSync(directory, {withFileTypes: true})) {
    if (!entry.isFile() || !/\.(cs|razor)$/.test(entry.name)) continue
    const source = readFileSync(resolve(directory, entry.name), 'utf8')
    for (const match of source.matchAll(property)) {
      // `[Parameter]` is what makes a property the component's PUBLIC surface. Without this check,
      // hlp.ui.action-menu's `public bool IsOpen {get; private set;}` -- internal state a caller
      // cannot set -- was read as a controllable mode and reported as a lane disagreement against
      // React, which holds the same thing in useState. A false lane FAIL is exactly the failure this
      // file warns about two readers above, so the attribute is required rather than assumed.
      // `[Parameter, EditorRequired]` and stacked attribute lists both count -- matching a bare
      // `[Parameter]` missed hlp.ui.switch-field's Checked and produced a second false disagreement.
      const preceding = source.slice(Math.max(0, match.index - 120), match.index)
      props.push({name: match[2], type: match[1], parameter: /\[Parameter[^\]]*\]\s*(?:\[[^\]]*\]\s*)*$/.test(preceding)})
    }
    for (const record of source.matchAll(positional)) {
      // Split on commas OUTSIDE angle brackets, so `Dictionary<string, int> Map` stays one parameter
      // instead of becoming two malformed ones.
      const parts = []
      let depth = 0
      let current = ''
      for (const character of record[1]) {
        if (character === '<') depth += 1
        else if (character === '>') depth -= 1
        if (character === ',' && depth === 0) {
          parts.push(current)
          current = ''
        } else current += character
      }
      parts.push(current)
      for (const part of parts) {
        const member = part.trim().match(parameter)
        if (member) props.push({name: member[2], type: member[1], parameter: true})
      }
    }
  }
  return props.length > 0 ? props : null
}

// React spells a prop `collapsed`, Blazor spells it `Collapsed`. Compare and report one of them.
const camel = name => name.charAt(0).toLowerCase() + name.slice(1)

export function byLane(platformRoot, moduleId, pick) {
  const of = props => props === null ? null : [...new Set(props.filter(pick).map(prop => camel(prop.name)))].sort()
  const react = of(reactProps(platformRoot, moduleId))
  const blazor = of(blazorProps(platformRoot, moduleId))
  return {
    props: [...new Set([...(react ?? []), ...(blazor ?? [])])].sort(),
    react,
    blazor,
    laneAgreement: react && blazor ? JSON.stringify(react) === JSON.stringify(blazor) : null,
  }
}

// A prop typed as an array is a collection the caller can hand over EMPTY, which makes "what do I
// draw with nothing" this component's own question rather than the caller's. Found when CIC asked
// what a chart with no data draws: hlp.ui.chart names no empty prop, so the gate reported it had no
// empty state -- but Chart.tsx:43 falls back to a 0-1 axis range on an empty series, so it draws a
// blank grid. That state is reachable and undesigned, which is a defect, not an absence.
const COLLECTION = /\[\]|\b(?:Array|ReadonlyArray|IReadOnlyList|IReadOnlyCollection|IList|List|ICollection|IEnumerable)\s*</

export function collectionProps(platformRoot, moduleId) {
  return byLane(platformRoot, moduleId, prop => COLLECTION.test(prop.type))
}

// State the interface carries that the STATUS axis cannot name. deriveStateSet recognises only
// visual status vocabulary -- error, success, warning, loading, disabled -- and only spelled as
// literal union members. It is blind to disclosure state, to selection and route state, and to state
// carried as `value`. Reporting those modules as expressing "no canonical state" was true;
// concluding they were stateless was not, and it was wrong for 32 of the 50 it covered.
// Split on the camelCase boundary and compare WORDS, not name prefixes. Prefix matching missed
// `endPanelOpen` and `mobileNavOpen` -- two of app-shell's four real disclosure props -- because
// neither name begins with its state word. A case-insensitive substring was the next wrong answer:
// it matched `openSearchShortcut`, a keybinding string, and `collapsedGroups`, a string[] of group
// ids. Words plus the boolean requirement below keep both out while letting both lanes agree, which
// matters because the lanes spell the same prop `endPanelOpen` and `EndPanelOpen`.
const words = name => name.split(/(?=[A-Z])/).map(part => part.toLowerCase())
const STATE_WORDS = new Set(['open', 'expanded', 'collapsed', 'visible', 'dismissed', 'selected', 'checked', 'pressed'])
const STATE_WORD = {test: name => words(name).some(word => STATE_WORDS.has(word))}
const IDENTITY_STATE = /^(?:default)?(?:active|current)|^(?:default)?value$/i
const BOOLEAN = /^(?:bool|boolean)\??$/i

// A handler is not state. React spells them `onCollapsedChange`, Blazor spells the two-way binding
// half `CollapsedChanged` as an EventCallback -- both were arriving in the note as though the
// component carried a state called "collapsedChanged".
const CALLBACK = /=>|EventCallback|\bAction\s*<|\bFunc\s*</

function isCallback(prop) {
  return CALLBACK.test(prop.type) || /^on[A-Z]/.test(prop.name) || /Changed$/.test(prop.name)
}

function carriesState(prop) {
  if (isCallback(prop) || !prop.parameter) return false
  if (IDENTITY_STATE.test(prop.name)) return true
  // Disclosure and selection words count only on a BOOLEAN, which is what keeps `collapsedGroups`
  // (a string[] of group ids) and `openSearchShortcut` (a keybinding) out of the state list.
  return BOOLEAN.test(prop.type.replace(/\s/g, '')) && STATE_WORD.test(prop.name)
}

export function stateBearingProps(platformRoot, moduleId) {
  return byLane(platformRoot, moduleId, carriesState)
}

// ---------------------------------------------------------------------------
// The MODE axis.
//
// CANONICAL at the top of this file is the STATUS axis: error, success, loading, disabled -- states
// that change how a component LOOKS. Mode states change what it RENDERS: an accordion panel is
// expanded or collapsed, a row selected or not.
//
// Appending these to CANONICAL was the obvious fix and the wrong one: one flat list lets a module
// satisfy "completeness" by drawing its error state while never drawing its collapsed one, which
// makes the gate weaker while looking broader. Checked as a separate axis, both must be covered.
//
// Boolean-typed only, on purpose. `collapsedGroups: string[]` is a collection, not a mode, and
// `value` is the controlled-component idiom rather than a discrete state -- 18 of the 76 modules
// declare one, and requiring a "value state" scenario from all of them would be noise.
const MODE = [
  // `visible` is NOT here. It is the word the rule-evaluation OUTCOME records use --
  // hlp.ui.schema-form's SchemaFormVisibility(bool Visible = true, ...) is a rule outcome the form
  // never takes as a parameter -- so reading it as a disclosure mode derived a collapsed/expanded
  // mode for a form that has neither, against a React lane that keeps the same outcome type in the
  // shared hlp.ui.form-view contract rather than in the module. That read as a lane disagreement
  // (ticket 381). Disclosure in this codebase is spelled open / expanded / collapsed, which stay.
  {words: new Set(['open', 'expanded', 'collapsed']), states: ['expanded', 'collapsed']},
  {words: new Set(['selected', 'checked', 'pressed']), states: ['selected', 'unselected']},
]
const modeRuleFor = name => MODE.find(rule => words(name).some(word => rule.words.has(word)))

export function deriveModeSet(platformRoot, moduleId) {
  const lanes = byLane(
    platformRoot,
    moduleId,
    prop => !isCallback(prop)
      && prop.parameter
      && BOOLEAN.test(prop.type.replace(/\s/g, ''))
      && modeRuleFor(prop.name) !== undefined,
  )
  const expand = names => names === null
    ? null
    : [...new Set(names.flatMap(name => modeRuleFor(name)?.states ?? []))].sort()
  const react = expand(lanes.react)
  const blazor = expand(lanes.blazor)
  return {
    derived: expand(lanes.props) ?? [],
    props: lanes.props,
    react,
    blazor,
    laneAgreement: react && blazor ? JSON.stringify(react) === JSON.stringify(blazor) : null,
  }
}

// Returns [status, note]. A derivation that produced nothing is UNBUILT, never a vacuous PASS.
export function stateCompletenessVerdict(derivation, declaredSurfaces, renderedStates = null, outsideVocabulary = [], mode = null) {
  // TWO AXES. `derivation` is the STATUS axis -- how the component looks. `mode` is what it renders:
  // expanded/collapsed, selected/unselected. A module must cover both; covering one while the other
  // goes undrawn is the hole that appending mode words to CANONICAL would have opened.
  const status = derivation.derived ?? []
  const modeStates = mode?.derived ?? []
  if (!derivation.derived && modeStates.length === 0) return ['UNBUILT', derivation.reason]
  if (status.length === 0 && modeStates.length === 0) {
    // Say WHICH state was found and could not be named, rather than reporting the module stateless.
    return outsideVocabulary.length > 0
      ? ['UNBUILT', `interface carries state neither axis can name -- ${outsideVocabulary.join(', ')}; widening the vocabulary or rescoping the gate is a model change, not a fixture change`]
      : ['UNBUILT', 'interface expresses no status or mode state and declares no state-bearing property; nothing derived to check']
  }
  if (derivation.laneAgreement === false) {
    return ['FAIL', `lanes derive different state sets: react=${JSON.stringify(derivation.react)} blazor=${JSON.stringify(derivation.blazor)}`]
  }
  if (mode?.laneAgreement === false) {
    return ['FAIL', `lanes derive different mode states: react=${JSON.stringify(mode.react)} blazor=${JSON.stringify(mode.blazor)}`]
  }
  const required = [...new Set([...status, ...modeStates])].sort()
  const axes = modeStates.length > 0 && status.length > 0
    ? `${status.length} status and ${modeStates.length} mode state(s)`
    : modeStates.length > 0
      ? `${modeStates.length} mode state(s)`
      : `${status.length} state(s)`
  // The DERIVATION above is structural -- it reads the types. This coverage half is name-based: it
  // asks whether any declared scenario id or surface names the state. That is weaker, and it is
  // labelled weaker rather than dressed up, because a name-based check is exactly what the old
  // assertStateCompleteness was (it asked whether a surface contained the word "state") and the
  // improvement here is the derived set, not the matching. A structural coverage check needs the
  // scenario to declare which state it renders, which is a fixture change, not a tool change.
  // STRUCTURAL coverage, when the fixtures supply it. A scenario may declare `states: [...]` naming
  // which canonical states it renders; the union of those, across scenarios a story actually DRAWS
  // in both lanes, is checked against the derived set. That is the fixture change this verdict's
  // earlier note asked for, and it is why the gate can now return PASS at all: previously both
  // branches returned PARTIAL, so every module carrying a derived state set was permanently
  // non-terminal however many scenarios anyone wrote.
  //
  // The name-based fallback stays for fixtures that have not declared yet, and stays labelled
  // weaker rather than dressed up -- asking whether a surface contains the state word is a
  // substring test, not evidence.
  if (renderedStates !== null) {
    const covered = new Set(renderedStates)
    const uncovered = required.filter(state => !covered.has(state))
    if (uncovered.length === 0) {
      return ['PASS', `${axes} derived from the interface, each declared by a scenario drawn in both lanes`]
    }
    if (covered.size > 0) {
      // Name the axis the shortfall sits on -- "collapsed unrendered" and "error unrendered" are
      // different pieces of work, and a note that blurs them sends the reader to the wrong fixture.
      const shortAxis = uncovered.every(state => modeStates.includes(state))
        ? 'mode'
        : uncovered.every(state => status.includes(state)) ? 'status' : 'both axes'
      return ['PARTIAL', `derived ${JSON.stringify(required)}; scenarios drawn in both lanes declare ${JSON.stringify([...covered].sort())}, leaving ${JSON.stringify(uncovered)} unrendered on ${shortAxis}`]
    }
  }

  const haystack = declaredSurfaces.join(' ').toLowerCase()
  const uncovered = required.filter(state => !haystack.includes(state.replace('-', '')) && !haystack.includes(state))
  return uncovered.length === 0
    ? ['PARTIAL', `${axes} derived from the interface; all NAMED by a scenario, but no scenario declares which state it renders, so coverage is a substring match`]
    : ['PARTIAL', `derived ${JSON.stringify(required)} from the interface; no scenario names ${JSON.stringify(uncovered)}`]
}

// Which of the empty / error states a component actually EXPRESSES, read from its props rather than
// assumed. assertEmptyAndErrorStates originally demanded both from every module, which is wrong in
// both directions: a text-box has an error state and no empty one, a data-grid the reverse, and an
// atomic badge has neither. Demanding both made the gate unreachable for 57 modules and told nobody
// which of the two was actually missing.
//
// Props, not literal-union members, so this reads the whole React source rather than reusing
// reactSurface() -- an empty state surfaces as the canonical `empty` prop, never as a union member.
export function derivedEmptyAndErrorSupport(platformRoot, moduleId) {
  const directory = resolve(platformRoot, `projections/react/ui/${moduleId}/src`)
  if (!existsSync(directory)) return {supportsEmpty: false, supportsError: false, props: [], read: false}
  let source = ''
  for (const entry of readdirSync(directory, {withFileTypes: true})) {
    if (entry.isFile() && /\.tsx?$/.test(entry.name)) source += `${readFileSync(resolve(directory, entry.name), 'utf8')}\n`
  }
  // Declared PROPERTY NAMES, not a regex over the whole file. Matching the source text found
  // an earlier alternate spelling but missed hlp.ui.conversation-list's prop named plainly `empty`, and would equally
  // match the word in a comment. A property named for the state is the evidence that the component
  // has one.
  const props = new Set()
  for (const match of source.matchAll(/^\s*(?:readonly\s+)?([A-Za-z][A-Za-z0-9]*)\??\s*:/gm)) props.add(match[1])
  const names = [...props]
  return {
    supportsEmpty: names.some(name => /^empty$/i.test(name)),
    supportsError: names.some(name => /error|invalid/i.test(name)),
    props: names.filter(name => /^(?:empty|.*error.*|.*invalid.*)$/i.test(name)).sort(),
    read: true,
  }
}
