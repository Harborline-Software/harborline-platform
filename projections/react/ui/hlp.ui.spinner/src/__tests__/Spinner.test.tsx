import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Spinner } from '../Spinner'

describe('Spinner revision-1', () => {
  it('preserves labels, sizes, types, themes, and host attributes', () => {
    const { rerender } = render(<Spinner size="xs" type="ring" themeColor="success" data-case="spinner" />)
    const ring = screen.getByRole('status', { name: 'Loading…' })
    expect(ring).toHaveClass('hl-spinner--xs', 'hl-spinner--ring', 'hl-spinner--success')
    expect(ring.querySelectorAll('.hl-spinner__arc')).toHaveLength(1)
    rerender(<Spinner label="Importing" size="lg" type="converging" />)
    const converging = screen.getByRole('status', { name: 'Importing' })
    expect(converging).toHaveClass('hl-spinner--lg', 'hl-spinner--converging')
    expect(converging.querySelectorAll('.hl-spinner__arc')).toHaveLength(2)
  })

  // Both arcs are <path> elements in a 24x24 view-box, and both of these were shipped wrong once.
  it('turns both converging arcs about the ring, and in opposite directions', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')

    // Without transform-box, `transform-origin: center` is the centre of each path's OWN bounding
    // box -- (17,7) for the leading arc, (7,17) for the trailing one, never the ring's (12,12). The
    // two then orbit different points and the pair morphs instead of spinning.
    expect(css).toMatch(/\.hl-spinner__arc\{[^}]*transform-box:\s*view-box/)

    // And the reverse arc must actually reverse. It carries both classes, so a bare
    // `.hl-spinner__arc--reverse` at (0,1,0) loses the animation shorthand to the (0,3,0) rule above
    // it and both arcs turn the same way -- a converging spinner that never converges.
    expect(css).toMatch(/\[data-hl-type='converging'\]\s*\.hl-spinner__arc--reverse\{animation-name:hl-spinner-reverse\}/)
  })
})
