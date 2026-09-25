import type { LayoutRuntimeProps } from './LayoutRuntime.types'

export function LayoutRuntime({ plan, onSubmit }: LayoutRuntimeProps) {
  const source = JSON.stringify({ definitionId: plan.definitionId, definitionVersionId: plan.definitionVersionId })
  if (plan.diagnostics?.length) return <section className="hl-layout-runtime__diagnostics" role="alert" data-definition-source={source}>{plan.diagnostics.map(diagnostic => <p key={`${diagnostic.code}:${diagnostic.pointer}`}>{diagnostic.code} at {diagnostic.pointer}</p>)}</section>
  const Tag = plan.medium === 'page' ? 'article' : 'section'
  // layout-eng-26: read-only unless the platform's authority says this principal may submit.
  const readOnly = plan.authority?.canSubmit !== true ? 'true' : undefined
  return <Tag className={`hl-layout-runtime hl-layout-runtime--${plan.medium}`} data-definition-source={source} data-fragmentainer={plan.medium} data-layout-readonly={readOnly}>
    {plan.staticRegions.map(block => <aside key={block.id} data-layout-static-region={block.zone ?? 'static'} data-layout-kind={block.kind} />)}
    {plan.flow.map(block => <div key={block.id} data-layout-block={block.id} data-layout-kind={block.kind} data-layout-depth={block.depth} data-layout-zone={block.zone} data-layout-readonly={readOnly} />)}
    {readOnly ? null : <button type="button" data-layout-submit onClick={onSubmit}>Submit</button>}
  </Tag>
}
