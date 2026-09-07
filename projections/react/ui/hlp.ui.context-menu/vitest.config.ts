import {resolve} from 'node:path'

const buttonRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(buttonRoot, 'node_modules/react'),
      'react-dom': resolve(buttonRoot, 'node_modules/react-dom'),
      '@testing-library/react': resolve(buttonRoot, 'node_modules/@testing-library/react'),
      '@testing-library/user-event': resolve(buttonRoot, 'node_modules/@testing-library/user-event'),
      '@testing-library/jest-dom': resolve(buttonRoot, 'node_modules/@testing-library/jest-dom'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
  },
}
