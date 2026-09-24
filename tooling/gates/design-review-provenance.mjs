// New judgements bind staged blobs, not a branch commit which a squash merge can discard.
// The manifest contains every declared path (including absent inputs), its Git mode and blob ID.
// The record also retains a self-contained surface archive: a neutral edit before the first
// commit can leave the reviewed raw blobs unreachable, even though the review still stands.
import {execFileSync} from 'node:child_process'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import {isDeepStrictEqual} from 'node:util'

import {componentStem, recordVerdict, referenceRevision, referenceSurface, surfaceFiles} from './design-review.mjs'
import {checkSurfacePath, createSurfaceArchive, provenanceDigest, validateIndexProvenance} from './design-review-provenance-format.mjs'

export {provenanceDigest} from './design-review-provenance-format.mjs'

const KIND = 'git-index-surface-v1'
const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'pipe'],
})
const gitText = (root, ...args) => git(root, ...args).toString('utf8').trim()

function checkModuleId(moduleId) {
  if (typeof moduleId !== 'string' || !/^[a-z0-9][a-z0-9.-]*$/.test(moduleId)) {
    throw new Error('design-review requires a module ID, not a filesystem path')
  }
}

// This is a staging envelope, not a second declaration rule: surfaceFiles still decides exactly
// which of these inputs belong to the surface once the staged files have been materialised.
function inModuleEnvelope(path, moduleId) {
  const stem = componentStem(moduleId)
  const fixed = [
    `specs/modules/ui/${moduleId}/scenarios.json`,
    `specs/modules/ui/${moduleId}/style.css`,
    `gallery/scenarios/${moduleId}.json`,
    `gallery/projections/react/src/${stem}.stories.tsx`,
    `gallery/projections/blazor/Stories/${stem}.stories.razor`,
    `gallery/projections/blazor/Stories/${stem}Scenario.razor`,
  ]
  const parts = path.split('/')
  return fixed.includes(path) || path.startsWith(`conformance/${moduleId}/`)
    || (parts[0] === 'projections' && ['react', 'blazor'].includes(parts[1]) && parts[3] === moduleId)
}

function indexEntries(platformRoot, moduleId) {
  const entries = new Map()
  for (const line of git(platformRoot, 'ls-files', '--stage', '-z').toString('utf8').split('\0').filter(Boolean)) {
    const [, mode, blob, stage, path] = /^(\d{6}) ([0-9a-f]+) ([0-3])\t([\s\S]+)$/.exec(line) ?? []
    if (!path) throw new Error('could not parse the Git index')
    if (!inModuleEnvelope(path, moduleId)) continue
    checkSurfacePath(path)
    if (stage !== '0') throw new Error(`unmerged design-review index input: ${path}`)
    entries.set(path, {path, mode, blob})
  }
  return entries
}

function withSnapshot(files, readBlob, action) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'design-review-index-'))
  try {
    for (const file of files) {
      checkSurfacePath(file.path)
      if (file.blob === null) continue
      const target = resolve(scratch, file.path)
      mkdirSync(dirname(target), {recursive: true})
      // Never create a symlink or execute a filter from the index. The declaration check below
      // refuses non-regular render inputs; excluded files have no authority over the surface.
      // Archived Git modes are checked independently of the temporary rendering filesystem;
      // Windows cannot represent the Unix executable bit faithfully.
      writeFileSync(target, file.mode === '160000' ? '' : readBlob(file.blob))
    }
    return action(scratch)
  } finally {
    rmSync(scratch, {recursive: true, force: true})
  }
}

function checkBase(platformRoot, baseHead) {
  try {
    git(platformRoot, 'cat-file', '-e', `${baseHead}^{commit}`)
    git(platformRoot, 'merge-base', '--is-ancestor', baseHead, 'HEAD')
    git(platformRoot, 'merge-base', '--is-ancestor', baseHead, 'origin/main')
  } catch {
    throw new Error('design-review provenance baseHead must be a reachable ancestor of HEAD and origin/main (fetch main history)')
  }
}

// Historical verification does not compare to the working tree: an honestly expired judgement
// still has valid provenance. The normal reviewVerdict surface comparison owns current expiry.
export function verifyIndexProvenance(platformRoot, record) {
  checkModuleId(record.moduleId)
  const reference = record.reference
  const provenance = reference?.provenance
  const archivedBlobs = validateIndexProvenance(provenance)
  checkBase(platformRoot, provenance.baseHead)
  for (const file of provenance.files) {
    if (!inModuleEnvelope(file.path, record.moduleId)) throw new Error(`undeclared provenance surface path: ${file.path}`)
  }
  return withSnapshot(provenance.files, blob => archivedBlobs.get(blob), snapshot => {
    if (!isDeepStrictEqual(surfaceFiles(snapshot, record.moduleId), provenance.files.map(file => file.path))) {
      throw new Error('design-review provenance manifest does not describe the complete declared surface')
    }
    if (!isDeepStrictEqual(referenceSurface(snapshot, record.moduleId), reference.surface)) {
      throw new Error('design-review provenance blobs do not reconstruct the recorded surface')
    }
    if (referenceRevision(snapshot, record.moduleId) !== reference.revision) {
      throw new Error('design-review provenance blobs do not reconstruct the recorded revision')
    }
    if (reference.source !== `gallery/scenarios/${record.moduleId}.json`) throw new Error('invalid design-review reference source')
    return true
  })
}

export function recordIndexVerdict({platformRoot, moduleId, root, ...judgement}) {
  checkModuleId(moduleId)
  platformRoot = resolve(platformRoot)
  let baseHead
  try {
    baseHead = gitText(platformRoot, 'merge-base', 'HEAD', 'origin/main')
  } catch {
    throw new Error('recording a design verdict requires reachable origin/main history')
  }
  checkBase(platformRoot, baseHead)
  const entries = indexEntries(platformRoot, moduleId)
  const stagedBlobs = new Map()
  const readIndexBlob = blob => {
    if (!stagedBlobs.has(blob)) stagedBlobs.set(blob, git(platformRoot, 'cat-file', 'blob', blob))
    return stagedBlobs.get(blob)
  }
  return withSnapshot([...entries.values()], readIndexBlob, snapshot => {
    const surface = referenceSurface(snapshot, moduleId)
    if (!surface) throw new Error(`${moduleId} has no staged gallery scenario catalog to review against`)
    const files = surfaceFiles(snapshot, moduleId).map(path => entries.get(path) ?? {path, mode: null, blob: null})
    const provenance = {kind: KIND, baseHead, files, archive: createSurfaceArchive(files, readIndexBlob)}
    provenance.digest = provenanceDigest(provenance)
    validateIndexProvenance(provenance)
    const working = referenceSurface(platformRoot, moduleId)
    if (!isDeepStrictEqual(surface, working)) {
      const moved = [...new Set([...Object.keys(surface), ...Object.keys(working ?? {})])]
        .filter(path => surface[path] !== working?.[path]).sort()
      throw new Error(`working render surface differs from the staged index: ${moved.join(', ')}`)
    }
    // Refuse an index changed while its blobs were being read. Unrelated repository paths do
    // not participate; neither a dirty worktree nor an unstaged test file is a blanket veto.
    if (!isDeepStrictEqual(entries, indexEntries(platformRoot, moduleId))) {
      throw new Error('design-review index changed while recording; retry against a stable staged surface')
    }
    return recordVerdict({
      ...judgement, platformRoot: snapshot, moduleId, provenance,
      root: root ?? resolve(platformRoot, 'docs/evidence/design-review'),
    })
  })
}
