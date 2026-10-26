import {appendFileSync, existsSync, mkdirSync, writeFileSync} from 'node:fs'
import path from 'node:path'
import {setTimeout as delay} from 'node:timers/promises'

const args = process.argv.slice(2)
const value = name => args[args.indexOf(name) + 1]
const control = value('--control'), mode = value('--mode')

if (args[0] === 'wrapper') {
  const {mutateProject} = await import('../../stryker.mjs')
  const cancel = setInterval(() => {
    if (existsSync(path.join(control, 'cancel'))) process.emit('SIGTERM')
  }, 50)
  try {
    const command = mode === 'spawn-error' ? [path.join(control, 'absent-executable')] : [process.execPath, import.meta.filename, 'child']
    const result = await mutateProject(value('--test'), undefined, ['--control', control, '--mode', mode], command)
    writeFileSync(path.join(control, 'result.json'), JSON.stringify(result))
  } finally {
    clearInterval(cancel)
  }
} else {
  const output = value('--output'), logs = path.join(output, 'logs'), log = path.join(logs, 'log-20261026.txt')
  mkdirSync(logs, {recursive: true})
  process.stdout.write(Buffer.from('stdout before release\0\r\n::warning::untrusted stdout\n'))
  process.stderr.write(Buffer.from('stderr before release\0\r\n::error::untrusted stderr\n'))
  writeFileSync(log, 'Session succeeded; mutant outcome unknown\nActual timeout budget 12000ms\n::notice::untrusted native\n' +
    '2026-10-26T09:07:42.8119840-04:00  [DBG] "Runner 0": Using 1606 ms as test run timeout (281bb51f)\n' +
    '2026-10-26T09:07:45.2880760-04:00  [DBG] Test run for "5: Statement mutation" is "success"  (26093294)\n')
  writeFileSync(path.join(logs, 'log-Runner-0.txt'), 'worker trace retained only\n')
  writeFileSync(path.join(control, 'ready.json'), JSON.stringify({output, pid: process.pid, argv: args}))
  if (['wait', 'cancel', 'observer-error', 'rotation'].includes(mode)) {
    const deadline = Date.now() + 20000
    while (!existsSync(path.join(control, 'release')) && Date.now() < deadline) await delay(25)
    if (!existsSync(path.join(control, 'release'))) process.exit(99)
  }
  const utf8 = Buffer.from('native UTF8 café 🙂\n')
  appendFileSync(log, utf8.subarray(0, utf8.length - 3))
  await delay(1100)
  appendFileSync(log, utf8.subarray(utf8.length - 3))
  writeFileSync(path.join(logs, 'log-20261027.txt'), 'late log file\n')
  appendFileSync(log, 'native final line without newline')
  if (mode !== 'missing') {
    mkdirSync(path.join(output, 'reports'), {recursive: true})
    writeFileSync(path.join(output, 'reports', 'mutation-report.json'), mode === 'malformed' ? '{' :
      mode === 'invalid-shape' ? '{}' : JSON.stringify({files: {'Fixture.cs': {mutants:
        mode === 'pending' ? [{status: 'Killed'}, {status: 'Pending'}] :
        mode === 'unknown' ? [{status: 'Killed'}, {status: 'Unknown'}] :
        mode.startsWith('final-') ? [{status: mode.slice('final-'.length)}] : [{status: 'Killed'}]}}}))
  }
  if (mode === 'signal') process.kill(process.pid, 'SIGTERM')
  process.exitCode = mode === 'nonzero' ? 23 : 0
}
