#!/usr/bin/env node
// The candidate-side wrapper around base-sourced CI policy (T-577, owner 2026-10-01). Base-sourced policy
// reduces self-classification risk. This wrapper and the workflow wiring that calls it remain
// candidate-controlled and have no enforced independent review (PROC-0001).
//
//   plan       run the base commit's planner; publish exactly one validated `headless-required` output.
//   aggregate  merge group: run the base commit's lane aggregator. Only a base with NO aggregator falls back
//              to the bootstrap lane list below; an aggregator that exists and fails blocks.
import {spawnSync} from 'node:child_process'
import {appendFileSync, existsSync} from 'node:fs'
import {resolve} from 'node:path'

const sha = /^[0-9a-f]{40}$/

// Bootstrap only, for the landing that introduces the base aggregator. Its removal is tracked in Control.
export const bootstrapLanes = ['phase-4-gate', 'gallery-shard', 'gallery-collect']

// The planner gets no way to write workflow outputs or environment itself.
function policyEnvironment(env) {
  const {GITHUB_OUTPUT, GITHUB_ENV, GITHUB_PATH, GITHUB_STEP_SUMMARY, ...rest} = env
  return rest
}

// Never throws; returns 'false' only for a clean exit whose entire output is `false`.
export function planHeadless({policyRoot, base, head, env = process.env, run = spawnSync}) {
  if (!sha.test(base ?? '') || !sha.test(head ?? '')) return {required: 'true', reason: 'missing exact PR commits'}
  const planner = resolve(policyRoot ?? '', 'tooling/plan-pr-validation.mjs')
  if (!policyRoot || !existsSync(planner)) return {required: 'true', reason: 'the base commit has no planner'}
  const result = run(process.execPath, [planner, base, head],
    {cwd: policyRoot, encoding: 'utf8', env: policyEnvironment(env), timeout: 120_000})
  const output = result.status === 0 ? (result.stdout ?? '').trim() : undefined
  if (output === 'true' || output === 'false') return {required: output, reason: 'base planner'}
  return {required: 'true', reason: `base planner failed (exit ${result.status ?? result.error?.message ?? 'unknown'})`}
}

export function aggregateMergeGroup({policyRoot, base, env = process.env, run = spawnSync}) {
  const fail = failure => ({status: 'FAIL', mode: 'none', failures: [failure]})
  if (!sha.test(base ?? '')) return fail('missing exact merge group base commit')
  // An absent aggregator must mean the base commit lacks one, not that its checkout went missing.
  const checkout = run('git', ['-C', policyRoot ?? '', 'rev-parse', 'HEAD'], {encoding: 'utf8'})
  if (checkout.status !== 0 || checkout.stdout.trim() !== base) return fail('base policy checkout is not the merge group base commit')
  const aggregator = resolve(policyRoot, 'tooling/verify-ci-lanes.mjs')
  if (existsSync(aggregator)) {
    const result = run(process.execPath, [aggregator], {cwd: policyRoot, encoding: 'utf8', env, timeout: 120_000})
    process.stdout.write(`${result.stdout ?? ''}${result.stderr ?? ''}`)
    return result.status === 0
      ? {status: 'PASS', mode: 'base', failures: []}
      : {status: 'FAIL', mode: 'base', failures: [`base aggregator exited ${result.status ?? result.error?.message ?? 'unknown'}`]}
  }
  let needs
  try { needs = JSON.parse(env.NEEDS) } catch { return fail('unreadable lane results') }
  const failures = bootstrapLanes.filter(lane => needs?.[lane]?.result !== 'success')
    .map(lane => `${lane}: expected success, observed ${needs?.[lane]?.result ?? 'missing'}`)
  return {status: failures.length ? 'FAIL' : 'PASS', mode: 'bootstrap', failures}
}

if (import.meta.main) {
  const [mode] = process.argv.slice(2)
  const policyRoot = process.env.BASE_POLICY_ROOT && resolve(process.env.BASE_POLICY_ROOT)
  if (mode === 'plan') {
    let plan = {required: 'true', reason: 'wrapper error'}
    try { plan = planHeadless({policyRoot, base: process.env.BASE_SHA, head: process.env.HEAD_SHA}) } catch {}
    console.log(`headless-required=${plan.required} (${plan.reason})`)
    appendFileSync(process.env.GITHUB_OUTPUT, `headless-required=${plan.required}\n`)
  } else if (mode === 'aggregate') {
    const result = aggregateMergeGroup({policyRoot, base: process.env.BASE_SHA})
    console.log(`${result.status} (${result.mode} aggregator)${result.failures.length ? `: ${result.failures.join('; ')}` : ''}`)
    process.exitCode = result.status === 'PASS' ? 0 : 1
  } else {
    console.error('usage: run-base-policy.mjs plan|aggregate')
    process.exitCode = 1
  }
}
