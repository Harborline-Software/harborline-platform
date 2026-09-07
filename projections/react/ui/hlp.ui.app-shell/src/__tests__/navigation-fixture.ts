import type { PackNavigationAction, PackNavigationDeclaration, PackPanelDeclaration, ShellNavigationState, ShellNavItem, ShellNavThread } from '../types'

export interface TestWorkspace {
  id: string; label: string; count?: string | number
  groups: readonly { id: string; label?: string; items: readonly ShellNavItem[]; threads?: readonly ShellNavThread[] }[]
  createActions?: readonly { id: string; label: string; binding?: string; permittedRoles?: readonly string[] }[]
  defaultCreateActionId?: string; recent?: readonly ShellNavItem[]; suggested?: ShellNavItem
}
export function navigationFixture(workspaces: readonly TestWorkspace[], panels: readonly PackPanelDeclaration[] = []) {
  const items: Record<string, ShellNavItem> = {}
  const counts: Record<string, string | number> = {}
  const recentByWorkspace: Record<string, readonly ShellNavItem[]> = {}
  const suggestedByWorkspace: Record<string, ShellNavItem> = {}
  const defaults: Record<string, string> = {}
  const seedWorkspaces = workspaces.map(workspace => {
    const countQueryRef = workspace.count === undefined ? undefined : `counts.${workspace.id}`
    if (countQueryRef) counts[countQueryRef] = workspace.count!
    if (workspace.recent) recentByWorkspace[workspace.id] = workspace.recent
    if (workspace.suggested) suggestedByWorkspace[workspace.id] = workspace.suggested
    if (workspace.defaultCreateActionId) defaults[workspace.id] = workspace.defaultCreateActionId
    for (const group of workspace.groups) for (const item of group.items) items[item.id] = item
    return {
      id: workspace.id, labelKey: workspace.label, countQueryRef,
      groups: workspace.groups.map(group => ({ id: group.id, labelKey: group.label ?? group.id, itemIds: group.items.map(item => item.id) })),
      createActions: workspace.createActions?.map((action): PackNavigationAction => ({ id: action.id, verbKey: action.label, icon: 'plus', binding: action.binding ?? action.id, shortcut: 'mod+n', permittedRoles: action.permittedRoles ?? [] })),
    }
  })
  const navigation: PackNavigationDeclaration = { seedWorkspaces, panelSet: panels }
  const navigationState: ShellNavigationState = { items, counts, recentByWorkspace, suggestedByWorkspace, defaultCreateActionByWorkspace: defaults }
  return { navigation, navigationState, resolveLabel: (key: string) => key }
}
