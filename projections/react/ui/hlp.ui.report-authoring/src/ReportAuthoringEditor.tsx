export interface ReportDraft { tenant: string; key: string; version: string; measures: readonly { measure: string }[] }
export interface ReportAuthoringEditorProps { value: ReportDraft | null; catalogue: readonly string[]; onCreate: () => void; onChange: (value: ReportDraft) => void }
export function ReportAuthoringEditor({ value, onCreate }: ReportAuthoringEditorProps) {
  return value ? <section aria-label="Report authoring" /> : <button type="button" onClick={onCreate}>Create report</button>
}
