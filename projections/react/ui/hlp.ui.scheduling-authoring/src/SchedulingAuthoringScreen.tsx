import type { ReactNode } from 'react'
import { SchedulingAuthoringEditor, type SchedulingAuthoringEditorProps } from './SchedulingAuthoringEditor'

/** The host supplies its shell, so this aggregate contribution never leaks a private shell package (the LayoutAuthoringShell seam). */
export interface SchedulingAuthoringShell { readonly render: (body: ReactNode) => ReactNode }
export interface SchedulingAuthoringScreenProps extends SchedulingAuthoringEditorProps { readonly shell: SchedulingAuthoringShell }
/** The Schedule authoring editor inside the host's shell. */
export function SchedulingAuthoringScreen({ shell, ...editor }: SchedulingAuthoringScreenProps) { return <>{shell.render(<SchedulingAuthoringEditor {...editor} />)}</> }
