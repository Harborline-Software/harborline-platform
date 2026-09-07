import {createHash} from 'node:crypto'
import {readFileSync, writeFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const manifestPath = resolve(root, 'projections/typescript/contracts/hlp.contracts.forms/projection-manifest.json')
const wirePath = resolve(root, 'projections/typescript/contracts/hlp.contracts.forms/src/wire.ts')
const formsJsonPath = resolve(root, 'projections/dotnet/contracts/hlp.contracts.identities/Forms/FormsJson.g.cs')
const provenancePath = resolve(root, 'docs/provenance/source-map.yaml')
const moduleCatalogPath = resolve(root, 'catalog/modules.yaml')
const projectionCatalogPath = resolve(root, 'catalog/projections.yaml')
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))

const wire = readFileSync(wirePath, 'utf8')
  .replace(/^const model = .*$/m, `const model = ${JSON.stringify(manifest.model)}`)
  .replace(/\n      if \(\/Roles\$\/\.test\(type\)\) return value\.map\([\s\S]*?\n      \}\)\n/, '\n')
writeFileSync(wirePath, wire)

const formsJson = readFileSync(formsJsonPath, 'utf8')
  .replace(/private const string ModelJson = """[\s\S]*?\n""";/, `private const string ModelJson = """\n${JSON.stringify(manifest.model, null, 2)}\n""";`)
  .replace(/\n                if \(type\.EndsWith\("Roles", StringComparison\.Ordinal\)\)[\s\S]*?\n                \}/, '')
  .replace(/\n    private static void ValidateRoleReference\(JsonElement value\)[\s\S]*?\n    \}\n\n    private static void Require/, '\n\n    private static void Require')
writeFileSync(formsJsonPath, formsJson)

const moduleCatalog = JSON.parse(readFileSync(moduleCatalogPath, 'utf8'))
const authoritativeProjections = Object.entries(moduleCatalog.modules).flatMap(([moduleId, module]) =>
  Object.entries(module.projections ?? {}).map(([projection, value]) => ({
    moduleId, projection, role: value.role, status: value.status, path: value.path,
    ...(value.artifact ? {artifact: value.artifact.id} : {}),
  })))
let projectionCatalogText = readFileSync(projectionCatalogPath, 'utf8')
const presentKeys = new Set((JSON.parse(projectionCatalogText).projections ?? [])
  .map(row => `${row.moduleId}/${row.projection}`))
const missingRows = authoritativeProjections.filter(row => !presentKeys.has(`${row.moduleId}/${row.projection}`))
if (missingRows.length) {
  projectionCatalogText = projectionCatalogText.replace(/\r?\n  \]\r?\n}\s*$/, `${missingRows.map(row =>
    `,\n    ${JSON.stringify(row).replaceAll('\":', '\": ').replaceAll(',', ', ')}`).join('')}\n  ]\n}\n`)
  writeFileSync(projectionCatalogPath, projectionCatalogText)
}

const provenance = JSON.parse(readFileSync(provenancePath, 'utf8'))
const authorizationPaths = [
  'conformance/hlp.contracts.authorization/fixtures.yaml',
  'conformance/hlp.contracts.authorization/runners/run-shared.mjs',
  'projections/dotnet/contracts/hlp.contracts.identities.tests/AuthorizationContractTests.cs',
  'projections/dotnet/contracts/hlp.contracts.identities/Authorization/AuthorizationContracts.g.cs',
  'projections/dotnet/architecture/hlp.architecture.tests/RoleGateArchitectureTests.cs',
  'projections/typescript/contracts/hlp.contracts.forms/src/authorization.ts',
  'projections/typescript/contracts/hlp.contracts.forms/tests/authorization.test.mjs',
  'projections/typescript/contracts/hlp.contracts.forms/tests/authorization-separation.ts',
  'specs/modules/contracts/hlp.contracts.authorization/interface.yaml',
]
if (!provenance.records.some(record => record.moduleId === 'hlp.contracts.authorization')) {
  provenance.records.unshift({
    moduleId: 'hlp.contracts.authorization',
    phase4Evidence: 'specs/modules/contracts/hlp.contracts.authorization/interface.yaml',
    sourceExtracted: true,
    sourceState: 'Revision 1 mirrors the fixed API role DTO shapes and adds powerless, pure vocabulary resolution.',
    contentHashes: Object.fromEntries(authorizationPaths.map(path => [path, ''])),
    nextGate: 'Consumed by workflow, Forms, and app-shell role gates.',
  })
}
for (const record of provenance.records) {
  for (const path of Object.keys(record.contentHashes ?? {})) {
    record.contentHashes[path] = createHash('sha256').update(readFileSync(resolve(root, path))).digest('hex')
  }
}
writeFileSync(provenancePath, `${JSON.stringify(provenance, null, 2)}\n`)

process.stdout.write('generated authorization role bindings from projection-manifest.json\n')
