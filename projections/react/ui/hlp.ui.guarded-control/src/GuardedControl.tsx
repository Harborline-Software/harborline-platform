import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useHarborlineLocale, useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { useMediaQuery } from '@harborline-platform/hlp.ui.use-media-query'

export type GuardedControlState = 'covered' | 'armed' | 'committing'
export type GuardRecoveryReason = 'escape' | 'timeout' | 'navigation' | 'document-hidden' | 'window-blur' | 'became-disabled'
export type GuardedControlEvent =
  | { type: 'transition'; previous: GuardedControlState; next: GuardedControlState }
  | { type: 'recovered'; reason: GuardRecoveryReason }
  | { type: 'commit-settled'; outcome: 'completed' | 'rejected' }

export interface GuardedControlProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'action' | 'onError'> {
  action: string
  classificationId: string
  label: string
  onCommit: () => void | Promise<void>
  disabled?: boolean
  describedBy?: string | readonly string[]
  observe?: (event: GuardedControlEvent) => void
}

const ARM_WINDOW_MS = 5000
const ARM_WINDOW_SECONDS = ARM_WINDOW_MS / 1000
const BLUR_GRACE_MS = 800

function descriptionIds(value: GuardedControlProps['describedBy']): string | undefined {
  if (typeof value === 'string') return value || undefined
  return value?.filter(Boolean).join(' ') || undefined
}

// Interface revision 2. The name is the visible label (aria-labelledby the label span, so the
// role=timer countdown never joins it). The description is the caller's describedBy ids, then the
// module's whole message for the state, which is also rendered visibly: covered and armed
// instructions and the committing status. The keys keep their revision-1 names
// (chrome.guard.covered.label, chrome.guard.committing.label) so host catalogs keep working; they
// hold descriptions now, never the name.
const COVERED_INSTRUCTION = 'Guarded action: {action}. Activate to arm.'
const ARMED_INSTRUCTION = 'Armed for {action}. Activate again within {duration} to commit.'
const COMMITTING_STATUS = 'Committing {action}.'

export function GuardedControl({
  action, classificationId, label, onCommit, disabled = false, describedBy, observe,
  className, ...hostAttributes
}: GuardedControlProps) {
  const invalid = !action.trim() || !classificationId.trim() || !label.trim()
  const { locale, direction, formatNumber, t } = useHarborlineStrings()
  const { catalog } = useHarborlineLocale()
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)')
  const [state, setState] = React.useState<GuardedControlState>('covered')
  const [seconds, setSeconds] = React.useState(ARM_WINDOW_SECONDS)
  const [announcement, setAnnouncement] = React.useState('')
  const stateRef = React.useRef(state)
  const mounted = React.useRef(true)
  const committed = React.useRef(false)
  const deadline = React.useRef(0)
  const coverRef = React.useRef<HTMLButtonElement>(null)
  const armedRef = React.useRef<HTMLButtonElement>(null)
  const deferredFocus = React.useRef(false)
  const id = React.useId()
  const labelId = `${id}-label`
  const instructionId = `${id}-instruction`
  const isolatedLabel = `\u2068${label}\u2069`

  const wholeMessage = React.useCallback((key: string, fallback: string, variables: Record<string, string>) => {
    let message = catalog[key] ?? fallback
    for (const [name, value] of Object.entries(variables)) message = message.replaceAll(`{${name}}`, value)
    return message
  }, [catalog])

  const transition = React.useCallback((next: GuardedControlState) => {
    const previous = stateRef.current
    if (previous === next) return false
    stateRef.current = next
    setState(next)
    observe?.({ type: 'transition', previous, next })
    return true
  }, [observe])

  const restoreFocus = React.useCallback(() => {
    if (!mounted.current) return
    if (document.visibilityState === 'hidden' || !document.hasFocus()) {
      deferredFocus.current = true
      return
    }
    queueMicrotask(() => mounted.current && coverRef.current?.focus())
  }, [])

  const recover = React.useCallback((reason: GuardRecoveryReason) => {
    if (stateRef.current !== 'armed' || !transition('covered')) return
    setAnnouncement(wholeMessage('chrome.guard.recovered.whole', 'Guard restored for {action}.', { action: `\u2068${label}\u2069` }))
    restoreFocus()
    observe?.({ type: 'recovered', reason })
  }, [label, observe, restoreFocus, transition, wholeMessage])

  React.useEffect(() => {
    stateRef.current = state
  }, [state])
  React.useEffect(() => () => { mounted.current = false }, [])
  React.useEffect(() => {
    if (!disabled || stateRef.current !== 'armed') return
    recover('became-disabled')
  }, [disabled, recover])

  React.useEffect(() => {
    if (state !== 'armed') return
    deadline.current = Date.now() + ARM_WINDOW_MS
    const tick = window.setInterval(() => {
      const next = Math.max(0, Math.ceil((deadline.current - Date.now()) / 1000))
      setSeconds(current => current === next ? current : next)
      if (next === 0) recover('timeout')
    }, 250)
    const navigate = () => recover('navigation')
    const visibility = () => document.visibilityState === 'hidden' && recover('document-hidden')
    let blurTimer = 0
    const blur = () => { blurTimer = window.setTimeout(() => recover('window-blur'), BLUR_GRACE_MS) }
    const focus = () => {
      window.clearTimeout(blurTimer)
      if (deferredFocus.current && stateRef.current === 'covered') {
        deferredFocus.current = false
        coverRef.current?.focus()
      }
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        recover('escape')
      }
    }
    window.addEventListener('popstate', navigate)
    window.addEventListener('blur', blur)
    window.addEventListener('focus', focus)
    document.addEventListener('visibilitychange', visibility)
    document.addEventListener('keydown', escape)
    return () => {
      window.clearInterval(tick)
      window.clearTimeout(blurTimer)
      window.removeEventListener('popstate', navigate)
      window.removeEventListener('blur', blur)
      window.removeEventListener('focus', focus)
      document.removeEventListener('visibilitychange', visibility)
      document.removeEventListener('keydown', escape)
    }
  }, [recover, state])

  React.useLayoutEffect(() => {
    if (state === 'armed') armedRef.current?.focus()
  }, [state])

  // An amount of time ("5 seconds"), which the armed instruction places after "within"; never a
  // relative time ("in 5 seconds"). The locale's CLDR unit pattern supplies the unit word, its plural
  // form and its placement, as Intl.RelativeTimeFormat did before; the number in it is the locale
  // provider's formatNumber, called with no options so a host formatter that writes only the number
  // cannot drop the unit. Without a host formatter that is the same Intl number. A form with no number
  // in it (Arabic "ثانيتان", 2 seconds) asks for none. The Blazor lane resolves its catalog's
  // chrome.guard.duration forms instead; one duration source for both lanes is T-1105's deferred
  // duration-parity work.
  const formatDuration = (count: number) => {
    try {
      let number: string | undefined
      return new Intl.NumberFormat(locale, { style: 'unit', unit: 'second', unitDisplay: 'long' })
        .formatToParts(count)
        .map(({ type, value }) => {
          if (type === 'unit' || type === 'literal') return value
          // The number's parts (integer, group, decimal, fraction, sign) become one host number.
          if (number !== undefined) return ''
          number = formatNumber(count)
          return number
        })
        .join('')
    } catch {
      return `${formatNumber(count)} ${count === 1 ? 'second' : 'seconds'}`
    }
  }

  // The armed instruction always states the full window, never the countdown: it is announced once
  // on arming and is the armed description for the whole armed state, so it never changes with a tick.
  const armedInstruction = () => wholeMessage('chrome.guard.armed.whole', ARMED_INSTRUCTION, { action: isolatedLabel, duration: formatDuration(ARM_WINDOW_SECONDS) })

  // Every arm states the full window, never the countdown a previous attempt left behind.
  const arm = () => {
    if (disabled || invalid || stateRef.current !== 'covered') return
    committed.current = false
    setSeconds(ARM_WINDOW_SECONDS)
    transition('armed')
    setAnnouncement(armedInstruction())
  }
  const commit = () => {
    if (disabled || invalid || stateRef.current !== 'armed' || committed.current) return
    committed.current = true
    transition('committing')
    let result: void | Promise<void>
    try {
      result = onCommit()
    } catch (error) {
      settle('rejected')
      window.setTimeout(() => { throw error }, 0)
      return
    }
    void Promise.resolve(result).then(
      () => settle('completed'),
      error => {
        settle('rejected')
        window.setTimeout(() => { throw error }, 0)
      },
    )
  }
  const settle = (outcome: 'completed' | 'rejected') => {
    if (!mounted.current || stateRef.current !== 'committing') return
    transition('covered')
    setAnnouncement(outcome === 'rejected'
      ? wholeMessage('chrome.guard.rejected.whole', 'Could not complete {action}. Guard restored.', { action: `\u2068${label}\u2069` })
      : wholeMessage('chrome.guard.completed.whole', 'Guard restored for {action}.', { action: `\u2068${label}\u2069` }))
    restoreFocus()
    observe?.({ type: 'commit-settled', outcome })
  }

  const duration = formatDuration(seconds)
  const inert = disabled || invalid
  const instruction = state === 'covered'
    ? wholeMessage('chrome.guard.covered.label', COVERED_INSTRUCTION, { action: isolatedLabel })
    : state === 'armed'
      ? armedInstruction()
      : wholeMessage('chrome.guard.committing.label', COMMITTING_STATUS, { action: isolatedLabel })
  const ids = [descriptionIds(describedBy), instructionId].filter(Boolean).join(' ')
  return (
    <div
      {...hostAttributes}
      dir={direction}
      className={cn('hl-guarded-control', className)}
      data-guard-action={action}
      data-guard-classification={classificationId}
      data-hl-motion={reducedMotion ? 'reduced' : 'full'}
    >
      <span role="status" aria-live="polite" aria-atomic="true" className="hl-visually-hidden">{announcement}</span>
      {state === 'covered' ? (
        <button ref={coverRef} type="button" disabled={inert} aria-labelledby={labelId} aria-describedby={ids} data-guard-state="covered" className="hl-guarded-control__action" onClick={arm}>
          <span aria-hidden="true">▣</span><span id={labelId}>{label}</span>
        </button>
      ) : (
        <button ref={armedRef} type="button" disabled={state === 'committing' || inert} aria-labelledby={labelId} aria-describedby={ids} data-guard-state={state} className="hl-guarded-control__action hl-guarded-control__action--armed" onClick={commit}>
          <span aria-hidden="true">□</span><span id={labelId}>{label}</span>
          {state === 'armed' ? <span role="timer" aria-label={duration} className="hl-guarded-control__timer">{duration}</span> : <span>{t('common.loading')}</span>}
        </button>
      )}
      <span id={instructionId} className="hl-guarded-control__instruction">{instruction}</span>
    </div>
  )
}
