import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Host } from './setup'

// Contract: caller labels override the rail's chrome and no control or region is ever unnamed; blank
// caller text keeps the default. (Catalog-backed defaults are blocked: the module declares no
// locale-provider or default-strings dependency, and the catalog has no rail keys.)
function names() {
  for (const element of [screen.getByRole('complementary'), screen.getByRole('group'), screen.getByRole('tree'), ...screen.getAllByRole('button')]) {
    const name = (element.getAttribute('aria-label') ?? element.textContent ?? '').trim()
    expect(name, element.outerHTML.slice(0, 80)).not.toBe('')
  }
}

describe('LayersRail labels', () => {
  it('names every control and region with the defaults', () => {
    render(<Host onInsert={() => undefined}/>)
    names()
  })

  it('keeps the default for blank caller labels, including blank label functions', () => {
    render(<Host labels={{ exitLens: '  ', insert: '', collapseNode: ' ', activateLens: () => ' ', railRegion: '' }} onInsert={() => undefined}/>)
    expect(screen.getByRole('button', { name: 'Exit lens (back to Layout)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Insert' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show Layout lens on the canvas' })).toBeInTheDocument()
    expect(screen.getByRole('complementary', { name: 'Layers' })).toBeInTheDocument()
    names()
  })

  it('carries Arabic and pseudo-localized caller text without truncation', () => {
    const view = render(<div dir="rtl"><Host labels={{ railRegion: 'الطبقات', exitLens: 'الخروج من العدسة' }}/></div>)
    expect(screen.getByRole('complementary', { name: 'الطبقات' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'الخروج من العدسة' })).toBeInTheDocument()
    view.unmount()
    const pseudo = '⟦ Ļåÿëřš ··· ⟧'
    render(<Host labels={{ railRegion: pseudo }}/>)
    expect(screen.getByRole('complementary', { name: pseudo })).toBeInTheDocument()
  })
})
