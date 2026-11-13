import type { ReactNode } from 'react'
import { DocumentAuthoringEditor, type DocumentAuthoringEditorProps } from './DocumentAuthoringEditor'

export interface DocumentAuthoringScreenProps extends DocumentAuthoringEditorProps { readonly shell: { readonly render: (body: ReactNode) => ReactNode } }
export function DocumentAuthoringScreen({ shell, ...editor }: DocumentAuthoringScreenProps) { return <>{shell.render(<DocumentAuthoringEditor {...editor} />)}</> }
