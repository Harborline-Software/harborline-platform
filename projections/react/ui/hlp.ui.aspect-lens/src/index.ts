/**
 * Projection-neutral builder contracts extracted from earlier source commit
 * a5036ff8b500e0d857d7e0a698408e7cf7f6cf31.
 *
 * Hosts own canvas state and localization. This projection only carries the
 * ordered outline, lens callbacks, and provenance read models consumed by the
 * later React Layers Rail projection.
 */

export interface CanvasNode {
  id: string
  kind: string
  label: string
  depth: number
  parentId: string | null
  detail?: string
  hasChildren?: boolean
}

export interface CanvasModel {
  nodes: CanvasNode[]
  selectedId: string | null
  select: (id: string) => void
}

export type LensTone =
  | 'accent'
  | 'warning'
  | 'danger'
  | 'success'
  | 'info'
  | 'muted'
  | 'sensitivity-none'
  | 'sensitivity-low'
  | 'sensitivity-medium'
  | 'sensitivity-high'

export interface AspectState {
  active: boolean
  badge?: string
  tone?: LensTone
  title?: string
}

export interface AspectEdge {
  from: string
  to: string
  label?: string
}

export interface AspectLens {
  id: string
  label: string
  tone: LensTone
  kind: 'colorize' | 'filter'
  project: (nodeId: string) => AspectState
  edges?: () => AspectEdge[]
  editor?: { hint: string }
  empty?: string
}

export type ProvenanceSource = 'base' | 'pack' | 'tenant' | 'instance' | 'unknown'

export interface ProvenanceInfo {
  source: ProvenanceSource
  chain: ProvenanceSource[]
  overridden: boolean
  locked: boolean
  resolved: boolean
}

export interface ProvenanceResolver {
  resolve: (nodeId: string) => ProvenanceInfo
}
