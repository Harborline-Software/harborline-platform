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

test('PR mutation keeps full selection and reserves time to collect diagnostics after step failure', () => {
  const source=readFileSync(new URL('../workflows/stryker.yml',import.meta.url),'utf8').replaceAll('\r\n','\n')
  const pr=source.slice(source.indexOf('      - name: Mutate changed projects'),source.indexOf('      - name: Mutate every project'))
  assert.ok(pr.includes("        if: github.event_name == 'pull_request'\n"))
  assert.ok(pr.includes('        timeout-minutes: 330\n'))
  assert.ok(pr.includes('          node tooling/stryker.mjs run -- --log-to-file\n'))
  const reports=source.slice(source.indexOf('      - name: Mutation reports'))
  assert.ok(reports.includes('        if: always()\n        uses: actions/upload-artifact@v7\n'))
  assert.ok(reports.includes('          path: |\n            StrykerOutput/*/reports/mutation-report.json\n            StrykerOutput/*/stryker-config.json\n            StrykerOutput/*/logs/*.txt\n'))
})
