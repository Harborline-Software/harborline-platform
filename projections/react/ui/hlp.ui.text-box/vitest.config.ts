import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(aggregateRoot, 'node_modules/react'),
      'react-dom': resolve(aggregateRoot, 'node_modules/react-dom'),
      '@testing-library/react': resolve(aggregateRoot, 'node_modules/@testing-library/react'),
      '@testing-library/user-event': resolve(aggregateRoot, 'node_modules/@testing-library/user-event'),
      '@testing-library/jest-dom': resolve(aggregateRoot, 'node_modules/@testing-library/jest-dom'),
      clsx: resolve(aggregateRoot, 'node_modules/clsx'),
      'tailwind-merge': resolve(aggregateRoot, 'node_modules/tailwind-merge'),
      '@harborline-platform/hlp.ui.cn': resolve(import.meta.dirname, '../hlp.ui.cn/src/index.ts'),
      '@harborline-platform/hlp.ui.default-strings': resolve(import.meta.dirname, '../hlp.ui.default-strings/src/index.ts'),
      '@harborline-platform/hlp.ui.form-field-context': resolve(import.meta.dirname, '../hlp.ui.form-field/src/FormFieldContext.tsx'),
      '@harborline-platform/hlp.ui.input': resolve(import.meta.dirname, '../hlp.ui.input/src/index.ts'),
      '@harborline-platform/hlp.ui.locale-provider': resolve(import.meta.dirname, '../hlp.ui.locale-provider/src/index.ts'),
      '@harborline-platform/hlp.ui.use-can-show-master-detail': resolve(import.meta.dirname, '../hlp.ui.use-can-show-master-detail/src/index.ts'),
      '@harborline-platform/hlp.ui.use-media-query': resolve(import.meta.dirname, '../hlp.ui.use-media-query/src/index.ts'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
  },
}
