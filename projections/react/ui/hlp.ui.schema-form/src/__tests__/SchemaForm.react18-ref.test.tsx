import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'

vi.mock('@harborline-platform/hlp.ui.button', async () => {
  const React = await import('react')
  return {
    Button(props: React.ButtonHTMLAttributes<HTMLButtonElement> & {
      loading?: boolean
      variant?: string
      ref?: React.Ref<HTMLButtonElement>
    }) {
      const { children, loading: _loading, ref: _react19OnlyRef, variant: _variant, ...buttonProps } = props
      return React.createElement('button', buttonProps, children)
    },
  }
})

import { SchemaForm } from '../SchemaForm'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, form, section } from './fixtures'

it('recovers focus through the owned submit DOM element when a React 18 plain-function Button drops refs', async () => {
  const graph: RuleGraphLike = {
    evaluateInstance: vi.fn(instance => evaluation({
      visibility: instance.fields.trigger === 'hide'
        ? [['field:trigger', { visible: false, required: false, readOnly: false }]]
        : [],
    })),
  }
  render(<SchemaForm
    initialValues={{ trigger: '', retained: '' }}
    onSubmit={vi.fn()}
    ruleGraph={graph}
    view={form([section('s', [field('trigger', 'Trigger'), field('retained', 'Retained')])])}
  />)

  const input = screen.getByRole('textbox', { name: 'Trigger' })
  input.focus()
  fireEvent.change(input, { target: { value: 'hide' } })

  await waitFor(() => expect(screen.getByRole('button', { name: 'Submit' })).toHaveFocus())
  expect(screen.queryByRole('textbox', { name: 'Trigger' })).toBeNull()
})
