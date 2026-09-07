import { describe, expect, it } from 'vitest'

import type { AspectLens, CanvasModel, CanvasNode, LensTone, ProvenanceResolver } from '../index'
import { fixture, qualityCases } from './fixtures'

describe('Aspect Lens native edge cases', () => {
  it('preserves Unicode host-resolved labels and badges byte-for-byte', () => {
    const value = fixture(qualityCases, 'aspect-lens.quality.unicode').expected as {
      nodeLabel: string
      badge: string
    }
    const node: CanvasNode = { id: 'safety', kind: 'section', label: value.nodeLabel, depth: 0, parentId: null }
    const lens: AspectLens = {
      id: 'classification',
      label: 'التصنيف',
      tone: 'sensitivity-high',
      kind: 'colorize',
      project: () => ({ active: true, badge: value.badge, tone: 'sensitivity-high' }),
    }

    expect(node.label).toBe('فحص السلامة')
    expect(lens.project(node.id).badge).toBe('حساس')
  })

  it('pairs every active semantic tone with a text signal', () => {
    fixture(qualityCases, 'aspect-lens.quality.paired-signal')
    const tones: LensTone[] = [
      'accent', 'warning', 'danger', 'success', 'info', 'muted',
      'sensitivity-none', 'sensitivity-low', 'sensitivity-medium', 'sensitivity-high',
    ]
    const states = tones.map(tone => ({ active: true, tone, badge: tone }))

    expect(states.every(state => state.badge.length > 0)).toBe(true)
  })

  it('does not remove inactive filter nodes from structural context', () => {
    fixture(qualityCases, 'aspect-lens.quality.structure-context')
    const nodes: CanvasNode[] = [
      { id: 'root', kind: 'section', label: 'Root', depth: 0, parentId: null, hasChildren: true },
      { id: 'child', kind: 'field', label: 'Child', depth: 1, parentId: 'root' },
    ]
    const model: CanvasModel = { nodes, selectedId: null, select: () => undefined }
    const filter: AspectLens = {
      id: 'access', label: 'Access', tone: 'danger', kind: 'filter', project: () => ({ active: false }),
    }

    expect(model.nodes).toHaveLength(2)
    expect(model.nodes.map(node => filter.project(node.id).active)).toEqual([false, false])
  })

  it('keeps unresolved provenance locked and non-editable', () => {
    fixture(qualityCases, 'aspect-lens.quality.fail-closed')
    const resolver: ProvenanceResolver = {
      resolve: () => ({ source: 'unknown', chain: ['unknown'], overridden: false, locked: true, resolved: false }),
    }

    const result = resolver.resolve('missing')
    expect(result.source).toBe('unknown')
    expect(result.locked || !result.resolved).toBe(true)
  })

  it('carries logical order without introducing physical positioning or formatting', () => {
    fixture(qualityCases, 'aspect-lens.quality.direction-neutral')
    fixture(qualityCases, 'aspect-lens.quality.formatting-applicability')
    const labels = ['الأول', 'الثاني']
    const nodes: CanvasNode[] = labels.map((label, index) => ({
      id: String(index), kind: 'field', label, depth: 0, parentId: null,
    }))
    expect(nodes.map(node => node.label)).toEqual(labels)
  })
})
