import childProcess from 'node:child_process'
import fs from 'node:fs'
import {syncBuiltinESMExports} from 'node:module'
import {resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

const text = process.env.HARBORLINE_AUTHORIZATION_RUNNER_FIXTURE
if (typeof text !== 'string') throw new Error('authorization runner preload requires fixture text')

const target = resolve(import.meta.dirname, '../../../conformance/hlp.contracts.authorization/fixtures.yaml')
const packageRoot = resolve(import.meta.dirname, '../../../projections/typescript/contracts/hlp.contracts.forms')
const readFileSync = fs.readFileSync
fs.readFileSync = (file, options) => {
  const actual = file instanceof URL ? fileURLToPath(file) : typeof file === 'string' ? resolve(file) : null
  if (actual !== target) return readFileSync(file, options)
  const encoding = typeof options === 'string' ? options : options?.encoding
  return encoding ? text : Buffer.from(text)
}

childProcess.spawnSync = (_executable, args, options) => {
  if (options?.cwd !== packageRoot || args?.at(-2) !== 'run' || args?.at(-1) !== 'build')
    throw new Error('Unexpected authorization runner child process')
  throw new Error('AUTHORIZATION_FIXTURE_BUILD_SENTINEL')
}

syncBuiltinESMExports()
