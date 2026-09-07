// A NuGet.Config that names a machine is a clone that does not restore. #51 (ticket 226) put one
// developer's global packages folder in the platform root config both as a package SOURCE and as
// a globalPackagesFolder pin; every other machine got NU1301. This test is the fence: no tracked
// NuGet.Config in this repository may carry an absolute path, a drive letter, a UNC path, an
// unexpandable home reference, or a globalPackagesFolder pin.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

const root = resolve(import.meta.dirname, '../..')

// A <add key=".." value=".." /> value is portable when it is a URL or a repo-relative path.
export function portabilityViolations(text) {
  const violations = []
  for (const match of text.matchAll(/<add\s+[^>]*key="([^"]*)"[^>]*value="([^"]*)"[^>]*\/?>/g)) {
    const [, key, value] = match
    if (key === 'globalPackagesFolder') {
      violations.push(`${key}: globalPackagesFolder pins the package cache to one machine`)
      continue
    }
    if (/^[A-Za-z]:[\\/]/.test(value)) violations.push(`${key}: drive-letter path "${value}"`)
    else if (/^\\\\/.test(value)) violations.push(`${key}: UNC path "${value}"`)
    else if (/^\//.test(value)) violations.push(`${key}: absolute path "${value}"`)
    else if (/^~|%[A-Za-z_]+%|\$\{?[A-Za-z_]/.test(value)) violations.push(`${key}: unexpandable home or environment reference "${value}"`)
  }
  return violations
}

// These files carry long rationale comments, and NuGet fails the whole restore ("NuGet.Config is
// not valid XML") on a comment body containing "--" — which a prose mention of a CLI flag such as
// "--source" produces. Caught exactly that way while writing ticket 262's config.
export function commentViolations(text) {
  return [...text.matchAll(/<!--([\s\S]*?)-->/g)]
    .filter(([, body]) => body.includes('--') || body.endsWith('-'))
    .map(([, body]) => `XML comment is invalid ("--" in the body, or trailing "-"): ${body.trim().slice(0, 60)}…`)
}

const configs = execFileSync('git', ['ls-files', '-z', '*NuGet.Config', '*nuget.config'], {cwd: root, encoding: 'utf8'})
  .split('\0').filter(Boolean)

test('the detector fires on every shape ticket 262 removed', () => {
  const hostile = String.raw`<configuration><packageSources>
    <add key="local-global-packages" value="C:\Users\Chris\.nuget\packages" />
    <add key="visual-studio-offline" value="C:\Program Files (x86)\Microsoft SDKs\NuGetPackages" />
    <add key="share" value="\\build01\feed" />
    <add key="posix" value="/home/chris/.nuget/packages" />
    <add key="home" value="~/.harborline/nuget-feed" />
    <add key="env" value="%USERPROFILE%\feed" />
  </packageSources><config>
    <add key="globalPackagesFolder" value="C:\Users\Chris\.nuget\packages" />
  </config></configuration>`
  const found = portabilityViolations(hostile)
  assert.equal(found.length, 7, found.join('\n'))
  assert.match(found.join('\n'), /drive-letter path/)
  assert.match(found.join('\n'), /UNC path/)
  assert.match(found.join('\n'), /globalPackagesFolder pins/)
})

test('the detector accepts the portable shapes this repository uses', () => {
  const friendly = `<configuration><packageSources>
    <add key="harborline-artifacts" value="artifacts/packages/nuget" />
    <add key="gallery-relative" value="../../../artifacts/packages/nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources><config>
    <add key="signatureValidationMode" value="accept" />
  </config></configuration>`
  assert.deepEqual(portabilityViolations(friendly), [])
})

test('the comment detector fires on a body NuGet rejects', () => {
  assert.equal(commentViolations('<!-- pass --source here -->\n<configuration/>').length, 1)
  assert.deepEqual(commentViolations('<!-- pass an explicit source argument -->\n<configuration/>'), [])
})

test('every tracked NuGet.Config in the repository is portable and parseable', () => {
  assert.ok(configs.length >= 2, `expected the root and gallery configs to be discovered, found ${configs.length}`)
  const found = configs.flatMap(path => {
    const text = readFileSync(resolve(root, path), 'utf8')
    return [...portabilityViolations(text), ...commentViolations(text)].map(v => `${path} — ${v}`)
  })
  assert.deepEqual(found, [], found.join('\n'))
})
