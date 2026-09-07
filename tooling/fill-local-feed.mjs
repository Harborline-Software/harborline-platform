#!/usr/bin/env node
// Fills the repo-relative first-party feed (artifacts/packages/nuget) that NuGet.Config declares.
//
// The feed's PRIMARY producer is this repository: tooling/verify-package-fixtures.mjs packs all
// 24 Harborline.* packages from projections/dotnet/** and projections/blazor/** into it. This
// script is the cheap path for a developer who already has those nupkgs packed somewhere local
// (harborline-api's convention: ~/.harborline/nuget-feed, override HARBORLINE_NUGET_FEED) and
// wants a restore to work without re-packing them from source.
//
// It copies ONLY the exact id+version pairs this repository declares — never a whole cache — it
// writes nothing outside the gitignored artifacts/ tree, and it is a no-op with a clear message
// when the source feed is absent or holds none of them.
import {copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync} from 'node:fs'
import {homedir} from 'node:os'
import {basename, join, resolve} from 'node:path'

const root = resolve(import.meta.dirname, '..')
const destination = resolve(root, 'artifacts/packages/nuget')
const source = process.env.HARBORLINE_NUGET_FEED
  ? resolve(process.env.HARBORLINE_NUGET_FEED)
  : join(homedir(), '.harborline', 'nuget-feed')

// The wanted set is discovered from the declarations, not hard-coded: a new PackageReference is
// picked up without editing this script, and a stale hard-coded id can never drift out of date.
function projectFiles(directory) {
  return readdirSync(directory, {withFileTypes: true}).flatMap(entry => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      return ['node_modules', 'bin', 'obj', 'artifacts', '.git', '.claude', 'dist'].includes(entry.name) ? [] : projectFiles(path)
    }
    return entry.name.endsWith('.csproj') || entry.name === 'Directory.Packages.props' ? [path] : []
  })
}

// Directly declared Harborline.* references — the set the report names as "declared".
const wanted = new Map()
for (const path of projectFiles(root)) {
  const text = readFileSync(path, 'utf8')
  for (const [, id, version] of text.matchAll(/<Package(?:Reference|Version)\s+Include="(Harborline\.[^"]+)"\s+Version="([^"]+)"/g)) {
    wanted.set(`${id.toLowerCase()}.${version.toLowerCase()}.nupkg`, `${id} ${version}`)
  }
}

// Those references drag in a transitive closure this repository also packs — Harborline.Foundation
// and the legacy-named Shipyard.* assemblies among them — which appears in no csproj, so a
// declared-ids-only copy leaves the gallery unrestorable (NU1101). Every package THIS repository
// packs carries Directory.Build.props's <Version>, and no third party uses it, so that version
// set is the closure's exact fingerprint. This selects first-party builds out of a shared cache
// without a hard-coded family name — deliberately NOT a registry fallback for the legacy name
// (CONTRIBUTING.md); the source here is always a local directory.
const repositoryVersion = /<Version>([^<]+)<\/Version>/.exec(readFileSync(resolve(root, 'Directory.Build.props'), 'utf8'))?.[1]
if (!repositoryVersion) throw new Error('Directory.Build.props declares no <Version>; cannot identify first-party packages.')
const firstPartyVersions = new Set([repositoryVersion.toLowerCase(), ...[...wanted.values()].map(label => label.split(' ')[1].toLowerCase())])
const isFirstParty = name => [...firstPartyVersions].some(version => name.toLowerCase().endsWith(`.${version}.nupkg`))

if (!existsSync(source)) {
  process.stdout.write(`fill-local-feed: no source feed at ${source} — nothing copied.\n`
    + 'Set HARBORLINE_NUGET_FEED to a directory holding packed Harborline.*.nupkg files, or pack\n'
    + 'them from this repository with `node tooling/verify-package-fixtures.mjs`.\n')
  process.exit(0)
}

// Two source layouts are accepted, because both exist in practice: a FLAT feed directory (what
// harborline-api's tooling/nuget-feed/pack-and-push.sh and `dotnet pack --output` produce) and
// the NuGet id/version/id.version.nupkg layout of a global packages folder (which is what #51
// reached into). Names are compared lowercased: the global-folder copy is lowercased on disk.
function candidates(directory, depth) {
  return readdirSync(directory, {withFileTypes: true}).flatMap(entry => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return depth === 0 ? [] : candidates(path, depth - 1)
    const name = entry.name.toLowerCase()
    return name.endsWith('.nupkg') && !name.endsWith('.symbols.nupkg') && isFirstParty(name) ? [path] : []
  })
}

const found = candidates(source, 2)
if (found.length === 0) {
  process.stdout.write(`fill-local-feed: ${source} holds no package built at ${[...firstPartyVersions].join(' / ')} — nothing copied.\n`)
  process.exit(0)
}

mkdirSync(destination, {recursive: true})
for (const path of found) copyFileSync(path, join(destination, basename(path)))
const missing = [...wanted].filter(([name]) => !found.some(path => basename(path).toLowerCase() === name)).map(([, label]) => label)
process.stdout.write(`fill-local-feed: copied ${found.length} first-party package(s) from ${source} to artifacts/packages/nuget.\n`)
if (missing.length > 0) {
  process.stdout.write(`fill-local-feed: ${missing.length} declared package(s) were not in that source. Only the\n`
    + 'disposable consumers under tests/package-consumers/** reference them, and the gate packs those\n'
    + `from this repository; pack them yourself with \`node tooling/verify-package-fixtures.mjs\`.\n  ${missing.join('\n  ')}\n`)
}
