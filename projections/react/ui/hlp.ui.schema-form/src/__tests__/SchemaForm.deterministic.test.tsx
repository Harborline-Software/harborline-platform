import { act, fireEvent, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import type { ControlArgs } from '../SchemaForm.types'
import { field, form, section } from './fixtures'

const fieldCount = 256
const updateCycles = 96
const controls = {
  text: ({ field: viewField, onChange, strValue }: ControlArgs) => (
    <input
      id={viewField.name}
      name={viewField.name}
      onChange={event => onChange(event.target.value)}
      type="text"
      value={strValue}
    />
  ),
}

function valuesForCycle(cycle: number): Record<string, string> {
  return Object.fromEntries(
    Array.from({ length: fieldCount }, (_, index) => [
      `field-${index}`,
      index === 0 ? `${cycle}-0` : `stable-${index}`,
    ]),
  )
}

describe('SchemaForm deterministic structural performance proof', () => {
  it('reuses an exact field structure and the latest state across repeated updates', async () => {
    const view = form([
      section('large', Array.from({ length: fieldCount }, (_, index) => field(`field-${index}`, `Field ${index}`))),
    ])
    const callbacks = Array.from({ length: updateCycles }, () => vi.fn())
    const initialValues = valuesForCycle(-1)
    const rendered = render(
      <SchemaForm controls={controls} initialValues={initialValues} onSubmit={callbacks[0]} view={view} />,
    )
    const root = rendered.container.querySelector('.hl-schema-form')
    const firstField = rendered.container.querySelector('.hl-form-field')

    expect(root).not.toBeNull()
    expect(firstField).not.toBeNull()

    const createElement = vi.spyOn(document, 'createElement')
    const createElementNs = vi.spyOn(document, 'createElementNS')
    const addEventListener = vi.spyOn(EventTarget.prototype, 'addEventListener')

    try {
      for (let cycle = 0; cycle < updateCycles; cycle += 1) {
        rendered.rerender(
          <SchemaForm controls={controls} initialValues={initialValues} onSubmit={callbacks[cycle]} view={view} />,
        )
        fireEvent.change(rendered.container.querySelector("input[name='field-0']") as HTMLInputElement, {
          target: { value: `${cycle}-0` },
        })

        const fields = rendered.container.querySelectorAll('.hl-form-field')

        // Invariant: exact-field-count
        expect(fields).toHaveLength(fieldCount)
        expect(rendered.container.querySelector('.hl-schema-form')).toBe(root)
        expect(fields[0]).toBe(firstField)

        // Invariant: latest-value-visible
        expect(rendered.container.querySelector<HTMLInputElement>("input[name='field-0']")?.value).toBe(`${cycle}-0`)

        // Invariant: no-stale-callbacks
        expect(callbacks.every(callback => callback.mock.calls.length === 0)).toBe(true)
      }

      expect(createElement).not.toHaveBeenCalled()
      expect(createElementNs).not.toHaveBeenCalled()
      expect(addEventListener).not.toHaveBeenCalled()

      await act(async () => {
        fireEvent.submit(root as HTMLFormElement)
      })

      expect(callbacks.slice(0, -1).every(callback => callback.mock.calls.length === 0)).toBe(true)
      expect(callbacks.at(-1)).toHaveBeenCalledTimes(1)
      expect(callbacks.at(-1)).toHaveBeenCalledWith(valuesForCycle(updateCycles - 1))
    } finally {
      createElement.mockRestore()
      createElementNs.mockRestore()
      addEventListener.mockRestore()
    }
  })
})
