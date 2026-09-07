import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import test from 'node:test'

const toolingRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const prepareGalleriesModuleUrl = pathToFileURL(resolve(toolingRoot, 'prepare-galleries.mjs')).href

// Ticket 275: prepareGalleries's package-fixture verification is a multi-minute step
// (dotnet restore --force --no-cache x ~20, npm installs, tsc builds) that the gate did not
// record as a step at all -- a standalone lane running it paid a prerequisite the gate skips
// and saw nothing on stderr for the whole time. A one-line breadcrumb before the call makes the
// silence self-explaining.
//
// HARBORLINE_API_REPO is pointed at a path with no `.git`/bare-repo marker so resolveAppshellFeed
// fails fast on a cache miss ("is not a git repository") instead of cloning or fetching over the
// network; on a cache hit it returns instantly regardless. Either way the child exits quickly and
// the breadcrumb -- written before that call -- is what this test checks for.
test('prepareGalleries writes a breadcrumb before verifying package fixtures', () => {
  const script = `
    import { prepareGalleries } from ${JSON.stringify(prepareGalleriesModuleUrl)}
    try { prepareGalleries({}) } catch { /* the fixture build itself is not what this test checks */ }
  `
  // spawnSync (not execFileSync) so stderr is captured whether the fixture step itself succeeds
  // or fails, which depends on what's already built in this checkout -- either way the breadcrumb
  // must be in it.
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], {
    encoding: 'utf8',
    env: { ...process.env, HARBORLINE_API_REPO: toolingRoot },
    timeout: 30_000,
  })
  assert.match(result.stderr ?? '', /^preparing galleries: verifying package fixtures$/m)
})
