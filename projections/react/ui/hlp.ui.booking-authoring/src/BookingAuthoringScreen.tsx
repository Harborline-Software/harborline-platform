import type { ReactNode } from 'react'
import { BookingAuthoringEditor, type BookingAuthoringEditorProps } from './BookingAuthoringEditor'

/** The host supplies its shell, so this aggregate contribution never leaks a private shell package (the LayoutAuthoringShell seam). */
export interface BookingAuthoringShell { readonly render: (body: ReactNode) => ReactNode }
export interface BookingAuthoringScreenProps extends BookingAuthoringEditorProps { readonly shell: BookingAuthoringShell }
/** The Booking authoring editor inside the host's shell. */
export function BookingAuthoringScreen({ shell, ...editor }: BookingAuthoringScreenProps) { return <>{shell.render(<BookingAuthoringEditor {...editor} />)}</> }
