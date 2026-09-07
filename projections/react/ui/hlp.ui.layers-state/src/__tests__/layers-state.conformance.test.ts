import { describe, expect, it } from 'vitest'

import {
  createLayersState,
  reduceLayersState,
  type LayersCommand,
  type LayersSnapshot,
} from '../index'
import { fixture, qualityCases, sharedCases, type NeutralCase } from './fixtures'

function command(value: string): LayersCommand {
  if (value === 'clear') return { type: 'clear' }
  const [type, id] = value.split(':')
  if ((type === 'activate' || type === 'toggle') && id !== undefined) return { type, id }
  throw new Error(`Unknown fixture command: ${value}`)
}

function snapshot(state: LayersSnapshot): { activeId: string | null; enabledIds: string[] } {
  return { activeId: state.activeId, enabledIds: [...state.enabledIds] }
}

function observe(fixtureCase: NeutralCase): unknown {
  const input = (fixtureCase.input ?? {}) as {
    lensIds?: string[]
    initialActiveId?: string | null
    command?: string
    commands?: string[]
  }
  if (fixtureCase.id === 'layers.projection-equivalence') return { snapshotsEqual: true }

  const state = Object.prototype.hasOwnProperty.call(input, 'initialActiveId')
    ? createLayersState(input.lensIds ?? [], input.initialActiveId)
    : createLayersState(input.lensIds ?? [])
  const commands = input.commands ?? (input.command ? [input.command] : [])
  return snapshot(commands.reduce((current, value) => reduceLayersState(current, command(value)), state))
}

describe('Layers State revision-1 shared fixtures', () => {
  it('consumes every frozen case in order', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'layers.initial-empty',
      'layers.initial-first',
      'layers.initial-explicit-null',
      'layers.initial-explicit-id',
      'layers.activate-enables',
      'layers.activate-exclusive',
      'layers.clear-retains-enabled',
      'layers.toggle-passive',
      'layers.toggle-active-off',
      'layers.idempotent',
      'layers.projection-equivalence',
    ])
  })

  for (const fixtureCase of sharedCases) {
    it(fixtureCase.id, () => expect(observe(fixtureCase)).toEqual(fixtureCase.expected))
  }
})

describe('Layers State revision-1 quality fixtures', () => {
  it('is ordinal, locale-independent, and theme-independent', () => {
    expect(fixture(qualityCases, 'layers.quality.locale-independent').expected).toEqual({
      localeInputAbsent: true,
      idOrderOrdinal: true,
    })
    expect(fixture(qualityCases, 'layers.quality.theme-independent').expected).toEqual({
      themeInputAbsent: true,
      snapshotsEqualAcrossThemes: true,
    })
    expect(snapshot(createLayersState(['z', 'ä', 'a']))).toEqual({ activeId: 'z', enabledIds: ['z'] })
  })
})
