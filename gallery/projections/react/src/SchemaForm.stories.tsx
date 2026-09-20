import * as React from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { SchemaForm } from '@harborline-software/ui-react'

type ScenarioId =
  | 'schema-form.domain-editors'
  | 'schema-form.host-readonly'
  | 'schema-form.controls-structure'
  | 'schema-form.rules-gate'
  | 'schema-form.validation'
  | 'schema-form.locale-theme'
  | 'schema-form.content'
  | 'schema-form.empty-state'
  | 'schema-form.error-state'
type SchemaFormProps = React.ComponentProps<typeof SchemaForm>
type FormView = SchemaFormProps['view']
type FormField = FormView['sections'][number]['fields'][number]
type ControlRenderer = NonNullable<SchemaFormProps['controls']>[string]
type GalleryControlArgs = {
  field: FormField
  strValue: string
}
type RuleGraph = NonNullable<SchemaFormProps['ruleGraph']>
type RuleEvaluation = ReturnType<RuleGraph['evaluateInstance']>

const localized = (value: string, locale = 'en') => ({ defaultLocale: locale, values: { [locale]: value } })
const field = (name: string, label: string, overrides: Partial<FormField> = {}): FormField => ({
  name,
  label: localized(label),
  isSensitive: false,
  isReadable: true,
  controlHint: 'text',
  ...overrides,
})

const ENGLISH_STRINGS = {
  submit: 'Save request',
  submitting: 'Saving request…',
  errorSummaryTitle: 'Review these request errors',
  redacted: 'Restricted value',
  empty: 'No request fields are available.',
  addItem: 'Add charge',
  removeItem: 'Remove charge',
  itemLabel: 'Charge {n}',
  controlUnavailable: 'Control unavailable: {control}',
  submitBlocked: 'Saving is unavailable while request rules are pending.',
} satisfies NonNullable<SchemaFormProps['strings']>

const PSEUDO_STRINGS = {
  submit: '⟦ Šåṽë řëqûëšţ ···· ⟧',
  submitting: '⟦ Šåṽïñĝ řëqûëšţ… ···· ⟧',
  errorSummaryTitle: '⟦ Řëṽïëŵ ţħëšë řëqûëšţ ëřřøřš ········ ⟧',
  redacted: '⟦ Řëšţřïçţëð ṽåļûë ···· ⟧',
  empty: '⟦ Ñø řëqûëšţ ƒïëļðš åřë åṽåïļåɓļë ········ ⟧',
  addItem: '⟦ Åðð ïţëm ·· ⟧',
  removeItem: '⟦ Řëmøṽë ïţëm ··· ⟧',
  itemLabel: '⟦ Îţëm {n} ·· ⟧',
  controlUnavailable: '⟦ Çøñţřøļ ûñåṽåïļåɓļë: {control} ······ ⟧',
  submitBlocked: '⟦ Šåṽïñĝ ïš ûñåṽåïļåɓļë ŵħïļë řûļëš åřë þëñðïñĝ ·········· ⟧',
} satisfies NonNullable<SchemaFormProps['strings']>

const ARABIC_STRINGS = {
  submit: 'حفظ الطلب',
  submitting: 'جارٍ حفظ الطلب…',
  errorSummaryTitle: 'راجع أخطاء الطلب',
  redacted: 'قيمة محظورة',
  empty: 'لا توجد حقول متاحة.',
  addItem: 'إضافة بند',
  removeItem: 'إزالة البند',
  itemLabel: 'البند {n}',
  controlUnavailable: 'عنصر التحكم غير متاح: {control}',
  submitBlocked: 'يتعذر الحفظ أثناء انتظار القواعد.',
} satisfies NonNullable<SchemaFormProps['strings']>

const bespokeControl = ((args: GalleryControlArgs) => (
  <output
    aria-describedby={args.field.helpText ? `${args.field.name}-hint` : undefined}
    aria-labelledby={`${args.field.name}-label`}
    className="hl-schema-form__readonly"
    id={args.field.name}
  >
    {args.strValue} · custom registry
  </output>
)) as ControlRenderer

const CONTROLS_VIEW = {
  formId: 'arrival-request',
  version: '1.0.0',
  title: localized('Arrival request'),
  description: localized('Fields and sections remain in the exact order supplied by the caller.'),
  sections: [
    {
      id: 'routing',
      title: localized('Routing'),
      fields: [
        field('services', 'Services', { controlHint: 'multiselect', options: [
          { value: 'pilotage', label: localized('Pilotage') }, { value: 'towage', label: localized('Towage') },
        ] }),
        field('berthCode', 'Berth code', {
          controlHint: 'bespoke',
          helpText: localized('Resolved by the caller registry.'),
        }),
        field('arrivalWindow', 'Arrival window', {
          controlHint: 'future-string',
          helpText: localized('An unknown string hint falls back to text.'),
        }),
        field('cargoShape', 'Cargo shape', {
          controlHint: 'future-object',
          valueKind: 'object',
          helpText: localized('Structured values fail closed.'),
        }),
      ],
    },
    {
      id: 'charges',
      title: localized('Charges'),
      fields: [],
      items: [
        {
          kind: 'group',
          key: 'agent',
          title: localized('Agent'),
          items: [
            { kind: 'field', key: 'name', field: field('name', 'Agent name') },
          ],
        },
        {
          kind: 'collection',
          key: 'charges',
          title: localized('Port charges'),
          cardinality: { min: 1, max: 2 },
          items: [
            { kind: 'field', key: 'description', field: field('description', 'Description') },
          ],
        },
      ],
    },
  ],
} satisfies FormView

const RULES_VIEW = {
  formId: 'rule-projection',
  version: '1.0.0',
  title: localized('Rule projection'),
  description: localized('The hidden value remains in the complete candidate while the save gate fails closed.'),
  sections: [{
    id: 'decision',
    title: localized('Decision'),
    fields: [
      field('contact', 'Operations contact', { helpText: localized('Required by an active rule.') }),
      field('reference', 'Request reference'),
      field('total', 'Computed total', { controlHint: 'currency', config: { currencyCode: 'USD', decimals: 2 } }),
      field('status', 'Review status'),
      field('internalNote', 'Internal note'),
    ],
  }],
} satisfies FormView

const RULE_GRAPH: RuleGraph = {
  evaluateInstance: () => ({
    byRule: new Map([['presentation-status', {
      ruleId: 'presentation-status',
      target: 'field:status',
      outputType: 'Presentation',
      presentation: { badge: localized('Needs review'), severity: 'warn', styleToken: 'attention' },
    }]]),
    values: new Map([['field:total', { state: 'Resolved', value: 1250.5 }]]),
    visibility: new Map([
      ['field:contact', { visible: true, required: true, readOnly: false }],
      ['field:reference', { visible: true, required: false, readOnly: true }],
      ['field:internalNote', { visible: false, required: false, readOnly: false }],
    ]),
    validations: [],
    options: new Map(),
    hasPending: true,
    isSaveBlocked: true,
  } as RuleEvaluation),
}

const VALIDATION_VIEW = {
  formId: 'validation-request',
  version: '1.0.0',
  title: localized('Validation request'),
  description: localized('Save to place a one-segment pointer inline and deeper pointers in the focused summary.'),
  sections: [{
    id: 'requester',
    title: localized('Requester'),
    fields: [
      field('name', 'Requester name', { helpText: localized('Use the name shown on the manifest.') }),
      field('email', 'Contact email', { controlHint: 'email' }),
    ],
  }],
} satisfies FormView

const READ_ONLY_INSPECTION_VIEW = {
  formId: 'harbor-inspection-application',
  version: '1.0.0',
  title: localized('Harbor inspection application'),
  description: localized('Completed application and inspection values from every visible built-in field kind.'),
  sections: [
    {
      id: 'application',
      title: localized('Application'),
      fields: [
        field('vesselName', 'Vessel name', { controlHint: 'text' }),
        field('inspectionScope', 'Inspection scope', { controlHint: 'textarea' }),
        field('grossTonnage', 'Gross tonnage', { controlHint: 'number' }),
        field('crewCount', 'Crew count', { controlHint: 'integer' }),
        field('inspectionType', 'Inspection type', { controlHint: 'select', options: [
          { value: 'annual', label: localized('Annual safety inspection') },
          { value: 'arrival', label: localized('Arrival inspection') },
        ] }),
        field('systemsReviewed', 'Systems reviewed', { controlHint: 'multiselect', options: [
          { value: 'navigation', label: localized('Navigation') },
          { value: 'fire', label: localized('Fire suppression') },
          { value: 'lifesaving', label: localized('Lifesaving equipment') },
        ] }),
      ],
    },
    {
      id: 'declarations',
      title: localized('Declarations and schedule'),
      fields: [
        field('documentsVerified', 'Documents verified', { controlHint: 'checkbox' }),
        field('masterAttested', 'Master attested', { controlHint: 'boolean' }),
        field('followUpRequired', 'Follow-up required', { controlHint: 'boolean-toggle' }),
        field('inspectionDate', 'Inspection date', { controlHint: 'date' }),
        field('inspectionStarted', 'Inspection started', { controlHint: 'datetime' }),
        field('highTide', 'High tide', { controlHint: 'time' }),
      ],
    },
    {
      id: 'commercial-contact',
      title: localized('Commercial and contact details'),
      fields: [
        field('estimatedCost', 'Estimated inspection cost', { controlHint: 'currency', config: { currencyCode: 'USD', decimals: 2 } }),
        field('completion', 'Inspection completion', { controlHint: 'percentage', config: { decimals: 1 } }),
        field('agentPhone', 'Agent phone', { controlHint: 'phone' }),
        field('agentEmail', 'Agent email', { controlHint: 'email' }),
        field('certificateUrl', 'Certificate URL', { controlHint: 'url' }),
        field('applicationStatus', 'Application status', { controlHint: 'readonly' }),
        field('inspectorCredential', 'Inspector credential', { controlHint: 'text', isSensitive: true }),
        field('internalFinding', 'Internal finding', { controlHint: 'text', isReadable: false }),
        field('evidenceBundle', 'Evidence bundle', { controlHint: 'future-object', valueKind: 'object' }),
      ],
    },
  ],
} satisfies FormView

const READ_ONLY_INSPECTION_VALUES = {
  vesselName: 'MV North Star',
  inspectionScope: 'Annual hull, machinery, navigation, and lifesaving equipment inspection.',
  grossTonnage: 18425.5,
  crewCount: 24,
  inspectionType: 'annual',
  systemsReviewed: ['navigation', 'fire', 'lifesaving'],
  documentsVerified: true,
  masterAttested: true,
  followUpRequired: false,
  inspectionDate: '2026-09-16',
  inspectionStarted: '2026-09-16T09:30:00-04:00',
  highTide: '14:45',
  estimatedCost: 48750.5,
  completion: 92.5,
  agentPhone: '+1 410 555 0142',
  agentEmail: 'port.agent@example.test',
  certificateUrl: 'https://records.example.test/inspections/HLI-2048',
  applicationStatus: 'Approved for certificate issuance',
  inspectorCredential: 'credential-should-not-render',
  internalFinding: 'internal-finding-should-not-render',
  evidenceBundle: { photos: 12, report: 'HLI-2048.pdf' },
} satisfies Record<string, unknown>

const EMPTY_VIEW = {
  formId: 'empty-request',
  version: '1.0.0',
  title: localized('Empty inspection request'),
  description: localized('No inspection fields are currently available.'),
  sections: [],
} satisfies FormView

const CONTENT_VIEW = {
  formId: 'content-resilience',
  version: '1.0.0',
  title: localized('Content resilience'),
  description: localized('Caller-supplied labels and values remain text.'),
  sections: [{
    id: 'hostile-content',
    title: localized('Hostile-but-valid content'),
    fields: [
      field('longLabel', 'Awaiting third-party structural certification review'),
      field('largeNumber', 'Grouped large number'),
      field('escapedText', 'Escaped location text'),
      field('surveyor', 'Survey contact'),
    ],
  }],
} satisfies FormView

const PSEUDO_VIEW = {
  formId: 'pseudo-request',
  version: '1.0.0',
  title: localized('⟦ Ëẋþåñðëð řëqûëšţ ······ ⟧', 'en-XA'),
  description: localized('⟦ Çåļļëř-šûþþļïëð çøþÿ ëẋþåñðš ŵïţħøûţ ţřûñçåţïøñ ············ ⟧', 'en-XA'),
  sections: [{
    id: 'figures',
    title: localized('⟦ Šçħëðûļë åñð ƒïĝûřëš ······ ⟧', 'en-XA'),
    fields: [
      field('replacementCost', '⟦ Řëþļåçëmëñţ çøšţ ······ ⟧', {
        label: localized('⟦ Řëþļåçëmëñţ çøšţ ······ ⟧', 'en-XA'),
        helpText: localized('⟦ Ëñţëř ţħë ƒûļļ ëšţïmåţëð çøšţ ········ ⟧', 'en-XA'),
        controlHint: 'currency',
        config: { currencyCode: 'USD', decimals: 2 },
      }),
      field('completion', '⟦ Çømþļëţïøñ þëřçëñţåĝë ······ ⟧', {
        label: localized('⟦ Çømþļëţïøñ þëřçëñţåĝë ······ ⟧', 'en-XA'),
        controlHint: 'percentage',
        config: { decimals: 1 },
      }),
      field('inspectionDate', '⟦ Îñšþëçţïøñ ðåţë ······ ⟧', {
        label: localized('⟦ Îñšþëçţïøñ ðåţë ······ ⟧', 'en-XA'),
        controlHint: 'date',
      }),
    ],
  }],
} satisfies FormView

const ARABIC_VIEW = {
  formId: 'arabic-request',
  version: '1.0.0',
  title: localized('طلب الميناء', 'ar'),
  description: localized('كل النصوص المرئية مقدمة من المتصل.', 'ar'),
  sections: [{
    id: 'amounts',
    title: localized('المبالغ والتاريخ', 'ar'),
    fields: [
      field('amount', 'المبلغ الإجمالي', {
        label: localized('المبلغ الإجمالي', 'ar'),
        helpText: localized('بالعملة المحلية', 'ar'),
        controlHint: 'currency',
        config: { currencyCode: 'AED', decimals: 2 },
      }),
      field('date', 'تاريخ الوصول', {
        label: localized('تاريخ الوصول', 'ar'),
        controlHint: 'date',
      }),
    ],
  }],
} satisfies FormView

const validSubmit: SchemaFormProps['onSubmit'] = () => ({ isValid: true, errors: [] })

const DOMAIN_COPY = 'Renderer demonstration of resolved domain output. No live API; runtime domain resolution is covered by existing producer tests.'
const DOMAIN_VALUES = { readOnlyStatus: 'Approved' }
const DOMAIN_CANDIDATE_FIELDS = [
  ['none', 'No readable values'], ['single', 'One readable value'],
  ['radio', 'Three readable values'], ['choice', 'Literal choices'],
  ['taxonomy', 'Taxonomy'], ['record', 'Record'], ['readOnlyStatus', 'Read-only status'],
] as const
const DOMAIN_VIEW = {
  formId: 'domain-editors',
  version: '1.0.0',
  title: localized('Resolved domain editors'),
  description: localized('Choose a value, then save to review validation. Search text does not change the selected candidate.'),
  sections: [{
    id: 'resolved-domain', title: localized('Readable choices'), fields: [
      field('none', 'No readable values', { controlHint: 'None', permittedValues: [], helpText: localized('No editor is available.') }),
      field('single', 'One readable value', { controlHint: 'SingleValue', permittedValues: ['Approved'], helpText: localized('Choose explicitly; nothing is selected automatically.') }),
      field('radio', 'Three readable values', { controlHint: 'RadioGroup', permittedValues: ['Draft', 'Review', 'Approved'] }),
      field('choice', 'Literal choices', { controlHint: 'ChoiceList', permittedValues: ['Draft', 'Review', 'Approved', 'Rejected', 'Paused', 'Closed'] }),
      field('taxonomy', 'Taxonomy', {
        controlHint: 'TaxonomyPicker',
        permittedValues: Array.from({ length: 80 }, (_, index) => `Category ${String(index + 1).padStart(2, '0')}`),
        helpText: localized('80 categories. Search for Category 08; at most 25 matches are shown.'),
      }),
      field('record', 'Record', {
        controlHint: 'RecordPicker',
        permittedValues: Array.from({ length: 40000 }, (_, index) => String(index).padStart(5, '0')),
        helpText: localized('40,000 records. Search for 3999; at most 25 matches are shown. Try an unmatched query before selecting.'),
      }),
      field('readOnlyStatus', 'Read-only status', { controlHint: 'ChoiceList', permittedValues: ['Draft', 'Review', 'Approved'], readOnly: true }),
      field('restricted', 'Restricted field', { controlHint: 'None', isReadable: false }),
    ],
  }],
} satisfies FormView

function DomainEditorsExample() {
  const [candidate, setCandidate] = React.useState<Record<string, unknown>>(DOMAIN_VALUES)
  const [saveStatus, setSaveStatus] = React.useState('Not saved.')
  const submit: SchemaFormProps['onSubmit'] = values => {
    const isValid = typeof values.record === 'string' && values.record.length > 0
    setSaveStatus(isValid ? 'Saved selected candidate.' : 'Choose a record before saving.')
    return { isValid, errors: isValid ? [] : [{ jsonPointer: '/record', message: 'Choose a record before saving.', kind: 'Schema' }] }
  }
  return (
    <div style={{ minWidth: 0, width: '100%' }}>
      <SchemaForm initialValues={DOMAIN_VALUES} onChange={setCandidate} onSubmit={submit} strings={ENGLISH_STRINGS} view={DOMAIN_VIEW} />
      <section aria-label="Selected candidate" style={{ overflowWrap: 'anywhere' }}>
        <h3>Selected candidate</h3>
        <p>Typing a query leaves these values unchanged. Select a result to update them.</p>
        <dl>{DOMAIN_CANDIDATE_FIELDS.map(([key, label]) => <React.Fragment key={key}><dt>{label}</dt><dd>{String(candidate[key] ?? 'Not selected')}</dd></React.Fragment>)}</dl>
        <p role="status">{saveStatus}</p>
      </section>
    </div>
  )
}
const validationSubmit: SchemaFormProps['onSubmit'] = () => ({
  isValid: false,
  errors: [
    { jsonPointer: '/name', message: 'Enter the caller-provided requester name.', kind: 'Schema' },
    { jsonPointer: '/shipping/address', message: 'Complete the shipping address.', kind: 'Schema' },
    { jsonPointer: '', message: 'The request cannot be saved yet.', kind: 'Schema' },
  ],
})

function SchemaFormScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const titles: Record<ScenarioId, [string, string]> = {
    'schema-form.domain-editors': ['Domain-driven editors', DOMAIN_COPY],
    'schema-form.host-readonly': ['Read-only harbor inspection', 'A completed application exercises every visible built-in field kind without exposing editing or submission.'],
    'schema-form.controls-structure': ['Ordered controls and collections', 'Registry resolution, fallback, nested updates, and bounded rows share one caller-defined order.'],
    'schema-form.rules-gate': ['Rule outcomes and save gate', 'Visibility, required, read-only, computed, and presentation outcomes project before save.'],
    'schema-form.validation': ['Inline and summary validation', 'Save to inspect inline association, summary partitioning, and summary focus.'],
    'schema-form.locale-theme': ['Caller locales and dark theme', 'Pseudo-expanded and Arabic copy exercise locale formatting, logical direction, and shared theme tokens.'],
    'schema-form.content': ['Content resilience', 'Hostile form content carries a label longer than its control, a grouped large number, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.'],
    'schema-form.empty-state': ['Empty state', 'No inspection fields are currently available.'],
    'schema-form.error-state': ['Error state', 'Save the request to review its validation errors.'],
  }
  const [title, description] = titles[scenarioId]
  const localeTheme = scenarioId === 'schema-form.locale-theme'

  return (
    <section
      className="hl-gallery-scene"
      data-gallery-probe
      data-gallery-scenario={scenarioId}
      data-theme={localeTheme ? 'dark' : undefined}
    >
      <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
      <div className="hl-gallery-stage hl-gallery-feedback-grid">
        {scenarioId === 'schema-form.domain-editors' ? (
          <DomainEditorsExample />
        ) : scenarioId === 'schema-form.host-readonly' ? (
          <SchemaForm readOnly initialValues={READ_ONLY_INSPECTION_VALUES} onSubmit={validSubmit} strings={ENGLISH_STRINGS} view={READ_ONLY_INSPECTION_VIEW} />
        ) : scenarioId === 'schema-form.controls-structure' ? (
          <SchemaForm
            controls={{ bespoke: bespokeControl }}
            initialValues={{
              berthCode: 'D-14',
              arrivalWindow: '04:00–06:00',
              cargoShape: { kind: 'bulk' },
              agent: { name: 'North Harbor Agency', sibling: 'preserved' },
              charges: [{ description: 'Pilotage' }],
            }}
            onSubmit={validSubmit}
            strings={ENGLISH_STRINGS}
            view={CONTROLS_VIEW}
          />
        ) : scenarioId === 'schema-form.rules-gate' ? (
          <SchemaForm
            initialValues={{
              contact: '',
              reference: 'REQ-2048',
              total: 0,
              status: 'Awaiting review',
              internalNote: 'Retained in the submitted candidate',
            }}
            onSubmit={validSubmit}
            ruleGraph={RULE_GRAPH}
            strings={ENGLISH_STRINGS}
            view={RULES_VIEW}
          />
        ) : scenarioId === 'schema-form.validation' ? (
          <SchemaForm
            initialValues={{ name: '', email: 'ops@example.test' }}
            onSubmit={validationSubmit}
            strings={ENGLISH_STRINGS}
            view={VALIDATION_VIEW}
          />
        ) : scenarioId === 'schema-form.error-state' ? (
          <SchemaForm
            initialValues={{ name: '', email: 'ops@example.test' }}
            onSubmit={validationSubmit}
            strings={ENGLISH_STRINGS}
            view={VALIDATION_VIEW}
          />
        ) : scenarioId === 'schema-form.empty-state' ? (
          <SchemaForm
            initialValues={{}}
            onSubmit={validSubmit}
            strings={ENGLISH_STRINGS}
            view={EMPTY_VIEW}
          />
        ) : scenarioId === 'schema-form.content' ? (
          <SchemaForm
            initialValues={{ longLabel: 'Certified', largeNumber: '1,284,905', escapedText: 'Bay 4 <grid C-7> & 8', surveyor: 'Ordnance Survey — Niño Ångström' }}
            onSubmit={validSubmit}
            strings={ENGLISH_STRINGS}
            view={CONTENT_VIEW}
          />
        ) : (
          <>
            <SchemaForm
              initialValues={{ replacementCost: 1234567.89, completion: 62.5, inspectionDate: '2026-08-13' }}
              localeChain={['en-XA']}
              onSubmit={validSubmit}
              strings={PSEUDO_STRINGS}
              view={PSEUDO_VIEW}
            />
            <SchemaForm
              initialValues={{ amount: 1234567.89, date: '2026-08-13' }}
              localeChain={['ar']}
              onSubmit={validSubmit}
              strings={ARABIC_STRINGS}
              view={ARABIC_VIEW}
            />
          </>
        )}
      </div>
    </section>
  )
}

const meta = {
  title: 'Platform/Schema Form',
  component: SchemaFormScenario,
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof SchemaFormScenario>

export default meta
type Story = StoryObj<typeof meta>

export const ControlsAndStructure: Story = { name: 'Ordered controls and collections', args: { scenarioId: 'schema-form.controls-structure' } }
export const DomainDrivenEditors: Story = { name: 'Domain-driven editors', args: { scenarioId: 'schema-form.domain-editors' } }
export const ReadOnly: Story = { name: 'Read-only harbor inspection', args: { scenarioId: 'schema-form.host-readonly' } }
export const RulesAndGate: Story = { name: 'Rule outcomes and save gate', args: { scenarioId: 'schema-form.rules-gate' } }
export const Validation: Story = { name: 'Inline and summary validation', args: { scenarioId: 'schema-form.validation' } }
export const LocaleAndTheme: Story = { name: 'Caller locales and dark theme', args: { scenarioId: 'schema-form.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'schema-form.content' } }
;export const EmptyState: Story = { name: 'Empty state', args: { scenarioId: 'schema-form.empty-state' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'schema-form.error-state' } }
