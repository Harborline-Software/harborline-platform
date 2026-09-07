import * as React from 'react'

export interface FormFieldContextValue {
  id?: string
  labelId?: string
  describedBy?: string
  required?: boolean
  disabled?: boolean
}

const emptyContext: FormFieldContextValue = Object.freeze({})

export const FormFieldContext = React.createContext<FormFieldContextValue>(emptyContext)

export function useFormFieldContext(): FormFieldContextValue {
  return React.useContext(FormFieldContext)
}
