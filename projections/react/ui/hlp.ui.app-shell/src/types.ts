import * as React from 'react'

/** Projection-neutral mirror of harborline-api's api#58 wire declaration. */
export interface PackNavigationDeclaration {
  seedWorkspaces: readonly PackNavigationWorkspace[]
  modeSwitch?: PackNavigationModeSwitch
  panelSet?: readonly PackPanelDeclaration[]
}
export interface PackNavigationModeSwitch { modes: readonly PackNavigationMode[] }
export interface PackNavigationMode { id: string; labelKey: string; workspaceIds: readonly string[] }
export interface PackNavigationWorkspace { id: string; labelKey: string; icon?: string; destinationQueryRef?: string; countQueryRef?: string; groups?: readonly PackNavigationGroup[]; createActions?: readonly PackNavigationAction[]; documentSpine?: readonly PackDocumentSpineNode[]; defaultForPersonas?: readonly string[] }
export interface PackNavigationGroup { id: string; labelKey: string; destinationQueryRef?: string; countQueryRef?: string; itemIds: readonly string[]; addAction?: PackNavigationAction }
export interface PackNavigationAction { id: string; verbKey: string; icon: string; binding: string; shortcut: string; permittedRoles: readonly string[] }
export interface PackDocumentSpineNode { id: string; labelKey: string; binding: string; children?: readonly PackDocumentSpineNode[] }
export interface PackPanelDeclaration { id: string; labelKey?: string; binding: string; shortcut: string; defaultWidth: number; minimumHeight: number; defaultOpen: boolean; footer?: PackPanelFooter; traits?: readonly string[]; popOut?: boolean }
export interface PackPanelFooter { kind: string; labelKey: string; binding?: string }

export interface ShellScopeOption { id: string; label: string; icon?: React.ReactNode; monogram?: string; pinnable?: boolean }
export interface ShellRowAction { id: string; label: string; keyHint?: string; destructive?: boolean }
export interface ShellNavThread { id: string; label: string; mark?: React.ReactNode; active?: boolean; editing?: boolean; actions?: readonly ShellRowAction[] }
/** Shell-owned resolved navigation data. It is runtime state, never pack-authored declaration data. */
export interface ShellNavItem { id: string; label: string; icon?: React.ReactNode; kind?: string; count?: string | number; pinnable?: boolean; threads?: readonly ShellNavThread[] }
export interface ShellNavigationState { items?: Readonly<Record<string, ShellNavItem>>; counts?: Readonly<Record<string, string | number>>; recentByWorkspace?: Readonly<Record<string, readonly ShellNavItem[]>>; suggestedByWorkspace?: Readonly<Record<string, ShellNavItem | undefined>>; defaultCreateActionByWorkspace?: Readonly<Record<string, string>>; capabilityGuidanceByBinding?: Readonly<Record<string, string>> }
export interface ShellScopeDirectory { label: string; invoke: () => void }
export interface ShellFooterIdentity { label: string; role: string; icon?: React.ReactNode }
export interface ShellSystemItem { id: string; label: string; icon?: React.ReactNode; invoke?: () => void }

export const SHELL_RAIL_ZONE_ORDER = ['head', 'mode', 'primary-action', 'workspaces', 'pinned', 'groups', 'recent', 'suggested', 'footer'] as const
/** The 216px rail and 420px content floor are spec-owned taste, not published platform guidance. */
export const SHELL_LAYOUT = { barHeight: 34, railWidth: 216, railMinimum: 120, contentFloor: 420 } as const
/** chrome-spec §6:174 — the shell header is 33px; the toolbar and footer are the body's other content-sized slots. */
export const SHELL_PANEL_SLOTS = { header: 33, toolbar: 32, footer: 28 } as const
/**
 * chrome-spec §6:188-190 — "The header has exactly two forms ... Only panels that open one item take the second."
 * The trigger is the OpensOne trait, not a style choice, so a panel that does not open one item cannot earn form 2.
 */
export const panelHeaderForm = (panel: PackPanelDeclaration): 'title' | 'toggle-chip' => panel.traits?.includes('OpensOne') ? 'toggle-chip' : 'title'
/** chrome-spec §6:218 — "⋮ is earned by OpensOne or Consequential"; every other header is three affordances. */
export const panelEarnsOverflow = (panel: PackPanelDeclaration): boolean => panel.traits?.includes('OpensOne') === true || panel.traits?.includes('Consequential') === true
/**
 * chrome-spec §6:236-238 — "A panel's declared minimum is the content-sized slots above its one flexible slot".
 * The body contributes nothing: it is allowed to scroll, so a tall body never raises the panel's floor.
 */
export const panelSlotMinimum = (panel: PackPanelDeclaration): number => SHELL_PANEL_SLOTS.header + SHELL_PANEL_SLOTS.toolbar + (panel.footer ? SHELL_PANEL_SLOTS.footer : 0)
export const panelMinimumHeight = (panel: PackPanelDeclaration): number => Math.max(panel.minimumHeight, panelSlotMinimum(panel))
/** Both shells round a fractional drag half-up (floor(x + 0.5)), so a 12.5px drag lands on the same integer. */
export const roundHalfUp = (value: number): number => Math.floor(value + 0.5)
export const SHELL_BREAKPOINTS = { compact: 0, medium: 600, expanded: 840, large: 1200, 'extra-large': 1600 } as const
export type ShellBreakpoint = keyof typeof SHELL_BREAKPOINTS

export function shellBreakpoint(width: number): ShellBreakpoint {
  if (width >= SHELL_BREAKPOINTS['extra-large']) return 'extra-large'
  if (width >= SHELL_BREAKPOINTS.large) return 'large'
  if (width >= SHELL_BREAKPOINTS.expanded) return 'expanded'
  if (width >= SHELL_BREAKPOINTS.medium) return 'medium'
  return 'compact'
}

export function shellAddress(kind: string, id: string): string {
  const segment = kind.replace(/^\/+|\/+$/g, '') || 'workspaces'
  return `/${encodeURIComponent(segment)}/${encodeURIComponent(id)}`
}
