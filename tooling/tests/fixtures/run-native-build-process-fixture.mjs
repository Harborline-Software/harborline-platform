import childProcess from 'node:child_process'
import { appendFileSync } from 'node:fs'
import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import { syncBuiltinESMExports } from 'node:module'

const logPath = process.env.HARBORLINE_RUN_NATIVE_FIXTURE_LOG
let reactBuildComplete = false

function record(event) {
  appendFileSync(logPath, `${event}\n`)
}

function fakeChild({ exitCode = 0, delayMs = 0, stderr = '', onComplete } = {}) {
  const child = new EventEmitter()
  child.stdout = new PassThrough()
  child.stderr = new PassThrough()
  setTimeout(() => {
    if (stderr) child.stderr.write(stderr)
    child.stdout.end()
    child.stderr.end()
    onComplete?.()
    child.emit('close', exitCode)
  }, delayMs)
  return child
}

childProcess.spawnSync = () => ({
  status: 0,
  stdout: '11.0.100-preview.7.26381.103\n',
  stderr: '',
})

childProcess.spawn = (_executable, args, options) => {
  const cwd = options.cwd.replaceAll('\\', '/')
  if (cwd.endsWith('/projections/react/ui/hlp.ui.button') && args.includes('build')) {
    record('react-build:start')
    return fakeChild({
      delayMs: 40,
      onComplete: () => {
        reactBuildComplete = true
        record('react-build:complete')
      },
    })
  }
  if (cwd.endsWith('/projections/react/ui/hlp.ui.select-field')) {
    record('select-field-typecheck:start')
    if (!reactBuildComplete) {
      return fakeChild({ exitCode: 2, stderr: 'fixture: React declarations are not ready\n' })
    }
    if (process.env.HARBORLINE_RUN_NATIVE_INVALID_TYPECHECK === '1') {
      return fakeChild({ exitCode: 2, stderr: 'fixture: invalid SelectField typecheck\n' })
    }
  }
  return fakeChild()
}

syncBuiltinESMExports()
