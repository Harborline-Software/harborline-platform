import assert from 'node:assert/strict'
import test from 'node:test'

import {formsWireSurface, readFormsWire, HARBORLINE_JSONLOGIC_V1} from '../dist/index.js'

test('exports the complete frozen forms surface', () => {
  assert.equal(formsWireSurface.declarations.length, 84)
  assert.equal(HARBORLINE_JSONLOGIC_V1, 'harborline-jsonlogic/v1')
  assert.deepEqual(formsWireSurface.closedValues.LayoutGap, [0, 1, 2, 3, 4, 5, 6, 8])
  assert.deepEqual(formsWireSurface.closedValues.RuleActionKind, ['Visibility', 'Required', 'ReadOnly', 'Validate', 'Compute', 'Presentation', 'Options'])
  assert.deepEqual(formsWireSurface.closedValues.OutputType, ['Value', 'Validity', 'Visibility', 'Presentation', 'Options'])
})

test('round-trips exact App authoring metadata', () => {
  const definition = readFormsWire('FormDefinition', {
    id: 'inspection', version: '1.0.0', status: 'Published', tenant: 'acme',
    owner: {scheme: 'system', value: 'harborline'}, schemaRef: 'sha256:test',
    overlay: {fields: {}, sections: [], rules: []},
    createdAt: '2026-08-08T00:00:00Z', updatedAt: '2026-08-08T00:00:00Z',
    fieldsMeta: {condition: {type: 'radio', required: true, validations: [{code: 'required'}], options: ['PASS', 'FAIL']}},
  })
  assert.equal(definition.fieldsMeta.condition.type, 'radio')
  assert.deepEqual(definition.fieldsMeta.condition.options, ['PASS', 'FAIL'])
})

test('round-trips the complete server-side field-rule projection', () => {
  const field = readFormsWire('FormViewField', {
    name: 'total', label: {defaultLocale: 'en', values: {en: 'Total'}},
    isSensitive: false, isReadable: true,
    rules: {visible: true, required: false, readOnly: true, computed: 10, presentationSeverity: 'warn'},
  })
  assert.equal(field.rules.computed, 10)
  assert.equal(field.rules.presentationSeverity, 'warn')
})

test('reads recursive discriminated items and strips additive properties', () => {
  const input = {kind: 'collection', key: 'photos', items: [{kind: 'field', key: 'photo', future: true}]}
  assert.deepEqual(readFormsWire('FormItem', input), {kind: 'collection', key: 'photos', items: [{kind: 'field', key: 'photo'}]})
})

test('fails closed for unknown and cross-variant discriminated payloads', () => {
  assert.throws(() => readFormsWire('FormItem', {kind: 'script', key: 'unsafe'}), /unknown-discriminator/)
  assert.throws(() => readFormsWire('FormItem', {kind: 'field', key: 'result', items: []}), /variant-member-mismatch/)
})

test('distinguishes required, optional, nullable, open, and closed values', () => {
  assert.throws(() => readFormsWire('InternationalizedText', {values: {en: 'Inspection'}}), /missing-required-property/)
  assert.throws(() => readFormsWire('InternationalizedText', {defaultLocale: null, values: {en: 'Inspection'}}), /invalid-string/)
  assert.equal(readFormsWire('ControlHint', 'future-spatial-control'), 'future-spatial-control')
  assert.throws(() => readFormsWire('LayoutGap', 7), /unknown-closed-value/)
})
