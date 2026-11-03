import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

const items: NotificationCenterItem[] = [
  { id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal', read: false },
  { id: 'r1', title: 'Export ready', timestamp: 'Earlier', kind: 'result', read: false },
]
describe('NotificationCenter React projection', () => {
  it('shows bounded badge, opens a dialog, and invokes one proposal action before close', () => {
    const confirm = vi.fn()
    render(<NotificationCenter items={items} onConfirm={confirm}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    expect(screen.getByRole('dialog', { name: 'Notifications' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Confirm: Approve export' }))
    expect(confirm).toHaveBeenCalledOnce()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('filters without reordering and keeps informational actions open', () => {
    const action = vi.fn()
    render(<NotificationCenter items={items} onAction={action}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    fireEvent.click(screen.getByRole('tab', { name: 'Result' }))
    expect(screen.queryByText('Approve export')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Export ready' }))
    expect(action).toHaveBeenCalledWith(items[1])
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  // b19: the unread marker is a labelled graphic, as in the Blazor lane. aria-label on a role-less
  // span is prohibited (axe aria-prohibited-attr) and is not announced, found once the gallery scan
  // reached the portaled popover.
  it('exposes each unread marker as a named image', () => {
    render(<NotificationCenter items={items}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    expect(screen.getAllByRole('img', { name: 'Unread' })).toHaveLength(2)
  })

  it('uses an authored icon for item dismissal', () => {
    render(<NotificationCenter items={items} onDismiss={() => {}}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    const dismiss = screen.getByRole('button', { name: 'Dismiss: Export ready' })
    expect(dismiss.querySelector('svg')).toBeInTheDocument()
    expect(dismiss).not.toHaveTextContent('×')
  })

  // b20: WCAG 2.2 SC 1.4.11 Non-text Contrast. With no host token reaching them (a portaled panel
  // leaves the host's themed subtree), the unread marker (role="img") and the focus ring resolve to
  // the module's own fallbacks. Those must reach 3:1 against Canvas in light and dark. Canvas and
  // CanvasText take the common UA values; host tokens (anything the module does not define) are absent.
  it.each([
    ['light', { Canvas: [255, 255, 255], CanvasText: [0, 0, 0] }],
    ['dark', { Canvas: [18, 18, 18], CanvasText: [255, 255, 255] }],
  ] as const)('unread marker, badge and focus ring meet non-text and text contrast without host tokens (%s)', (scheme, system) => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8').replace(/@media\s*\(forced-colors:active\)\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}/g, '')
    const block = scheme === 'dark'
      ? css.match(/\[data-theme='dark'\]\.hl-notification-center__dialog,\[data-theme='dark'\] \.hl-notification-center__dialog\{([^}]*)\}/)?.[1]
      : css.match(/\.hl-notification-center__dialog\{(--hl-notification-surface[^}]*)\}/)?.[1]
    expect(block).toBeTruthy()
    const local = new Map([...(block as string).matchAll(/(--hl-[a-z-]+):((?:[^;()]|\((?:[^()]|\([^()]*\))*\))+)/g)].map(match => [match[1], match[2]]))
    // The last declaration of the property among the rules naming the selector (the cascade's winner).
    const declaration = (selector: string, property: string) => css.split('}')
      .filter(rule => rule.split('{')[0].split(',').map(part => part.trim()).includes(selector))
      .map(rule => rule.split('{')[1]?.match(new RegExp(`(?:^|;)${property}:([^;]+)`))?.[1])
      .filter(Boolean).at(-1)
    const colour = (value: string): number[] => {
      const text = value.trim()
      if (text in system) return [...system[text as keyof typeof system]]
      const variable = text.match(/^var\((--hl-[a-z-]+)(?:,(.*))?\)$/)
      if (variable) return colour(local.get(variable[1]) ?? variable[2] ?? 'unresolved')
      const mix = text.match(/^color-mix\(in srgb,(\w+) (\d+)%,(\w+)\)$/)
      if (mix) {
        const [a, b, weight] = [colour(mix[1]), colour(mix[3]), Number(mix[2]) / 100]
        return a.map((channel, index) => channel * weight + b[index] * (1 - weight))
      }
      throw new Error(`${text} is not resolved (${scheme}): outside forced-colors Highlight and Mark are UA background colours (Chrome light Highlight #b3d7fe reads 1.49:1 on Canvas), so no indicator may end there`)
    }
    const luminance = (rgb: number[]) => rgb.map(channel => channel / 255)
      .map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
      .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0)
    const contrast = (value: string) => {
      const [x, y] = [luminance(colour(value)), luminance(system.Canvas)]
      return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)
    }
    const unread = declaration('.hl-notification-center__unread', 'background') as string
    const focus = (declaration('.hl-notification-center__dialog button:focus-visible', 'outline') as string).replace(/^2px solid /, '')
    expect(contrast(unread)).toBeGreaterThanOrEqual(3)
    expect(contrast(focus)).toBeGreaterThanOrEqual(3)
    // b20 review: the trigger badge is an indicator (3:1 against Canvas) that carries text (4.5:1
    // against its own fill). It fell to Mark with #fff text before, about 1.07:1.
    const badgeFill = declaration('.hl-notification-center__badge', 'background') as string
    const badgeText = declaration('.hl-notification-center__badge', 'color') as string
    expect(contrast(badgeFill)).toBeGreaterThanOrEqual(3)
    const [fill, text] = [luminance(colour(badgeFill)), luminance(colour(badgeText))]
    expect((Math.max(fill, text) + 0.05) / (Math.min(fill, text) + 0.05)).toBeGreaterThanOrEqual(4.5)
  })
})
