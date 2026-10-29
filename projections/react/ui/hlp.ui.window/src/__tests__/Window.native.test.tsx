import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Window } from '../Window'
import * as publicSurface from '../index'
import { qualityCases } from './fixtures'

const allLabels = {
  window: 'Ventana', close: 'Cerrar', minimize: 'Minimizar', maximize: 'Maximizar', restore: 'Restaurar',
  move: 'Mover la ventana', moveDescription: 'Use las flechas', resizeHeight: 'Cambiar la altura', resizeWidth: 'Cambiar el ancho',
}

describe('Window React projection', () => {
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'window.quality.dialog', 'window.quality.controls', 'window.quality.keyboard', 'window.quality.focus', 'window.quality.reflow',
      'window.quality.caller-copy', 'window.quality.rtl', 'window.quality.pseudo', 'window.quality.light-dark', 'window.quality.tokens',
      'window.quality.forced-colors', 'window.quality.reduced-motion', 'window.quality.visual-parity', 'window.quality.dismissal', 'window.quality.pointer-cancel',
    ])
  })

  it('publishes only the frozen runtime exports', () => {
    expect(Object.keys(publicSurface).sort()).toEqual(['Window', 'WindowActionsBar'])
  })

  it('uses caller-localized names for built-in controls and the untitled window', () => {
    render(<Window labels={allLabels} />)
    expect(screen.getByRole('dialog', { name: 'Ventana' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cerrar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Minimizar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Maximizar' })).toBeInTheDocument()
  })

  // T-1123: the component carries no built-in text. A missing label for a control it renders is a contract
  // error, as in the Blazor lane, never an English fallback.
  it.each([
    ['close', { closable: true }],
    ['minimize', { minimizable: true }],
    ['maximize', { maximizable: true }],
    ['restore', { maximizable: true }],
    ['move', { draggable: true }],
    ['moveDescription', { draggable: true }],
    ['resizeHeight', { resizable: true }],
    ['resizeWidth', { resizable: true }],
  ] as const)('rejects a missing %s label for a control it renders', (key, flags) => {
    const labels: Record<string, string> = { ...allLabels }
    delete labels[key]
    expect(() => render(<Window labels={labels} title="Editor" {...flags} />)).toThrow('localized-control-labels-required')
  })

  it('rejects a blank label and an untitled window without a name', () => {
    expect(() => render(<Window labels={{ ...allLabels, close: ' ' }} title="Editor" />)).toThrow('localized-control-labels-required')
    expect(() => render(<Window labels={{ ...allLabels, window: '' }} title={null} />)).toThrow('accessible-name-required')
  })

  // T-1123: moving works only in the default state, so the header is a named move target only then, as in Blazor.
  it.each(['maximized', 'minimized'] as const)('offers no move target in the header when %s', state => {
    render(<Window labels={allLabels} state={state} title="Editor" />)
    const titleBar = screen.getByTestId('window-title-bar')
    expect(titleBar).not.toHaveAttribute('role')
    expect(titleBar).not.toHaveAttribute('aria-label')
    expect(titleBar).not.toHaveAttribute('aria-description')
    expect(titleBar).not.toHaveAttribute('tabindex')
  })

  it('cycles modal focus in both directions and restores prior focus on removal', async () => {
    function Harness({ shown }: { shown: boolean }) {
      return <><button type="button">Prior</button>{shown ? <Window labels={allLabels} modal title="Editor"><button type="button">Body action</button></Window> : null}</>
    }
    const rendered = render(<Harness shown={false} />)
    const prior = screen.getByRole('button', { name: 'Prior' })
    prior.focus()
    rendered.rerender(<Harness shown />)
    prior.focus()
    expect(screen.getByRole('dialog')).toHaveFocus()
    const titleBar = screen.getByRole('group', { name: allLabels.move })
    titleBar.focus()
    await userEvent.setup().tab({ shift: true })
    expect(screen.getByRole('separator', { name: allLabels.resizeWidth })).toHaveFocus()
    await userEvent.setup().tab()
    expect(titleBar).toHaveFocus()
    rendered.rerender(<Harness shown={false} />)
    expect(prior).toHaveFocus()
  })

  it('clears pointer interaction on cancellation without stale callbacks', () => {
    const onMove = vi.fn()
    render(<Window labels={allLabels} onMove={onMove} title="Editor" />)
    const titleBar = screen.getByTestId('window-title-bar')
    const dialog = screen.getByRole('dialog')
    fireEvent.pointerDown(titleBar, { pointerId: 9, clientX: 10, clientY: 10 })
    fireEvent.pointerCancel(dialog, { pointerId: 9 })
    fireEvent.pointerMove(dialog, { pointerId: 9, clientX: 50, clientY: 50 })
    expect(onMove).not.toHaveBeenCalled()
  })

  it('rejects invalid numeric size and impossible explicit bounds', () => {
    expect(() => render(<Window labels={allLabels} title="Bad" width={Number.NaN} />)).toThrow('invalid-size')
    expect(() => render(<Window labels={allLabels} dragBounds={{ left: 0, right: 100 }} title="Bad" />)).toThrow('invalid-bounds')
  })

  it('preserves numeric geometry under RTL', () => {
    render(<div dir="rtl"><Window labels={allLabels} initialLeft={40} initialTop={30} title="نافذة" /></div>)
    expect(screen.getByRole('dialog', { name: 'نافذة' })).toHaveStyle({ top: '30px', left: '40px' })
  })

  it('publishes reflow, logical, token, dark, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-window-surface')
    expect(css).toContain('max-inline-size')
    expect(css).toContain('max-block-size')
    expect(css).toContain('inset-inline-end')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-window")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion:[\s\S]*transition: none/)
  })
})
