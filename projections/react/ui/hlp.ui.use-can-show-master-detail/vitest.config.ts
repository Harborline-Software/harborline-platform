import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(aggregateRoot, 'node_modules/react'),
      'react-dom': resolve(aggregateRoot, 'node_modules/react-dom'),
      '@testing-library/react': resolve(aggregateRoot, 'node_modules/@testing-library/react'),
      '@harborline-platform/hlp.ui.use-media-query': resolve(
        import.meta.dirname,
        '../hlp.ui.use-media-query/src/index.ts',
      ),
    },
  },
  test: {
    environment: 'jsdom',
  },
}
