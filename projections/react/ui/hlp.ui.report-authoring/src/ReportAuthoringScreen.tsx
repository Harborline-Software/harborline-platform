import type { ReactNode } from 'react'
import { ReportAuthoringEditor, type ReportAuthoringEditorProps } from './ReportAuthoringEditor'

export interface ReportAuthoringScreenProps extends ReportAuthoringEditorProps { shell?: { render: (body: ReactNode) => ReactNode } }
export function ReportAuthoringScreen({ shell, ...editor }: ReportAuthoringScreenProps) {
  const body = <ReportAuthoringEditor {...editor} />
  return <>{shell ? shell.render(body) : body}</>
}
