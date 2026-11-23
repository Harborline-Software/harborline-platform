import { resolve } from 'node:path'
const aggregate = resolve(import.meta.dirname, '../hlp.ui.button/node_modules')
export default {
  resolve: { alias: {
    react: resolve(aggregate, 'react'),
    'react-dom': resolve(aggregate, 'react-dom'),
    '@testing-library/react': resolve(aggregate, '@testing-library/react'),
    '@testing-library/jest-dom': resolve(aggregate, '@testing-library/jest-dom'),
  } },
  test: { globals: true, environment: 'jsdom', setupFiles: ['./src/test-setup.ts'], css: false },
}
