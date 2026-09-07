import type { DispatchReceipt, PilotEffectAdapter } from '../../src/index.js'
declare const adapter: PilotEffectAdapter
const missingKey = { surface: 'forms.builder', command: 'ap', args: {}, tier: 'ap' as const }
const invalidReceipt: DispatchReceipt = missingKey
adapter.execute(missingKey)
