#!/usr/bin/env node

import { spawn, spawnSync } from 'node:child_process'
import { resolve } from 'node:path'

import { prepareGalleries } from './prepare-galleries.mjs'
import { resolveCommand } from './resolve-command.mjs'

const mode = process.argv[2] ?? 'both'
if (!['react', 'blazor', 'both'].includes(mode)) throw new Error(`unknown gallery mode ${mode}`)

const prepared = prepareGalleries({ packagesReady: process.argv.includes('--packages-ready') })
const children = []
const detached = process.platform !== 'win32'

function start(executable, args, cwd) {
  const resolved = resolveCommand(executable, args)
  const child = spawn(resolved.executable, resolved.args, { cwd, stdio: 'inherit', detached, env: { ...process.env, NO_COLOR: '1' } })
  children.push(child)
  child.on('exit', code => {
    if (code && !process.exitCode) process.exitCode = code
  })
}

function stop() {
  for (const child of children) {
    if (!child.pid || child.killed) continue
    try {
      if (detached) process.kill(-child.pid, 'SIGTERM')
      // On Windows child.kill() reaches only the direct child; taskkill /T fells the tree.
      else if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
      else child.kill('SIGTERM')
    } catch {
      // The process already exited.
    }
  }
}

process.on('SIGINT', () => { stop(); process.exit(130) })
process.on('SIGTERM', () => { stop(); process.exit(143) })
process.on('exit', stop)

if (mode === 'react' || mode === 'both') {
  start('npm', ['run', 'dev', '--', '--host', '127.0.0.1'], prepared.reactGallery)
  process.stdout.write('React Storybook: http://127.0.0.1:6106\n')
}
if (mode === 'blazor' || mode === 'both') {
  start(prepared.dotnet.executable, [
    'run', '--project', prepared.blazorProject,
    '--configuration', 'Release', '--no-restore',
    '--urls', 'http://127.0.0.1:6107',
  ], resolve(prepared.blazorProject, '..'))
  process.stdout.write('Blazor Blazing Story: http://127.0.0.1:6107\n')
}

await Promise.all(children.map(child => new Promise(resolveExit => child.on('exit', resolveExit))))
