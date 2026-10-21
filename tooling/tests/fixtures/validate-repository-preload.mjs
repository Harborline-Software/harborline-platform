import fs from 'node:fs'
import {syncBuiltinESMExports} from 'node:module'
import {resolve, sep} from 'node:path'
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

// Add one synthetic subtree to the real root; dirent kinds/read faults are deliberate
// test inputs, not a reproduction of OS symlink or permission behavior.
const filesInput = process.env.HARBORLINE_VALIDATE_REPOSITORY_FILES
if (filesInput) {
  const root = process.cwd()
  const treeRoot = resolve(root, 't684-validator-files')
  const readFileSync = fs.readFileSync
  const readdirSync = fs.readdirSync
  const statSync = fs.statSync
  if (statSync(treeRoot, {throwIfNoEntry: false})) throw new Error('validator preload subtree already exists')
  const nodes = new Map()
  function addDirectory(path, entries) {
    if (!entries || typeof entries !== 'object' || Array.isArray(entries)) throw new Error('expected fixture directory entries')
    nodes.set(path, {kind: 'directory', entries})
    for (const [name, node] of Object.entries(entries)) {
      if (!name || name === '.' || name === '..' || /[/\\]/.test(name)
          || !node || !['file', 'directory', 'symlink'].includes(node.kind)) throw new Error('invalid fixture entry')
      const child = resolve(path, name)
      if (node.kind === 'directory') addDirectory(child, node.entries ?? {})
      else nodes.set(child, node)
    }
  }
  addDirectory(treeRoot, JSON.parse(filesInput))
  const actualPath = file => file instanceof URL ? fileURLToPath(file) : typeof file === 'string' ? resolve(file) : null
  const isVirtual = path => path === treeRoot || path?.startsWith(`${treeRoot}${sep}`)
  const dirent = (name, kind) => ({name, isDirectory: () => kind === 'directory', isSymbolicLink: () => kind === 'symlink'})
  const failure = (code, path) => Object.assign(new Error(`${code}: fixture source ${path}`), {code, path})
  fs.readdirSync = (file, options) => {
    const path = actualPath(file)
    if (path === root && options?.withFileTypes) return [...readdirSync(file, options), dirent('t684-validator-files', 'directory')]
    if (!isVirtual(path)) return readdirSync(file, options)
    const node = nodes.get(path)
    if (node?.kind !== 'directory') throw failure(node ? 'ENOTDIR' : 'ENOENT', path)
    return Object.entries(node.entries).map(([name, child]) => options?.withFileTypes ? dirent(name, child.kind) : name)
  }
  fs.statSync = (file, options) => {
    const path = actualPath(file)
    if (!isVirtual(path)) return statSync(file, options)
    const node = nodes.get(path)
    if (!node) {
      if (options?.throwIfNoEntry === false) return undefined
      throw failure('ENOENT', path)
    }
    return {isFile: () => node.kind === 'file', isDirectory: () => node.kind === 'directory'}
  }
  fs.readFileSync = (file, options) => {
    const path = actualPath(file)
    if (!isVirtual(path)) return readFileSync(file, options)
    const node = nodes.get(path)
    if (!node) throw failure('ENOENT', path)
    if (node.error === 'EACCES') throw failure('EACCES', path)
    if (node.kind !== 'file') throw failure('EISDIR', path)
    const encoding = typeof options === 'string' ? options : options?.encoding
    return encoding ? node.text ?? '' : Buffer.from(node.text ?? '')
  }
  syncBuiltinESMExports()
}
