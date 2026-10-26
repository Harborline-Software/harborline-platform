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
// instructions and the committing status, under chrome.guard.covered.instruction,
// chrome.guard.armed.whole and chrome.guard.committing.status.
const COVERED_INSTRUCTION = 'Guarded action: {action}. Activate to arm.'
const ARMED_INSTRUCTION = 'Armed for {action}. Activate again within {duration} to commit.'
const COMMITTING_STATUS = 'Committing {action}.'
const RECOVERED_ANNOUNCEMENT = 'Guard restored for {action}.'
const COMPLETED_ANNOUNCEMENT = 'Guard restored for {action}.'
const REJECTED_ANNOUNCEMENT = 'Could not complete {action}. Guard restored.'

// One pass with a function replacer, as HarborlineDefaultStrings.Interpolate does in the Blazor lane:
// inserted caller text is never read as a replacement pattern ($$, $&, $`, $') or scanned again for
// placeholders, and an unknown placeholder stays visible. Own properties only, so {constructor} is
// not a variable.
function interpolateWhole(template: string, variables: Readonly<Record<string, string>>): string {
  return template.replace(/\{(\w+)\}/g, (match, name: string) => Object.hasOwn(variables, name) ? variables[name]! : match)
}

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

  // An amount of time ("5 seconds"), which the armed instruction places after "within"; never a
  // relative time ("in 5 seconds"). The locale's CLDR unit pattern supplies the unit word, its plural
  // form and its placement, as Intl.RelativeTimeFormat did before; the number in it is the locale
  // provider's formatNumber, called with no options so a host formatter that writes only the number
  // cannot drop the unit. Without a host formatter that is the same Intl number. A form with no number
  // in it (Arabic "ثانيتان", 2 seconds) asks for none. The Blazor lane resolves its catalog's
  // chrome.guard.duration forms instead; one duration source for both lanes is T-1105's deferred
  // duration-parity work.
  const formatDuration = React.useCallback((count: number) => {
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
  }, [locale, formatNumber])

  // Every guard message takes the same placeholders: {action}, the label bidi-isolated, and
  // {duration}, the full arm window, never the live countdown, so no message depends on how the last
  // arm ended. The host catalog's whole message wins, else the English one.
  const messageVariables = React.useMemo(() => ({ action: `\u2068${label}\u2069`, duration: formatDuration(ARM_WINDOW_SECONDS) }), [label, formatDuration])
  const wholeMessage = React.useCallback((key: string, fallback: string) =>
    interpolateWhole(catalog[key] ?? fallback, messageVariables), [catalog, messageVariables])

  // The message resolver and the observe callback change identity whenever the host passes a new
  // catalog, number formatter, locale or inline callback. The timer and host listeners read them
  // through refs, so recover keeps one identity for the control's lifetime: a host re-render never
  // re-registers the listeners, drops the window-blur grace timer or touches the deadline.
  const wholeMessageRef = React.useRef(wholeMessage)
  const observeRef = React.useRef(observe)
  React.useLayoutEffect(() => {
    wholeMessageRef.current = wholeMessage
    observeRef.current = observe
  })

  const transition = React.useCallback((next: GuardedControlState) => {
    const previous = stateRef.current
    if (previous === next) return false
    stateRef.current = next
    setState(next)
    observeRef.current?.({ type: 'transition', previous, next })
    return true
  }, [])

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
    setAnnouncement(wholeMessageRef.current('chrome.guard.recovered.whole', RECOVERED_ANNOUNCEMENT))
    restoreFocus()
    observeRef.current?.({ type: 'recovered', reason })
  }, [restoreFocus, transition])

  React.useEffect(() => {
    stateRef.current = state
  }, [state])
  React.useEffect(() => () => { mounted.current = false }, [])
  React.useEffect(() => {
    if (!disabled || stateRef.current !== 'armed') return
    recover('became-disabled')
  }, [disabled, recover])

  // Keyed on the state alone (recover is stable): the deadline is set once, by the arming activation,
  // and this effect only reads it.
  React.useEffect(() => {
    if (state !== 'armed') return
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
  }, [state]) // recover keeps one identity for the control's lifetime

  React.useLayoutEffect(() => {
    if (state === 'armed') armedRef.current?.focus()
  }, [state])

  // The armed instruction always states the full window, never the countdown: it is announced once
  // on arming and is the armed description for the whole armed state, so it never changes with a tick.
  const armedInstruction = () => wholeMessage('chrome.guard.armed.whole', ARMED_INSTRUCTION)

  // Every arm states the full window, never the countdown a previous attempt left behind.
  const arm = () => {
    if (disabled || invalid || stateRef.current !== 'covered') return
    committed.current = false
    deadline.current = Date.now() + ARM_WINDOW_MS
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
      ? wholeMessageRef.current('chrome.guard.rejected.whole', REJECTED_ANNOUNCEMENT)
      : wholeMessageRef.current('chrome.guard.completed.whole', COMPLETED_ANNOUNCEMENT))
    restoreFocus()
    observeRef.current?.({ type: 'commit-settled', outcome })
  }

  const duration = formatDuration(seconds)
  const inert = disabled || invalid
  const instruction = state === 'covered'
    ? wholeMessage('chrome.guard.covered.instruction', COVERED_INSTRUCTION)
    : state === 'armed'
      ? armedInstruction()
      : wholeMessage('chrome.guard.committing.status', COMMITTING_STATUS)
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
