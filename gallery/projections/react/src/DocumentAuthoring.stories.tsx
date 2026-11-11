import type { Meta, StoryObj } from '@storybook/react-vite'
import { DocumentAuthoringEditor, emptyDocumentDraft, type DocumentDraft } from '@harborline-software/ui-react'
const draft: DocumentDraft = {"identity":"","revision":null,"name":"template.invoice","documentType":"invoice","locale":{"kind":"fixed","tag":"fr-FR"},"style":{"brandName":"Harborline","logoAssetRef":"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},"blocks":[{"id":"block-1","collapseBelow":"md","breakBefore":true,"breakAfter":true,"breakInside":"avoid_page","lines":[{"runs":[{"text":"Total: "},{"fieldPath":"record.total","fallback":"Not supplied"}]}]}],"masters":{"first":{"centerRegion":"masthead"},"left":{"leftRegion":"folio"},"right":{"rightRegion":"folio"}}}
const catalogue = { fields: ['record.total'], regions: [{ id: 'masthead', label: 'Masthead' }, 'folio'], samples: [{ id: 'example', label: 'Example invoice (sample)' }], records: ['record.1'] }
const names: Record<string, string> = { draft: 'Template draft', refusals: 'Admission refusals', 'read-only': 'Read only', empty: 'From empty' }
const scenarioIds: Record<string, string> = { draft: 'document-authoring.draft', refusals: 'document-authoring.refusals', 'read-only': 'document-authoring.read-only', empty: 'document-authoring.empty' }
function DocumentAuthoringScenario({ kind }: { kind: string }) {
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioIds[kind]}><header className="hl-gallery-heading"><h2>{names[kind]}</h2><p>A local template with Layout text runs and page controls. Preview uses a labelled host contract fixture; native integration tests use DocumentRuntime.</p></header><div className="hl-gallery-stage"><DocumentAuthoringEditor value={kind === 'empty' ? emptyDocumentDraft() : draft} pageFlow={{ blocks: [{ id: 'block-1', pageVariant: 'first', content: 'Host contract fixture' }] }} catalogue={catalogue} readOnly={kind === 'read-only'} canPreview denial={kind === 'refusals' ? { identity: '', revision: null, generation: 0, diagnostics: [{ code: 'documents.preview.denied', pointer: '/style' }] } : undefined} onDraftChange={() => undefined} onPreview={async () => ({ minted: false, localeTag: 'fr-FR', blocks: [{ id: 'block-1', content: 'Total: Not supplied' }] })} /></div></section>
}
const meta = { title: 'Platform/Document authoring', component: DocumentAuthoringScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof DocumentAuthoringScenario>
export default meta
type Story = StoryObj<typeof meta>
export const TemplateDraft: Story = { name: 'Template draft', args: { kind: 'draft' } }
export const AdmissionRefusals: Story = { name: 'Admission refusals', args: { kind: 'refusals' } }
export const ReadOnly: Story = { name: 'Read only', args: { kind: 'read-only' } }
export const FromEmpty: Story = { name: 'From empty', args: { kind: 'empty' } }
