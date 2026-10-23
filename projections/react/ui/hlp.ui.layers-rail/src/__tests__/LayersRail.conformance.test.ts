import { describe, expect, it } from 'vitest'
import { contractCases, sharedCases } from './fixtures'
describe('LayersRail frozen conformance coverage', () => { it('consumes every revision-2 case', () => expect(sharedCases.map(x => x.id)).toEqual(contractCases.map(x => x.id))) })
