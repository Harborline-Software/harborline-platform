import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import test from 'node:test'
import ts from 'typescript'

const root = resolve(import.meta.dirname, '../..')
const read = path => readFileSync(resolve(root, path), 'utf8')
const declarations = JSON.parse(read('_shared/layout/released-field-controls.json')).controls
const reactSource = read('projections/react/ui/hlp.ui.schema-form/src/controls.tsx')
const react = ts.createSourceFile('controls.tsx', reactSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
const blazor = read('projections/blazor/ui/hlp.ui.schema-form/SchemaFormControls.cs')
const blazorRenderer = read('projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaControl.razor')

function nodesWhere(tree, predicate) {
  const found = []
  function visit(node) {
    if (predicate(node)) found.push(node)
    ts.forEachChild(node, visit)
  }
  visit(tree)
  return found
}

test('layout-bound-3: every released control id names an existing renderer in both lanes', () => {
  // The runtime implementations are independent of the embedded descriptor corpus.
  const registry = nodesWhere(react, node => ts.isVariableDeclaration(node) && node.name.getText(react) === 'DEFAULT_CONTROLS')[0]?.initializer
  assert.ok(registry && ts.isObjectLiteralExpression(registry), 'React controls must be an explicit registry')
  const reactHints = registry.properties.map(property => property.name?.getText(react).replace(/^['"]|['"]$/g, ''))
  const blazorHints = [...blazor.match(/Hints\s*=\s*\[([\s\S]*?)\];/)[1].matchAll(/"([^"]+)"/g)].map(match => match[1])
  assert.deepEqual(declarations.map(control => control.id), ['text', 'currency']) // T-1012's bounded release.
  for (const { id } of declarations) {
    assert.ok(reactHints.includes(id), `React does not render released control ${id}`)
    assert.ok(blazorHints.includes(id), `Blazor does not render released control ${id}`)
  }
})

test('layout-bound-3: released currency parameter names are exactly those both lane controls consume', () => {
  const currency = declarations.find(control => control.id === 'currency')
  const numeric = nodesWhere(react, node => ts.isFunctionDeclaration(node) && node.name?.text === 'NumericControl')[0]
  assert.ok(numeric, 'React numeric renderer must exist')
  const reactNames = [...new Set(nodesWhere(numeric, node => ts.isPropertyAccessExpression(node) && node.expression.getText(react) === 'config').map(node => node.name.text))].sort()
  const blazorNames = [...new Set([...blazorRenderer.matchAll(/Config(?:Int|String|Double)\("([^"]+)"\)/g)].map(match => match[1]))].sort()
  const names = Object.keys(currency.parameterSchema.properties).sort()
  assert.deepEqual(names, ['currencyCode', 'decimals', 'max', 'min']) // T-1012, not production constants.
  assert.deepEqual(names, reactNames)
  assert.deepEqual(names, blazorNames)
  assert.equal(currency.parameterSchema.additionalProperties, false)
  assert.match(reactSource, /currency:\s*args\s*=>\s*<NumericControl args=\{args\} kind="currency"/)
  assert.match(blazorRenderer, /Hint is "currency" or "percentage"/)
})

test('layout-bound-3: released text takes no parameters in either lane', () => {
  assert.equal(declarations.find(control => control.id === 'text').parameterSchema, undefined)
  const text = nodesWhere(react, node => ts.isVariableDeclaration(node) && node.name.getText(react) === 'textControl')[0]?.initializer
  assert.ok(text, 'React text renderer must exist')
  assert.doesNotMatch(text.getText(react), /\bconfig\b|\bfieldConfig\b/)

  const branch = blazorRenderer.match(/else if \(Hint is "text"[^\n]*\)\s*\{([\s\S]*?)\n\}/)?.[1]
  assert.ok(branch, 'Blazor text renderer must exist')
  assert.doesNotMatch(branch, /\bConfig\b|Config(?:Int|String|Double)\(/)
  for (const name of new Set([...branch.matchAll(/@([A-Z]\w*)/g)].map(match => match[1]).filter(name => name !== 'Args'))) {
    const helper = blazorRenderer.match(new RegExp(`private [^\\n]+\\b${name}\\b[^;]*;`))?.[0]
    assert.ok(helper, `Blazor text helper ${name} must be explicit`)
    assert.doesNotMatch(helper, /\bConfig\b|Config(?:Int|String|Double)\(/)
  }
})
