import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Page } from '../Page'

describe('Page React projection', () => {
  it('renders header without introducing main and keeps body keyboard focusable', () => {
    const { container } = render(<Page title="Health" subtitle="Observe" actions={<button>Refresh</button>}><p>Route body</p></Page>)
    expect(screen.getByRole('heading', { level: 1, name: 'Health' })).toBeInTheDocument()
    expect(container.querySelector('main')).toBeNull()
    expect(container.querySelector('header')).toBeInTheDocument()
    expect(container.querySelector('section')).toHaveAttribute('tabindex', '0')
  })

  it('rejects blank titles and unsupported options', () => {
    expect(() => render(<Page title=" "/>)).toThrow('page-title-required')
    expect(() => render(<Page title="Valid" bodyPadding={'xxl' as 'md'}/>)).toThrow('unsupported-page-option')
  })
})
