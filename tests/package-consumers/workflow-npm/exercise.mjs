// Workflows capability vertical — the RENDERER lane. Restores ONLY the packed
// @harborline-software/contracts artifact and runs the 13-case application-derived admission
// corpus through the packaged client admission mirror (validateWorkflowAdmission), writing
// its verdicts for the engine lane to cross-check pair by pair.
import { readFileSync, writeFileSync } from 'node:fs'

import { validateWorkflowAdmission, createWorkflowAuthorityResolver } from '@harborline-software/contracts'

const corpus = JSON.parse(readFileSync(new URL('./admission-mirror-cases.json', import.meta.url), 'utf8'))
const resolver = createWorkflowAuthorityResolver(corpus.registry)

const verdicts = corpus.cases.map(row => {
  const result = validateWorkflowAdmission(row.definition, resolver)
  return {
    id: row.id,
    isValid: result.isValid,
    codes: [...new Set(result.violations.map(violation => violation.code))].sort(),
  }
})

let mismatches = 0
for (const [index, verdict] of verdicts.entries()) {
  const expected = corpus.cases[index].expected
  if (verdict.isValid !== expected.isValid) mismatches++
  else if (!expected.isValid && expected.code && !verdict.codes.includes(expected.code)) mismatches++
}
if (mismatches > 0) {
  throw new Error(`packed client admission mirror disagreed with the pinned ledger on ${mismatches} case(s): ${JSON.stringify(verdicts)}`)
}

writeFileSync(new URL('./client-verdicts.json', import.meta.url), JSON.stringify(verdicts, null, 2))
console.log('WORKFLOW_CLIENT_PASS:' + JSON.stringify({ cases: verdicts.length, admitted: verdicts.filter(v => v.isValid).length, refused: verdicts.filter(v => !v.isValid).length }))
