// The published library inventory, read from the producer that actually packs it. Tests derive the
// expected package set from here rather than restating a count, which is how T-682 happened: a
// literal 27 outlived the 28th package and failed publication silently for nine commits.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')

export function producerIds() {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const declarations = new Map([...source.matchAll(/const (\w+) = '(projections\/[^']+\.csproj)'/g)]
    .map(([, name, path]) => [name, path]))
  return [...source.matchAll(/run\(dotnet\.executable, \['pack', (\w+),/g)].map(([, name]) => {
    const project = readFileSync(resolve(root, declarations.get(name)), 'utf8')
    return /<PackageId>([^<]+)<\/PackageId>/.exec(project)[1]
  }).sort()
}
