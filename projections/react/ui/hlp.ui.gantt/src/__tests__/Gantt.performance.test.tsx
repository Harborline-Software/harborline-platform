import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Gantt } from '../Gantt'
import type { GanttDependency, GanttTask } from '../Gantt.types'
import { prepareGantt } from '../gantt-model'
import { fixture, performanceCases } from './fixtures'

function taskSet(revision: number): readonly GanttTask[] {
  return Array.from({ length: 256 }, (_, index) => ({
    id: `r${revision}-task-${index}`,
    title: `Revision ${revision} task ${index}`,
    start: '2026-08-01',
    end: index % 3 === 0 ? '2026-08-03' : '2026-08-02',
    progress: index % 101,
  } as const))
}

function dependencySet(revision: number): readonly GanttDependency[] {
  return Array.from({ length: 320 }, (_, index) => ({
    fromId: `r${revision}-task-${index % 256}`,
    toId: `r${revision}-task-${(index + 1) % 256}`,
  }))
}

describe('Gantt deterministic Tier-C evidence', () => {
  it('gantt.quality.large-data performs linear preparation and renders exact structure', () => {
    fixture(performanceCases, 'gantt.quality.large-data')
    const tasks = taskSet(0)
    const dependencies = dependencySet(0)
    const prepared = prepareGantt(tasks, dependencies)
    expect(prepared.tasks).toHaveLength(256)
    expect(prepared.dependencies).toHaveLength(320)
    expect(prepared.validationOperations).toBe(576)
    expect(prepared.ignoredDependencies).toBe(0)
    render(<Gantt accessibleName="Large schedule" dependencies={dependencies} tasks={tasks} />)
    expect(document.querySelectorAll('[data-task-row]')).toHaveLength(256)
    expect(document.querySelectorAll('[data-task-bar]')).toHaveLength(256)
    expect(document.querySelectorAll('[data-dependency-from]')).toHaveLength(320)
  }, 30_000)

  it('gantt.quality.repeated-update keeps only update 96 with no stale tasks or edges', () => {
    fixture(performanceCases, 'gantt.quality.repeated-update')
    const rendered = render(<Gantt accessibleName="Large schedule" dependencies={dependencySet(0)} tasks={taskSet(0)} />)
    for (let revision = 1; revision <= 96; revision += 1) {
      const prepared = prepareGantt(taskSet(revision), dependencySet(revision))
      expect(prepared.validationOperations).toBe(576)
    }
    rendered.rerender(<Gantt accessibleName="Large schedule" dependencies={dependencySet(96)} tasks={taskSet(96)} />)
    expect(document.querySelectorAll('[data-task-row]')).toHaveLength(256)
    expect(document.querySelectorAll('[data-task-bar]')).toHaveLength(256)
    expect(document.querySelectorAll('[data-dependency-from]')).toHaveLength(320)
    expect(document.querySelector('[data-task-id="r96-task-0"]')).toBeInTheDocument()
    expect(document.querySelector('[data-task-id="r95-task-0"]')).toBeNull()
    expect(document.querySelector('[data-dependency-from="r96-task-0"]')).toBeInTheDocument()
    expect(document.querySelector('[data-dependency-from="r95-task-0"]')).toBeNull()
  }, 30_000)
})
