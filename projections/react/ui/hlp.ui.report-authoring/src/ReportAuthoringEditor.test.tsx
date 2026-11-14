import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ReportAuthoringEditor } from './ReportAuthoringEditor'

it('reports-auth-12 creates a report from nothing', () => {
  const create = vi.fn()
  render(<ReportAuthoringEditor value={null} catalogue={[]} onCreate={create} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Create report' }))
  expect(create).toHaveBeenCalledOnce()
})
