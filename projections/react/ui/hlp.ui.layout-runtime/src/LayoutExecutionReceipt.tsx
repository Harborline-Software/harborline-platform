import { useState } from 'react'
import type { LayoutAccessEvidence, LayoutAccessTrace, LayoutExecutionReceiptProps } from './LayoutRuntime.types'

const stageLabels = ['Act', 'Effective roles', 'Standings', 'Verdict']
const notices: Record<Exclude<LayoutAccessEvidence, 'valid'>, string> = {
  absent: 'No Access decision was recorded for this run.',
  missing: 'No authorization trace is stored for this decision.',
  forbidden: 'You may not read this authorization trace.',
  malformed: 'The stored authorization trace is malformed.',
}

/**
 * DES-0052 layout-eng-30: renders the authorized receipt, run identity, status and execution trace verbatim, and
 * reads the linked Access trace only when its disclosure opens. The platform classifies the read; this derives nothing.
 */
export function LayoutExecutionReceipt({ receipt, readAccessTrace }: LayoutExecutionReceiptProps) {
  const [trace, setTrace] = useState<LayoutAccessTrace | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)
  const decision = receipt.accessDecisionId
  async function load() {
    if (decision === null || loading) return
    setLoading(true)
    setFailed(false)
    try { setTrace(await readAccessTrace(decision)) }
    catch { setFailed(true) }
    finally { setLoading(false) }
  }
  return <section className="hl-layout-receipt" data-layout-receipt>
    <dl>
      <dt>Run</dt><dd data-layout-run-id>{receipt.runId}</dd>
      <dt>Status</dt><dd data-layout-run-status>{receipt.status}</dd>
    </dl>
    <ol aria-label="Execution trace">
      {receipt.trace.map(step => <li key={step.ordinal} data-layout-trace-ordinal={step.ordinal}>{step.phase}: {step.status}</li>)}
    </ol>
    {decision === null
      ? <p data-layout-access-evidence="absent">{notices.absent}</p>
      : <details onToggle={event => { if (event.currentTarget.open && trace === null) void load() }}>
        <summary>Access decision <span data-layout-access-decision>{decision}</span></summary>
        {loading && <p role="status">Loading authorization trace…</p>}
        {failed && <><p role="alert">Unable to read the authorization trace.</p><button type="button" onClick={() => void load()}>Retry trace read</button></>}
        {trace && (trace.evidence === 'valid'
          ? <div data-layout-access-evidence="valid">
            <p data-layout-access-version>Version {trace.version}</p>
            <ol aria-label="Authorization trace">
              {trace.stages.map((stage, index) => <li key={stage.ordinal} data-layout-access-stage={stage.stage}>
                <h4>{stageLabels[index]}</h4>
                {stage.facts.map((fact, factIndex) => <p key={factIndex} style={{ overflowWrap: 'anywhere' }}>{fact}</p>)}
              </li>)}
            </ol>
            <p data-layout-deciding-grant>Deciding grant: {trace.decidingGrant ?? 'None recorded'}</p>
          </div>
          : <p data-layout-access-evidence={trace.evidence}>{notices[trace.evidence]}</p>)}
      </details>}
  </section>
}
