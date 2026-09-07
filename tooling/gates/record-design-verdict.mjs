#!/usr/bin/env node
// Vendored from harborline-control/tools/record-design-verdict.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Records a design-review verdict. Deliberately a separate entry point from run-vertical-pair.mjs:
// reading gate verdicts is something tooling does, and writing one is something a person does.
//
// Usage:
//   node record-design-verdict.mjs <platform-root> <moduleId> --reviewer "Name" \
//        --verdict approved|rejected|changes-requested --date YYYY-MM-DD [--notes "..."]

import {recordVerdict} from './design-review.mjs'

const argv = process.argv.slice(2)
const flag = name => {
  const index = argv.indexOf(`--${name}`)
  return index >= 0 ? argv[index + 1] : undefined
}
const [platformRoot, moduleId] = argv.filter(a => !a.startsWith('--') && argv[argv.indexOf(a) - 1]?.startsWith('--') !== true)

try {
  const record = recordVerdict({
    platformRoot,
    moduleId,
    reviewer: flag('reviewer'),
    verdict: flag('verdict'),
    notes: flag('notes'),
    recordedAt: flag('date'),
  })
  process.stdout.write(`${JSON.stringify(record, null, 2)}\n`)
} catch (error) {
  process.stderr.write(`${error.message}\n`)
  process.exitCode = 1
}
