import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(root, 'node_modules/react'),
      'react-dom': resolve(root, 'node_modules/react-dom'),
      '@testing-library/react': resolve(root, 'node_modules/@testing-library/react'),
      '@testing-library/user-event': resolve(root, 'node_modules/@testing-library/user-event'),
      '@testing-library/jest-dom': resolve(root, 'node_modules/@testing-library/jest-dom'),
      // hlp.ui.cn is resolved from SOURCE, and its source imports these two. Without the aliases
      // vitest fails to resolve them from this root and the suite reports "no tests" — which the
      // gate then counts as zero results rather than as a failure. hlp.ui.action-menu does the same.
      clsx: resolve(root, 'node_modules/clsx'),
      'tailwind-merge': resolve(root, 'node_modules/tailwind-merge'),
      // Source-mapped rather than dist-mapped, matching hlp.ui.dialog: a test run must not depend
      // on a sibling module having been built first.
      '@harborline-platform/hlp.ui.cn': resolve(import.meta.dirname, '../hlp.ui.cn/src/index.ts'),
      '@harborline-platform/hlp.ui.dialog': resolve(import.meta.dirname, '../hlp.ui.dialog/src/index.ts'),
      '@harborline-platform/hlp.ui.default-strings': resolve(import.meta.dirname, '../hlp.ui.default-strings/src/index.ts'),
      '@harborline-platform/hlp.ui.locale-provider': resolve(import.meta.dirname, '../hlp.ui.locale-provider/src/index.ts'),
      // Transitively required: hlp.ui.dialog imports it, and resolving dialog from source means
      // its own imports must resolve too.
      '@harborline-platform/hlp.ui.use-scroll-affordance': resolve(import.meta.dirname, '../hlp.ui.use-scroll-affordance/src/index.ts'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
  },
}
