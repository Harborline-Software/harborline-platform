import type { InternationalizedText as FormsText, PresentationHint as FormsPresentation, RuleDefinition as FormsRule } from '../src/forms.js'
import type { InternationalizedText } from '../src/text.js'
import type { PresentationHint } from '../src/presentation.js'
import type { RuleDefinition } from '../src/rules.js'
import type { WorkflowDefinition } from '../src/workflow.js'

declare const text: InternationalizedText
declare const presentation: PresentationHint
declare const rule: RuleDefinition
declare const formRule: FormsRule
declare const formText: FormsText
declare const formPresentation: FormsPresentation
declare const workflowGuard: WorkflowDefinition['guards'][number]

export const ownerRule: RuleDefinition = formRule
export const compatibleFormsRule: FormsRule = rule
export const ownerText: InternationalizedText = formText
export const compatibleFormsText: FormsText = text
export const ownerPresentation: PresentationHint = formPresentation
export const compatibleFormsPresentation: FormsPresentation = presentation
export const ownerWorkflowGuard: RuleDefinition = workflowGuard

// @ts-expect-error Wire expressions remain strings; parsed Json belongs to the Runtime API.
export const parsedWireRule: RuleDefinition = { ...rule, expression: { var: 'total' } }

// @ts-expect-error Shared presentation severity remains closed.
export const unknownSeverity: PresentationHint = { severity: 'fatal' }
