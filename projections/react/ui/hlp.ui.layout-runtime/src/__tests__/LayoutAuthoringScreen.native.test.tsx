import { render, screen } from '@testing-library/react'
import { AppLayout } from '@harborline-platform/hlp.ui.app-layout'
import { emptyLayoutAuthoringDraft } from '../LayoutAuthoringEditor'
import { LayoutAuthoringScreen } from '../LayoutAuthoringScreen'

describe('Layout authoring screen shell', () => {
  // Oracle: DES-0052:183,533 and AppLayout.tsx:112,117; expectations are literals.
  // Named mutation: RX-app-layout-second-main wraps the editor in another main.
  it('layout-eng-11: the real shell keeps one main around the editor with main or page scrolling', () => {
    const editor = { value: emptyLayoutAuthoringDraft(), catalogue: { blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }, onChange: vi.fn() }
    const { rerender } = render(<LayoutAuthoringScreen {...editor} shell={{ render: body => <AppLayout body={body} railCapable={false} /> }} />)

    expect(screen.getAllByRole('main')).toHaveLength(1)
    expect(screen.getByRole('main').querySelector('form.hl-layout-authoring')).toBeInTheDocument()
    expect(screen.getByRole('main').closest('.hl-app-layout')).toHaveAttribute('data-content-scroll', 'main')

    rerender(<LayoutAuthoringScreen {...editor} shell={{ render: body => <AppLayout body={body} contentScroll="page" railCapable={false} /> }} />)

    expect(screen.getAllByRole('main')).toHaveLength(1)
    expect(screen.getByRole('main').querySelector('form.hl-layout-authoring')).toBeInTheDocument()
    expect(screen.getByRole('main').closest('.hl-app-layout')).toHaveAttribute('data-content-scroll', 'page')
  })

  // Oracle: BookingAuthoringEditor.test.tsx's booking-eng-20 host-shell probe and DES-0052:183.
  // Named mutation: RX-host-second-main wraps the editor in another main inside the host.
  it('layout-eng-11: the editor uses the plain host main without adding its own', () => {
    render(<LayoutAuthoringScreen value={emptyLayoutAuthoringDraft()} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={vi.fn()} shell={{ render: body => <main aria-label="Host shell">{body}</main> }} />)

    expect(screen.getAllByRole('main')).toHaveLength(1)
    expect(screen.getByRole('main', { name: 'Host shell' }).querySelector('form.hl-layout-authoring')).toBeInTheDocument()
  })
})
