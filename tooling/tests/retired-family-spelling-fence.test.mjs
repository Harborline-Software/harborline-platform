// Ticket 256 slice 11 — THE platform retired-family spelling fence.
//
// Slices 5-10 renamed the load-bearing source-era identities (the locale-provider module key and
// its twelve public symbols, the JsonLogic evaluator/constant names, the class-6 tail). Nothing on
// the platform gate route rejected their RETURN: the renames were defended only by two fixture
// conformance runners and the slice-9 regen check, so a reintroduction in a file no fixture reads
// would land silently (256 s12 review note N2). This is that fence, shaped after the api's
// `RetiredFamilySpellingFenceTests`: one table of rows, one scanned set, one exact allow-list.
//
// SCANNED SET. Every tracked file (`git ls-files`) except the binary extensions listed in
// `BINARY_EXTENSIONS`. Tracked-file discovery replaces the api fence's hand-walked root set: it
// cannot miss a directory, an extension or a repository-root file, and it yields exactly the
// repository-relative path the path rows match on. `the scan covers the whole tracked tree` is the
// floor that keeps it from silently shrinking.
//
// ROWS. Two kinds, both built from code points so this file never spells a retired word.
//   IDENTIFIER — matches on an identifier boundary, so a live longer name that merely contains a
//   retired one is not an offender. Content only.
//   LITERAL — matches as a plain substring in the file's CONTENT and in its repository-relative
//   PATH, so a re-added file or directory named after a retired spelling is an offender even if its
//   content is clean.
//
// NOT rows: the bare family words. They are ordinary English elsewhere in this tree (insurance and
// scheduling domain data), the wire value `<f5>-jsonlogic/v1` is deliberately stable, and the forms
// overlay record still carries the retired spelling until slice 12 lands. A fence on the bare word
// would be a fence nobody could keep green. Rows name the exact spellings slices 5-10 removed.
import assert from 'node:assert/strict'
import {execFileSync} from 'node:child_process'
import {mkdtempSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, join, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import test from 'node:test'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const cp = (...codes) => String.fromCharCode(...codes)

const Retired = cp(83, 104, 105, 112, 121, 97, 114, 100)          // the F5 word, PascalCase
const f5 = Retired.toLowerCase()
const F5_UPPER = Retired.toUpperCase()
const F2Pascal = cp(67, 97, 114, 114, 105, 101, 114)              // the F2 word, PascalCase

/** Retired identifiers, by the slice that removed them. */
export const RETIRED_IDENTIFIERS = [
  // Slices 5-8 — the locale provider's public symbols.
  `${Retired}LocaleProvider`,
  `${Retired}LocaleProviderProps`,
  `${Retired}LocaleContext`,
  `${Retired}LocaleContextValue`,
  `${Retired}LocaleCatalog`,
  `${Retired}StringCatalog`,
  `${Retired}StringKey`,
  `${Retired}NumberFormatter`,
  `${Retired}NumberFormatRequest`,
  `${Retired}Direction`,
  `use${Retired}Locale`,
  `use${Retired}Strings`,
  `Use${Retired}StringsResult`,
  `to${Retired}Catalog`,
  `${F5_UPPER}_DEFAULT_STRINGS`,
  // Slice 9 — the generated-binding names (the VALUE `<f5>-jsonlogic/v1` is untouched and stays).
  `${Retired}JsonLogic`,
  `${Retired}JsonLogicV1`,
  `${F5_UPPER}_JSONLOGIC_V1`,
]

/** Retired literals: matched in content AND in the repository-relative path. */
export const RETIRED_LITERALS = [
  // Slice 5 — the locale-provider module key, and the six directories named after it.
  `${f5}-locale-provider`,
  // Slice 10 — the reports provisionality seam: the interface, its file, and its two test names.
  `Provisionality${F2Pascal}`,
]

/**
 * Class-A rows the ledger keeps: a captured historical record whose subject IS the retired
 * spelling. Exact (file, symbol, reason); held equal to the discovered offender set by
 * `the allow-list is exactly the discovered set`, so a row that stops being needed fails.
 */
export const ALLOW_LIST = [
  {
    path: 'specs/modules/ui/hlp.ui.locale-provider/interface.yaml',
    symbol: `${Retired}LocaleProvider`,
    reason: 'sourceSurface: the pinned upstream capture (repository, path, capturedExports) names the file that was read, not a live symbol',
  },
  {
    path: 'specs/modules/ui/hlp.ui.locale-provider/interface.yaml',
    symbol: `use${Retired}Locale`,
    reason: 'sourceSurface capturedExports — same capture record',
  },
  {
    path: 'specs/modules/ui/hlp.ui.locale-provider/interface.yaml',
    symbol: `use${Retired}Strings`,
    reason: 'sourceSurface capturedExports — same capture record',
  },
  {
    path: 'docs/provenance/source-map.yaml',
    symbol: `${Retired}LocaleProvider`,
    reason: 'sourcePaths: the pre-Harborline capture path, hashed by its own blob; ticket 269 decides the sourcePaths field',
  },
  {
    path: 'docs/evidence/gate-model/ui-gate-model.json',
    symbol: `${Retired}LocaleProvider`,
    reason: 'regenerated gate evidence (2026-09-07): the button module design-review note, now expired, names the surface files as they were at the verdict commit; the note is the gate output and is not rewritten',
  },
  {
    path: 'docs/evidence/gate-model/ui-gate-model.json',
    symbol: `${Retired}LocaleContext`,
    reason: 'regenerated gate evidence (2026-09-07): the button module design-review note, now expired, names the surface files as they were at the verdict commit; the note is the gate output and is not rewritten',
  },
  {
    path: 'docs/evidence/design-review/hlp.ui.button.json',
    symbol: `${Retired}LocaleContext`,
    reason: 'captured design-review evidence (ticket 138 slice 6): the verdict binds the surface AS IT WAS at the commit it was given against, and the file was named that then; rewriting it would be rewriting what the reviewer approved',
  },
  {
    path: 'docs/evidence/design-review/hlp.ui.button.json',
    symbol: `${Retired}LocaleProvider`,
    reason: 'captured design-review evidence (ticket 138 slice 6) -- same bound surface',
  },
  {
    path: 'docs/evidence/phase-4/gate.json',
    symbol: `${Retired}LocaleProvider`,
    reason: 'regenerated gate evidence (2026-09-07): the button module design-review note, now expired, names the surface files as they were at the verdict commit; the note is the gate output and is not rewritten',
  },
  {
    path: 'docs/evidence/phase-4/gate.json',
    symbol: `${Retired}LocaleContext`,
    reason: 'regenerated gate evidence (2026-09-07): the button module design-review note, now expired, names the surface files as they were at the verdict commit; the note is the gate output and is not rewritten',
  },
]

const BINARY_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.ico', '.webp', '.avif', '.pdf', '.zip', '.gz',
  '.woff', '.woff2', '.ttf', '.eot', '.otf', '.dll', '.exe', '.snk', '.mp4', '.webm',
])

const extensionOf = (path) => {
  const dot = path.lastIndexOf('.')
  const slash = path.lastIndexOf('/')
  return dot > slash ? path.slice(dot).toLowerCase() : ''
}

// CONTENT COMES FROM THE INDEX, never from the working file. The landing chain judges a DETACHED
// tree built from `git write-tree` (tooling/run-phase4-receipt.mjs), so a fence that read working
// files would answer one thing here and another thing there -- any unstaged edit, or a tracked
// evidence file a local gate run rewrote, would move the verdict. `git grep --cached` reads the
// staged tree, which in the tested checkout IS the tested tree; `the discovery set is the same in a
// detached tested tree` holds the two equal.
const git = (cwd, ...args) => execFileSync('git', args, {cwd, encoding: 'utf8', maxBuffer: 1 << 28})

const trackedIn = (cwd) => git(cwd, 'ls-files', '-z').split('\0').filter(Boolean)
const scannedIn = (cwd) => trackedIn(cwd).filter(path => !BINARY_EXTENSIONS.has(extensionOf(path)))

const trackedFiles = trackedIn(root)
const scannedFiles = scannedIn(root)

const identifierPattern = (name) => new RegExp(`(?<![A-Za-z0-9_])${name}(?![A-Za-z0-9_])`)

/** Matching lines of the staged tree, as [path, line] pairs. One `git grep` per kind, all rows in
 *  one invocation: twenty processes over a 3k-file index cost ten seconds a pass. `-I` leaves git's
 *  binary files alone; the exact row match is re-applied per line below. */
function grepIndex(cwd, flags, symbols) {
  let output
  try {
    output = git(cwd, 'grep', '--cached', '-I', '-z', '-F', ...flags, ...symbols.flatMap(symbol => ['-e', symbol]), '--')
  } catch (error) {
    if (error.status === 1) return []   // git grep exits 1 for "no match"
    throw error
  }
  return output.split('\n').filter(Boolean).map(line => {
    const cut = line.indexOf('\0')
    return [line.slice(0, cut), line.slice(cut + 1)]
  })
}

/** Every (path, symbol) the tables find in the staged tree of `cwd`. */
function discoverOffenders(cwd = root) {
  const scanned = new Set(scannedIn(cwd))
  const found = new Set()
  for (const [path, line] of grepIndex(cwd, ['-w'], RETIRED_IDENTIFIERS)) {
    if (!scanned.has(path)) continue
    for (const symbol of RETIRED_IDENTIFIERS) if (identifierPattern(symbol).test(line)) found.add(`${path}::${symbol}`)
  }
  for (const [path, line] of grepIndex(cwd, [], RETIRED_LITERALS)) {
    if (!scanned.has(path)) continue
    for (const symbol of RETIRED_LITERALS) if (line.includes(symbol)) found.add(`${path}::${symbol}`)
  }
  for (const path of scanned) {
    for (const symbol of RETIRED_LITERALS) if (path.includes(symbol)) found.add(`${path}::${symbol}`)
  }
  return [...found].map(entry => {
    const cut = entry.indexOf('::')
    return {path: entry.slice(0, cut), symbol: entry.slice(cut + 2)}
  })
}

const key = (row) => `${row.path}::${row.symbol}`

test('the fence tables are not empty', () => {
  assert.ok(RETIRED_IDENTIFIERS.length > 0, 'no retired identifier rows — the fence would pass vacuously')
  assert.ok(RETIRED_LITERALS.length > 0, 'no retired literal rows — the fence would pass vacuously')
  assert.equal(new Set(RETIRED_IDENTIFIERS).size, RETIRED_IDENTIFIERS.length, 'duplicate identifier row')
  assert.equal(new Set(RETIRED_LITERALS).size, RETIRED_LITERALS.length, 'duplicate literal row')
  for (const row of ALLOW_LIST) {
    assert.ok(row.path && row.symbol && row.reason, `allow-list row is missing a field: ${JSON.stringify(row)}`)
    assert.ok([...RETIRED_IDENTIFIERS, ...RETIRED_LITERALS].includes(row.symbol),
      `allow-list row names ${row.symbol}, which is not a fence row`)
  }
})

test('the scan covers the whole tracked tree', () => {
  assert.ok(trackedFiles.length >= 3000, `only ${trackedFiles.length} tracked files — the scan shrank`)
  assert.ok(scannedFiles.length >= 2900, `only ${scannedFiles.length} scanned files — the skip list grew`)
  const roots = new Set(scannedFiles.map(path => path.split('/')[0]).map(top => top.includes('.') ? '<root file>' : top))
  for (const expected of ['catalog', 'conformance', 'docs', 'eng', 'gallery', 'projections', 'specs', 'tests', 'tooling', '<root file>']) {
    assert.ok(roots.has(expected), `nothing scanned under ${expected}`)
  }
})

test('no retired family spelling outside the allow-list', () => {
  const allowed = new Set(ALLOW_LIST.map(key))
  const offenders = discoverOffenders().filter(row => !allowed.has(key(row)))
  assert.deepEqual(offenders, [],
    `retired family spellings reintroduced:\n${offenders.map(row => `  ${row.path}: ${row.symbol}`).join('\n')}`)
})

test('the allow-list is exactly the discovered set', () => {
  const discovered = new Set(discoverOffenders().map(key))
  const stale = ALLOW_LIST.filter(row => !discovered.has(key(row)))
  assert.deepEqual(stale.map(key), [],
    'allow-list rows no longer match anything — delete them, an inexact allow-list hides the next one')
})

// The tree every landing is judged on is a detached worktree whose index is `git write-tree` of this
// one. Build that tree here -- `--no-checkout`, because index-sourced discovery needs no files -- and
// require the same discovery set. This is the test that would have caught the fence answering
// differently under tooling/run-phase4-receipt.mjs than it does in the working tree.
test('the discovery set is the same in a detached tested tree', () => {
  const scratch = mkdtempSync(join(tmpdir(), 'retired-family-fence-tested-tree-'))
  const testedCheckout = join(scratch, 'tested-tree')
  try {
    git(root, 'worktree', 'add', '--detach', '--no-checkout', testedCheckout, 'HEAD')
    git(testedCheckout, 'read-tree', git(root, 'write-tree').trim())
    assert.deepEqual(
      discoverOffenders(testedCheckout).map(key).sort(),
      discoverOffenders(root).map(key).sort(),
      'the fence discovers a different set in the tested tree than in the working tree')
  } finally {
    try { git(root, 'worktree', 'remove', '--force', testedCheckout) } catch {}
    try { rmSync(scratch, {recursive: true, force: true}) } catch {}
  }
})
