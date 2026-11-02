import {appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync} from 'node:fs'
import {spawn} from 'node:child_process'
import path from 'node:path'
import {setTimeout as delay} from 'node:timers/promises'
import {Writable} from 'node:stream'

const args = process.argv.slice(2)
const value = name => args[args.indexOf(name) + 1]
const control = value('--control'), mode = value('--mode')

if (args[0] === 'wrapper') {
  const {mutateProject, run, full, repository} = await import('../../stryker.mjs')
  if (mode.startsWith('sink-')) {
    const sink = new Writable({highWaterMark: 1, write(bytes, encoding, callback) {
      setTimeout(() => { appendFileSync(path.join(control, 'sink-wire.log'), bytes); callback() }, 5)
    }})
    sink.fd = process.stdout.fd
    let writes = 0, pending = false
    if (mode !== 'sink-core') sink.write = (bytes, callback = () => {}) => {
      appendFileSync(path.join(control, 'sink-wire.log'), bytes)
      appendFileSync(path.join(control, 'sink-writes.jsonl'), `${JSON.stringify({sequence: ++writes, pending})}\n`)
      const terminal = bytes.includes(Buffer.from('"source":"terminal"'))
      const gate = mode.includes('terminal') ? terminal : mode.includes('summary') ? bytes.includes('**FAIL**') :
        mode.includes('late') ? bytes.includes(Buffer.from('"source":"heartbeat"')) : writes === 1
      if (!gate) { queueMicrotask(() => callback()); return true }
      if (mode.includes('throw')) throw new Error('fixture synchronous write failure')
      pending = true
      writeFileSync(path.join(control, 'sink-pending.json'), JSON.stringify({terminal}))
      if (mode.includes('late-callback')) {
        queueMicrotask(() => { callback(); writeFileSync(path.join(control, 'callback-done'), '') })
      }
      const release = setInterval(() => {
        if (!existsSync(path.join(control, 'sink-release'))) return
        clearInterval(release)
        if (mode.includes('error')) {
          const error = new Error('fixture callback failure')
          if (!mode.includes('late-callback')) callback(error)
          setImmediate(() => sink.emit('error', error))
        } else if (mode.includes('close')) sink.emit('close')
        else if (mode.includes('callback-first')) {
          callback()
          setTimeout(() => { pending = false; sink.emit('drain') }, 75)
        } else {
          sink.emit('drain')
          setTimeout(() => { pending = false; callback() }, 75)
        }
      }, 25)
      return mode.includes('true-error') ? true : false
    }
    Object.defineProperty(process, 'stdout', {value: sink})
  }
  const cancel = setInterval(() => {
    if (existsSync(path.join(control, 'cancel'))) process.emit('SIGTERM')
  }, 50)
  try {
    const command = mode === 'spawn-error' ? [path.join(control, 'absent-executable')] : [process.execPath, import.meta.filename, 'child']
    const stop = mode.startsWith('live-') ? pid => {
      writeFileSync(path.join(control, 'cleanup-called.json'), JSON.stringify({pid}))
      if (mode.includes('true')) {
        writeFileSync(path.join(control, 'release'), '')
        return true
      }
      if (mode.includes('throw')) throw new Error('fixture cleanup unavailable')
      return false
    } : undefined
    let attempts = 0
    const mutate = async test => {
      appendFileSync(path.join(control, 'projects.jsonl'), `${JSON.stringify(test)}\n`)
      if (attempts++ > 0) throw new Error('Refusal incorrectly reached the next project')
      const result = await mutateProject(test, undefined, ['--control', control, '--mode', mode], command, stop)
      writeFileSync(path.join(control, 'result.json'), JSON.stringify(result))
      writeFileSync(path.join(control, 'returned'), '')
      return result
    }
    if (args.includes('--operation')) {
      const operation = value('--operation'), repo = repository(), test = value('--test')
      repo.testProjects = [test, test]
      const outcome = operation === 'run' ? await run(repo, undefined, [], mutate, () => ['Fixture.cs'], files => files)
        : await full(repo, undefined, [], operation === 'baseline', mutate)
      if (outcome.signal) process.kill(process.pid, outcome.signal)
      else process.exitCode = outcome
    } else await mutate(value('--test'))
  } catch (error) {
    writeFileSync(path.join(control, 'wrapper-error.json'), JSON.stringify({message: error.message}))
    process.exitCode = 1
  } finally {
    clearInterval(cancel)
  }
} else if (args[0] === 'descendant') {
  const identity = {pid: process.pid, nonce: readFileSync(path.join(control, 'nonce'), 'utf8')}
  appendFileSync(path.join(control, 'alive.jsonl'), `${JSON.stringify({...identity, sequence: 0})}\n`)
  writeFileSync(path.join(control, 'descendant.json'), JSON.stringify(identity))
  const deadline = Date.now() + 30000
  let sequence = 1
  while (Date.now() < deadline) {
    appendFileSync(path.join(control, 'alive.jsonl'), `${JSON.stringify({...identity, sequence: sequence++})}\n`)
    if (existsSync(path.join(control, 'descendant-release.json')) &&
        readFileSync(path.join(control, 'descendant-release.json'), 'utf8') === JSON.stringify(identity)) break
    await delay(25)
  }
  writeFileSync(path.join(control, 'descendant-stopped.json'), JSON.stringify(identity))
} else {
  const output = value('--output'), logs = path.join(output, 'logs'), log = path.join(logs, 'log-20261026.txt')
  mkdirSync(logs, {recursive: true})
  process.stdout.write(Buffer.from('stdout before release\0\r\n::warning::untrusted stdout\n'))
  process.stderr.write(Buffer.from('stderr before release\0\r\n::error::untrusted stderr\n'))
  writeFileSync(log, 'Session succeeded; mutant outcome unknown\nActual timeout budget 12000ms\n::notice::untrusted native\n' +
    '2026-10-26T09:07:42.8119840-04:00  [DBG] "Runner 0": Using 1606 ms as test run timeout (281bb51f)\n' +
    '2026-10-26T09:07:45.2880760-04:00  [DBG] Test run for "5: Statement mutation" is "success"  (26093294)\n')
  writeFileSync(path.join(logs, 'log-Runner-0.txt'), 'worker trace retained only\n')
  if (mode === 'burst') writeFileSync(log, ('nativeDiagnostic ' + 'X'.repeat(2000) + '\n').repeat(1600))
  writeFileSync(path.join(control, 'ready.json'), JSON.stringify({output, pid: process.pid, argv: args}))
  if (mode === 'burst') {
    const identity = {pid: process.pid, nonce: readFileSync(path.join(control, 'nonce'), 'utf8')}
    writeFileSync(path.join(control, 'producer.json'), JSON.stringify(identity))
    const deadline = Date.now() + 180000
    while (Date.now() < deadline && (!existsSync(path.join(control, 'burst-release.json')) ||
        readFileSync(path.join(control, 'burst-release.json'), 'utf8') !== JSON.stringify(identity))) await delay(25)
    if (Date.now() >= deadline) process.exit(99)
    mkdirSync(path.join(output, 'reports'), {recursive: true})
    writeFileSync(path.join(output, 'reports', 'mutation-report.json'), JSON.stringify({files: {'Fixture.cs': {mutants: [{status: 'Killed'}]}}}))
    writeFileSync(path.join(control, 'producer-stopped.json'), JSON.stringify(identity))
    process.exit(0)
  }
  if (mode.startsWith('live-')) {
    const deadline = Date.now() + 30000
    while (!existsSync(path.join(control, 'release')) && Date.now() < deadline) await delay(25)
    writeFileSync(path.join(control, 'leader-stopped'), '')
    process.exit(0)
  }
  if (mode.startsWith('drain-')) {
    const descendant = spawn(process.execPath, [import.meta.filename, 'descendant', '--control', control],
      {stdio: ['ignore', 1, 2], detached: true, windowsHide: true})
    const deadline = Date.now() + 10000
    while (!existsSync(path.join(control, 'descendant.json')) && Date.now() < deadline) await delay(25)
    if (!existsSync(path.join(control, 'descendant.json'))) process.exit(99)
    descendant.unref()
    process.exit(mode.includes('nonzero') ? 23 : 0)
  }
  if (['wait', 'cancel', 'observer-error', 'rotation'].includes(mode) || mode.startsWith('sink-late')) {
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
  process.exitCode = mode.includes('nonzero') ? 23 : 0
}
