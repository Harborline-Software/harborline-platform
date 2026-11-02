import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Dialog } from '../Dialog'
import { qualityCases } from './fixtures'

describe('Dialog React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'dialog.quality.modal-name',
      'dialog.quality.reflow',
      'dialog.quality.catalog',
      'dialog.quality.pseudo',
      'dialog.quality.light-dark',
      'dialog.quality.tokens',
      'dialog.quality.forced-colors',
      'dialog.quality.visual-parity',
      'dialog.quality.keyboard',
      'dialog.quality.focus',
      'dialog.quality.dismissal',
      'dialog.quality.rtl',
      'dialog.quality.reduced-motion',
    ])
  })

  it('uses catalog copy and logical RTL layout without clipping long pseudo-localized content', () => {
    const long = '[!! A deliberately expanded pseudo-localized value that must wrap safely !!]'
    render(
      <HarborlineLocaleProvider catalog={{ 'common.close': `[!! Close expanded !!]` }} direction="rtl" locale="ar-SA">
        <Dialog description={long} footer={<button type="button">{long}</button>} onOpenChange={vi.fn()} open theme="light" title={long}>
          <p>{long}</p>
        </Dialog>
      </HarborlineLocaleProvider>,
    )
    const dialog = screen.getByRole('dialog', { name: long })
    expect(dialog).toHaveAttribute('dir', 'rtl')
    expect(dialog.closest('.hl-dialog__portal')).toHaveAttribute('data-theme', 'light')
    expect(screen.getByRole('button', { name: '[!! Close expanded !!]' })).toBeInTheDocument()
  })

  it('publishes semantic theme, reflow, forced-color, focus, and reduced-motion CSS', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-dialog-surface')
    expect(css).toContain('--hl-dialog-overlay')
    expect(css).toContain('--hl-dialog-focus')
    expect(css).toContain('inline-size: calc(100dvw - 2rem)')
    expect(css).toContain('max-block-size')
    expect(css).toContain('padding-inline')
    expect(css).toContain('.hl-dialog__footer button')
    expect(css).toContain("[data-theme='dark'] .hl-dialog__surface")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/animation:\s*none/)
    expect(css).toMatch(/transition:\s*none/)
  })

  // T-1122: the translate is physical, so the start edge must be too, or right-to-left content
  // moves the surface off the left edge. This deliberately pins the chosen implementation and the
  // byte identity of the copies; the behaviour itself is proved by gallery/tests/tests/dialog-rtl.spec.ts.
  it('centres the surface with a physical left edge in the authority and both lane stylesheets', () => {
    const root = resolve(import.meta.dirname, '../../../../../..')
    const [authority, react, blazor] = [
      'specs/modules/ui/hlp.ui.dialog/style.css',
      'projections/react/ui/hlp.ui.dialog/src/style.css',
      'projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.css',
    ].map(path => readFileSync(resolve(root, path), 'utf8'))
    expect(react).toBe(authority)
    expect(blazor).toBe(authority)
    const surface = /\n\.hl-dialog__surface \{([^}]*)\}/.exec(authority)?.[1] ?? ''
    expect(surface).toMatch(/\n {2}left: 50%;/)
    expect(surface).toContain('transform: translate(-50%, -50%)')
    expect(surface).not.toMatch(/\n {2}inset-inline-start:/)
    const enter = /@keyframes hl-dialog-enter \{([^}]*\}[^}]*\})/.exec(authority)?.[1] ?? ''
    expect(enter).toContain('translate(-50%, calc(-50% + 0.5rem))')
    expect(enter).toContain('translate(-50%, -50%)')
  })
})
