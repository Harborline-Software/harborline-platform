import * as React from 'react'
import { SelectField, type SelectFieldProps } from '../index'

const common = { name: 'status', options: [{ value: 'active', label: 'Active' }] }
const inputRef = React.createRef<HTMLInputElement>()
const buttonRef = React.createRef<HTMLButtonElement>()
const searchable: SelectFieldProps = {
  ...common, searchable: true, value: '', onValueChange(value) { value.toUpperCase() },
  onFocus(event) { const input: HTMLInputElement = event.currentTarget; input.select() },
  onBlur(event) { const input: HTMLInputElement = event.currentTarget; input.select() },
  onKeyDown(event) { const input: HTMLInputElement = event.currentTarget; input.select() },
}
const ordinary: SelectFieldProps = {
  ...common, value: '', onValueChange(value) { value.toUpperCase() },
  onFocus(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
  onBlur(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
  onKeyDown(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
}
const multiple: SelectFieldProps = {
  ...common, multiple: true, searchable: true, value: [], onValueChange(values) { values.map(value => value.toUpperCase()) },
  onFocus(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
  onBlur(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
  onKeyDown(event) { const button: HTMLButtonElement = event.currentTarget; void button.formAction },
}
void <SelectField {...searchable} ref={inputRef} />
void <SelectField {...ordinary} ref={buttonRef} />
void <SelectField {...multiple} ref={buttonRef} />
// @ts-expect-error Searchable single exposes an input, not a button.
void <SelectField {...searchable} ref={buttonRef} />
// @ts-expect-error Ordinary single exposes a button, not an input.
void <SelectField {...ordinary} ref={inputRef} />
// @ts-expect-error Searchable multiple still exposes its button trigger.
void <SelectField {...multiple} ref={inputRef} />
// @ts-expect-error Input handlers cannot receive button events.
void <SelectField {...searchable} onKeyDown={(event: React.KeyboardEvent<HTMLButtonElement>) => { void event }} />
