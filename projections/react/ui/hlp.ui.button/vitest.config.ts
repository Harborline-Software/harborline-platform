import { defineConfig } from 'vitest/config'
import { resolve } from 'node:path'

export default defineConfig({
  resolve: {
    alias: {
      react: resolve(import.meta.dirname, 'node_modules/react'),
      'react-dom': resolve(import.meta.dirname, 'node_modules/react-dom'),
      '@testing-library/react': resolve(import.meta.dirname, 'node_modules/@testing-library/react'),
      '@harborline-platform/hlp.ui.default-strings': resolve(import.meta.dirname, '../hlp.ui.default-strings/src/index.ts'),
      '@harborline-platform/hlp.ui.locale-provider': resolve(import.meta.dirname, '../hlp.ui.locale-provider/src/index.ts'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
  },
})
