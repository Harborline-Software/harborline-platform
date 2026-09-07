import { resolve } from 'node:path'

const buttonRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      clsx: resolve(buttonRoot, 'node_modules/clsx/dist/clsx.mjs'),
      'tailwind-merge': resolve(buttonRoot, 'node_modules/tailwind-merge/dist/bundle-mjs.mjs'),
    },
  },
  test: {
    globals: true,
    environment: 'node',
  },
}
