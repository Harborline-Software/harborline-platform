import { describe, expect, it } from 'vitest'
import { contractCases, sharedCases } from './fixtures'
describe('Toaster frozen conformance coverage', () => { it('consumes every revision-1 case', () => expect(sharedCases.map(x => x.id)).toEqual(contractCases.map(x => x.id))) })
