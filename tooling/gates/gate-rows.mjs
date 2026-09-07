// The gate model's verdicts, extracted from run-vertical-pair.mjs so the two-module deep report and
// the 76-module receipt evaluate the SAME rules. Two copies of a verdict is two answers to one
// question, and this programme has already paid for that shape more than once.
//
// Everything is built by a factory rather than exported flat because every helper below closes over
// the platform root. Passing the root explicitly to each of them would be a wider diff for no gain.
//
// Verdicts: PASS | FAIL | UNBUILT | PARTIAL | VOID | NOT-APPLICABLE.
// UNBUILT is the honest default and never a fallback for "unsure".

import {execFileSync} from 'node:child_process'
import {existsSync, readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {collectionProps, deriveModeSet, deriveStateSet, derivedEmptyAndErrorSupport, stateBearingProps, stateCompletenessVerdict} from './derive-state-set.mjs'
import {loadRecord, referenceRevision, referenceSurface, reviewVerdict, rollUp} from './design-review.mjs'

const here = dirname(fileURLToPath(import.meta.url))

export function createGateModel(platformRoot) {
  const read = path => JSON.parse(readFileSync(resolve(platformRoot, path), 'utf8'))
  const gate = existsSync(resolve(platformRoot, 'docs/evidence/phase-4/gate.json'))
    ? read('docs/evidence/phase-4/gate.json')
    : null

  function catalogFor(moduleId) {
    const path = `gallery/scenarios/${moduleId}.json`
    return existsSync(resolve(platformRoot, path)) ? read(path) : null
  }

  // specs/modules/ui/<id>/quality.yaml -- the PROFILE. Distinct from ctx.quality, which is the
  // conformance quality-FIXTURE. The gate dispositions belong beside `dimensions` in the profile,
  // per ticket 098's ruling that the model is expressed in the existing schema rather than in a
  // parallel register: a second register drifts from the first, and this programme has three worked
  // examples of what that costs.
  // Which scenarios a story actually DRAWS. scan-catalog-story-drift reconciles the catalog against
  // the story files, and the gallery runs one browser test per scenario id it finds there -- so
  // "drawn in both lanes" is the difference between a scenario that was declared and one that is
  // rendered, scanned by axe and compared for parity. Read once and indexed, like the other
  // scanner reads: per-module shell-outs were already fixed here once.
  let renderedIndex
  function renderedScenariosFor(moduleId) {
    if (renderedIndex === undefined) {
      renderedIndex = new Map()
      let parsed = null
      try {
        parsed = JSON.parse(execFileSync(process.execPath,
          [resolve(here, 'scan-catalog-story-drift.mjs'), platformRoot, '--json'],
          {encoding: 'utf8', maxBuffer: 64 * 1024 * 1024}))
      } catch (error) {
        // A scanner signals findings with a non-zero exit; its stdout is still the evidence.
        try { parsed = JSON.parse(error?.stdout ?? '') } catch { parsed = null }
      }
      for (const entry of parsed?.rendered ?? []) {
        const [id, scenarioId, lane] = entry.split('|')
        if (!renderedIndex.has(id)) renderedIndex.set(id, new Map())
        const lanes = renderedIndex.get(id)
        lanes.set(scenarioId, [...(lanes.get(scenarioId) ?? []), lane])
      }
    }
    const lanes = renderedIndex.get(moduleId) ?? new Map()
    return new Set([...lanes.entries()]
      .filter(([, laneList]) => laneList.includes('react') && laneList.includes('blazor'))
      .map(([scenarioId]) => scenarioId))
  }

  function profileFor(moduleId) {
    const path = `specs/modules/ui/${moduleId}/quality.yaml`
    return existsSync(resolve(platformRoot, path)) ? read(path) : null
  }

  // A gate a person declared not-applicable is EARNED, not skipped -- which is why the rationale and
  // the decider are REQUIRED and why absence means `required`. Defaulting to not-applicable would let
  // a module reach terminal status by omission, and ticket 098 acceptance 5 -- "the sweep produces
  // the worksheet; it does not get to answer it" -- is precisely a rule against that.
  function declaredDisposition(profile, gateId) {
    const entry = profile?.gates?.[gateId]
    if (!entry) return null
    if (entry.disposition !== 'not-applicable') return null
    if (!entry.rationale || !entry.decidedBy) {
      throw new Error(`${gateId}: a not-applicable disposition needs both a rationale and a decidedBy`)
    }
    return ['NOT-APPLICABLE', `${entry.rationale} -- ${entry.decidedBy}`]
  }

  function qualityFor(catalog) {
    return catalog?.qualityFixture && existsSync(resolve(platformRoot, catalog.qualityFixture))
      ? read(catalog.qualityFixture)
      : null
  }

  // The cheap half of assertDeterminism already exists as a scanner; reuse it rather than reimplement.
  // Each scanner sweeps the whole tree, so it is run ONCE and its rows indexed by module. The
  // vertical pair called it per module, which was invisible at two modules and is 152 full scans at
  // seventy-six -- ticket 098 acceptance 7 gives the static sweep a two-minute budget, and a budget
  // is the one thing a per-module shell-out cannot meet.
  const scannerCache = new Map()
  function scannerRows(script, moduleId) {
    if (!scannerCache.has(script)) {
      // A scanner signals FINDINGS with a non-zero exit, which makes execFileSync throw even though
      // its stdout is exactly the evidence wanted. Reading only the happy path cached null for every
      // failing scanner, so ten real focus failures presented as seventy-six UNBUILT -- a gate with
      // evidence in hand reporting that it had none. The error carries the stdout; use it.
      const parse = text => { try { return JSON.parse(text) } catch { return null } }
      try {
        const out = execFileSync(process.execPath, [resolve(here, script), platformRoot, '--json'], {
          encoding: 'utf8', maxBuffer: 64 * 1024 * 1024,
        })
        scannerCache.set(script, parse(out))
      } catch (error) {
        scannerCache.set(script, parse(error?.stdout ?? ''))
      }
    }
    const parsed = scannerCache.get(script)
    if (parsed === null) return null
    return (parsed.modules ?? parsed.findings ?? []).filter(row => (row.moduleId ?? row.id) === moduleId)
  }

  function tokenSummary(moduleId) {
    const rows = scannerRows('scan-token-adherence.mjs', moduleId)
    if (rows === null) return null
    return {
      directLiterals: rows.reduce((t, r) => t + (r.directLiteral ?? r.directLiterals ?? 0), 0),
      moduleLocal: rows.reduce((t, r) => t + (r.moduleLocalToken ?? r.moduleLocal ?? 0), 0),
    }
  }

  // Every Tier-1 gate from ticket 098's model, with the evidence that would settle it.

  const GATES = [
    {
      id: 'assertDeterminism',
      // The first draft of this read `row.unguarded`, a field the scanner does not emit, so every
      // module came back PARTIAL and the VOID path had no live trigger. The scanner's real shape is
      // {timing[], unclassified[], async[]} per scenario. An `async` entry present in ONE lane is a
      // determinism failure: the lanes cannot be relied on to reach the same state at the same moment,
      // which is precisely the divergence ticket 097 spent two CI campaigns diagnosing -- on this
      // module, in this scenario.
      verdict: ctx => {
        if (ctx.determinism === null) return ['UNBUILT', 'determinism scanner did not run']
        if (ctx.determinism.length === 0) return ['PASS', 'cheap half only: no timing or async construct declared']
        const asymmetric = ctx.determinism.filter(row => {
          const lanes = new Set((row.async ?? []).map(a => a.lane))
          return (row.async ?? []).length > 0 && lanes.size < 2
        })
        const unclassified = ctx.determinism.filter(row => (row.unclassified ?? []).length > 0)
        if (asymmetric.length > 0) {
          const which = asymmetric.map(r => `${r.scenarioId} (${r.async.map(a => `${a.lane}:${a.construct}`).join(', ')})`).join('; ')
          return ['FAIL', `lane-asymmetric async construct in ${which}`]
        }
        if (unclassified.length > 0) return ['FAIL', `${unclassified.length} unclassified numeric prop(s)`]
        return ['PARTIAL', `${ctx.determinism.length} timing construct(s), lane-symmetric; expensive half (run twice, compare) UNBUILT`]
      },
    },
    {
      id: 'assertAccessible',
      verdict: ctx => ctx.gallery
        ? ['PASS', `axe-core over ${ctx.catalog?.scenarios.length ?? 0} scenarios x 2 lanes in gallery-gate`]
        : ['UNBUILT', 'no recorded gallery-gate evidence'],
    },
    {
      id: 'assertVisualParity',
      verdict: ctx => {
        if (!ctx.gallery) return ['UNBUILT', 'no recorded gallery-gate evidence']
        const parity = ctx.catalog?.scenarios.filter(s =>
          s.sourceQualityCaseIds?.some(id => id.endsWith('.quality.visual-parity'))).length ?? 0
        return parity > 0
          ? ['PASS', `${parity} visual-parity scenario(s), lane vs lane, pixelmatch`]
          : ['PARTIAL', 'module declares NO visual-parity scenario; the gate cannot fire for it']
      },
    },
    {
      id: 'assertFunctionalParity',
      verdict: ctx => ctx.conformanceCases > 0
        ? ['PASS', `${ctx.conformanceCases} conformance case(s) across both lanes`]
        : ['UNBUILT', 'no conformance fixtures'],
    },
    {
      id: 'assertInternationalization',
      verdict: ctx => {
        const locales = Object.keys(ctx.quality?.locales ?? {}).length
        return locales > 0
          ? ['PASS', `${locales} locale fixture(s) projected in both lanes`]
          : ['PARTIAL', 'no locale fixtures; only the shared canvas is exercised']
      },
    },
    {
      id: 'assertPerformanceProfile',
      // Ticket 098 asks for client performance characteristics: startup, rendering, interaction, route
    // change, asset weight, memory. tooling/run-ui-performance.mjs answers NONE of them. Its
    // durationMs is performance.now() around a spawnSync of a test process -- how long vitest and
    // dotnet test took on this machine -- and it names its own method 'deterministic-structural-bound'
    // with wallClockBudgetUsed: false. Wiring PASS to that made the gate answer a question nobody
    // asked, which is the defect this whole ticket family exists to catch.
    //
    // Real budgets DO exist, for exactly one module: hlp.ui.schema-form's quality fixture declares
    // keystroke-latency 16ms, large-form 400ms and deep-collection 400ms, and its React test asserts
    // elapsed against them. That earns PARTIAL, never PASS -- only the React lane is measured, and
    // asset weight, memory and route change are unmeasured everywhere.
    verdict: ctx => {
      const budgets = (ctx.quality?.performanceCases ?? [])
        .filter(entry => typeof entry?.expected?.budgetMs === 'number')
      return budgets.length > 0
        ? ['PARTIAL', `${budgets.length} millisecond budget(s) declared and asserted in the React lane; the Blazor lane, asset weight, memory and route change are unmeasured`]
        : ['UNBUILT', 'no client-performance measurement exists for this module; run-ui-performance.mjs records test-process wall clock and declares wallClockBudgetUsed: false']
    },
  },
    {
      id: 'assertTokenAdherence',
      parent: 'assertDesignQuality',
      verdict: ctx => ctx.tokens === null
        ? ['UNBUILT', 'token scanner did not run']
        : ctx.tokens.directLiterals > 0
          ? ['FAIL', `${ctx.tokens.directLiterals} direct literal(s), ${ctx.tokens.moduleLocal} module-local token(s)`]
          : ['PASS', 'every value resolves to an approved token'],
    },
    {
      id: 'assertStateCompleteness',
      parent: 'assertDesignQuality',
      // Ruling 3, made observable: the required set is derived from the TS and C# type surfaces, and
      // the two lanes' derivations are compared against each other before either is used.
      verdict: ctx => stateCompletenessVerdict(ctx.stateSet, ctx.declaredSurfaces, ctx.renderedStates, ctx.stateBearingProps, ctx.modeSet),
    },
    {
      id: 'assertFocusQuality',
      parent: 'assertDesignQuality',
      // The FLOOR only: a ring that exists, is thick enough to read as a ring rather than a border, is
    // offset from the control edge, and resolves through a token so it follows the theme. Whether the
    // treatment is *good* stays with assertDesignReview -- ticket 098 ruling 3 refuses to AND a human
    // verdict with a lint, because six green mechanical children then hide the one that matters.
    verdict: ctx => ctx.focus == null
      ? ['UNBUILT', 'focus scanner reported nothing for this module']
      : ctx.focus.verdict === 'PASS'
        ? ['PASS', 'focus ring is tokened, at least 2px, and offset from the control edge']
        : ['FAIL', ctx.focus.findings.join('; ')],
    },
    {
      id: 'assertResponsiveQuality',
      parent: 'assertDesignQuality',
      verdict: ctx => ctx.gallery
        // PASS requires the module to have a scenario that declares a reflow case AND is DRAWN in both
        // lanes, because that is exactly what makes assertReflow run for it: a 320px viewport at 200%
        // text with no horizontal overflow, measured on the rendered page. Declared-but-undrawn earns
        // nothing, the same rule the empty/error, content and state gates take.
        ? (ctx.rendersReflow
            ? ['PASS', 'reflow measured at 320px and 200% text in both lanes, with no horizontal overflow']
            : ctx.declaresReflow
              ? ['PARTIAL', 'a reflow case is declared but no lane draws the scenario, so nothing measured it']
              : ['PARTIAL', 'no scenario declares a reflow case, so WCAG 1.4.10 reflow is unmeasured for this module'])
        : ['UNBUILT', 'no recorded gallery-gate evidence'],
    },
    {
      id: 'assertEmptyAndErrorStates',
      parent: 'assertDesignQuality',
      // PASS requires both states RENDERED in both lanes, not declared. A catalog scenario nothing
      // draws is prose -- ticket 102's finding -- so "declared" was capped at PARTIAL and this gate
      // could never pass at all. scan-catalog-story-drift reconciles the two, and the gallery runs a
      // browser test per drawn scenario, so a rendered one is axe-scanned and parity-compared too.
      // Requires only the states this component EXPRESSES, derived from its props. Demanding both
      // empty and error from every module was wrong in both directions -- a text-box has an error
      // state and no empty one, a data-grid the reverse, an atomic badge neither -- and it made the
      // gate unreachable for fifty-seven modules while saying nothing about which half was missing.
      verdict: ctx => {
        const {supportsEmpty, supportsError} = ctx.emptyErrorSupport
        if (!supportsEmpty && !supportsError) {
          // A component holding a collection can be handed an empty one whether or not it named a
          // prop for that, so "expresses no empty state" was false for thirteen of the thirty-eight
          // modules this branch covered. Distinguish the two: a collection-bearing component owns an
          // undesigned empty state, which is work; a container's empty rendering belongs to whatever
          // the caller puts inside it, which is not this component's to design.
          return ctx.collectionProps.length > 0
            ? ['UNBUILT', `no empty or error property, but ${ctx.collectionProps.join(', ')} can be handed an empty collection; that state is reachable and undesigned`]
            : ['UNBUILT', 'no empty or error property and no collection to be empty; content comes from the caller, so an empty rendering is not this component’s to design']
        }
        const wanted = [supportsEmpty && 'empty', supportsError && 'error'].filter(Boolean)
        const drawn = wanted.filter(state => ctx.renders[state])
        if (drawn.length === wanted.length) {
          return ['PASS', `${wanted.join(' and ')} scenario(s) drawn in both lanes for every such state the interface expresses`]
        }
        const missing = wanted.filter(state => !ctx.renders[state])
        const declaredOnly = missing.filter(state => ctx.declares[state])
        return ['PARTIAL', `interface expresses ${wanted.join(' and ')}; ${missing.join(' and ')} `
          + (declaredOnly.length > 0 ? 'declared but drawn by no lane' : 'has no scenario')]
      },
    },
    {
      id: 'assertContentResilience',
      parent: 'assertDesignQuality',
      // Same rule, same reason: a declared content case is not a rendered one.
      verdict: ctx => ctx.renders.content
        ? ['PASS', 'a long-content, dense or large-number scenario is drawn in both lanes and reconciled against the catalog']
        : ctx.declares.content
          ? ['PARTIAL', 'content case declared but no lane draws it']
          : ['UNBUILT', 'no long-content, missing-field or large-number case declared'],
    },
    {
      id: 'assertDesignReview',
      parent: 'assertDesignQuality',
      // Ruling 2, made observable. The record shape and the expiry rule now exist (design-review.mjs);
      // what does not exist is a HUMAN verdict, and the tooling will not write one on a human's behalf.
      // So real modules report UNBUILT with an accurate reason, and the veto's propagation is proven by
      // the canary against a synthetic record rather than by a fabricated approval.
      verdict: ctx => reviewVerdict({record: ctx.designReview, revision: ctx.reviewRevision, surface: ctx.reviewSurface}),
    },
    {
      id: 'assertDesignQuality',
      rollup: true,
      verdict: () => ['UNBUILT', 'placeholder; replaced by rollUp() over the seven sub-gates'],
    },
  ]

  const VOIDED_BY_DETERMINISM = ['assertVisualParity', 'assertFunctionalParity']
  const verdictOf = id => GATES.find(g => g.id === id).verdict

  // The ONE place VOID is applied. The canary and the report builder both call this; an earlier draft
  // had the canary recompute the rule locally, and perturbing the real propagation left the canary
  // green -- a canary that tests a copy of the logic reports DEAD gates as healthy, which is the exact
  // failure assertGateCanFail exists to catch. Caught by perturbing it.
  function gateRows(ctx) {
    const voided = verdictOf('assertDeterminism')(ctx)[0] === 'FAIL'
    const rows = GATES.map(g => {
      // A declared disposition wins over every computed verdict, including VOID: a gate that cannot
      // apply to this module cannot be voided by a determinism failure either.
      const declared = declaredDisposition(ctx.qualityProfile, g.id)
      if (declared) return {id: g.id, parent: g.parent, status: declared[0], note: declared[1]}
      const [status, note] = g.verdict(ctx)
      return VOIDED_BY_DETERMINISM.includes(g.id) && voided
        ? {id: g.id, parent: g.parent, status: 'VOID', note: 'voided by an assertDeterminism failure in this lane'}
        : {id: g.id, parent: g.parent, status, note}
    })
    // The rollup runs last and over the real sub-verdicts, so a design-review veto reaches the group
    // rather than being a claim about what would happen if it did.
    const rollupRow = rows.find(r => r.id === 'assertDesignQuality')
    const [status, note] = rollUp(rows.filter(r => r.parent === 'assertDesignQuality'))
    rollupRow.status = status
    rollupRow.note = note
    return rows
  }

  // Canary. The VOID path has no live trigger -- nothing in the pair currently fails determinism -- so
  // without this, ruling 1 would be untested code that reads as implemented. It forces a determinism

  function contextFor(moduleId) {
    const catalog = catalogFor(moduleId)
    const scenarios = catalog?.scenarios ?? []
    const surfaces = scenarios.map(s => String(s.surface ?? ''))
    const fixturePath = `conformance/${moduleId}/fixtures.yaml`
    return {
      moduleId,
      catalog,
      quality: qualityFor(catalog),
      qualityProfile: profileFor(moduleId),
      conformanceCases: existsSync(resolve(platformRoot, fixturePath)) ? read(fixturePath).cases.length : 0,
      gallery: Boolean(gate?.results?.find(r => r.id === 'gallery-gate')?.passed),
      determinism: scannerRows('scan-scenario-determinism.mjs', moduleId),
      focus: (scannerRows('scan-focus-quality.mjs', moduleId) ?? [])[0] ?? null,
      tokens: tokenSummary(moduleId),
      stateSet: deriveStateSet(platformRoot, moduleId),
      declaredSurfaces: scenarios.map(s => `${s.id} ${s.surface ?? ''}`),
      designReview: loadRecord(moduleId),
      reviewRevision: referenceRevision(platformRoot, moduleId),
      reviewSurface: referenceSurface(platformRoot, moduleId),
      declares: {
        states: surfaces.some(s => /state|hover|focus|disabled|active|selected|readonly/i.test(s)),
        empty: surfaces.some(s => /empty|no-results/i.test(s)),
        error: surfaces.some(s => /error|invalid|denied/i.test(s)),
        content: surfaces.some(s => /long|overflow|truncat|large|dense/i.test(s)),
      },
      // The same questions asked of the scenarios a story actually DRAWS in both lanes.
      // `declares` answers "is it written down"; this answers "does it render", and only the
      // second is evidence. Both lanes, never either: a scenario drawn only in React is the lane
      // asymmetry the parity gates exist to catch, and counting it here would hide one.
      emptyErrorSupport: derivedEmptyAndErrorSupport(platformRoot, moduleId),
      collectionProps: collectionProps(platformRoot, moduleId).props,
      stateBearingProps: stateBearingProps(platformRoot, moduleId).props,
      modeSet: deriveModeSet(platformRoot, moduleId),
      declaresReflow: scenarios.some(scenario =>
        scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.reflow'))),
      rendersReflow: (() => {
        const drawn = renderedScenariosFor(moduleId)
        return scenarios.some(scenario => drawn.has(scenario.id)
          && scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.reflow')))
      })(),
      renderedStates: (() => {
        const drawn = renderedScenariosFor(moduleId)
        const declared = scenarios
          .filter(scenario => drawn.has(scenario.id) && Array.isArray(scenario.states))
          .flatMap(scenario => scenario.states)
        return declared.length > 0 ? [...new Set(declared)].sort() : null
      })(),
      renders: (() => {
        const drawn = renderedScenariosFor(moduleId)
        const rendered = scenarios.filter(scenario => drawn.has(scenario.id))
          .map(scenario => String(scenario.surface ?? ''))
        return {
          empty: rendered.some(s => /empty|no-results/i.test(s)),
          error: rendered.some(s => /error|invalid|denied/i.test(s)),
          content: rendered.some(s => /long|overflow|truncat|large|dense/i.test(s)),
        }
      })(),
    }
  }

  // assertGateCanPass -- the mirror of assertGateCanFail, and the reason this exists is that its
  // absence hid a structural dead end for the whole programme. Five gates could only ever return
  // PARTIAL or UNBUILT, and a module is terminal only on PASS or NOT-APPLICABLE, so fifty-nine
  // visual modules were unreachable no matter how much fixture work anyone did. Nothing said so:
  // the blocked-by-gate table showed 59 against each of them and read like a backlog rather than a
  // wall.
  //
  // A ceiling is not a defect -- every one of these is an honest refusal to claim more than the
  // evidence supports. What is a defect is a ceiling nobody declared. So each must be listed here
  // with what it would take to lift it, and the selftest fails when this list and the code disagree
  // in either direction: a gate that gains a PASS path must lose its entry, and a gate that loses
  // one must gain it.
  const PARTIAL_CEILINGS = {
    assertPerformanceProfile: 'Only hlp.ui.schema-form declares real millisecond budgets. Lifting it needs client-performance measurement -- render, interaction, asset weight, memory -- which does not exist in this repository at all.',
  }

  // The Tier-1 set in report order. A module is terminal only when every one of these reads PASS or
  // NOT-APPLICABLE; assertDesignQuality is included so a module cannot go terminal while its rollup
  // is still UNBUILT.
  const TIER1_GATE_IDS = GATES.map(gate => gate.id)

  // platformRoot is returned so the canary can derive a REAL surface for a real module rather than
  // inventing file names; a canary that only ever sees invented names cannot notice the derivation
  // dropping a whole class of file (ticket 138 slice 3).
  return {platformRoot, GATES, TIER1_GATE_IDS, PARTIAL_CEILINGS, VOIDED_BY_DETERMINISM, verdictOf, gateRows, contextFor, gate, declaredDisposition}
}
