import type { LayoutRuntimeProps } from './LayoutRuntime.types'

export function LayoutRuntime({ plan }: LayoutRuntimeProps) {
  const source = JSON.stringify({ definitionId: plan.definitionId, definitionVersionId: plan.definitionVersionId })
  if (plan.diagnostics?.length) return <section className="hl-layout-runtime__diagnostics" role="alert" data-definition-source={source}>{plan.diagnostics.map(diagnostic => <p key={`${diagnostic.code}:${diagnostic.pointer}`}>{diagnostic.code} at {diagnostic.pointer}</p>)}</section>
  const Tag = plan.medium === 'page' ? 'article' : 'section'
  return <Tag className={`hl-layout-runtime hl-layout-runtime--${plan.medium}`} data-definition-source={source} data-fragmentainer={plan.medium}>
    {plan.staticRegions.map(block => <aside key={block.id} data-layout-static-region={block.zone ?? 'static'} data-layout-kind={block.kind} />)}
    {plan.flow.map(block => <div key={block.id} data-layout-block={block.id} data-layout-kind={block.kind} data-layout-depth={block.depth} data-layout-zone={block.zone} />)}
  </Tag>
}
