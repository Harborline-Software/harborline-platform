/**
 * The ADR 0146 D5 named-rule registry + version policies (TS tier — mirrors the .NET
 * `RuleRegistryTests`). Pins: resolve latest/pinned/draft; the board-F6 draft-exclusion MECHANISM
 * (production never returns a draft; sandbox reaches it); the S-8 monotonic-watermark offline
 * determinism (order-independent "latest"); D7 instance-pin deterministic replay; and the
 * HomeEpochFence read-tip-reject-stale discipline on the per-node compile/cache swap.
 */
import { describe, it, expect } from 'vitest'

import type { RuleDefinition } from '../model.js'
import { compile } from '../index.js'
import {
  RuleVersion,
  RuleVersionPolicy,
  RuleRegistry,
  RuleCompileCache,
  StaleRulePublishError,
  type PublishedRuleVersion,
} from '../index.js'

const TENANT = 't1'
const KEY = 'invoice.approval-threshold'

const def = (id: string): RuleDefinition => ({
  id,
  tier: 'JsonLogic',
  scope: 'Field',
  scopeTarget: 'f',
  action: 'Compute',
  expression: '{"+":[1,2]}',
})

const pub = (version: string, isDraft = false): PublishedRuleVersion => ({
  ruleKey: KEY,
  version,
  isDraft,
  definition: def(`${KEY}@${version}`),
})

describe('ADR 0146 D5 rule version comparator (S-8 watermark)', () => {
  it('orders numeric segments numerically, not lexically', () => {
    expect(RuleVersion.compare('1.10.0', '1.2.0')).toBeGreaterThan(0)
    expect(RuleVersion.compare('2.0.0', '1.9.9')).toBeGreaterThan(0)
    expect(RuleVersion.compare('1.0.0', '1.0.0')).toBe(0)
    expect(RuleVersion.compare('1.0.0-rc1', '1.0.0')).toBeLessThan(0) // pre-release precedes stable
    expect(RuleVersion.isDowngrade('1.2.0', '1.1.0')).toBe(true)
    expect(RuleVersion.isDowngrade('1.2.0', '1.3.0')).toBe(false)
  })

  it('clamps an Int32-overflowing segment to 0 (fail-closed), matching .NET int.TryParse', () => {
    // .NET int.TryParse(NumberStyles.None) fails to parse a segment > int.MaxValue and the
    // comparator falls back to 0 (fail-closed / lowest). A naive parseInt would fail OPEN here
    // (accept the huge number as a real, higher segment) — this pins the clamp (council F3).
    const overflowing = '9999999999.0.0' // > 2147483647
    // The overflowing segment clamps to 0, so '9999999999.0.0' reads as '0.0.0' — strictly below
    // '1.0.0', not equal to it (the clamp is fail-CLOSED to the lowest value, not a wildcard).
    expect(RuleVersion.compare(overflowing, '1.0.0')).toBeLessThan(0)
    expect(RuleVersion.compare(overflowing, '0.0.0')).toBe(0)
    expect(RuleVersion.compare('2147483647.0.0', '1.0.0')).toBeGreaterThan(0) // Int32.MaxValue itself is fine
  })
})

describe('ADR 0146 D5 named-rule registry', () => {
  it('resolves latest as the highest published version', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))
    reg.publish(TENANT, pub('1.10.0'))
    reg.publish(TENANT, pub('1.2.0'))

    const r = reg.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'production')
    expect(r.status).toBe('Resolved')
    expect(r.version).toBe('1.10.0')
    expect(r.definition).toBeDefined()
  })

  it('resolves latest order-independently across offline peers', () => {
    const peerA = new RuleRegistry()
    peerA.publish(TENANT, pub('1.0.0'))
    peerA.publish(TENANT, pub('2.0.0'))

    const peerB = new RuleRegistry()
    peerB.publish(TENANT, pub('2.0.0'))
    peerB.publish(TENANT, pub('1.0.0'))

    const a = peerA.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'production')
    const b = peerB.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'production')
    expect(a.version).toBe('2.0.0')
    expect(b.version).toBe(a.version)
  })

  it('excludes drafts from latest on production but reaches them in sandbox', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))
    reg.publish(TENANT, pub('2.0.0', true)) // higher-versioned draft

    expect(reg.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'production').version).toBe('1.0.0')
    expect(reg.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'sandbox').version).toBe('2.0.0')
  })

  it('refuses the draft policy on production and resolves it in sandbox', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))
    reg.publish(TENANT, pub('2.0.0', true))

    const prod = reg.resolve(TENANT, KEY, RuleVersionPolicy.draft, 'production')
    const sandbox = reg.resolve(TENANT, KEY, RuleVersionPolicy.draft, 'sandbox')
    expect(prod.status).toBe('DraftRefused')
    expect(sandbox.status).toBe('Resolved')
    expect(sandbox.version).toBe('2.0.0')
  })

  it('refuses a pinned draft on production and reaches it in sandbox', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('3.0.0', true))

    const prod = reg.resolve(TENANT, KEY, RuleVersionPolicy.pinned('3.0.0'), 'production')
    const sandbox = reg.resolve(TENANT, KEY, RuleVersionPolicy.pinned('3.0.0'), 'sandbox')
    expect(prod.status).toBe('DraftRefused')
    expect(prod.version).toBe('3.0.0')
    expect(sandbox.status).toBe('Resolved')
  })

  it('resolves pinned to the exact version, not the latest', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))
    reg.publish(TENANT, pub('2.0.0'))
    const r = reg.resolve(TENANT, KEY, RuleVersionPolicy.pinned('1.0.0'), 'production')
    expect(r.status).toBe('Resolved')
    expect(r.version).toBe('1.0.0')
  })

  it('returns NotFound for an unknown key, pin, or tenant', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))
    expect(reg.resolve(TENANT, 'no.such.rule', RuleVersionPolicy.latest, 'production').status).toBe('NotFound')
    expect(reg.resolve(TENANT, KEY, RuleVersionPolicy.pinned('9.9.9'), 'production').status).toBe('NotFound')
    expect(reg.resolve('other-tenant', KEY, RuleVersionPolicy.latest, 'production').status).toBe('NotFound')
  })

  it('freezes a D7 pin and replays it deterministically past a later publish', () => {
    const reg = new RuleRegistry()
    reg.publish(TENANT, pub('1.0.0'))

    const pin = reg.pin(TENANT, KEY, RuleVersionPolicy.latest, 'production')
    expect(pin).not.toBeNull()
    expect(pin!.version).toBe('1.0.0')

    reg.publish(TENANT, pub('2.0.0')) // newer version after the instance pinned

    expect(reg.resolve(TENANT, KEY, RuleVersionPolicy.latest, 'production').version).toBe('2.0.0')
    expect(reg.resolvePinned(pin!, 'production').version).toBe('1.0.0')
  })

  it('returns null from pin when nothing resolves', () => {
    const reg = new RuleRegistry()
    expect(reg.pin(TENANT, KEY, RuleVersionPolicy.latest, 'production')).toBeNull()
  })

  it('does not alias (tenant, key) pairs across a would-be delimiter (council F1/F2)', () => {
    // A delimited string key (`${tenant} ${ruleKey}` or `${tenant}\0${ruleKey}`) collides here:
    // ("t", "a b") and ("t a", "b") would concatenate to the same string. The structural
    // (nested-Map) key must keep them distinct.
    const reg = new RuleRegistry()
    reg.publish('t', { ruleKey: 'a b', version: '1.0.0', isDraft: false, definition: def('x@1.0.0') })
    reg.publish('t a', { ruleKey: 'b', version: '2.0.0', isDraft: false, definition: def('y@2.0.0') })

    const first = reg.resolve('t', 'a b', RuleVersionPolicy.latest, 'production')
    const second = reg.resolve('t a', 'b', RuleVersionPolicy.latest, 'production')
    expect(first.version).toBe('1.0.0')
    expect(second.version).toBe('2.0.0')
    // Cross-check: neither pair reaches the other's version.
    expect(reg.resolve('t', 'a b', RuleVersionPolicy.pinned('2.0.0'), 'production').status).toBe('NotFound')
    expect(reg.resolve('t a', 'b', RuleVersionPolicy.pinned('1.0.0'), 'production').status).toBe('NotFound')
  })
})

describe('ADR 0146 D5 compile/cache swap fence (HomeEpochFence discipline)', () => {
  it('is monotonic and refuses a stale publish', () => {
    const cache = new RuleCompileCache()
    const g10 = compile([def('r@1.0.0')])
    const g11 = compile([def('r@1.1.0')])
    const g10Late = compile([def('r@1.0.0-late')])

    cache.swap(TENANT, KEY, '1.0.0', g10)
    cache.swap(TENANT, KEY, '1.1.0', g11) // newer installs

    // A stale publish (a lower version arriving late) is refused — the newer AST stands.
    expect(() => cache.swap(TENANT, KEY, '1.0.0', g10Late)).toThrow(StaleRulePublishError)

    const cached = cache.tryGet(TENANT, KEY)
    expect(cached?.version).toBe('1.1.0')
    expect(cached?.graph).toBe(g11)
  })

  it('is idempotent for an equal version', () => {
    const cache = new RuleCompileCache()
    const first = compile([def('r@2.0.0')])
    const recompiled = compile([def('r@2.0.0')])
    cache.swap(TENANT, KEY, '2.0.0', first)
    cache.swap(TENANT, KEY, '2.0.0', recompiled) // no throw
    expect(cache.tryGet(TENANT, KEY)?.graph).toBe(recompiled)
  })

  it('does not alias (tenant, key) pairs across a would-be delimiter (council F1/F2)', () => {
    const cache = new RuleCompileCache()
    const gAB = compile([def('ab@1.0.0')])
    const gBA = compile([def('ba@2.0.0')])

    cache.swap('t', 'a b', '1.0.0', gAB)
    cache.swap('t a', 'b', '2.0.0', gBA)

    expect(cache.tryGet('t', 'a b')?.graph).toBe(gAB)
    expect(cache.tryGet('t a', 'b')?.graph).toBe(gBA)
    expect(cache.tryGet('t', 'a b')?.version).toBe('1.0.0')
    expect(cache.tryGet('t a', 'b')?.version).toBe('2.0.0')
  })
})
