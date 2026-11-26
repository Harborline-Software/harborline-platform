import * as React from 'react'
import type { SchemaFormProps } from './SchemaForm.types'

export const RecordCreateContext = React.createContext<Pick<SchemaFormProps, 'recordCreateCapabilities' | 'onRecordCreate'>>({})
