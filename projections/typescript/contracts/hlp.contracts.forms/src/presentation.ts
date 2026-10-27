import type { InternationalizedText } from './text.js'

export interface PresentationHint {
  severity?: 'info' | 'warn' | 'error'
  badge?: InternationalizedText
  styleToken?: string
}
