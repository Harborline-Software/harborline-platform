export type RoleVocabularyName = 'sys.platform-roles' | 'tax.roles'

export interface RoleReference {
  vocabulary: RoleVocabularyName
  name: string
}

export interface RoleOwner {
  kind: 'Platform' | 'Package' | 'Tenant'
  ownerId: string
}

export interface RoleDefinition {
  roleDefinitionId: string
  role: RoleReference
  displayName: string
  owner: RoleOwner
  isSealed: boolean
}

export interface RoleGate { requiredRoles: readonly RoleReference[] }
export interface HeldRoleSet { readonly roles: readonly RoleReference[] }
export interface RecordStandingReference { readonly name: string }

const keyOf = ({vocabulary, name}: RoleReference) => `${vocabulary}\u0000${name}`

function validateReference(role: RoleReference): void {
  if (role.vocabulary !== 'sys.platform-roles' && role.vocabulary !== 'tax.roles')
    throw new TypeError(`unknown-role-vocabulary: ${String(role.vocabulary)}`)
  if (!role.name) throw new TypeError('invalid-role-reference: name')
}

export class RoleVocabulary {
  readonly #definitions: ReadonlyMap<string, RoleDefinition>

  private constructor(definitions: ReadonlyMap<string, RoleDefinition>) {
    this.#definitions = definitions
  }

  static fromApi(definitions: readonly RoleDefinition[]): RoleVocabulary {
    const snapshot = new Map<string, RoleDefinition>()
    for (const definition of definitions) {
      validateReference(definition.role)
      const platformVocabulary = definition.role.vocabulary === 'sys.platform-roles'
      const platformOwner = definition.owner.kind === 'Platform'
      if (platformVocabulary !== platformOwner || definition.isSealed !== platformOwner)
        throw new TypeError(`invalid-role-ownership: ${definition.role.vocabulary}/${definition.role.name}`)
      if (platformOwner && definition.role.name !== 'administrator' && definition.role.name !== 'auditor')
        throw new TypeError(`invalid-platform-role: ${definition.role.name}`)
      if (!definition.roleDefinitionId || !definition.displayName || !definition.owner.ownerId)
        throw new TypeError(`invalid-role-definition: ${definition.role.vocabulary}/${definition.role.name}`)
      const key = keyOf(definition.role)
      if (snapshot.has(key)) throw new TypeError(`duplicate-role-reference: ${definition.role.vocabulary}/${definition.role.name}`)
      snapshot.set(key, Object.freeze({...definition, role: Object.freeze({...definition.role}), owner: Object.freeze({...definition.owner})}))
    }
    return new RoleVocabulary(snapshot)
  }

  resolve(role: RoleReference): RoleDefinition | undefined {
    validateReference(role)
    return this.#definitions.get(keyOf(role))
  }

  require(role: RoleReference): RoleDefinition {
    const definition = this.resolve(role)
    if (!definition) throw new TypeError(`unknown-role-reference: ${role.vocabulary}/${role.name}`)
    return definition
  }
}

export function roleGateAllows(gate: RoleGate, vocabulary: RoleVocabulary, held: HeldRoleSet): boolean {
  if (gate.requiredRoles.length === 0) return true
  try {
    if (gate.requiredRoles.some(role => vocabulary.resolve(role) === undefined)) return false
  } catch {
    return false
  }
  const heldKeys = new Set(held.roles.map(role => keyOf(role)))
  return gate.requiredRoles.some(role => heldKeys.has(keyOf(role)))
}

export interface AuthorizationCapabilityReference { readonly name: string }
export interface AuthorizationCapabilityDefinition {
  readonly capability: AuthorizationCapabilityReference
  readonly version: number
}

const capabilityName = /^[a-z][a-z0-9-]*:[a-z][a-z0-9-]*$/

function validateCapability(capability: AuthorizationCapabilityReference): void {
  if (typeof capability?.name !== 'string' || !capabilityName.test(capability.name))
    throw new TypeError(`invalid-authorization-capability: ${String(capability?.name)}`)
}

export class AuthorizationCapabilityRegister {
  readonly #definitions: ReadonlyMap<string, AuthorizationCapabilityDefinition>

  private constructor(definitions: ReadonlyMap<string, AuthorizationCapabilityDefinition>) {
    this.#definitions = definitions
  }

  static fromDeclarations(definitions: readonly AuthorizationCapabilityDefinition[]): AuthorizationCapabilityRegister {
    const snapshot = new Map<string, AuthorizationCapabilityDefinition>()
    for (const definition of definitions) {
      validateCapability(definition.capability)
      const {name} = definition.capability
      if (!Number.isInteger(definition.version) || definition.version < 1)
        throw new TypeError(`invalid-authorization-capability-version: ${name}`)
      if (snapshot.has(name)) throw new TypeError(`duplicate-authorization-capability: ${name}`)
      snapshot.set(name, Object.freeze({capability: Object.freeze({name}), version: definition.version}))
    }
    return new AuthorizationCapabilityRegister(snapshot)
  }

  resolve(capability: AuthorizationCapabilityReference): AuthorizationCapabilityDefinition | undefined {
    validateCapability(capability)
    return this.#definitions.get(capability.name)
  }

  require(capability: AuthorizationCapabilityReference): AuthorizationCapabilityDefinition {
    const definition = this.resolve(capability)
    if (!definition) throw new TypeError(`unknown-authorization-capability: ${capability.name}`)
    return definition
  }
}
