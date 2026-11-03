/** Locale-keyed text shared by wire contracts. */
export interface InternationalizedText {
  defaultLocale: string
  values: Record<string, string>
}
