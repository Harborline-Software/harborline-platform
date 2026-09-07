// Vendored from harborline-control/tools/design-review.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 ruling: `assertDesignReview` is "the verdict on the group" -- a recorded human
// judgement against an approved reference. It had no record shape at all, which is why no veto could
// propagate and why `assertDesignQuality` could never render anything but UNBUILT.
//
// The reference is the sha256 of the module's gallery scenario catalog. That is the thing a reviewer
// actually looked at, it exists for every module, and it moves the moment the design changes -- so
// expiry is automatic rather than a date somebody has to remember to bump. A verdict recorded
// against a revision that no longer matches is EXPIRED, which is a FAIL, not an absence.

import {createHash} from 'node:crypto'
import {existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {carriesRender, renderDigest} from './render-digest.mjs'

const here = dirname(fileURLToPath(import.meta.url))
export const recordsRoot = resolve(here, '../../docs/evidence/design-review')

// The reference is what the reviewer actually LOOKED AT, which is the rendered component: the
// scenario catalog that decides which stories exist, and the stylesheet that decides how they draw.
//
// The first version hashed the catalog alone, and that was wrong in a way ticket 106 made obvious:
// fixing hlp.ui.badge's CSS changes every pixel a reviewer judged and would not have moved the hash
// at all, so a verdict would have survived the exact change it should expire on. A design review that
// cannot expire when the design changes is decoration.
// The approved SURFACE is the set of files that decide what the reviewer saw, hashed one by one
// rather than blended into a single opaque digest. Per-file is the whole point: a blended hash can
// only say "something moved", and a refusal that cannot name the file it is refusing over is a
// refusal nobody acts on (ticket 138).
//
// Slice 1 bound three named files -- the spec authority and the gallery catalog. That set could not
// see a change to a `.tsx`, a `.razor` or a gallery story, which is the defect ticket 138 opened
// with: five modules were fixed entirely in projection source and stories on 2026-08-26 and their
// verdicts still read as current. HLP-0008 rules that the surface is every file that can change what
// the gallery draws for that module, DERIVED by one rule for all 102 modules rather than hand-listed:
//
//   1. the spec authority        specs/modules/ui/<id>/{scenarios.json,style.css}
//   2. the derived gallery catalog gallery/scenarios/<id>.json
//   3. the gallery stories        <Component>.stories.tsx / .stories.razor / <Component>Scenario.razor
//   4. the projection render inputs under the module's react and blazor lanes -- component,
//      template, stylesheet, wwwroot -- minus test and build files
//   5. the conformance fixtures the stories and the shared runners read, conformance/<id>/*.{yaml,json}
//
// Excluded, because they cannot reach a render: __tests__, *.test.*, test-setup, vitest/tsconfig
// build config, node_modules and build output, and the sibling `<module>.tests` projects (which are
// not under the module's projection directory, so they are never walked). Slice 4 owns the
// demonstration that this exclusion holds.
//
// Reach without noise: the widening alone is the source-tree hashing HLP-0008 rejected, and slice 4
// measured it -- four behaviour-neutral edits to one .tsx expiring a verdict. What makes the reach
// safe is that a render-CARRYING file is hashed over a normalised form (render-digest.mjs), so a
// rename, a reformat, a comment and an import reorder move no digest. See the HLP-0008 addendum.
const RENDER_EXTENSION = /\.(tsx|ts|jsx|js|css|razor|cs)$/
const NON_RENDER_DIRECTORY = new Set(['__tests__', 'node_modules', 'dist', 'bin', 'obj'])
const NON_RENDER_FILE = /(\.test\.|\.spec\.|^test-setup\.|^vitest\.config\.|^tsconfig)/

// The gallery names its stories after the COMPONENT, not the module id, so the file names are
// derived through the same stem scan-catalog-story-drift.mjs resolves them with -- one spelling of
// the rule, imported rather than copied.
export const componentStem = moduleId => moduleId.replace(/^hlp\.ui\./, '').split('-')
  .map(part => part.charAt(0).toUpperCase() + part.slice(1)).join('')

// The two RENDERING lanes, found where they live: projections/<lane>/<family>/<module id>. A dotnet
// vocabulary or contracts projection of the same module cannot change what the gallery draws.
// Directory presence rather than catalog/modules.yaml, because the surface has to be computable for
// any tree that holds the module's files -- a scratch copy of one module included, which is how
// slice 4 proves the false-positive rate -- and a catalog read makes the rule unrunnable there.
function laneDirectories(platformRoot, moduleId) {
  const found = []
  for (const lane of ['react', 'blazor']) {
    const laneRoot = resolve(platformRoot, `projections/${lane}`)
    if (!existsSync(laneRoot)) continue
    for (const family of readdirSync(laneRoot, {withFileTypes: true}).filter(e => e.isDirectory())) {
      if (existsSync(resolve(laneRoot, family.name, moduleId))) found.push(`projections/${lane}/${family.name}/${moduleId}`)
    }
  }
  return found
}

function renderFilesUnder(platformRoot, relativeDir) {
  const absolute = resolve(platformRoot, relativeDir)
  if (!existsSync(absolute)) return []
  const found = []
  for (const entry of readdirSync(absolute, {withFileTypes: true}).sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.isDirectory()) {
      if (!NON_RENDER_DIRECTORY.has(entry.name)) found.push(...renderFilesUnder(platformRoot, `${relativeDir}/${entry.name}`))
    } else if (RENDER_EXTENSION.test(entry.name) && !NON_RENDER_FILE.test(entry.name)) {
      found.push(`${relativeDir}/${entry.name}`)
    }
  }
  return found
}

function fixtureFilesUnder(platformRoot, relativeDir) {
  const absolute = resolve(platformRoot, relativeDir)
  if (!existsSync(absolute)) return []
  return readdirSync(absolute, {withFileTypes: true})
    .filter(entry => entry.isFile() && /\.(yaml|json)$/.test(entry.name))
    .map(entry => `${relativeDir}/${entry.name}`)
}

export function surfaceFiles(platformRoot, moduleId) {
  const stem = componentStem(moduleId)
  const declared = [
    `specs/modules/ui/${moduleId}/scenarios.json`,
    `specs/modules/ui/${moduleId}/style.css`,
    `gallery/scenarios/${moduleId}.json`,
    `gallery/projections/react/src/${stem}.stories.tsx`,
    `gallery/projections/blazor/Stories/${stem}.stories.razor`,
    `gallery/projections/blazor/Stories/${stem}Scenario.razor`,
  ]
  const sources = laneDirectories(platformRoot, moduleId).flatMap(dir => renderFilesUnder(platformRoot, dir))
  return [...new Set([...declared, ...sources, ...fixtureFilesUnder(platformRoot, `conformance/${moduleId}`)])].sort()
}

// A file that CARRIES a render is hashed over its normalised form, not its bytes: see
// render-digest.mjs. Binding raw bytes is the source-tree hashing HLP-0008 rejected, and slice 4
// measured the cost -- four behaviour-neutral edits to one .tsx expiring a verdict. Everything else
// (the spec authority, the scenario catalogs, the conformance fixtures) is hashed raw, because every
// byte of those files was written to be read.
const hashFile = path => {
  if (!existsSync(path)) return createHash('sha256').update(Buffer.alloc(0)).digest('hex')
  return carriesRender(path) ? renderDigest(path) : createHash('sha256').update(readFileSync(path)).digest('hex')
}

// A missing file hashes as empty rather than being skipped, so ADDING one later moves the surface.
// Skipping it would let a module acquire a stylesheet without expiring the verdict that predates it.
export function referenceSurface(platformRoot, moduleId) {
  if (!existsSync(resolve(platformRoot, `gallery/scenarios/${moduleId}.json`))) return null
  return Object.fromEntries(surfaceFiles(platformRoot, moduleId).map(rel => [rel, hashFile(resolve(platformRoot, rel))]))
}

// The legacy single digest over the two files v1 records bound. Kept computed exactly as it was:
// records written before the surface existed are compared against it, so widening the surface does
// not retroactively expire fifty-seven verdicts that are still true about what they bound.
export function referenceRevision(platformRoot, moduleId) {
  const sources = [
    `gallery/scenarios/${moduleId}.json`,
    `specs/modules/ui/${moduleId}/style.css`,
  ].map(relative => resolve(platformRoot, relative))
  if (!existsSync(sources[0])) return null
  const digest = createHash('sha256')
  for (const source of sources) digest.update(existsSync(source) ? readFileSync(source) : Buffer.alloc(0))
  return digest.digest('hex')
}

export function loadRecord(moduleId, root = recordsRoot) {
  const path = resolve(root, `${moduleId}.json`)
  if (!existsSync(path)) return null
  return JSON.parse(readFileSync(path, 'utf8'))
}

// Writing a verdict is a deliberate, attributed act, so it takes a reviewer and refuses to invent
// one. `assertDesignReview` is defined as a recorded HUMAN judgement; tooling that signs off on a
// human's behalf would make the whole gate decorative -- the same failure as a canary that cannot
// fail. The reviewer must be a real person, and the revision is computed here rather than supplied,
// so a verdict cannot be recorded against a revision nobody looked at.
export function recordVerdict({platformRoot, moduleId, reviewer, verdict, notes, recordedAt, root = recordsRoot}) {
  if (!reviewer || /^(tooling|automation|claude|assistant|canary|ci)$/i.test(reviewer.trim())) {
    throw new Error('design-review verdict requires a named human reviewer')
  }
  if (!['approved', 'rejected', 'changes-requested'].includes(verdict)) {
    throw new Error(`unrecognised verdict "${verdict}"; expected approved, rejected or changes-requested`)
  }
  const revision = referenceRevision(platformRoot, moduleId)
  const surface = referenceSurface(platformRoot, moduleId)
  if (!revision) throw new Error(`${moduleId} has no gallery scenario catalog to review against`)
  // `recordedAt` used to be taken verbatim, so omitting --date wrote `undefined`, which
  // JSON.stringify DROPS -- producing a record that reviewVerdict then rejected with "names no
  // reviewer or no date". The CLI exited 0 while writing something it knew was invalid. Recording
  // 57 verdicts that way on 2026-08-26 turned every one of them, approvals included, into a FAIL
  // that read as a review problem rather than a tooling one.
  const stamp = recordedAt ?? new Date().toISOString()
  if (Number.isNaN(Date.parse(stamp))) throw new Error(`recordedAt "${stamp}" is not a date`)
  const record = {
    schemaVersion: 1,
    moduleId,
    verdict,
    reviewer: reviewer.trim(),
    recordedAt: stamp,
    notes: notes ?? undefined,
    reference: {revision, source: `gallery/scenarios/${moduleId}.json`, surface},
  }
  // Fail closed: a writer that can emit a record its own reader refuses is worse than one that
  // refuses up front, because the refusal surfaces as a gate FAIL long after the writing.
  const [status] = reviewVerdict({record, revision, surface})
  if (status === 'FAIL' && verdict === 'approved') {
    throw new Error(`refusing to write a record that would read as FAIL: ${moduleId}`)
  }
  mkdirSync(root, {recursive: true})
  writeFileSync(resolve(root, `${moduleId}.json`), `${JSON.stringify(record, null, 2)}\n`)
  return record
}

// Returns [status, note]. FAIL outranks UNBUILT deliberately: an expired verdict is a stronger
// statement than a missing one -- somebody approved this, and then the thing they approved changed.
export function reviewVerdict({record, revision, surface}) {
  if (revision === null) return ['UNBUILT', 'module has no gallery scenario catalog to review against']
  if (!record) return ['UNBUILT', 'no design-review verdict recorded for this module']
  if (record.schemaVersion !== 1) return ['FAIL', `unrecognised design-review schemaVersion ${record.schemaVersion}`]
  if (!record.reviewer || !record.recordedAt) return ['FAIL', 'design-review record names no reviewer or no date']
  // Expiry is checked BEFORE the verdict value, and the order is load-bearing. An expired
  // changes-requested is not still a changes-requested: the reviewer was describing a design that no
  // longer exists, and reporting their old words as the current verdict claims a judgement nobody
  // made. Expired means nobody has reviewed what is there now, whatever they said about what was.
  // Ticket 138 slice 6: a record that binds NO surface is refused. Every record on disk was
  // migrated onto the surface of the commit its verdict was given against, so an unbound record
  // from here on is either hand-written or predates the migration -- in both cases nobody can say
  // which files the verdict covered, and the legacy two-file digest is exactly the blindness this
  // ticket opened with. Refusing sends it to re-review; passing it would keep the blind spot open.
  const bound = record.reference?.surface
  if (!bound || !surface) {
    return ['FAIL', 'design-review record binds no surface: the verdict names no files, so it cannot expire when they change (ticket 138) -- re-review the module']
  }
  // The surface is judged file by file and the refusal names the files, because "the design
  // changed" sends a reviewer hunting and "style.css changed" sends them to the diff.
  //
  // A file that ARRIVED or WENT AWAY since the verdict is a change to the surface like any other,
  // not a malformed record: the module acquired a stylesheet, or its component was renamed, and
  // either way the reviewer never saw what is there now. Folding those two sets into the expiry is
  // also what keeps the slice-1 review's guarantee -- a hand-edited record binding {} or half the
  // surface still cannot read PASS, it now expires naming every file it failed to bind.
  const gone = Object.keys(bound).filter(file => !(file in surface))
  const arrived = Object.keys(surface).filter(file => !(file in bound))
  const moved = Object.keys(bound).filter(file => file in surface && bound[file] !== surface[file])
  if (moved.length + gone.length + arrived.length > 0) {
    const parts = [
      moved.length > 0 && `changed in ${moved.join(', ')}`,
      gone.length > 0 && `no longer has ${gone.join(', ')}`,
      arrived.length > 0 && `has gained ${arrived.join(', ')}`,
    ].filter(Boolean)
    return ['FAIL', `verdict EXPIRED: "${record.verdict}" was recorded against a surface that has since ${parts.join('; ')}`]
  }
  return record.verdict !== 'approved'
    ? ['FAIL', `recorded verdict is "${record.verdict}"`]
    : ['PASS', `approved by ${record.reviewer} on ${record.recordedAt} against ${Object.keys(bound).length} surface files`]
}

// The rollup, and the reason this module exists. FAIL beats UNBUILT beats PARTIAL beats PASS, so a
// single expired design review takes the whole group off green even when every other sub-gate is
// green -- which is the veto ruling, made observable.
// NOT-APPLICABLE sits LAST, which gives the right answer for every mix by falling through:
// a group with any PASS and the rest not-applicable rolls up PASS, and only a group where
// EVERY sub-gate is not-applicable rolls up not-applicable. Without it here, seventeen
// modules whose every sub-gate a person had dispositioned still reported UNBUILT, because
// the status fell off the end of the list into 'no sub-gates'.
const PRECEDENCE = ['FAIL', 'VOID', 'UNBUILT', 'PARTIAL', 'PASS', 'NOT-APPLICABLE']

export function rollUp(subVerdicts) {
  for (const status of PRECEDENCE) {
    const hit = subVerdicts.filter(v => v.status === status)
    if (hit.length > 0) return [status, `${status} inherited from ${hit.map(v => v.id).join(', ')}`]
  }
  return ['UNBUILT', 'no sub-gates']
}
