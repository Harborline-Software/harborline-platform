import { cleanup, render, screen } from '@testing-library/react'
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from '../Sheet'

afterEach(() => { cleanup(); document.body.replaceChildren() })

// Oracle: invoking trigger (Sheet revision 1), otherwise the pre-open element (existing Dialog
// behavior and APG dialog focus return). Expected targets are independent caller-owned buttons.
it.each(['close', 'unmount'] as const)('returns focus to the opener when a controlled sheet without a trigger is %s', action => {
  const opener = document.createElement('button')
  opener.textContent = 'Open settings'
  document.body.append(opener)
  opener.focus()
  const view = render(<Sheet open><SheetContent closeLabel="Close settings"><SheetTitle>Settings</SheetTitle></SheetContent></Sheet>)
  expect(screen.getByRole('button', { name: 'Close settings' })).toHaveFocus()
  if (action === 'close') view.rerender(<Sheet open={false}><SheetContent closeLabel="Close settings"><SheetTitle>Settings</SheetTitle></SheetContent></Sheet>)
  else view.unmount()
  expect(opener).toHaveFocus()
  opener.remove()
})

it('returns focus to a registered trigger', () => {
  const content = <><SheetTrigger>Open settings</SheetTrigger><SheetContent closeLabel="Close settings"><SheetTitle>Settings</SheetTitle></SheetContent></>
  const view = render(<Sheet open>{content}</Sheet>)
  view.rerender(<Sheet open={false}>{content}</Sheet>)
  expect(screen.getByRole('button', { name: 'Open settings' })).toHaveFocus()
})
