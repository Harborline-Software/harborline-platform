import { useState, type ChangeEvent } from 'react'
import { GuidedExpressionEditor } from '@harborline-platform/hlp.ui.rule-authoring'
import { formulaDraftToSkin, type FormulaExpr, type RuleDefinitionExpression } from '@harborline-software/rule-authoring'
import type { LayoutAuthoringBlock, LayoutAuthoringCapture, LayoutAuthoringCatalogue, LayoutAuthoringEditorProps, LayoutAuthoringDraft, LayoutAuthoringPageRun, LayoutBindingKind, LayoutContainerFlow, LayoutIntent, LayoutPredicatePin } from './LayoutRuntime.types'

const bindingKinds: readonly { readonly id: LayoutBindingKind; readonly label: string }[] = [
  { id: 'record_field', label: 'Record field' },
  { id: 'query', label: 'Query' },
  { id: 'measure', label: 'Measure' },
  { id: 'template', label: 'Template' },
  { id: 'static', label: 'Static content' },
]

const tokens = ['sm', 'md', 'lg'] as const
const canonicalKinds = ['layout.table', 'layout.list', 'layout.board', 'layout.calendar', 'layout.map', 'layout.dashboard-widget', 'layout.file-library']
const intents = ['capture', 'observe', 'issue'] as const
const flows = ['stack', 'flow', 'areas'] as const
const alignments = ['start', 'center', 'end', 'stretch'] as const
const sizings = ['hug', 'fill', 'fixed'] as const
const gaps = [0, 1, 2, 3, 4, 5, 6, 7, 8] as const

/** layout-auth-18: only a collection can repeat, and a query or a record field is the collection. */
const collectionKinds: readonly LayoutBindingKind[] = ['query', 'record_field']

// The guided editor speaks the definition shape; Rules' formula lowering takes its editor shape.
// ponytail: mirrors rule-authoring's private editorExpression, since that frozen module exports no guard lowering.
function formulaExpression(value: RuleDefinitionExpression): FormulaExpr {
  const type = { Number: 'number', Text: 'text', Boolean: 'boolean' } as const
  switch (value.kind) {
    case 'Ref': return { kind: 'ref', ref: value.name }
    case 'Literal': return { kind: 'literal', value: value.value, valueType: type[value.valueType] }
    case 'Binary': return { kind: 'binary', op: value.op, left: formulaExpression(value.left), right: formulaExpression(value.right) }
    case 'Call': return { kind: 'call', op: value.op, args: value.args.map(formulaExpression) }
    case 'If': return { kind: 'if', when: { op: value.when.op, left: formulaExpression(value.when.left), right: formulaExpression(value.when.right) }, then: formulaExpression(value.then), else: formulaExpression(value.else) }
  }
}

/** Lowers a guided guard to the Rules text publication compiles (T-724 ruling 39). */
function guardText(guide: RuleDefinitionExpression): string {
  return JSON.stringify(formulaDraftToSkin({ skin: 'formula', scope: 'Schema', scopeTarget: '', outputType: 'Validate', inputs: [], expression: formulaExpression(guide) }, 'guard').expression)
}

/** A repeating block or a parent needs a container; the default is a stack (T-724 ruling 41). */
function withContainer(block: LayoutAuthoringBlock): LayoutAuthoringBlock {
  return block.container ? block : { ...block, container: 'stack' }
}

/** Whether `block` sits anywhere inside `ancestorId`, bounded so a malformed cycle cannot spin. */
function isInside(blocks: readonly LayoutAuthoringBlock[], block: LayoutAuthoringBlock, ancestorId: string): boolean {
  let current: LayoutAuthoringBlock | undefined = block
  for (let step = 0; current?.parentId !== undefined && step < blocks.length; step++) {
    if (current.parentId === ancestorId) return true
    current = blocks.find(candidate => candidate.id === current!.parentId)
  }
  return false
}

/**
 * layout-auth-18: a repeating block binds a collection, and its row subtree is authored once as
 * its children. A block can be placed inside any block but itself or its own descendant.
 */
function BlockStructure({ index, block, blocks, onChange, onBlocks }: { index: number; block: LayoutAuthoringBlock; blocks: readonly LayoutAuthoringBlock[]; onChange: (next: LayoutAuthoringBlock) => void; onBlocks: (next: readonly LayoutAuthoringBlock[]) => void }) {
  const parents = blocks.map((candidate, position) => ({ candidate, position })).filter(({ candidate }) => candidate.id !== block.id && !isInside(blocks, candidate, block.id))
  // T-724 ruling 41: a block that repeats or has children gets the container admission requires.
  const place = (parentId: string) => onBlocks(blocks.map(candidate => candidate.id === block.id ? { ...candidate, parentId: parentId || undefined } : candidate.id === parentId ? withContainer(candidate) : candidate))
  return <>
    <select aria-label={`Block ${index + 1} parent`} value={block.parentId ?? ''} onChange={event => place(event.currentTarget.value)}>
      <option value="">Surface root</option>
      {parents.map(({ candidate, position }) => <option key={candidate.id} value={candidate.id}>{`Block ${position + 1}`}</option>)}
    </select>
    {block.binding && collectionKinds.includes(block.binding.kind) && <label><input aria-label={`Block ${index + 1} repeats per row`} type="checkbox" checked={block.repeating ?? false} onChange={event => onChange(event.currentTarget.checked ? withContainer({ ...block, repeating: true }) : { ...block, repeating: undefined })} />Repeats per row</label>}
    {(block.repeating || blocks.some(child => child.parentId === block.id)) && <select aria-label={`Block ${index + 1} container`} value={block.container ?? 'stack'} onChange={event => onChange({ ...block, container: event.currentTarget.value as LayoutContainerFlow })}>{flows.map(flow => <option key={flow} value={flow}>{flow}</option>)}</select>}
  </>
}

/**
 * A block keeps only the members its intent admits: a related block observes (layout-auth-19)
 * and only a capture block carries capture properties (layout-auth-21).
 */
function withIntent(block: LayoutAuthoringBlock, intent: LayoutIntent): LayoutAuthoringBlock {
  const { relatedRelationship, capture, ...rest } = block
  return { ...rest, intent, ...(intent === 'observe' && relatedRelationship ? { relatedRelationship } : {}), ...(intent === 'capture' && capture ? { capture } : {}) }
}

/** Sets the filter edges, storing none rather than an empty list (layout-auth-34). */
function withFilterTargets(block: LayoutAuthoringBlock, targets: readonly string[]): LayoutAuthoringBlock {
  const { filterTargets, ...rest } = block
  return targets.length > 0 ? { ...rest, filterTargets: targets } : filterTargets ? rest : block
}

/** Sets capture members, dropping empty ones so an unnarrowed block carries no capture object. */
function withCapture(block: LayoutAuthoringBlock, patch: Partial<LayoutAuthoringCapture>): LayoutAuthoringBlock {
  const { capture, ...rest } = block
  const next = { ...capture, ...patch }
  const kept: LayoutAuthoringCapture = { ...(next.required ? { required: true } : {}), ...(next.validationRules?.length ? { validationRules: next.validationRules } : {}), ...(next.promptOverride ? { promptOverride: next.promptOverride } : {}), ...(next.control ? { control: next.control } : {}) }
  return Object.keys(kept).length > 0 ? { ...rest, capture: kept } : rest
}

/**
 * The block's behaviour on this surface. layout-auth-19: an observing block may traverse one
 * Records relationship the catalogue declares, and stores only its key; the relationship
 * declaration supplies the target, so the editor never asks for or writes one.
 * layout-auth-20: `show_when` is authored with the shared guided expression editor and lowered
 * through Rules' own formula lowering into its `expression`; raw Rules text, stored exactly as
 * written, is the escape hatch. The editor keeps no grammar of its own, and publication compiles
 * the result. layout-ck-29: a guard may instead cite a catalogue predicate by its exact pin; the
 * guard always holds exactly one form.
 * layout-auth-21: a capture block may add a requirement and name registered validation rules;
 * a requirement Records declares is shown and cannot be removed here. layout-auth-22: its prompt
 * override is stored on the block, so it applies to this surface's context and nowhere else.
 * layout-bound-3: a captured field picks its control from the controls the host registers,
 * unless a value domain governs the field: its resolver picks the editor (layout-bound-10).
 * layout-auth-33: the selection the block opens with is authored here; what a reader has
 * selected at run time is execution state and is never offered for saving.
 * layout-auth-34: the blocks this block's selection filters are chosen from the surface's other
 * blocks, never itself, so every edge stays on the surface.
 */
function BlockBehaviour({ index, block, blocks, intent, catalogue, onChange }: { index: number; block: LayoutAuthoringBlock; blocks: readonly LayoutAuthoringBlock[]; intent: LayoutIntent; catalogue: LayoutAuthoringCatalogue; onChange: (next: LayoutAuthoringBlock) => void }) {
  const targets = block.filterTargets ?? []
  // layout-auth-20 (T-724 ruling 39): the guided editor is the default; raw Rules text is the escape hatch.
  // layout-ck-29: a named predicate by exact pin is the third form, picked from the catalogue.
  const [guardMode, setGuardMode] = useState(block.showWhen?.predicate ? 'predicate' : block.showWhen !== undefined && block.showWhenGuide === undefined ? 'raw' : 'guided')
  const pinId = (pin: LayoutPredicatePin) => `${pin.name}@${pin.version}`
  const predicates = catalogue.predicates ?? []
  const { showWhen: _text, showWhenGuide: _guide, ...unguarded } = block
  const guardContract = { site: 'rule' as const, returnContract: 'a boolean', executionTimeContract: 'render', palette: catalogue.guardReferences ?? [] }
  const declaredRequired = block.binding?.kind === 'record_field' && (catalogue.requiredFields ?? []).includes(block.binding.name)
  const rules = block.capture?.validationRules ?? []
  return <>
    {intent === 'observe' && <select aria-label={`Block ${index + 1} related record`} value={block.relatedRelationship ?? ''} onChange={event => onChange({ ...block, relatedRelationship: event.currentTarget.value || undefined })}>
      <option value="">Not related</option>
      {(catalogue.relationships ?? []).map(relationship => <option key={relationship.id} value={relationship.id}>{relationship.label}</option>)}
    </select>}
    <label>Default selection<input aria-label={`Block ${index + 1} default selection`} value={block.defaultSelection ?? ''} onChange={event => onChange({ ...block, defaultSelection: event.currentTarget.value || undefined })} /></label>
    {blocks.map((other, position) => other.id !== block.id && <label key={other.id}><input aria-label={`Block ${index + 1} filters Block ${position + 1}`} type="checkbox" checked={targets.includes(other.id)} onChange={event => onChange(withFilterTargets(block, event.currentTarget.checked ? [...targets, other.id] : targets.filter(id => id !== other.id)))} />{`Filters Block ${position + 1}`}</label>)}
    <select aria-label={`Block ${index + 1} show when authoring`} value={guardMode} onChange={event => setGuardMode(event.currentTarget.value)}><option value="guided">Guided expression</option><option value="raw">Rules text</option><option value="predicate">Named predicate</option></select>
    {guardMode === 'raw' && <label>Show when<input aria-label={`Block ${index + 1} show when`} value={block.showWhen?.expression ?? ''} onChange={event => onChange(event.currentTarget.value ? { ...unguarded, showWhen: { expression: event.currentTarget.value } } : unguarded)} /></label>}
    {guardMode === 'guided' && <GuidedExpressionEditor site="rule" label={`Block ${index + 1} show when`} value={block.showWhenGuide ?? null} contract={guardContract} onChange={guide => onChange({ ...unguarded, showWhen: { expression: guardText(guide) }, showWhenGuide: guide })} />}
    {guardMode === 'predicate' && <select aria-label={`Block ${index + 1} show when predicate`} value={block.showWhen?.predicate ? pinId(block.showWhen.predicate) : ''} onChange={event => { const picked = predicates.find(option => pinId(option.pin) === event.currentTarget.value); onChange(picked ? { ...unguarded, showWhen: { predicate: picked.pin } } : unguarded) }}>
      <option value="">No predicate</option>
      {predicates.map(option => <option key={pinId(option.pin)} value={pinId(option.pin)}>{option.label}</option>)}
    </select>}
    {block.showWhen !== undefined && <button type="button" aria-label={`Remove block ${index + 1} show when`} onClick={() => onChange(unguarded)}>Remove guard</button>}
    {intent === 'capture' && <>
      <label><input aria-label={`Block ${index + 1} required`} type="checkbox" checked={declaredRequired || (block.capture?.required ?? false)} disabled={declaredRequired} onChange={event => onChange(withCapture(block, { required: event.currentTarget.checked }))} />Required</label>
      {block.binding?.kind === 'record_field' && (catalogue.valueDomainFields ?? []).includes(block.binding.name) && <span>{`Block ${index + 1} control is chosen by its value domain`}</span>}
      {block.binding?.kind === 'record_field' && !(catalogue.valueDomainFields ?? []).includes(block.binding.name) && <select aria-label={`Block ${index + 1} field control`} value={block.capture?.control ?? ''} onChange={event => onChange(withCapture(block, { control: event.currentTarget.value }))}>
        <option value="">Runtime default</option>
        {(catalogue.fieldControls ?? []).map(control => <option key={control.id} value={control.id}>{control.label}</option>)}
      </select>}
      <label>Prompt override<input aria-label={`Block ${index + 1} prompt override`} value={block.capture?.promptOverride ?? ''} onChange={event => onChange(withCapture(block, { promptOverride: event.currentTarget.value }))} /></label>
      {(catalogue.validationRules ?? []).map(rule => <label key={rule.id}><input aria-label={`Block ${index + 1} validation rule ${rule.label}`} type="checkbox" checked={rules.includes(rule.id)} onChange={event => onChange(withCapture(block, { validationRules: event.currentTarget.checked ? [...rules, rule.id] : rules.filter(id => id !== rule.id) }))} />{rule.label}</label>)}
    </>}
  </>
}

export function emptyLayoutAuthoringDraft(): LayoutAuthoringDraft { return { name: '', medium: 'screen', defaultIntent: 'observe', containerFlow: 'stack', blocks: [] } }

/** Rebinding keeps the block, clears the name, and stops a repeat the new kind cannot carry. */
function rebind(block: LayoutAuthoringBlock, kind: LayoutBindingKind | ''): LayoutAuthoringBlock {
  const { repeating, ...rest } = block
  if (kind === '') return { ...rest, binding: undefined }
  return { ...rest, binding: { kind, name: '' }, ...(repeating && collectionKinds.includes(kind) ? { repeating } : {}) }
}

/**
 * Binds one block to a record field, query, measure, template or static content
 * (layout-auth-13 to layout-auth-16, layout-ck-21 to layout-ck-25). Changing the kind keeps
 * the block and clears its name rather than deleting it, so an author rebinds one block at a
 * time and an unbound block stays visible as `needs a binding` (layout-auth-31).
 */
function BindingPicker({ index, block, catalogue, onChange }: { index: number; block: LayoutAuthoringBlock; catalogue: LayoutAuthoringEditorProps['catalogue']; onChange: (next: LayoutAuthoringBlock) => void }) {
  const binding = block.binding
  const label = `Block ${index + 1} binding`
  const names = binding && binding.kind !== 'static' ? catalogue.bindables?.[binding.kind] ?? [] : []
  return <>
    <select aria-label={`${label} kind`} value={binding?.kind ?? ''} onChange={event => onChange(rebind(block, event.currentTarget.value as LayoutBindingKind | ''))}>
      <option value="">Needs a binding</option>
      {bindingKinds.map(kind => <option key={kind.id} value={kind.id}>{kind.label}</option>)}
    </select>
    {binding?.kind === 'static'
      ? <label>Static content<input aria-label={`${label} content`} value={binding.name} onChange={event => onChange({ ...block, binding: { kind: 'static', name: event.currentTarget.value } })} /></label>
      : binding && <select aria-label={`${label} name`} value={binding.name} onChange={event => onChange({ ...block, binding: { kind: binding.kind, name: event.currentTarget.value } })}>
          <option value="">Needs a binding</option>
          {names.map(name => <option key={name.id} value={name.id}>{name.label}</option>)}
        </select>}
    {binding && binding.name === '' && <span role="status">{`Block ${index + 1} needs a binding`}</span>}
  </>
}

export function LayoutAuthoringEditor({ value, catalogue, onChange }: LayoutAuthoringEditorProps) {
  const set = <K extends keyof LayoutAuthoringDraft>(key: K, next: LayoutAuthoringDraft[K]) => onChange({ ...value, [key]: next })
  const changeBlock = (index: number, next: LayoutAuthoringBlock) => set('blocks', value.blocks.map((block, current) => current === index ? next : block))
  const changePageRun = (index: number, next: LayoutAuthoringPageRun) => set('pageRuns', (value.pageRuns ?? []).map((run, current) => current === index ? next : run))
  const choice = (event: ChangeEvent<HTMLSelectElement>) => event.currentTarget.value
  const kinds = catalogue.blockKinds.filter(kind => canonicalKinds.includes(kind.id))
  const drills = value.drillThroughTargets ?? []
  // layout-auth-35: drill-through targets belong to the surface and name released surfaces only.
  const setDrills = (next: readonly string[]) => { const { drillThroughTargets, ...rest } = value; onChange(next.length > 0 ? { ...rest, drillThroughTargets: next } : drillThroughTargets ? rest : value) }
  return <form className="hl-layout-authoring" onSubmit={event => event.preventDefault()}>
    <label>Layout name<input aria-label="Layout name" value={value.name} onChange={event => set('name', event.currentTarget.value)} /></label>
    <label>Medium<select aria-label="Layout medium" value={value.medium} onChange={event => set('medium', choice(event) as LayoutAuthoringDraft['medium'])}><option value="screen">Screen</option><option value="page">Page</option></select></label>
    <label>Default intent<select aria-label="Default intent" value={value.defaultIntent ?? 'observe'} onChange={event => set('defaultIntent', choice(event) as LayoutAuthoringDraft['defaultIntent'])}>{intents.map(intent => <option key={intent} value={intent}>{intent}</option>)}</select></label>
    <label>Container flow<select aria-label="Container flow" value={value.containerFlow ?? 'stack'} onChange={event => set('containerFlow', choice(event) as LayoutAuthoringDraft['containerFlow'])}>{flows.map(flow => <option key={flow} value={flow}>{flow}</option>)}</select></label>
    <label>Collapse below<select aria-label="Collapse below" value={value.collapseBelow ?? ''} onChange={event => set('collapseBelow', choice(event) === '' ? undefined : choice(event) as LayoutAuthoringDraft['collapseBelow'])}><option value="">No collapse</option>{tokens.map(token => <option key={token} value={token}>{token}</option>)}</select></label>
    <label>Gap<select aria-label="Container gap" value={value.gap ?? 0} onChange={event => set('gap', Number(choice(event)))}>{gaps.map(gap => <option key={gap} value={gap}>{gap}</option>)}</select></label>
    <label>Density<select aria-label="Container density" value={value.density ?? ''} onChange={event => set('density', choice(event) === '' ? undefined : choice(event) as LayoutAuthoringDraft['density'])}><option value="">Default density</option><option value="comfortable">comfortable</option><option value="compact">compact</option></select></label>
    <fieldset><legend>Blocks</legend>{value.blocks.map((block, index) => <div key={block.id}><select aria-label={`Block ${index + 1} kind`} value={block.kind} onChange={event => changeBlock(index, { ...block, kind: choice(event), widgetId: choice(event) === 'layout.dashboard-widget' ? block.widgetId : undefined })}><option value="">Choose a block</option>{kinds.map(kind => <option key={kind.id} value={kind.id}>{kind.label}</option>)}</select>{block.kind === 'layout.dashboard-widget' && <select aria-label={`Block ${index + 1} Helm widget`} value={block.widgetId ?? ''} onChange={event => changeBlock(index, { ...block, widgetId: choice(event) || undefined })}><option value="">Choose a registered widget</option>{(catalogue.helmWidgets ?? []).map(widget => <option key={widget.id} value={widget.id}>{widget.label}</option>)}</select>}<select aria-label={`Block ${index + 1} intent`} value={block.intent ?? value.defaultIntent ?? 'observe'} onChange={event => changeBlock(index, withIntent(block, choice(event) as LayoutIntent))}>{intents.map(intent => <option key={intent} value={intent}>{intent}</option>)}</select><BindingPicker index={index} block={block} catalogue={catalogue} onChange={next => changeBlock(index, next)} /><BlockStructure index={index} block={block} blocks={value.blocks} onChange={next => changeBlock(index, next)} onBlocks={next => set('blocks', next)} /><BlockBehaviour index={index} block={block} blocks={value.blocks} intent={block.intent ?? value.defaultIntent ?? 'observe'} catalogue={catalogue} onChange={next => changeBlock(index, next)} /><select aria-label={`Block ${index + 1} zone`} value={block.zone ?? ''} onChange={event => changeBlock(index, { ...block, zone: choice(event) || undefined })}><option value="">No zone</option>{catalogue.zones.map(zone => <option key={zone} value={zone}>{zone}</option>)}</select><select aria-label={`Block ${index + 1} width`} value={block.width ?? 'hug'} onChange={event => changeBlock(index, { ...block, width: choice(event) as LayoutAuthoringBlock['width'] })}>{sizings.map(token => <option key={token} value={token}>{token}</option>)}</select><select aria-label={`Block ${index + 1} alignment`} value={block.alignSelf ?? 'stretch'} onChange={event => changeBlock(index, { ...block, alignSelf: choice(event) as LayoutAuthoringBlock['alignSelf'] })}>{alignments.map(token => <option key={token} value={token}>{token}</option>)}</select><select aria-label={`Block ${index + 1} static region`} value={block.staticRegion ?? ''} onChange={event => changeBlock(index, { ...block, staticRegion: choice(event) || undefined })}><option value="">Flow content</option>{(catalogue.staticRegions ?? []).map(region => <option key={region} value={region}>{region}</option>)}</select><label><input aria-label={`Block ${index + 1} break before`} type="checkbox" checked={block.breakBefore ?? false} onChange={event => changeBlock(index, { ...block, breakBefore: event.currentTarget.checked })} />Start page run on a new sheet</label><label><input aria-label={`Block ${index + 1} avoid page break`} type="checkbox" checked={block.avoidPageBreak ?? false} onChange={event => changeBlock(index, { ...block, avoidPageBreak: event.currentTarget.checked })} />Keep row whole</label><button type="button" aria-label={`Remove block ${index + 1}`} onClick={() => set('blocks', value.blocks.filter((_, current) => current !== index).map(other => withFilterTargets(other, (other.filterTargets ?? []).filter(id => id !== block.id))))}>Remove</button></div>)}<button type="button" onClick={() => set('blocks', [...value.blocks, { id: `block-${value.blocks.length + 1}`, kind: kinds[0]?.id ?? 'layout.table' }])}>Add block</button></fieldset>
    <fieldset><legend>Drill-through targets</legend>{(catalogue.drillTargets ?? []).map(target => <label key={target.id}><input aria-label={`Drill through to ${target.label}`} type="checkbox" checked={drills.includes(target.id)} onChange={event => setDrills(event.currentTarget.checked ? [...drills, target.id] : drills.filter(id => id !== target.id))} />{target.label}</label>)}</fieldset>
    {value.medium === 'page' && <fieldset><legend>Page runs</legend>{(value.pageRuns ?? []).map((run, index) => <div key={run.id}><select aria-label={`Page run ${index + 1} layout`} value={run.pageLayoutId} onChange={event => changePageRun(index, { ...run, pageLayoutId: choice(event) })}>{(catalogue.pageLayouts ?? []).map(layout => <option key={layout.id} value={layout.id}>{layout.label}</option>)}</select><select aria-label={`Page run ${index + 1} master`} value={run.pageMasterId} onChange={event => changePageRun(index, { ...run, pageMasterId: choice(event) })}>{(catalogue.pageMasters ?? []).map(master => <option key={master.id} value={master.id}>{master.label}</option>)}</select><button type="button" onClick={() => set('pageRuns', (value.pageRuns ?? []).filter((_, current) => current !== index))}>Remove run</button></div>)}<button type="button" onClick={() => set('pageRuns', [...(value.pageRuns ?? []), { id: `run-${(value.pageRuns?.length ?? 0) + 1}`, pageLayoutId: catalogue.pageLayouts?.[0]?.id ?? '', pageMasterId: catalogue.pageMasters?.[0]?.id ?? '' }])}>Add page run</button></fieldset>}
  </form>
}
