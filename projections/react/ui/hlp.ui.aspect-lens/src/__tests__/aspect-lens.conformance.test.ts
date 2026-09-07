import { describe, expect, it, vi } from 'vitest'

import type {
  AspectEdge,
  AspectLens,
  AspectState,
  CanvasModel,
  CanvasNode,
  LensTone,
  ProvenanceInfo,
  ProvenanceResolver,
  ProvenanceSource,
} from '../index'
import { fixture, sharedCases } from './fixtures'

describe('Aspect Lens revision-1 shared fixtures', () => {
  it('consumes every frozen neutral case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'aspect-lens.canvas-tree',
      'aspect-lens.graph-projection',
      'aspect-lens.selection',
      'aspect-lens.colorize',
      'aspect-lens.filter',
      'aspect-lens.edges',
      'aspect-lens.optional-descriptors',
      'aspect-lens.closed-vocabulary',
      'aspect-lens.provenance',
      'aspect-lens.fail-closed-provenance',
      'aspect-lens.projection-equivalence',
    ])
  })

  it('preserves ordered single-parent canvas projections and delegates selection once', () => {
    const tree = fixture(sharedCases, 'aspect-lens.canvas-tree')
    const selection = fixture(sharedCases, 'aspect-lens.selection')
    const nodes: CanvasNode[] = (tree.input as {
      nodes: Array<Pick<CanvasNode, 'id' | 'depth' | 'parentId'>>
    }).nodes.map((node, index) => ({
      kind: index === 0 ? 'section' : 'field',
      label: index === 0 ? 'Inspection' : 'Amount',
      ...node,
    }))
    const select = vi.fn()
    const model: CanvasModel = { nodes, selectedId: null, select }

    model.select((selection.input as { select: string }).select)

    expect(model.nodes.map(node => node.id)).toEqual(['root', 'child'])
    expect(model.nodes[1]?.parentId).toBe('root')
    expect(select).toHaveBeenCalledOnce()
    expect(select).toHaveBeenCalledWith('child')
  })

  it('keeps graph cross-links in directed lens edges', () => {
    const value = fixture(sharedCases, 'aspect-lens.graph-projection')
    const input = value.input as { crossLinks: AspectEdge[] }
    const lens: AspectLens = {
      id: 'logic',
      label: 'Logic',
      tone: 'accent',
      kind: 'colorize',
      project: () => ({ active: true }),
      edges: () => input.crossLinks,
    }

    expect(lens.edges?.()).toEqual([{ from: 'alternate', to: 'review' }])
  })

  it('preserves colorize, filter, edge, and optional descriptor data', () => {
    const colorize = fixture(sharedCases, 'aspect-lens.colorize').input as { state: AspectState }
    const filter = fixture(sharedCases, 'aspect-lens.filter').input as { state: AspectState }
    const edge = fixture(sharedCases, 'aspect-lens.edges').input as { edges: AspectEdge[] }
    const optional = fixture(sharedCases, 'aspect-lens.optional-descriptors').input as {
      state: AspectState
      lens: Pick<AspectLens, 'editor' | 'empty'>
    }

    expect(colorize.state).toEqual({ active: true, badge: 'PII', tone: 'sensitivity-high' })
    expect(filter.state.active).toBe(false)
    expect(edge.edges[0]?.label).toBe('gates visibility')
    expect(optional).toEqual({
      state: { active: true, badge: 'Required', title: 'Required by policy' },
      lens: { editor: { hint: 'Edit requirement' }, empty: 'Select a field to add a rule' },
    })
  })

  it('preserves all closed vocabularies', () => {
    const value = fixture(sharedCases, 'aspect-lens.closed-vocabulary').input as {
      tones: LensTone[]
      kinds: AspectLens['kind'][]
      sources: ProvenanceSource[]
    }

    expect(value.tones).toHaveLength(10)
    expect(value.kinds).toEqual(['colorize', 'filter'])
    expect(value.sources).toEqual(['base', 'pack', 'tenant', 'instance', 'unknown'])
  })

  it('preserves resolved provenance and fail-closed unresolved provenance', () => {
    const resolved = fixture(sharedCases, 'aspect-lens.provenance').input as ProvenanceInfo
    const unresolved = fixture(sharedCases, 'aspect-lens.fail-closed-provenance').input as ProvenanceInfo
    const resolver: ProvenanceResolver = {
      resolve: nodeId => (nodeId === 'known' ? resolved : unresolved),
    }

    expect(resolver.resolve('known')).toEqual(resolved)
    expect(resolver.resolve('missing')).toEqual({
      source: 'unknown',
      chain: ['unknown'],
      overridden: false,
      locked: true,
      resolved: false,
    })
  })

  it('retains projection-equivalent optional fields', () => {
    const value = fixture(sharedCases, 'aspect-lens.projection-equivalence').input as {
      optionalFields: string[]
    }
    expect(value.optionalFields).toEqual([
      'detail',
      'hasChildren',
      'badge',
      'tone',
      'title',
      'edges',
      'editor',
      'empty',
    ])
  })
})
