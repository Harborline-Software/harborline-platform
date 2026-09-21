import { expect, it } from 'vitest'
import { focusSubmit } from '../../projections/blazor/ui/hlp.ui.schema-form/wwwroot/schema-form.js'

it('Blazor form focus recovery reaches the composed submit button', () => {
  document.body.innerHTML = '<form><button type="button">Add</button><button type="submit">Save</button></form>'
  focusSubmit(document.querySelector('form'))
  expect(document.querySelector('button[type=submit]')).toHaveFocus()
  document.body.replaceChildren()
})
