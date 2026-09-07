import test from 'node:test'
import assert from 'node:assert/strict'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { readdirSync } from 'node:fs'
import { join } from 'node:path'

// This inventories Platform's compiled modules. It cannot inventory an app we do not compile.
// The host/executor dependency fence belongs to the app build after ticket 107 (S4).
test('obligation: provider-reachable Platform modules do not import the effect adapter', () => {
  const src = fileURLToPath(new URL('../../projections/typescript/application/hlp.copilot.contracts/src/', import.meta.url))
  const files = readdirSync(src, { recursive: true }).filter(name => name.endsWith('.ts')).map(name => join(src, name))
  const program = ts.createProgram(files, { strict: true, noEmit: true, target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext })
  assert.deepEqual(ts.getPreEmitDiagnostics(program).map(d => ts.flattenDiagnosticMessageText(d.messageText, '\n')), [])
  const checker = program.getTypeChecker()
  const index = program.getSourceFile(join(src, 'index.ts'))
  const exports = checker.getExportsOfModule(checker.getSymbolAtLocation(index))
  const exportedAdapter = exports.find(symbol => symbol.name === 'PilotEffectAdapter')
  assert.ok(exportedAdapter, 'adapter seam must exist; an empty inventory is not proof')
  const canonical = symbol => symbol && (symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol)
  const adapter = canonical(exportedAdapter)
  const violations = new Set()
  let scanned = 0
  for (const file of files) {
    const source = program.getSourceFile(file)
    // The public barrel intentionally exposes the app-owned seam. It is not a provider module.
    if (source === index) continue
    scanned++
    function visit(node) {
      if (ts.isIdentifier(node) && canonical(checker.getSymbolAtLocation(node)) === adapter
        && !(ts.isInterfaceDeclaration(node.parent) && node.parent.name === node)) {
        const line = source.getLineAndCharacterOfPosition(node.getStart()).line + 1
        violations.add(`${source.fileName}:${line}: provider module references effect adapter`)
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
  }
  assert.ok(scanned >= 4, 'provider, receipt and descriptor modules must be inventoried')
  assert.deepEqual([...violations], [])
})
