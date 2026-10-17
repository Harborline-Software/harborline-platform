import fs from 'node:fs'
import {syncBuiltinESMExports} from 'node:module'
import {resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

// A single controlled fixture read in an actual validator CLI child; other reads stay real.
const input = process.env.HARBORLINE_VALIDATE_REPOSITORY_FIXTURE
if (input) {
  const {path, text} = JSON.parse(input)
  if (!/^conformance\/hlp\.[a-z0-9.-]+\/fixtures\.yaml$/.test(path) || typeof text !== 'string') {
    throw new Error('validator preload requires one conformance fixture path and text')
  }
  const target = resolve(process.cwd(), path)
  const readFileSync = fs.readFileSync
  fs.readFileSync = (file, options) => {
    const actual = file instanceof URL ? fileURLToPath(file) : typeof file === 'string' ? resolve(file) : null
    if (actual !== target) return readFileSync(file, options)
    const encoding = typeof options === 'string' ? options : options?.encoding
    return encoding ? text : Buffer.from(text)
  }
  syncBuiltinESMExports()
}
