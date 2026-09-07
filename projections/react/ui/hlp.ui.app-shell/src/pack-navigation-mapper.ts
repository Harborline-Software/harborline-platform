import { roleGateAllows, type HeldRoleSet, type RoleReference, type RoleVocabulary } from '@harborline-software/contracts/authorization'
import type { PackNavigationAction, PackNavigationDeclaration, PackPanelDeclaration, ShellNavigationState, ShellNavItem } from './types'

export interface ShellActionViewModel { id: string; label: string; icon: string; binding: string; shortcut: string }
export interface ShellGroupViewModel { id: string; label: string; items: readonly ShellNavItem[]; addAction?: ShellActionViewModel }
export interface ShellWorkspaceViewModel { id: string; label: string; icon?: string; count?: string | number; groups: readonly ShellGroupViewModel[]; createActions: readonly ShellActionViewModel[]; defaultCreateActionId?: string; createGuidance?: string; documentSpine: NonNullable<PackNavigationDeclaration['seedWorkspaces'][number]['documentSpine']>; recent: readonly ShellNavItem[]; suggested?: ShellNavItem }
export interface ShellNavigationViewModel { workspaces: readonly ShellWorkspaceViewModel[]; modes: readonly { id: string; label: string; workspaceIds: readonly string[] }[]; panels: readonly PackPanelDeclaration[] }
export interface PackNavigationMapperOptions { resolveLabel?: (labelKey: string) => string; state?: ShellNavigationState; roleVocabulary: RoleVocabulary; heldRoles: HeldRoleSet }

const roleReference = (name: string): RoleReference | undefined => {
  const separator = name.lastIndexOf('/')
  if (separator <= 0 || separator === name.length - 1) return undefined
  const vocabulary = name.slice(0, separator)
  if (vocabulary !== 'sys.platform-roles' && vocabulary !== 'tax.roles') return undefined
  return { vocabulary, name: name.slice(separator + 1) }
}
function permitted(entry: PackNavigationAction, vocabulary: RoleVocabulary, held: HeldRoleSet) {
  if (!entry.permittedRoles.length) return true
  const roles = entry.permittedRoles.map(roleReference)
  return roles.every((role): role is RoleReference => role !== undefined) && roleGateAllows({ requiredRoles: roles }, vocabulary, held)
}

/** The only React projection mapper from the api#58 declaration into shell render state. */
export function mapPackNavigationDeclaration(declaration: PackNavigationDeclaration, options: PackNavigationMapperOptions): ShellNavigationViewModel {
  const label = options.resolveLabel ?? (key => key)
  const state = options.state ?? {}
  const action = (entry: PackNavigationAction): ShellActionViewModel => ({ id: entry.id, label: label(entry.verbKey), icon: entry.icon, binding: entry.binding, shortcut: entry.shortcut })
  return {
    workspaces: declaration.seedWorkspaces.map(workspace => {
      const declaredActions = workspace.createActions ?? []
      const allowedActions = declaredActions.filter(entry => permitted(entry, options.roleVocabulary, options.heldRoles))
      const deniedGuidance = declaredActions.filter(entry => !permitted(entry, options.roleVocabulary, options.heldRoles)).map(entry => state.capabilityGuidanceByBinding?.[entry.binding]).find(Boolean)
      return { id: workspace.id, label: label(workspace.labelKey), icon: workspace.icon, count: workspace.countQueryRef ? state.counts?.[workspace.countQueryRef] : undefined,
        groups: (workspace.groups ?? []).map(group => ({ id: group.id, label: label(group.labelKey), items: group.itemIds.map(id => state.items?.[id] ?? { id, label: id }), addAction: group.addAction && permitted(group.addAction, options.roleVocabulary, options.heldRoles) ? action(group.addAction) : undefined })),
        createActions: allowedActions.map(action), defaultCreateActionId: state.defaultCreateActionByWorkspace?.[workspace.id], createGuidance: deniedGuidance, documentSpine: workspace.documentSpine ?? [], recent: state.recentByWorkspace?.[workspace.id] ?? [], suggested: state.suggestedByWorkspace?.[workspace.id] }
    }),
    modes: (declaration.modeSwitch?.modes ?? []).map(mode => ({ id: mode.id, label: label(mode.labelKey), workspaceIds: mode.workspaceIds })),
    panels: declaration.panelSet ?? [],
  }
}
