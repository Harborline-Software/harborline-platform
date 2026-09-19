import { AppLayout, type AppLayoutProps } from '@harborline-platform/hlp.ui.app-layout'
import { LayoutAuthoringEditor } from './LayoutAuthoringEditor'
import type { LayoutAuthoringEditorProps } from './LayoutRuntime.types'

export interface LayoutAuthoringScreenProps extends LayoutAuthoringEditorProps { readonly shell: Omit<AppLayoutProps, 'body'> }
export function LayoutAuthoringScreen({ shell, ...editor }: LayoutAuthoringScreenProps) { return <AppLayout {...shell} body={<LayoutAuthoringEditor {...editor} />} /> }
