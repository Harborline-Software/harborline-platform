import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Window, WindowActionsBar, type WindowState } from '../Window'
import { fixture, sharedCases } from './fixtures'

const labels = {
  window: 'Fenêtre', close: 'Fermer', minimize: 'Réduire', maximize: 'Agrandir', restore: 'Restaurer',
  move: 'Déplacer la fenêtre', moveDescription: 'Utilisez les flèches', resizeHeight: 'Redimensionner la hauteur', resizeWidth: 'Redimensionner la largeur',
}

describe('Window shared fixtures', () => {
  it('window.defaults', () => {
    fixture(sharedCases, 'window.defaults')
    render(<Window title="Editor">Body</Window>)
    const dialog = screen.getByRole('dialog', { name: 'Editor' })
    expect(dialog).toHaveAttribute('data-window-state', 'default')
    expect(dialog).toHaveStyle({ width: '400px', height: '300px' })
    expect(screen.getAllByRole('separator')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Minimize' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Maximize' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument()
    expect(dialog).not.toHaveAttribute('aria-modal')
  })

  it('window.actions', () => {
    fixture(sharedCases, 'window.actions')
    const { rerender } = render(<Window closable={false} maximizable={false} minimizable={false} title="Editor" />)
    expect(screen.queryAllByRole('button')).toHaveLength(0)
    rerender(<Window closable maximizable minimizable title="Editor" />)
    expect(screen.getAllByRole('button')).toHaveLength(3)
  })

  it('window.controlled-state', async () => {
    fixture(sharedCases, 'window.controlled-state')
    const onStateChange = vi.fn()
    render(<Window onStateChange={onStateChange} state="default" title="Editor" />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Maximize' }))
    expect(onStateChange).toHaveBeenCalledWith('maximized')
    expect(screen.getByRole('dialog')).toHaveAttribute('data-window-state', 'default')
  })

  it('window.uncontrolled-state', async () => {
    fixture(sharedCases, 'window.uncontrolled-state')
    const states: WindowState[] = []
    render(<Window onStateChange={state => states.push(state)} title="Editor" />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Minimize' }))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Restore' }))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Maximize' }))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Restore' }))
    expect(states).toEqual(['minimized', 'default', 'maximized', 'default'])
    expect(screen.getByRole('dialog')).toHaveAttribute('data-window-state', 'default')
  })

  it('window.minimized', () => {
    fixture(sharedCases, 'window.minimized')
    render(<Window modal state="minimized" title="Editor">Body</Window>)
    expect(screen.queryByText('Body')).toBeNull()
    expect(screen.queryByTestId('window-overlay')).toBeNull()
  })

  it('window.maximized', () => {
    fixture(sharedCases, 'window.maximized')
    render(<Window state="maximized" title="Editor" />)
    expect(screen.getByRole('dialog')).toHaveStyle({ inset: '0', width: '100vw', height: '100vh' })
    expect(screen.queryAllByRole('separator')).toHaveLength(0)
  })

  it('window.modal', async () => {
    fixture(sharedCases, 'window.modal')
    render(<Window labels={labels} modal title="Editor"><button type="button">Action</button></Window>)
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(screen.getByTestId('window-overlay')).toHaveAttribute('aria-hidden', 'true')
    const titleBar = screen.getByRole('group', { name: labels.move })
    titleBar.focus()
    await userEvent.setup().tab({ shift: true })
    expect(screen.getByRole('separator', { name: labels.resizeWidth })).toHaveFocus()
  })

  it('window.accessible-name', () => {
    fixture(sharedCases, 'window.accessible-name')
    render(<Window labels={labels} title={null} />)
    expect(screen.getByRole('dialog', { name: 'Fenêtre' })).toBeInTheDocument()
  })

  it('window.escape', () => {
    fixture(sharedCases, 'window.escape')
    const onClose = vi.fn()
    const onStateChange = vi.fn()
    render(<Window defaultState="maximized" onClose={onClose} onStateChange={onStateChange} title="Editor" />)
    const dialog = screen.getByRole('dialog')
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(onStateChange).toHaveBeenCalledWith('default')
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('window.focus', () => {
    fixture(sharedCases, 'window.focus')
    function Harness({ shown }: { shown: boolean }) {
      return <><button type="button">Prior</button>{shown ? <Window title="Editor" /> : null}</>
    }
    const rendered = render(<Harness shown={false} />)
    const prior = screen.getByRole('button', { name: 'Prior' })
    prior.focus()
    rendered.rerender(<Harness shown />)
    expect(screen.getByRole('dialog')).toHaveFocus()
    rendered.rerender(<Harness shown={false} />)
    expect(prior).toHaveFocus()
  })

  it('window.move', () => {
    fixture(sharedCases, 'window.move')
    const onMove = vi.fn()
    render(<Window onMove={onMove} title="Editor" />)
    fireEvent.pointerDown(screen.getByTestId('window-title-bar'), { pointerId: 3, clientX: 10, clientY: 10 })
    fireEvent.pointerMove(screen.getByRole('dialog'), { pointerId: 3, clientX: 30, clientY: 20 })
    expect(onMove).toHaveBeenCalledWith({ top: 90, left: 100 })
  })

  it('window.bounds', () => {
    fixture(sharedCases, 'window.bounds')
    const onMove = vi.fn()
    render(<Window dragBounds="viewport" onMove={onMove} title="Editor" />)
    fireEvent.keyDown(screen.getByTestId('window-title-bar'), { key: 'ArrowUp', shiftKey: true })
    fireEvent.keyDown(screen.getByTestId('window-title-bar'), { key: 'ArrowUp', shiftKey: true })
    expect(onMove).toHaveBeenLastCalledWith({ top: 0, left: 80 })
  })

  it('window.resize', () => {
    fixture(sharedCases, 'window.resize')
    const onResize = vi.fn()
    render(<Window onResize={onResize} title="Editor" />)
    const corner = screen.getByTestId('window-resize-corner')
    fireEvent.pointerDown(corner, { pointerId: 4, clientX: 400, clientY: 300 })
    fireEvent.pointerMove(screen.getByRole('dialog'), { pointerId: 4, clientX: 100, clientY: 50 })
    expect(onResize).toHaveBeenCalledWith({ width: 200, height: 100 })
  })

  it('window.keyboard', () => {
    fixture(sharedCases, 'window.keyboard')
    const onMove = vi.fn()
    const onResize = vi.fn()
    render(<Window onMove={onMove} onResize={onResize} title="Editor" />)
    fireEvent.keyDown(screen.getByTestId('window-title-bar'), { key: 'ArrowRight' })
    fireEvent.keyDown(screen.getByTestId('window-title-bar'), { key: 'ArrowDown', shiftKey: true })
    fireEvent.keyDown(screen.getByTestId('window-resize-width'), { key: 'ArrowRight' })
    fireEvent.keyDown(screen.getByTestId('window-resize-height'), { key: 'ArrowDown', shiftKey: true })
    expect(onMove.mock.calls.map(call => call[0])).toEqual([{ top: 80, left: 90 }, { top: 130, left: 90 }])
    expect(onResize.mock.calls.map(call => call[0])).toEqual([{ width: 410, height: 300 }, { width: 410, height: 350 }])
  })

  it('window.initial-aliases', () => {
    fixture(sharedCases, 'window.initial-aliases')
    render(<Window initialLeft={40} initialTop={30} left={20} title="Editor" top={10} />)
    expect(screen.getByRole('dialog')).toHaveStyle({ top: '30px', left: '40px' })
  })

  it('window.double-activation', () => {
    fixture(sharedCases, 'window.double-activation')
    const onStateChange = vi.fn()
    render(<Window onStateChange={onStateChange} title="Editor" />)
    fireEvent.doubleClick(screen.getByTestId('window-title-bar'))
    expect(onStateChange).toHaveBeenCalledWith('maximized')
  })

  it('window.actions-bar', () => {
    fixture(sharedCases, 'window.actions-bar')
    const onMove = vi.fn()
    render(<Window onMove={onMove} title="Editor"><WindowActionsBar><button type="button">Pin</button></WindowActionsBar></Window>)
    const buttons = screen.getAllByRole('button')
    expect(buttons[0]).toHaveAccessibleName('Pin')
    fireEvent.pointerDown(buttons[0], { pointerId: 8, clientX: 1, clientY: 1 })
    fireEvent.pointerMove(screen.getByRole('dialog'), { pointerId: 8, clientX: 30, clientY: 30 })
    expect(onMove).not.toHaveBeenCalled()
  })

  it('window.custom-actions', async () => {
    fixture(sharedCases, 'window.custom-actions')
    const Custom = (props: React.ButtonHTMLAttributes<HTMLButtonElement>) => <button data-custom="true" type="button" {...props} />
    render(<Window labels={labels} maximizeButton={Custom} minimizeButton={Custom} restoreButton={Custom} state="maximized" title="Editor" />)
    expect(screen.getByRole('button', { name: labels.restore })).toHaveAttribute('data-custom', 'true')
    expect(screen.getByRole('button', { name: labels.minimize })).toHaveAttribute('data-custom', 'true')
  })

  it('window.class-parity', () => {
    const declared = fixture(sharedCases, 'window.class-parity')
    const input = declared.input as { states: WindowState[], modal: boolean, resizable: boolean, draggable: boolean }
    const expected = declared.expected as {
      rootClasses: Record<string, string[]>
      resizeWidthClasses: string[]
      resizeHeightClasses: string[]
      resizeCornerClasses: string[]
      absentClasses: string[]
      inlinePosition: string
      inlineZIndex: string
    }

    for (const state of input.states) {
      const view = render(
        <Window draggable={input.draggable} labels={labels} modal={input.modal} resizable={input.resizable} state={state} title="Editor">Body</Window>,
      )
      const dialog = screen.getByRole('dialog')
      expect([...dialog.classList]).toEqual(expected.rootClasses[state])
      expect(dialog.style.position).toBe(expected.inlinePosition)
      expect(dialog.style.zIndex).toBe(expected.inlineZIndex)
      if (state === 'default') {
        expect([...screen.getByTestId('window-resize-width').classList]).toEqual(expected.resizeWidthClasses)
        expect([...screen.getByTestId('window-resize-height').classList]).toEqual(expected.resizeHeightClasses)
        expect([...screen.getByTestId('window-resize-corner').classList]).toEqual(expected.resizeCornerClasses)
      }
      // The Blazor lane used to wrap its portal in hl-window__portal and to spell the resize
      // handles --east/--south; neither lane emits a class the authority never defines.
      for (const absent of expected.absentClasses) {
        expect(document.querySelectorAll(`.${absent}`)).toHaveLength(0)
      }
      view.unmount()
    }
  })

  it('window.projection-equivalence', () => {
    fixture(sharedCases, 'window.projection-equivalence')
    const onStateChange = vi.fn()
    const onMove = vi.fn()
    render(<Window labels={labels} modal onMove={onMove} onStateChange={onStateChange} title="Editor" />)
    fireEvent.keyDown(screen.getByTestId('window-title-bar'), { key: 'ArrowRight' })
    fireEvent.click(screen.getByRole('button', { name: labels.maximize }))
    expect(onMove).toHaveBeenCalledWith({ top: 80, left: 90 })
    expect(onStateChange).toHaveBeenCalledWith('maximized')
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Editor')
  })
})
