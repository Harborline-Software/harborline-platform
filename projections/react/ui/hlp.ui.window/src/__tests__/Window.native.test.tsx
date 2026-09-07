import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Window } from '../Window'
import * as publicSurface from '../index'
import { qualityCases } from './fixtures'

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

  it('uses caller-localized names for built-in controls and fallback', () => {
    render(<Window labels={{ window: 'Ventana', close: 'Cerrar', minimize: 'Minimizar', maximize: 'Maximizar' }} />)
    expect(screen.getByRole('dialog', { name: 'Ventana' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cerrar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Minimizar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Maximizar' })).toBeInTheDocument()
  })

  it('cycles modal focus in both directions and restores prior focus on removal', async () => {
    function Harness({ shown }: { shown: boolean }) {
      return <><button type="button">Prior</button>{shown ? <Window modal title="Editor"><button type="button">Body action</button></Window> : null}</>
    }
    const rendered = render(<Harness shown={false} />)
    const prior = screen.getByRole('button', { name: 'Prior' })
    prior.focus()
    rendered.rerender(<Harness shown />)
    prior.focus()
    expect(screen.getByRole('dialog')).toHaveFocus()
    const titleBar = screen.getByRole('group', { name: 'Move window' })
    titleBar.focus()
    await userEvent.setup().tab({ shift: true })
    expect(screen.getByRole('separator', { name: 'Resize width from right edge' })).toHaveFocus()
    await userEvent.setup().tab()
    expect(titleBar).toHaveFocus()
    rendered.rerender(<Harness shown={false} />)
    expect(prior).toHaveFocus()
  })

  it('clears pointer interaction on cancellation without stale callbacks', () => {
    const onMove = vi.fn()
    render(<Window onMove={onMove} title="Editor" />)
    const titleBar = screen.getByTestId('window-title-bar')
    const dialog = screen.getByRole('dialog')
    fireEvent.pointerDown(titleBar, { pointerId: 9, clientX: 10, clientY: 10 })
    fireEvent.pointerCancel(dialog, { pointerId: 9 })
    fireEvent.pointerMove(dialog, { pointerId: 9, clientX: 50, clientY: 50 })
    expect(onMove).not.toHaveBeenCalled()
  })

  it('rejects invalid numeric size and impossible explicit bounds', () => {
    expect(() => render(<Window title="Bad" width={Number.NaN} />)).toThrow('invalid-size')
    expect(() => render(<Window dragBounds={{ left: 0, right: 100 }} title="Bad" />)).toThrow('invalid-bounds')
  })

  it('preserves numeric geometry under RTL', () => {
    render(<div dir="rtl"><Window initialLeft={40} initialTop={30} title="نافذة" /></div>)
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
