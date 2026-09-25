/**
 * Rules preview's expression environment declaration (rules-auth-11, TS lane): the authoring preview
 * evaluates sample records while authoring. Mirrors the .NET `RulesPreviewEnvironment`; Rules admits
 * it and the preview presents the admission (rules-eng-26).
 */
import { admitEnvironment, builtInFunctions, fieldReadEffect, lentGrammar, type BorrowerEnvironmentDeclaration } from '@harborline-software/rule-engine'

export const rulesPreviewDeclaration: BorrowerEnvironmentDeclaration = {
  borrower: 'rules-auth-11',
  grammar: lentGrammar,
  variables: { field: 'sample record field', row: 'sample record child row' },
  operations: builtInFunctions.map((f) => f.key),
  effects: [fieldReadEffect],
  missingValues: 'missing-field-reads-null',
  timeSource: 'pinned-preview-clock',
  timeZone: 'utc',
  phases: { AuthoringValidation: true, PublishValidation: false, Render: false, Submission: false, Run: false, SignOff: false },
  replay: 'deterministic-over-pinned-clock-and-snapshot',
}

export const rulesPreviewEnvironment = admitEnvironment(rulesPreviewDeclaration)
