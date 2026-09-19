import type { ReactNode } from 'react'
import { LayoutAuthoringEditor } from './LayoutAuthoringEditor'
import type { LayoutAuthoringEditorProps } from './LayoutRuntime.types'

/** The host supplies its shell so this aggregate contribution never leaks a private shell package into the public declaration. */
export interface LayoutAuthoringShell { readonly render: (body: ReactNode) => ReactNode }
export interface LayoutAuthoringScreenProps extends LayoutAuthoringEditorProps { readonly shell: LayoutAuthoringShell }
export function LayoutAuthoringScreen({ shell, ...editor }: LayoutAuthoringScreenProps) { return <>{shell.render(<LayoutAuthoringEditor {...editor} />)}</> }
