import { fileURLToPath } from 'node:url'
import type { StorybookConfig } from '@storybook/react-vite'

const repositoryRoot = fileURLToPath(new URL('../../../..', import.meta.url))

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  addons: ['@storybook/addon-a11y'],
  framework: {
    name: '@storybook/react-vite',
    options: {},
  },
  core: {
    disableTelemetry: true,
  },
  viteFinal: async viteConfig => ({
    ...viteConfig,
    // The gallery gate drives this server as http://127.0.0.1:6106. Vite's host check
    // normally accepts that alongside localhost, but it intermittently rejected a single
    // request mid-run ("configure allowedHosts") and failed one Playwright spec, so the
    // permitted hosts are pinned explicitly rather than left to the default heuristic.
    server: {
      ...viteConfig.server,
      allowedHosts: ['127.0.0.1', 'localhost'],
      // AppShell.stories.tsx reads its panel table from conformance/hlp.ui.app-shell/chrome-v1.json,
      // which lives above this project's Vite root; without this the dev server refuses to serve it.
      fs: { ...viteConfig.server?.fs, allow: [...(viteConfig.server?.fs?.allow ?? []), repositoryRoot] },
    },
    optimizeDeps: {
      ...viteConfig.optimizeDeps,
      include: [
        ...(viteConfig.optimizeDeps?.include ?? []),
        'react',
        'react-dom',
        'react/jsx-runtime',
        '@storybook/react',
      ],
    },
  }),
}

export default config
