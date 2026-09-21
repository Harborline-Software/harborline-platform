import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../../projections/react/ui/hlp.ui.button')

// Test dependencies come from the existing UI toolchain. Neither projection imports the other.
export default {
  resolve: {
    alias: {
      vitest: resolve(aggregateRoot, 'node_modules/vitest/dist/index.js'),
      '@testing-library/jest-dom': resolve(aggregateRoot, 'node_modules/@testing-library/jest-dom'),
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./setup.ts'],
    include: ['*.test.ts'],
  },
}
