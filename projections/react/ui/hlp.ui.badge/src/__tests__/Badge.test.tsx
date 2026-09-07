import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Badge } from '../Badge'

describe('Badge revision-1', () => {
  it('projects independent style axes, overlay, icons, and live region', () => {
    render(<Badge themeColor="success" fillMode="outline" rounded="full" size="sm" align={{ horizontal: 'end', vertical: 'top' }} position="edge" cutoutBorder announceChanges leadingIcon="L" trailingIcon="T" className="consumer">3 updates</Badge>)
    const root = screen.getByText('3 updates').closest('.hl-badge')!
    expect(root).toHaveClass('hl-badge--success', 'hl-badge--outline', 'hl-badge--rounded-full', 'hl-badge--top-end-edge', 'hl-badge--cutout', 'consumer')
    expect(screen.getByText('3 updates')).toHaveAttribute('aria-live', 'polite')
    expect(root.querySelectorAll('[aria-hidden=true]')).toHaveLength(2)
  })
})
