#!/usr/bin/env node
// T-577: admission feedback and landing proof have different required lanes.
import {appendFileSync, readFileSync} from 'node:fs'

export function verifyCiLanes({event, payload, repository, actionsEnabled}, needs) {
  const blocked = reason => ({status: 'BLOCKED', scope: 'none', reason})
  if (actionsEnabled !== 'true') return blocked('Actions are disabled; validation did not run')
  const preliminary = event === 'pull_request'
  if (!preliminary && event !== 'merge_group' && event !== 'workflow_dispatch') {
    return blocked(`unsupported validation event: ${event}`)
  }
  if (preliminary) {
    const pr = payload?.pull_request
    if (!repository || pr?.head?.repo?.full_name !== repository) return blocked('fork or missing PR metadata: maintainer validation required')
    if (pr.draft !== false) return blocked('draft PR: admission deferred; mark ready for review')
    if (pr.labels?.some(label => label.name === 'stacked')) return blocked('stacked PR: admission deferred until the parent lands')
  }
  const headless = needs?.['pr-preflight']?.outputs?.['headless-required']
  if (preliminary && headless !== 'true' && headless !== 'false') {
    return {status: 'FAIL', scope: 'preliminary', failures: ['missing explicit PR behavioral coverage plan']}
  }
  const expected = preliminary
    ? {'pr-preflight': 'success', 'phase-4-gate': headless === 'true' ? 'success' : 'skipped', 'gallery-shard': 'skipped', 'gallery-collect': 'skipped'}
    : {'pr-preflight': 'skipped', 'phase-4-gate': 'success', 'gallery-shard': 'success', 'gallery-collect': 'success'}
  const failures = Object.entries(expected)
    .filter(([job, result]) => needs?.[job]?.result !== result)
    .map(([job, result]) => `${job}: expected ${result}, observed ${needs?.[job]?.result ?? 'missing'}`)
  if (!needs || Object.keys(needs).length !== 4) failures.push('verify requires exactly four declared lanes')
  return {status: failures.length ? 'FAIL' : 'PASS', scope: preliminary ? 'preliminary' : 'full', failures}
}

if (import.meta.main) {
  try {
    const result = verifyCiLanes({
      event: process.env.GITHUB_EVENT_NAME,
      payload: JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')),
      repository: process.env.GITHUB_REPOSITORY,
      actionsEnabled: process.env.ACTIONS_ENABLED,
    }, JSON.parse(process.env.NEEDS))
    const message = result.status === 'PASS'
      ? result.scope === 'preliminary'
        ? 'Preliminary PR validation passed. Full landing proof awaits the merge-group gate.'
        : 'Full validation passed: headless gate, every browser shard and collection succeeded.'
      : `${result.status}: ${result.reason ?? result.failures.join('; ')}`
    console.log(message)
    console.log(`Validation commit: ${process.env.GITHUB_SHA}`)
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${message}\n\nValidation commit: ${process.env.GITHUB_SHA}\n`)
    process.exitCode = result.status === 'PASS' ? 0 : 1
  } catch (error) {
    console.error(`FAIL: unreadable validation inputs: ${error.message}`)
    process.exitCode = 1
  }
}
