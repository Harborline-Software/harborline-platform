#!/usr/bin/env node
// T-705: no network or publication here. The workflow owns signing and the draft lifecycle.
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {mkdirSync, readFileSync, readdirSync, writeFileSync} from 'node:fs'
import path from 'node:path'
import {checkReleaseReceipt, ARTIFACT_PATH} from './release-receipt.mjs'
import {requiredStepIds} from './gate-contract.mjs'

export const subjects = ['platform-pack.export.json', 'release-receipt.json', 'SHA256SUMS']
const digest = bytes => createHash('sha256').update(bytes).digest('hex')
const git = (root, ...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8'}).trim()

export function validateReceipt(root, bytes, commit) {
  if (!/^[0-9a-f]{40}$/.test(commit)) throw new Error('expected full approved commit SHA')
  const refusal = checkReleaseReceipt(root, bytes) // both strict defaults stay ON
  if (refusal) throw new Error(`${refusal.code} at ${refusal.field}`)
  const receipt = JSON.parse(bytes)
  const tree = git(root, 'rev-parse', `${commit}^{tree}`)
  if (receipt.commit !== commit || receipt.tree !== tree || receipt.transcript.mode !== 'exact-staged-tree')
    throw new Error('receipt does not bind the approved commit tree')
  const gitDir = git(root, 'rev-parse', '--git-dir')
  const phase4 = JSON.parse(readFileSync(path.resolve(root, gitDir, 'harborline-phase4-receipt.json')))
  const gate = phase4.gate
  if (phase4.schemaVersion !== 3 || phase4.repository !== 'harborline-platform' || phase4.phase !== 4
      || gate?.status !== 'PASS' || gate?.schemaVersion !== 3 || gate?.phase !== 4
      || gate?.subject?.repository !== 'harborline-platform'
      || gate?.subject?.baseHead !== commit || gate?.subject?.testedTree !== tree
      || JSON.stringify(gate?.requiredStepIds) !== JSON.stringify(requiredStepIds)
      || !requiredStepIds.every((id, index) => gate.results?.[index]?.id === id && gate.results[index].passed === true))
    throw new Error('complete phase-4 pass required for approved commit')
  // Check the CURRENT run as well as the committed evidence checked above.
  if (!Array.isArray(gate.designReview?.expired) || gate.designReview.expired.length)
    throw new Error('current design reviews absent or expired')
}

export function stage(root, directory, commit) {
  if (git(root, 'rev-parse', 'HEAD') !== commit || git(root, 'write-tree') !== git(root, 'rev-parse', `${commit}^{tree}`))
    throw new Error('checkout must be the approved commit tree')
  const gitDir = git(root, 'rev-parse', '--git-dir')
  const receipt = readFileSync(path.resolve(root, gitDir, 'harborline-release-receipt.json'))
  validateReceipt(root, receipt, commit)
  mkdirSync(directory, {recursive: true})
  if (readdirSync(directory).length) throw new Error('staging directory must be empty')
  const seed = execFileSync('git', ['cat-file', 'blob', `${commit}:${ARTIFACT_PATH}`], {cwd: root})
  writeFileSync(path.join(directory, subjects[0]), seed)
  writeFileSync(path.join(directory, subjects[1]), receipt)
  writeFileSync(path.join(directory, 'SHA256SUMS'), [subjects[0], subjects[1]]
    .map(name => `${digest(readFileSync(path.join(directory, name)))}  ${name}\n`).join(''))
  check(root, directory, commit)
}

export function check(root, directory, commit, withBundle = false) {
  const expected = [...subjects, ...(withBundle ? ['provenance.sigstore.json'] : [])].sort()
  if (JSON.stringify(readdirSync(directory).sort()) !== JSON.stringify(expected)) throw new Error('release asset inventory mismatch')
  const receipt = readFileSync(path.join(directory, 'release-receipt.json'))
  validateReceipt(root, receipt, commit)
  const seed = readFileSync(path.join(directory, 'platform-pack.export.json'))
  if (digest(seed) !== JSON.parse(receipt).artifact.digest) throw new Error('seed differs from receipt')
  const sums = ['platform-pack.export.json', 'release-receipt.json']
    .map(name => `${digest(readFileSync(path.join(directory, name)))}  ${name}\n`).join('')
  if (readFileSync(path.join(directory, 'SHA256SUMS'), 'utf8') !== sums) throw new Error('checksums differ from assets')
  if (withBundle && !readFileSync(path.join(directory, 'provenance.sigstore.json')).length) throw new Error('empty provenance bundle')
}
if (import.meta.main) {
  const [command, directory, commit] = process.argv.slice(2)
  if (command === 'stage') stage(process.cwd(), path.resolve(directory), commit)
  else if (command === 'check') check(process.cwd(), path.resolve(directory), commit, process.argv.includes('--bundle'))
  else throw new Error('usage: platform-release.mjs stage|check <directory> <full-commit> [--bundle]')
}
