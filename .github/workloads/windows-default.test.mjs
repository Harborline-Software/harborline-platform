import {test} from 'node:test'
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
test('enabled Windows default uses direct execution; non-Windows keeps the lock', () => {
  const source=readFileSync(new URL('../workflows/stryker.yml',import.meta.url),'utf8').replaceAll('\r\n','\n')
  const step=source.slice(source.indexOf('      - name: Mutate every project and hold each to its baseline (scheduled)'), source.indexOf('      - name: Mutation report'))
  assert.ok(step.includes("$env:RUNNER_OS -eq 'Windows'"))
  const windows=step.slice(step.indexOf("$env:RUNNER_OS -eq 'Windows'"),step.indexOf('python .github/workloads/host-workload-lock.py'))
  assert.ok(windows.includes('& ./.github/workloads/full-mutation.ps1'))
  assert.ok(!windows.includes('host-workload-lock.py'))
  assert.match(source,/if: .*runner\.os != 'Windows'\n        with:\n          python-version: '3\.14'/)
})
