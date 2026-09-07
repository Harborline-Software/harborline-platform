#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const toolDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(toolDirectory, '..')

export function resolvePinnedDotnet(root = repositoryRoot) {
  const globalJsonPath = resolve(root, 'global.json')
  const pinnedVersion = JSON.parse(readFileSync(globalJsonPath, 'utf8')).sdk?.version
  if (typeof pinnedVersion !== 'string' || pinnedVersion.length === 0) {
    throw new Error(`${globalJsonPath} does not declare sdk.version`)
  }

  const candidates = [
    process.env.DOTNET_HOST_PATH,
    process.env.DOTNET_ROOT ? resolve(process.env.DOTNET_ROOT, 'dotnet') : undefined,
    '/usr/local/share/dotnet/dotnet',
    '/opt/homebrew/share/dotnet/dotnet',
    '/usr/share/dotnet/dotnet',
    'dotnet',
  ].filter((candidate, index, all) => candidate && all.indexOf(candidate) === index)

  const attempts = []
  for (const candidate of candidates) {
    const probe = spawnSync(candidate, ['--version'], { cwd: root, encoding: 'utf8' })
    const version = probe.status === 0 ? probe.stdout.trim() : null
    attempts.push({ candidate, exitCode: probe.status, version })
    if (version === pinnedVersion) {
      return { executable: candidate, version, pinnedVersion, globalJsonPath, attempts }
    }
  }
  throw new Error(`No dotnet host resolved exact SDK ${pinnedVersion}: ${JSON.stringify(attempts)}`)
}

if (import.meta.main) {
  try {
    process.stdout.write(`${JSON.stringify(resolvePinnedDotnet(), null, 2)}\n`)
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.stack : String(error)}\n`)
    process.exitCode = 1
  }
}
