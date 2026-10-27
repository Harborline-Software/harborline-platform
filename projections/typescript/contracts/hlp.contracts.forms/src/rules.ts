import type { InternationalizedText } from './text.js'
import type { PresentationHint } from './presentation.js'

export type RuleTier = 'JsonSchema' | 'JsonLogic' | 'PowerFx'

export type RuleScope = 'Field' | 'Section' | 'Schema' | 'Row' | 'Table'

export type RuleActionKind = 'Visibility' | 'Required' | 'ReadOnly' | 'Validate' | 'Compute' | 'Presentation' | 'Options'

export interface RuleDefinition {
  id: string
  tier: RuleTier
  scope: RuleScope
  /**
   * Field name (Field), section id (Section), empty string (Schema),
   * `section/field` (Row), or `section/fn/col` (Table).
   */
  scopeTarget: string
  /** Opaque expression text in the language indicated by `tier`. */
  expression: string
  action: RuleActionKind
  errorMessage?: InternationalizedText
  /** Presentation payload for a `Presentation`-action rule (SPINE-1 Decision DA). */
  presentation?: PresentationHint
}
