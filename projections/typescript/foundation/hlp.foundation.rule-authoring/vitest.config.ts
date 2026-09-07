import { defineConfig } from 'vitest/config'
import { resolve } from 'node:path'

export default defineConfig({
  resolve: {
    alias: {
      // Dev-time sibling resolution (the packed peer is the production shape; the verify
      // fixtures install the real tarball). Same pattern as the React lane's aliases.
      '@harborline-software/rule-engine': resolve(
        import.meta.dirname,
        '../hlp.foundation.rule-runtime/src/index.ts',
      ),
    },
  },
  test: {
    globals: false,
    environment: 'node',
    css: false,
  },
})
