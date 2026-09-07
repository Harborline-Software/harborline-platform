import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(aggregateRoot, 'node_modules/react'),
      'react-dom': resolve(aggregateRoot, 'node_modules/react-dom'),
      '@testing-library/react': resolve(aggregateRoot, 'node_modules/@testing-library/react'),
      '@harborline-platform/hlp.ui.default-strings': resolve(import.meta.dirname, '../hlp.ui.default-strings/src/index.ts'),
      '@harborline-platform/hlp.ui.locale-provider': resolve(import.meta.dirname, '../hlp.ui.locale-provider/src/index.ts'),
    },
  },
  test: { environment: 'jsdom' },
}
