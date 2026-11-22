import type { RuleDefinitionDocument, RuleDefinitionEnvelope, RuleDefinitionExpression, RuleDefinitionValueType } from './definition.js'

/** Kind-appropriate shorthands that expand into ordinary editable validation rules. */
export type FieldPreset = 'NonNegative' | 'NotBlank' | 'MustBeTrue'

/** The preset offered for each closed source value kind (rules-auth-17). */
export function fieldPresetsFor(kind: RuleDefinitionValueType): FieldPreset[] {
  switch (kind) {
    case 'Number': return ['NonNegative']
    case 'Text': return ['NotBlank']
    case 'Boolean': return ['MustBeTrue']
  }
}

/** Produces Formula source for the existing codec/compiler; incompatible pairs throw. */
export function createFieldPreset(
  envelope: RuleDefinitionEnvelope, fieldKey: string, kind: RuleDefinitionValueType, preset: FieldPreset,
): RuleDefinitionDocument {
  if (!fieldPresetsFor(kind).includes(preset)) throw new Error(`${preset} is not a ${kind} preset`)
  let expression: RuleDefinitionExpression
  switch (preset) {
    case 'NonNegative':
      expression = { kind: 'Call', op: '>=', args: [{ kind: 'Ref', name: 'self' }, { kind: 'Literal', value: '0', valueType: 'Number' }] }
      break
    case 'NotBlank':
      expression = { kind: 'Call', op: '!=', args: [{ kind: 'Ref', name: 'self' }, { kind: 'Literal', value: '', valueType: 'Text' }] }
      break
    case 'MustBeTrue':
      expression = { kind: 'Call', op: '==', args: [{ kind: 'Ref', name: 'self' }, { kind: 'Literal', value: 'true', valueType: 'Boolean' }] }
      break
  }
  return {
    envelope, name: `${fieldKey} ${preset}`, tier: 'JsonLogic',
    draft: {
      kind: 'Formula', scope: 'Field', scopeTarget: fieldKey, outputType: 'Validate',
      inputs: [{ id: 'self', ref: 'self', type: kind }], expression,
    },
  }
}
