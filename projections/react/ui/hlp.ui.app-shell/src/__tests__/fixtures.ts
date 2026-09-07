import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
const root = resolve(import.meta.dirname, '../../../../../../')
export const sharedCases = (JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.app-shell/fixtures.yaml'), 'utf8')) as { cases: Array<{ id: string }> }).cases
export const contractCases = (JSON.parse(readFileSync(resolve(root, 'specs/modules/ui/hlp.ui.app-shell/interface.yaml'), 'utf8')) as { cases: Array<{ id: string }> }).cases
