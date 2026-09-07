// Dynamic Forms capability vertical — reference-lane half (ticket 073).
// Package-only: every Harborline import resolves from the packed npm artifacts
// (@harborline-software/ui-react, @harborline-software/rule-engine); the application-derived
// F-20 parity corpus (cases.json) is the pinned source ledger. Renders each corpus case
// through SchemaForm + useFormRuleGraph, evaluates SPINE-1 rules reactively, and writes
// per-case client verdicts for the packaged Forms Engine half to cross-check.
import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'

import { JSDOM } from 'jsdom'

const dom = new JSDOM('<!doctype html><html><body></body></html>', { pretendToBeVisual: true })
globalThis.window = dom.window
globalThis.document = dom.window.document
Object.defineProperty(globalThis, 'navigator', { configurable: true, get: () => dom.window.navigator })
globalThis.IS_REACT_ACT_ENVIRONMENT = true
Object.defineProperty(dom.window, 'matchMedia', {
  configurable: true,
  value: query => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  }),
})

const React = await import('react')
const { act } = React
const { createRoot } = await import('react-dom/client')
const { SchemaForm, useFormRuleGraph, ReactiveSchemaForm, FormView } = await import('@harborline-software/ui-react')
const { compile, FormRuleGraph } = await import('@harborline-software/rule-engine')

const corpus = JSON.parse(readFileSync(new URL('./cases.json', import.meta.url), 'utf8'))
const text = value => ({ defaultLocale: 'en', values: { en: value } })
const staticallyRequired = new Set(corpus.schema.required)
const allFieldNames = corpus.sections.flatMap(section => section.fields)
const fieldSection = new Map(corpus.sections.flatMap(section => section.fields.map(name => [name, section.id])))

const baseView = FormView.normalize({
  formId: 'parity',
  version: '1.0.0',
  sections: corpus.sections.map(section => ({
    id: section.id,
    title: text(section.id),
    fields: section.fields.map(name => ({
      name,
      label: text(name),
      isSensitive: false,
      isReadable: true,
      rules: { visible: true, required: staticallyRequired.has(name), readOnly: false },
    })),
  })),
}, {})

const toRule = id => {
  const rule = corpus.ruleCatalog[id]
  return {
    id: rule.id,
    tier: rule.tier,
    scope: rule.scope,
    scopeTarget: rule.scopeTarget,
    expression: JSON.parse(rule.expression),
    action: rule.action,
  }
}
const graphFor = ids => (ids.length ? new FormRuleGraph(compile(ids.map(toRule))) : null)

async function evaluateCase(caseRow) {
  const graph = graphFor(caseRow.rules)
  const scalarValues = Object.fromEntries(Object.entries(caseRow.candidate)
    .filter(([, value]) => typeof value !== 'object' || value === null))
  let latest = null
  function Harness() {
    latest = useFormRuleGraph(graph, baseView, { initialValues: scalarValues })
    return React.createElement(SchemaForm, {
      view: latest.view,
      values: latest.values,
      onValuesChange: latest.onValuesChange,
      onSubmit: () => undefined,
    })
  }
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(Harness)))
  assert.ok(latest, `case '${caseRow.name}' did not render through useFormRuleGraph`)
  const visibleFields = new Set(latest.view.sections.flatMap(section => section.fields.map(field => field.name)))
  const verdict = {
    name: caseRow.name,
    rules: caseRow.rules,
    hiddenFields: allFieldNames.filter(name => !visibleFields.has(name)).sort(),
    hiddenSections: corpus.sections
      .filter(section => !latest.view.sections.some(candidate => candidate.id === section.id))
      .map(section => section.id)
      .sort(),
    requiredFields: latest.view.sections
      .flatMap(section => section.fields.filter(field => field.required).map(field => field.name))
      .sort(),
    readOnlyFields: latest.view.sections
      .flatMap(section => section.fields.filter(field => field.readOnly).map(field => field.name))
      .sort(),
    saveBlocked: latest.saveBlocked,
  }
  const markup = container.innerHTML
  for (const name of verdict.hiddenFields) {
    assert.ok(!markup.includes(`>${name}<`), `case '${caseRow.name}' rendered rule-hidden field '${name}'`)
  }
  for (const name of visibleFields) {
    assert.ok(markup.includes(name), `case '${caseRow.name}' did not render visible field '${name}'`)
  }
  await act(async () => root.unmount())
  container.remove()
  return verdict
}

const verdicts = []
for (const caseRow of corpus.cases) verdicts.push(await evaluateCase(caseRow))

const byName = fragment => {
  const verdict = verdicts.find(candidate => candidate.name.includes(fragment))
  assert.ok(verdict, `missing corpus case containing '${fragment}'`)
  return verdict
}

// Ledger-pinned client behavior (Forms Engine ledger cases 70-79, renderer tier).
const ruleHidden = byName('rule-HIDDEN required field')
assert.deepEqual(ruleHidden.hiddenFields, ['legalName'])
assert.equal(ruleHidden.saveBlocked, false)
const hiddenSection = byName('HIDDEN SECTION')
assert.deepEqual(hiddenSection.hiddenSections, ['sec-company'])
assert.deepEqual(hiddenSection.hiddenFields, ['contactName', 'jurisdiction', 'legalName', 'requesterEmail'])
assert.equal(hiddenSection.saveBlocked, false)
assert.deepEqual(byName('(F2a)').readOnlyFields, ['tin'])
assert.equal(byName('(F2a)').saveBlocked, false)
assert.deepEqual(byName('F6:').readOnlyFields, ['tin'])
assert.equal(byName('F6:').saveBlocked, false)
const ruleRequired = byName('rule-driven required')
assert.ok(ruleRequired.requiredFields.includes('requesterEmail'),
  'Required-action rule did not project required onto requesterEmail')
const failedValidate = byName('expression validity')
assert.equal(failedValidate.saveBlocked, true, 'failing Validate rules did not fail-close the save gate')
const cleanPass = byName('clean pass')
assert.equal(cleanPass.saveBlocked, false)
assert.deepEqual(cleanPass.hiddenFields, [])

// Reactive proof: one live DOM interaction through the packed ReactiveSchemaForm.
{
  const graph = graphFor(['hide-legal-name-for-ae'])
  let reactive = null
  function ReactiveHarness() {
    reactive = useFormRuleGraph(graph, baseView, { initialValues: { jurisdiction: 'us' } })
    return React.createElement(SchemaForm, {
      view: reactive.view,
      values: reactive.values,
      onValuesChange: reactive.onValuesChange,
      onSubmit: () => undefined,
    })
  }
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(ReactiveHarness)))
  assert.ok(container.innerHTML.includes('legalName'), 'legalName should render while jurisdiction is us')
  await act(async () => reactive.setValue('jurisdiction', 'ae'))
  assert.ok(!container.innerHTML.includes('>legalName<'), 'legalName should hide reactively for ae')
  assert.equal(reactive.view.sections.some(section => section.fields.some(field => field.name === 'legalName')), false)
  await act(async () => reactive.setValue('jurisdiction', 'us'))
  assert.ok(container.innerHTML.includes('legalName'), 'legalName should return reactively for us')
  await act(async () => root.unmount())
  container.remove()

  const wrapped = document.createElement('div')
  document.body.appendChild(wrapped)
  const wrappedRoot = createRoot(wrapped)
  await act(async () => wrappedRoot.render(React.createElement(ReactiveSchemaForm, {
    graph,
    view: baseView,
    initialValues: { jurisdiction: 'ae' },
    onSubmit: () => undefined,
  })))
  assert.ok(!wrapped.innerHTML.includes('>legalName<'), 'ReactiveSchemaForm should hide legalName for ae')
  await act(async () => wrappedRoot.unmount())
  wrapped.remove()
}

writeFileSync(new URL('./client-verdicts.json', import.meta.url), `${JSON.stringify(verdicts, null, 2)}\n`)
process.stdout.write(`DYNAMIC_FORMS_CLIENT_PASS: packed reference lane rendered ${verdicts.length} App parity-corpus cases through SchemaForm + useFormRuleGraph with reactive SPINE-1 evaluation and a fail-closed save gate\n`)
