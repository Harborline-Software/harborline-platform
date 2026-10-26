import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider, type HarborlineNumberFormatter } from '@harborline-platform/hlp.ui.locale-provider'
import { GuardedControl } from '../GuardedControl'

// Interface revision 2 (T-1105 item 1, owner ruling 2026-10-26, shape A). The visible action label is
// the accessible name in every state; the guard instruction is a localized whole message that is the
// action's accessible description, after the caller's describedBy ids, and is rendered visibly. The
// description holds still while the countdown ticks, and the live region announces the armed message
// once per transition.
const isolated = (text: string) => `⁨${text}⁩`
const covered = (label: string) => `Guarded action: ${isolated(label)}. Activate to arm.`
const armed = (label: string) => `Armed for ${isolated(label)}. Activate again within 5 seconds to commit.`
const committing = (label: string) => `Committing ${isolated(label)}.`
const action = () => screen.getByRole('button')
const instruction = () => document.querySelector<HTMLElement>('.hl-guarded-control__instruction')
const pending = () => new Promise<void>(() => undefined)

afterEach(() => vi.useRealTimers())

describe('GuardedControl revision 2 names and descriptions', () => {
  it('names the action by its visible label in the covered, armed and committing states', () => {
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={pending}/>)
    expect(action()).toHaveAccessibleName('Publish')
    fireEvent.click(action())
    expect(action()).toHaveAttribute('data-guard-state', 'armed')
    expect(action()).toHaveAccessibleName('Publish')
    expect(screen.getByRole('timer')).toHaveTextContent('5 seconds')
    fireEvent.click(action())
    expect(action()).toHaveAttribute('data-guard-state', 'committing')
    expect(action()).toHaveAccessibleName('Publish')
  })

  it('describes each state with its whole message, after the caller describedBy ids', () => {
    render(<><p id="note">Signing records your name.</p><GuardedControl action="a" classificationId="c" label="Publish" describedBy={['note']} onCommit={pending}/></>)
    expect(action()).toHaveAccessibleDescription(`Signing records your name. ${covered('Publish')}`)
    expect(action().getAttribute('aria-describedby')).toBe(`note ${instruction()?.id}`)
    fireEvent.click(action())
    expect(action()).toHaveAccessibleDescription(`Signing records your name. ${armed('Publish')}`)
    fireEvent.click(action())
    expect(action()).toHaveAccessibleDescription(`Signing records your name. ${committing('Publish')}`)
  })

  it('describes the disabled covered action with the caller denial and the covered instruction', () => {
    render(<><p id="denial">Complete the affirmation first.</p><GuardedControl action="a" classificationId="c" label="Delete" disabled describedBy="denial" onCommit={pending}/></>)
    expect(action()).toBeDisabled()
    expect(action()).toHaveAccessibleName('Delete')
    expect(action()).toHaveAccessibleDescription(`Complete the affirmation first. ${covered('Delete')}`)
  })

  it('renders the instruction visibly as the element the description references', () => {
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={pending}/>)
    const element = instruction()
    expect(element).not.toBeNull()
    expect(element!.textContent).toBe(covered('Publish'))
    expect(element!.closest('[aria-hidden="true"], [hidden], .hl-visually-hidden')).toBeNull()
    expect(action().getAttribute('aria-describedby')).toBe(element!.id)
    fireEvent.click(action())
    expect(instruction()!.textContent).toBe(armed('Publish'))
  })

  it('keeps the armed name and description still while the countdown ticks', () => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    fireEvent.click(action())
    for (const timer of ['5 seconds', '4 seconds', '3 seconds', '2 seconds', '1 second']) {
      expect(screen.getByRole('timer')).toHaveTextContent(timer)
      expect(action()).toHaveAccessibleName('Publish')
      expect(action()).toHaveAccessibleDescription(armed('Publish'))
      expect(instruction()!.textContent).toBe(armed('Publish'))
      act(() => vi.advanceTimersByTime(1000))
    }
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveAccessibleDescription(covered('Publish'))
  })

  it('announces the armed message once per transition and never the countdown', () => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    const status = screen.getByRole('status')
    const observer = new MutationObserver(() => undefined)
    observer.observe(status, { childList: true, characterData: true, subtree: true })
    fireEvent.click(action())
    expect(observer.takeRecords().length).toBeGreaterThan(0)
    expect(status.textContent).toBe(armed('Publish'))
    act(() => vi.advanceTimersByTime(4000))
    expect(screen.getByRole('timer')).toHaveTextContent('1 second')
    expect(observer.takeRecords()).toEqual([])
    act(() => vi.advanceTimersByTime(1000))
    expect(observer.takeRecords().length).toBeGreaterThan(0)
    expect(status.textContent).toBe(`Guard restored for ${isolated('Publish')}.`)
    fireEvent.click(action())
    expect(observer.takeRecords().length).toBeGreaterThan(0)
    expect(status.textContent).toBe(armed('Publish'))
    observer.disconnect()
  })

  it('localizes the descriptions through a host catalog while the name stays the label', () => {
    render(
      <HarborlineLocaleProvider locale="fr-FR" catalog={{
        'chrome.guard.covered.instruction': 'Action protégée : {action}. Activez pour armer.',
        'chrome.guard.armed.whole': 'Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.',
        'chrome.guard.committing.status': 'Validation de {action}.',
      }}>
        <GuardedControl action="a" classificationId="c" label="Publier" onCommit={pending}/>
      </HarborlineLocaleProvider>,
    )
    expect(action()).toHaveAccessibleName('Publier')
    expect(action()).toHaveAccessibleDescription(`Action protégée : ${isolated('Publier')}. Activez pour armer.`)
    fireEvent.click(action())
    expect(action()).toHaveAccessibleName('Publier')
    // The duration is the locale's CLDR unit pattern in this lane (T-1105's deferred duration parity).
    expect(action()).toHaveAccessibleDescription(`Armé pour ${isolated('Publier')}. Activez de nouveau dans un délai de 5 secondes pour valider.`)
    fireEvent.click(action())
    expect(action()).toHaveAccessibleDescription(`Validation de ${isolated('Publier')}.`)
  })

  it('isolates the label inside right-to-left Arabic descriptions', () => {
    const arab: HarborlineNumberFormatter = (value, { locale, options }) => new Intl.NumberFormat(locale, { ...options, numberingSystem: 'arab' }).format(value)
    render(
      <HarborlineLocaleProvider locale="ar-SA" numberFormatter={arab} catalog={{
        'chrome.guard.covered.instruction': 'إجراء محمي: {action}. فعّل للتجهيز.',
        'chrome.guard.armed.whole': 'تم تجهيز {action}. فعّل مرة أخرى خلال {duration} للتأكيد.',
      }}>
        <GuardedControl action="a" classificationId="c" label="تصدير" onCommit={pending}/>
      </HarborlineLocaleProvider>,
    )
    expect(action().closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(action()).toHaveAccessibleName('تصدير')
    expect(action()).toHaveAccessibleDescription(`إجراء محمي: ${isolated('تصدير')}. فعّل للتجهيز.`)
    fireEvent.click(action())
    expect(action()).toHaveAccessibleName('تصدير')
    expect(action()).toHaveAccessibleDescription(`تم تجهيز ${isolated('تصدير')}. فعّل مرة أخرى خلال ٥ ثوانٍ للتأكيد.`)
  })

  it('localizes the descriptions in the pseudo-locale', () => {
    const label = '⟦ Šîĝñ åñđ šåṽë ······ ⟧'
    render(
      <HarborlineLocaleProvider locale="en-XA" catalog={{ 'chrome.guard.covered.instruction': '⟦ Ĝüåŕðéð åçţîöñ: {action}. Åçţîṽåţé ţö åŕɱ. ········ ⟧' }}>
        <GuardedControl action="a" classificationId="c" label={label} onCommit={pending}/>
      </HarborlineLocaleProvider>,
    )
    expect(action()).toHaveAccessibleName(label)
    expect(action()).toHaveAccessibleDescription(`⟦ Ĝüåŕðéð åçţîöñ: ${isolated(label)}. Åçţîṽåţé ţö åŕɱ. ········ ⟧`)
    expect(instruction()!.textContent).toBe(`⟦ Ĝüåŕðéð åçţîöñ: ${isolated(label)}. Åçţîṽåţé ţö åŕɱ. ········ ⟧`)
  })
})
