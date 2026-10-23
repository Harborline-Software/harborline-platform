export interface Owner { person: string; tenant: string; workspace: string }
export interface Tab { id: string; kind: string; target: string; preview: boolean; pinned: boolean; dirty: boolean }
export interface SaveRequest { owner: Owner; id: string; generation: number; requestId: number }
export interface Presentation { destination: string; tabs: Tab[]; active: string | null; pendingClose: string | null; saveRequest: SaveRequest | null; draftGenerations: Record<string, number>; caches: Record<string, unknown>; drafts: Record<string, unknown>; queuedActions: unknown[] }
export interface State { owner: Owner | null; owners: Record<string, Presentation>; nextRequest: number }
export function initialState(): State
export function presentation(state: State): Presentation | null
export function transition(state: State, event: object): State
export function runScenario(events: readonly object[]): State
