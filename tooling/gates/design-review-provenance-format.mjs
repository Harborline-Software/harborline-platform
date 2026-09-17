// A self-contained archive is necessary: a reviewed index blob can become unreachable after a
// render-neutral edit before the first commit. A digest or a local cat-file lookup cannot retain
// it. The visible manifest describes the archived paths/modes; this payload retains their bytes.
import {createHash} from 'node:crypto'
import {deflateRawSync, inflateRawSync} from 'node:zlib'
import {isDeepStrictEqual} from 'node:util'

const KIND = 'git-index-surface-v1'
const ENCODING = 'deflate-base64-json-v1'
const OBJECT_ID = /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/
const digest = bytes => `sha256:${createHash('sha256').update(bytes).digest('hex')}`

export function checkSurfacePath(path) {
  if (typeof path !== 'string' || /[\\:\x00]/.test(path)
    || path.split('/').some(part => !part || part === '.' || part === '..')) {
    throw new Error(`invalid design-review provenance path: ${path}`)
  }
}

export function createSurfaceArchive(files, readBlob) {
  const entries = files.map(({path, mode, blob}) => ({
    path, mode, contentBase64: blob === null ? null : readBlob(blob).toString('base64'),
  }))
  const bytes = Buffer.from(JSON.stringify(entries))
  return {encoding: ENCODING, digest: digest(bytes), data: deflateRawSync(bytes, {level: 9}).toString('base64')}
}

export function provenanceDigest({kind, baseHead, files, archive}) {
  // Fixed property order and ordinal path order define the v1 canonical encoding. Both the
  // descriptor and the complete archived artifact are bound, not merely locally available IDs.
  const canonical = {
    kind, baseHead, files: files.map(({path, mode, blob}) => ({path, mode, blob})),
    archive: archive && {encoding: archive.encoding, digest: archive.digest, data: archive.data},
  }
  return digest(JSON.stringify(canonical))
}

function decodeBase64(text, label) {
  if (typeof text !== 'string') throw new Error(`invalid design-review ${label} encoding`)
  const bytes = Buffer.from(text, 'base64')
  if (bytes.toString('base64') !== text) throw new Error(`noncanonical design-review ${label} encoding`)
  return bytes
}

// Pure validation is shared by the standalone verdict reader and historical Git verification.
// Returning the verified archive bytes ensures callers cannot fall back to the local object DB.
export function validateIndexProvenance(provenance) {
  if (provenance?.kind !== KIND) throw new Error('unrecognised design-review provenance kind')
  if (!OBJECT_ID.test(provenance.baseHead ?? '')) throw new Error('invalid design-review provenance baseHead')
  if (!Array.isArray(provenance.files) || !provenance.files.length) throw new Error('design-review provenance has no surface manifest')
  let previous
  for (const file of provenance.files) {
    if (!file || !isDeepStrictEqual(Object.keys(file).sort(), ['blob', 'mode', 'path'])) {
      throw new Error('invalid design-review provenance manifest entry')
    }
    checkSurfacePath(file.path)
    if (previous !== undefined && previous >= file.path) throw new Error('design-review provenance paths are not canonical')
    previous = file.path
    if (file.mode === null && file.blob === null) continue
    if (!['100644', '100755'].includes(file.mode)) throw new Error(`unsupported design-review provenance mode: ${file.path}`)
    if (!OBJECT_ID.test(file.blob ?? '')) throw new Error(`invalid design-review provenance blob: ${file.path}`)
  }
  const archive = provenance.archive
  if (!archive || !isDeepStrictEqual(Object.keys(archive).sort(), ['data', 'digest', 'encoding']) || archive.encoding !== ENCODING) {
    throw new Error('design-review provenance requires a self-contained surface archive')
  }
  if (provenance.digest !== provenanceDigest(provenance)) throw new Error('design-review provenance digest does not match its manifest and archive')
  let bytes
  let entries
  try {
    bytes = inflateRawSync(decodeBase64(archive.data, 'surface archive'), {maxOutputLength: 64 * 1024 * 1024})
    entries = JSON.parse(bytes.toString('utf8'))
  } catch {
    throw new Error('invalid design-review surface archive encoding')
  }
  if (archive.digest !== digest(bytes)) throw new Error('design-review surface archive digest does not match its bytes')
  if (!Array.isArray(entries) || entries.length !== provenance.files.length || JSON.stringify(entries) !== bytes.toString('utf8')) {
    throw new Error('design-review surface archive is not a canonical complete surface')
  }
  const blobs = new Map()
  entries.forEach((entry, index) => {
    const file = provenance.files[index]
    if (!entry || !isDeepStrictEqual(Object.keys(entry), ['path', 'mode', 'contentBase64'])) {
      throw new Error('invalid design-review surface archive entry')
    }
    if (entry.path !== file.path || entry.mode !== file.mode) {
      throw new Error(`design-review surface archive path/mode does not match manifest: ${file.path}`)
    }
    if (file.blob === null) {
      if (entry.contentBase64 !== null) throw new Error(`absent surface input has archived bytes: ${file.path}`)
      return
    }
    const content = decodeBase64(entry.contentBase64, 'surface content')
    const blob = createHash(file.blob.length === 40 ? 'sha1' : 'sha256')
      .update(`blob ${content.length}\0`).update(content).digest('hex')
    if (blob !== file.blob) throw new Error(`design-review surface archive blob does not match manifest: ${file.path}`)
    blobs.set(blob, content)
  })
  return blobs
}
