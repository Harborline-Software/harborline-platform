import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {test} from 'node:test'

const config = new URL('../../projections/react/ui/hlp.ui.data-grid/vitest.config.ts', import.meta.url).href

for (const mode of [undefined, '0', '1']) {
  test(`DataGrid file scheduling preserves ordinary defaults and serializes shared mode ${mode}`, () => {
    const env = {...process.env}
    delete env.HARBORLINE_SHARED_CONFORMANCE
    if (mode !== undefined) env.HARBORLINE_SHARED_CONFORMANCE = mode
    const result = spawnSync(process.execPath, ['--input-type=module', '-e',
      `const {default: config} = await import(${JSON.stringify(config)}); console.log(JSON.stringify(config.test))`],
      {encoding: 'utf8', env})
    assert.equal(result.status, 0, result.stderr)
    const settings = JSON.parse(result.stdout)
    if (mode === '1') assert.equal(settings.fileParallelism, false)
    else assert.equal(Object.hasOwn(settings, 'fileParallelism'), false, 'ordinary mode must retain Vitest defaults')
    assert.equal(Object.hasOwn(settings, 'testTimeout'), false, 'scheduling must not relax the harness timeout')
    assert.equal(Object.hasOwn(settings, 'exclude'), false, 'scheduling must not exclude test files')
  })
}
