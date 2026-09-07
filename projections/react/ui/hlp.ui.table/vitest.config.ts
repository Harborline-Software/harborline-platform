import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      '@harborline-platform/hlp.ui.cn': resolve(import.meta.dirname, '../hlp.ui.cn/src/index.ts'),
      clsx: resolve(aggregateRoot, 'node_modules/clsx'),
      react: resolve(aggregateRoot, 'node_modules/react'),
      'react-dom': resolve(aggregateRoot, 'node_modules/react-dom'),
      'tailwind-merge': resolve(aggregateRoot, 'node_modules/tailwind-merge'),
      '@testing-library/react': resolve(aggregateRoot, 'node_modules/@testing-library/react'),
      '@testing-library/user-event': resolve(aggregateRoot, 'node_modules/@testing-library/user-event'),
      '@testing-library/jest-dom': resolve(aggregateRoot, 'node_modules/@testing-library/jest-dom'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
  },
}
