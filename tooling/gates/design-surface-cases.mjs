// Ticket 138 slice 4 -- the false-positive proof, as runnable cases rather than an assertion.
//
// HLP-0008 rejected source-tree hashing because it produces a false-positive stream: a rename, a
// reformat, a comment, a README line would each expire a verdict nobody's design changed, and a
// reviewer who is re-approving noise stops reading. The declared surface is supposed to avoid that.
// "Supposed to" is what this module removes: each case below EDITS A REAL FILE of a real module in
// a scratch copy of the tree, recomputes the surface the gate itself computes, and reports whether
// the verdict expired. The positive control is in the same list, because a proof that only ever
// shows green cannot tell "neutral edits do not expire" apart from "nothing expires".
//
// This is fixture code. It must never be imported by design-review.mjs: a detector that shares a
// module with its own proof proves the copy, not the detector.

import {cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {recordVerdict, referenceRevision, referenceSurface, reviewVerdict} from './design-review.mjs'

export const platformRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

// A real module, with a real projection in both lanes, a real test file and a real README -- the
// four places a behaviour-neutral edit actually lands. A fabricated module would prove nothing
// about the tree the gate runs over.
export const CASE_MODULE = 'hlp.ui.button'

const TSX = `projections/react/ui/${CASE_MODULE}/src/Button.tsx`
const TEST = `projections/react/ui/${CASE_MODULE}/src/__tests__/Button.native.test.tsx`
const README = `projections/blazor/ui/${CASE_MODULE}/README.md`
const AUTHORITY_CSS = `specs/modules/ui/${CASE_MODULE}/style.css`

// Everything the module owns, so the copy stays a faithful subject as the declared surface widens
// (slice 3 binds the render inputs). Copying only today's three surface files would make every
// case below vacuously green forever.
const OWNED = [
  `specs/modules/ui/${CASE_MODULE}`,
  `projections/react/ui/${CASE_MODULE}`,
  `projections/blazor/ui/${CASE_MODULE}`,
  `gallery/scenarios/${CASE_MODULE}.json`,
  // slice 3 widens the surface to the stories and the conformance fixtures; the scratch copy must carry them.
  `conformance/${CASE_MODULE}`,
  `gallery/projections/react/src/Button.stories.tsx`,
  `gallery/projections/blazor/Stories/Button.stories.razor`,
  `gallery/projections/blazor/Stories/ButtonScenario.razor`,
]

export function scratchCopy(root = platformRoot) {
  const copy = mkdtempSync(resolve(tmpdir(), 'design-neutral-'))
  for (const rel of OWNED) {
    const from = resolve(root, rel)
    if (!existsSync(from)) throw new Error(`fixture source missing: ${rel}`)
    mkdirSync(dirname(resolve(copy, rel)), {recursive: true})
    cpSync(from, resolve(copy, rel), {recursive: true})
  }
  return copy
}

// `neutral: true` means "this edit cannot change what a reviewer sees, so the verdict must survive".
// Each `apply` returns the new text and MUST change it; runCase refuses a no-op, because an edit
// that silently did nothing is the easiest way to fake this whole proof.
export const CASES = [
  {
    id: 'rename-private-helper', neutral: true, file: TSX,
    apply: text => text.replaceAll('buildFillModeClass', 'fillModeClassFor'),
  },
  {
    id: 'reformat-projection', neutral: true, file: TSX,
    // A whitespace-only reformat: every indentation unit doubled. The repo ships no formatter to
    // shell out to, and inventing a dependency for one fixture is worse than reindenting here.
    apply: text => text.replace(/^( +)/gm, spaces => spaces.repeat(2)),
  },
  {
    id: 'comment-only', neutral: true, file: TSX,
    apply: text => `// ticket 138 slice 4: a comment is not an appearance.\n${text}`,
  },
  {
    id: 'test-file-change', neutral: true, file: TEST,
    apply: text => `${text}\n// ticket 138 slice 4: a test-file edit changes no rendered pixel.\n`,
  },
  {
    id: 'readme-change', neutral: true, file: README,
    apply: text => `${text}\n<!-- ticket 138 slice 4: documentation is not design. -->\n`,
  },
  {
    id: 'import-reorder', neutral: true, file: TSX,
    apply: text => {
      const lines = text.split('\n')
      const first = lines.findIndex(line => line.startsWith('import '))
      const second = lines.findIndex((line, index) => index > first && line.startsWith('import '))
      if (first < 0 || second < 0) throw new Error(`${TSX} no longer opens with two imports to reorder`)
      const reordered = [...lines]
      ;[reordered[first], reordered[second]] = [reordered[second], reordered[first]]
      return reordered.join('\n')
    },
  },
  // The positive control, deliberately in the same list and run by the same code path. An
  // appearance change to the spec authority MUST expire the verdict and name the file.
  {
    id: 'appearance-change-token', neutral: false, file: AUTHORITY_CSS,
    apply: text => `${text}\n.hl-button--proof { color: #b00020; }\n`,
  },
]

// Applies one case to a fresh scratch copy and reports what the gate's own rule says afterwards.
export function runCase(kase, root = platformRoot) {
  const platform = scratchCopy(root)
  try {
    const before = referenceSurface(platform, CASE_MODULE)
    const record = recordVerdict({
      platformRoot: platform, moduleId: CASE_MODULE, reviewer: 'Slice Four Proof',
      verdict: 'approved', recordedAt: '2026-09-06T00:00:00.000Z',
      root: resolve(platform, 'records'),
    })
    const target = resolve(platform, kase.file)
    const original = readFileSync(target, 'utf8')
    const edited = kase.apply(original)
    if (edited === original) throw new Error(`case ${kase.id} edited nothing in ${kase.file}`)
    writeFileSync(target, edited)
    const after = referenceSurface(platform, CASE_MODULE)
    const [status, note] = reviewVerdict({
      record, revision: referenceRevision(platform, CASE_MODULE), surface: after,
    })
    return {id: kase.id, neutral: kase.neutral, file: kase.file, before, after, status, note}
  } finally {
    rmSync(platform, {recursive: true, force: true})
  }
}

// One digest over the whole surface, for a report table that a person reads. The rule itself is
// per-file; this is presentation only.
export const digestOf = surface => Object.entries(surface)
  .map(([file, hash]) => `${file}=${hash.slice(0, 12)}`).join(' ')
