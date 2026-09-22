import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { RulesAuthoringEditor, emptyRulesDraft, type RulesDraft, type RulesOperationRequest, type RulesOperationResponse, type RulesOutcome } from '@harborline-software/ui-react'
import producerFixture from '../../../../conformance/hlp.blocks.builder-definitions/rules-editor-contract-fixtures.json'
const contracts = [{ site: 'rule' as const, returnContract: 'typed value', executionTimeContract: 'preview', palette: [{ id: 'amount', label: 'Amount', valueType: 'Number' as const }] }]
const evidence: RulesOutcome = { kind: 'Value', ruleName: 'preview-value', memberName: 'amount', value: '"ready"', inputLabel: 'sample', clockUtc: '2026-06-30T00:00:00.0000000Z' }
function Scene({ scenarioId = 'rule-authoring.from-empty' }: { scenarioId?: string }) { const [draft, setDraft] = useState<RulesDraft>(() => scenarioId === 'rule-authoring.lifecycle-fence' ? { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1', versionSelection: 'Latest' } : emptyRulesDraft()); const operate = (request: RulesOperationRequest): RulesOperationResponse | undefined => scenarioId === 'rule-authoring.lifecycle-fence' ? { requestId: request.requestId, identity: request.identity, expectedRevision: request.expectedRevision, generation: request.generation, authoritative: { identity: 'amount-rule', revision: '2', status: 'Published' }, materialization: producerFixture.lifecycle.responses[4].materialization } : undefined; return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>Rule authoring</h2><p>Build a closed formula or decision table without authoring raw JSON.</p></header><div className="hl-gallery-stage"><RulesAuthoringEditor value={draft} expressionContracts={contracts} previewKind="sample" outcome={scenarioId === 'rule-authoring.preview-outcomes' ? evidence : undefined} onChange={setDraft} onOperation={operate} /></div></section> }
const meta = { title: 'Platform/Rule authoring', component: Scene, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof Scene>
export default meta
type Story = StoryObj<typeof meta>
export const FromEmpty: Story = { name: 'Author from empty' }
export const PreviewOutcomes: Story = { name: 'Preview outcomes', args: { scenarioId: 'rule-authoring.preview-outcomes' } }
export const LifecycleFence: Story = { name: 'Lifecycle fence', args: { scenarioId: 'rule-authoring.lifecycle-fence' } }
