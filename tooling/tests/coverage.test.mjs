import assert from 'node:assert/strict'
import path from 'node:path'
import {test} from 'node:test'
import {coverageSummary} from '../coverage.mjs'
import {evaluateStepStdout} from '../gate-step-evidence.mjs'

// The repository itself is the fixture: tooling/run-native.mjs is tracked, and the report names it relative to a
// <source> root the way coverlet does; missing.cs maps to nothing and must be listed, not dropped.
test('Cobertura union counts lines once, maps through the source root and lists unmapped paths', () => {
  const root = path.resolve(import.meta.dirname, '../..')
  const xml = '<coverage><sources><source>' + path.join(root, 'tooling') + '/</source></sources><packages><package><classes>'
    + '<class filename="run-native.mjs"><lines><line number="1" hits="0"/><line number="2" hits="1"/></lines></class>'
    + '<class filename="run-native.mjs"><lines><line number="1" hits="2"/></lines></class>'
    + '<class filename="missing.cs"><lines><line number="9" hits="0"/></lines></class></classes></package></packages></coverage>'
  assert.deepEqual(coverageSummary(xml, root), {coveredLines: 2, validLines: 3, mappedPaths: ['tooling/run-native.mjs'], unmappedPaths: ['missing.cs']})
})

test('a repository-relative filename still maps without a source root', () => {
  const root = path.resolve(import.meta.dirname, '../..')
  const xml = '<coverage><packages><package><classes><class filename="tooling/run-native.mjs"><lines><line number="1" hits="1"/></lines></class></classes></package></packages></coverage>'
  assert.deepEqual(coverageSummary(xml, root).mappedPaths, ['tooling/run-native.mjs'])
})

test('a coverage-native result remains one JSON document for the gate evaluator', () => {
  const report = {status: 'PASS', coverage: [{suite: 'blazor', artifactPath: 'artifacts/quality/coverage/blazor/reports/1-coverage.cobertura.xml', coveredLines: 2, validLines: 3, mappedPaths: ['tooling/run-native.mjs'], unmappedPaths: ['missing.cs']}]}
  assert.deepEqual(evaluateStepStdout({stepId: 'native-tests', json: true, status: 0, stdout: JSON.stringify(report)}), {status: 0, report})
})
