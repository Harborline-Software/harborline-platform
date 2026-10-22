#!/usr/bin/env node
// Preliminary static checks, never a phase-4 receipt or a substitute for the landing gate.
import {spawnSync} from 'node:child_process'
import {existsSync, readFileSync} from 'node:fs'
import {resolve, sep} from 'node:path'

export function toolingTestsPassed(run) {
  const counts = Object.fromEntries(['tests', 'pass', 'fail', 'cancelled', 'skipped', 'todo']
    .map(key => [key, Number(new RegExp(`^# ${key} (\\d+)$`, 'm').exec(run.stdout ?? '')?.[1] ?? NaN)]))
  return run.status === 0 && counts.tests > 0 && counts.pass === counts.tests
    && counts.fail === 0 && counts.cancelled === 0 && counts.skipped === 0 && counts.todo === 0
}

// Feedback for the existing PR/review note only; presence cannot establish design quality.
export function reuseNoteAdvisories(body, {head, localExists = () => false} = {}) {
  let uncommented = typeof body === 'string' ? body : ''
  let previous
  // Removing a comment can join surrounding text into another comment.
  do {
    previous = uncommented
    uncommented = uncommented.replace(/<!--[\s\S]*?-->/g, '')
  } while (uncommented !== previous)
  const sections = uncommented.split(/^##[ \t]+/m).slice(1).filter(section => /^Reuse note[ \t]*\r?\n/i.test(section))
  if (sections.length === 0) return ['Reuse note missing; reviewer should confirm scope (routine changes need a reason).']
  if (sections.length !== 1) return ['Multiple Reuse note sections; keep one current note.']
  const section = sections[0]
  const fields = new Map()
  for (const match of section.matchAll(/^-[ \t]+(Scope|Revision|Checked|Chosen|Custom):[ \t]*(.*)$/gmi)) {
    const key = match[1].toLowerCase()
    if (fields.has(key)) return [`Reuse note repeats ${match[1]}; keep one value.`]
    fields.set(key, match[2].trim())
  }
  const messages = []
  const scope = fields.get('scope') ?? ''
  const substantial = scope.toLowerCase() === 'substantial'
  if (/^routine\b/i.test(scope)) {
    if (!/^routine[ \t]*[:—-][ \t]*\S/i.test(scope)) messages.push('Routine scope needs a reason for reviewer assessment.')
  } else if (!substantial) messages.push('Reuse note scope needs human classification: substantial or routine: reason.')
  const revision = fields.get('revision') ?? ''
  if (!/^[0-9a-f]{40}$/i.test(revision)) messages.push('Reuse note needs a full 40-digit head revision.')
  else if (typeof head !== 'string' || !/^[0-9a-f]{40}$/i.test(head)) messages.push('Current review head unavailable; Reuse note revision was not verified.')
  else if (revision.toLowerCase() !== head.toLowerCase()) messages.push('Reuse note revision does not match the current review head.')
  if (substantial) {
    if (!/\[[^\]]+\]\((<[^<>]+>|[^\s)<>]+)\)/.test(fields.get('checked') ?? '')) messages.push('Substantial Reuse note needs Checked references.')
    if (!fields.get('chosen')) messages.push('Substantial Reuse note needs Chosen approach.')
    if (!fields.get('custom')) messages.push('Substantial Reuse note needs Custom rationale (or none).')
  }
  // The documented inline-link form supports fragments and angle-bracket paths with spaces.
  for (const match of section.matchAll(/\[[^\]]+\]\((<[^<>]+>|[^\s)<>]+)\)/g)) {
    const target = match[1].replace(/^<|>$/g, '')
    if (/^(?:[a-z][a-z\d+.-]*:|\/\/|#)/i.test(target)) continue
    let path
    try { path = decodeURIComponent(target.split('#')[0]) }
    catch { messages.push(`Reuse note local reference is malformed: ${target}`); continue }
    if (!localExists(path)) messages.push(`Reuse note local reference was not found: ${path}`)
  }
  return messages
}

export function runPrPreflight(root, run = spawnSync, reuse = {}) {
  const localExists = path => {
    const file = resolve(root, path)
    return (file === root || file.startsWith(`${root}${sep}`)) && existsSync(file)
  }
  for (const message of reuseNoteAdvisories(reuse.body, {head: reuse.head, localExists})) {
    console.warn(`Reuse note advisory: ${message}`)
  }
  const checks = [
    ['consumer-neutral boundary', 'bash', ['eng/verify-boundaries.sh']],
    ['prop vocabulary', process.execPath, ['tooling/gates/scan-prop-vocabulary.mjs', '.']],
    ['recorded evidence provenance', process.execPath, ['tooling/gates/scan-evidence-provenance.mjs', '.']],
    // Existing catalog-preflight mode: checks source policy without demanding an updated full receipt.
    ['catalog preflight', process.execPath, ['tooling/validate-repository.mjs', '--allow-stale-gate']],
    ['CI contract tests', process.execPath, ['--test', '--test-reporter', 'tap',
      'tooling/tests/ci-verification.test.mjs', 'tooling/tests/ci-load.test.mjs',
      'tooling/tests/collect-gallery-shards.test.mjs', 'tooling/tests/evidence-provenance.test.mjs',
      'tooling/tests/prop-vocabulary.test.mjs']],
  ]
  for (const [name, command, args] of checks) {
    if (name === 'CI contract tests' && args.slice(3).some(file => !existsSync(resolve(root, file)))) {
      console.error('Preliminary CI contract tests: FAIL (a declared test file is missing)')
      return false
    }
    const result = run(command, args, {cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024})
    if (result.status !== 0 || (name === 'CI contract tests' && !toolingTestsPassed(result))) {
      process.stderr.write(`Preliminary ${name}: FAIL\n${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}\n`)
      return false
    }
    console.log(`Preliminary ${name}: passed`)
  }
  console.log('Preliminary PR preflight passed; no full gate proof produced.')
  return true
}

if (import.meta.main) {
  const root = resolve(import.meta.dirname, '..')
  const reuse = {}
  try {
    const option = process.argv.indexOf('--reuse-note')
    if (option !== -1 && (process.argv.lastIndexOf('--reuse-note') !== option || !process.argv[option + 1] || process.argv[option + 1].startsWith('--'))) {
      throw new Error('supply one --reuse-note file containing the PR/review body')
    }
    const event = process.env.GITHUB_EVENT_PATH ? JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : null
    reuse.body = option === -1 ? event?.pull_request?.body ?? '' : readFileSync(resolve(root, process.argv[option + 1]), 'utf8')
    if (event?.pull_request) reuse.head = event.pull_request.head?.sha
    else {
      const current = spawnSync('git', ['rev-parse', 'HEAD'], {cwd: root, encoding: 'utf8', maxBuffer: 1024 * 1024})
      if (current.status === 0) reuse.head = current.stdout.trim()
    }
  } catch (error) {
    console.warn(`Reuse note advisory: note/revision could not be read (${error.message}).`)
  }
  process.exitCode = runPrPreflight(root, spawnSync, reuse) ? 0 : 1
}
