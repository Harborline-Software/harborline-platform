import { defineConfig } from '@playwright/test'

const jsonReportPath = process.env.PLAYWRIGHT_JSON_OUTPUT_NAME ?? 'test-results/results.json'

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  // A single transient stall must not discard the whole gate. A retry does not hide
  // anything: Playwright reports a test that only passed on retry as flaky, and a
  // genuinely broken test fails every attempt. Raising `timeout` instead was rejected —
  // that would mask a story that has become slow rather than surface it.
  retries: 1,
  // Four workers on the 8-core relocation host cut the ~1h serial browser sweep to a
  // fraction; parity screenshots stay deterministic because the probes are snapped to
  // whole-pixel coordinates and clipped to CSS-sized integral rectangles before capture.
  workers: process.env.PW_WORKERS ? Number(process.env.PW_WORKERS) : 4,
  // `line` alone keeps the local gate's output terse. On CI the html reporter is ADDED — not
  // swapped — because it is the only reporter configured here that writes testInfo.attach bodies
  // to disk. Verified rather than assumed: a deliberately-failing probe test that attaches a PNG
  // leaves, under `line` alone, an error-context.md and nothing else, so the react/blazor/diff
  // images a failed visual-parity comparison attaches die with the runner. Adding html produced
  // playwright-report/data/<sha>.png, which .github/workflows/validate.yml then uploads on
  // failure (control ticket 083).
  // The json reporter is present in EVERY mode, deliberately. Sixteen gallery counts used to be
  // arithmetic over the catalog rather than observations of a run, so the gate reported numbers no
  // execution produced -- and `browserTests` drifted to 436-versus-437 on the very commit that added
  // the check meant to stop drift (control ticket 100). `test-results/` is already gitignored, so
  // the file never enters the tree the receipt hashes.
  // The json outputFile honours PLAYWRIGHT_JSON_OUTPUT_NAME. An explicit outputFile OVERRIDES that
  // variable rather than deferring to it, so under `--shard` every shard wrote to the same
  // results.json and raced -- four shards ran, all passed, and the merged report observed zero
  // tests. Reading the env var first is what lets each shard report separately for the merge.
  reporter: process.env.HARBORLINE_CI_GALLERY === '1'
    ? [['line'], ['json', { outputFile: jsonReportPath }], ['html', { open: 'never', outputFolder: 'playwright-report' }]]
    : [['line'], ['json', { outputFile: jsonReportPath }]],
  // 45s locally. The comment on `retries` above rejected raising this to hide a story that has
  // become slow, and that still holds for the LOCAL gate, which is the binding one. CI is a
  // different measurement: on a 2-core GitHub runner the Blazor circuit's own startup
  // (BlazingStory.readyView) eats most of the budget before a component renders, so 45s there
  // measures the runner's cold start rather than the story. Raised only under the CI flag, so a
  // slow story still surfaces on the machine that is allowed to judge it (control ticket 083).
  timeout: process.env.HARBORLINE_CI_GALLERY === '1' ? 90_000 : 45_000,
  use: {
    viewport: { width: 920, height: 720 },
    colorScheme: 'light',
  },
})
