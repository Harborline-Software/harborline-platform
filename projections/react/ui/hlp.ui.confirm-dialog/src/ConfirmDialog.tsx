import { cn } from '@harborline-platform/hlp.ui.cn'
import { defaultStrings } from '@harborline-platform/hlp.ui.default-strings'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { Dialog } from '@harborline-platform/hlp.ui.dialog'

export interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  confirmLabel?: string
  cancelLabel?: string
  onConfirm: () => void
  variant?: 'default' | 'destructive'
  closeOnOverlayClick?: boolean
  closeOnEscape?: boolean
}

/**
 * A confirmation modal that COMPOSES the frozen dialog shell.
 *
 * It owns only the action pair, their localization, and the confirm-then-close ordering. Role,
 * aria-modal, focus lifecycle, overlay, escape handling and body scrolling all belong to
 * hlp.ui.dialog. Re-implementing any of that here is the input-composition defect class corrected
 * in wave 04-01, where a projection re-built chrome a frozen interface said to compose.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  cancelLabel,
  onConfirm,
  variant = 'default',
  closeOnOverlayClick = true,
  closeOnEscape = true,
}: ConfirmDialogProps) {
  const { resolveString } = useHarborlineStrings()

  // Echo-safe resolution. resolveString falls through to t(key), and t returns whatever the catalog
  // holds — so a catalog that maps a key to itself puts the literal "common.confirm" in front of a
  // user about to confirm a destructive action. The seam does not detect this (see
  // HarborlineLocaleProvider resolveString), and fixing it there would change behaviour for all
  // consumers, so the guard lives here: if resolution returns the key unchanged, fall back to the
  // shared default catalog. Fixture confirm-dialog.key-echo-fallback pins it.
  const resolveLabel = (override: string | undefined, key: 'common.confirm' | 'common.cancel') => {
    const resolved = resolveString(override, key)
    return resolved === key ? defaultStrings[key] : resolved
  }

  const resolvedConfirmLabel = resolveLabel(confirmLabel, 'common.confirm')
  const resolvedCancelLabel = resolveLabel(cancelLabel, 'common.cancel')

  function handleConfirm() {
    // Order is load-bearing and fixture-asserted: the caller's callback runs BEFORE the close
    // request, so a handler that inspects open state sees the confirming state, not the closed one.
    onConfirm()
    onOpenChange(false)
  }

  const footer = (
    <>
      <button
        type="button"
        onClick={() => onOpenChange(false)}
        className="hl-confirm-dialog__cancel"
      >
        {resolvedCancelLabel}
      </button>
      <button
        type="button"
        onClick={handleConfirm}
        className={cn(
          'hl-confirm-dialog__confirm',
          variant === 'destructive' && 'hl-confirm-dialog__confirm--destructive',
        )}
      >
        {resolvedConfirmLabel}
      </button>
    </>
  )

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description={description}
      footer={footer}
      closeOnOverlayClick={closeOnOverlayClick}
      closeOnEscape={closeOnEscape}
    >
      {/* Body slot intentionally empty — the description carries the confirmation message. */}
      <span />
    </Dialog>
  )
}
