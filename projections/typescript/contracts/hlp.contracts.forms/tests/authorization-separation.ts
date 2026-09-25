import { roleGateAllows, type HeldRoleSet, type RecordStandingReference, type RoleVocabulary } from '../src/authorization.js'

declare const standing: RecordStandingReference
declare const vocabulary: RoleVocabulary
declare const held: HeldRoleSet

// @ts-expect-error Record standings are computed facts, never role references.
roleGateAllows({requiredRoles: [standing]}, vocabulary, held)

// @ts-expect-error Record standings are not held-role sets.
roleGateAllows({requiredRoles: []}, vocabulary, standing)

declare const capability: import('../src/authorization.js').AuthorizationCapabilityReference

// @ts-expect-error An authorization capability is not a role reference (T-747).
roleGateAllows({requiredRoles: [capability]}, vocabulary, held)
