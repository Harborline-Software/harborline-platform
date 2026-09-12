import { resolve } from 'node:path'

const aggregateRoot = resolve(import.meta.dirname, '../hlp.ui.button')

export default {
  resolve: {
    alias: {
      react: resolve(aggregateRoot, 'node_modules/react'),
      'react-dom': resolve(aggregateRoot, 'node_modules/react-dom'),
      '@testing-library/react': resolve(aggregateRoot, 'node_modules/@testing-library/react'),
      '@testing-library/user-event': resolve(aggregateRoot, 'node_modules/@testing-library/user-event'),
      '@testing-library/jest-dom': resolve(aggregateRoot, 'node_modules/@testing-library/jest-dom'),
      clsx: resolve(aggregateRoot, 'node_modules/clsx'),
      'tailwind-merge': resolve(aggregateRoot, 'node_modules/tailwind-merge'),
      '@harborline-platform/hlp.ui.form-view': resolve(import.meta.dirname, '../hlp.ui.form-view/src/index.ts'),
      '@harborline-platform/hlp.ui.form-field': resolve(import.meta.dirname, '../hlp.ui.form-field/src/index.ts'),
      '@harborline-platform/hlp.ui.form-field-context': resolve(import.meta.dirname, '../hlp.ui.form-field/src/FormFieldContext.tsx'),
      '@harborline-platform/hlp.ui.input': resolve(import.meta.dirname, '../hlp.ui.input/src/index.ts'),
      '@harborline-platform/hlp.ui.text-box': resolve(import.meta.dirname, '../hlp.ui.text-box/src/index.ts'),
      '@harborline-platform/hlp.ui.text-area': resolve(import.meta.dirname, '../hlp.ui.text-area/src/index.ts'),
      '@harborline-platform/hlp.ui.select-field': resolve(import.meta.dirname, '../hlp.ui.select-field/src/index.ts'),
      '@harborline-platform/hlp.ui.check-box': resolve(import.meta.dirname, '../hlp.ui.check-box/src/index.ts'),
      '@harborline-platform/hlp.ui.radio-group': resolve(import.meta.dirname, '../hlp.ui.radio-group/src/index.ts'),
      '@harborline-platform/hlp.ui.switch': resolve(import.meta.dirname, '../hlp.ui.switch/src/index.ts'),
      '@harborline-platform/hlp.ui.date-field': resolve(import.meta.dirname, '../hlp.ui.date-field/src/index.ts'),
      '@harborline-platform/hlp.ui.date-time-field': resolve(import.meta.dirname, '../hlp.ui.date-time-field/src/index.ts'),
      '@harborline-platform/hlp.ui.number-field': resolve(import.meta.dirname, '../hlp.ui.number-field/src/index.ts'),
      '@harborline-platform/hlp.ui.numeric-text-box': resolve(import.meta.dirname, '../hlp.ui.numeric-text-box/src/index.ts'),
      '@harborline-platform/hlp.ui.cn': resolve(import.meta.dirname, '../hlp.ui.cn/src/index.ts'),
      '@harborline-platform/hlp.ui.default-strings': resolve(import.meta.dirname, '../hlp.ui.default-strings/src/index.ts'),
      '@harborline-platform/hlp.ui.locale-provider': resolve(import.meta.dirname, '../hlp.ui.locale-provider/src/index.ts'),
      '@harborline-platform/hlp.ui.use-can-show-master-detail': resolve(import.meta.dirname, '../hlp.ui.use-can-show-master-detail/src/index.ts'),
      '@harborline-platform/hlp.ui.use-media-query': resolve(import.meta.dirname, '../hlp.ui.use-media-query/src/index.ts'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    css: false,
    // The deterministic structural file belongs to the performance stage, not the shared stage,
    // and it must not be collected alongside the wall-clock budgets. Rendering 256 fields
    // ninety-six times moves the 500-field budget measurement from 190-197ms to 250-436ms against
    // a 400ms ceiling when the files run in parallel, and to 447-489ms when they run serially in
    // one process, because the allocation churn outlives the file. Either way the budget file
    // measures its neighbour instead of the renderer. tooling/run-ui-performance.mjs sets
    // HARBORLINE_UI_PERFORMANCE and names the file explicitly, so the stage that owns it still
    // runs it. Relaxing the committed 400ms budget instead would hide a real regression.
    // Three collections out of one root, because this module is the only one carrying both
    // wall-clock budgets and a deterministic structural file:
    //   default (native-tests)  conformance + native evidence; [PerfBudget] rows go to perf-budgets
    //   shared conformance      wall-clock file excluded -- six runners compete, so a wall-clock
    //                           assertion there measures the scheduler, not the renderer; the
    //                           rule-reevaluation invariant is in its own file and still runs
    //   performance stage       the deterministic file only, named explicitly by the harness
    exclude: [
      '**/node_modules/**',
      ...(process.env.HARBORLINE_UI_PERFORMANCE === '1' ? [] : ['**/SchemaForm.deterministic.test.tsx']),
      ...(process.env.HARBORLINE_SHARED_CONFORMANCE === '1' ? ['**/SchemaForm.performance.test.tsx'] : []),
    ],
  },
}
