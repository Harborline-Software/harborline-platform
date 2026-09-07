// Merging Playwright's per-shard json reports into the single report the gallery gate observes.
//
// Its own module, not a helper inside run-gallery-gate.mjs, because that file starts servers and
// runs builds at import time: a test importing it for one pure function hangs waiting on a gallery
// it never asked for. Found the direct way.
//
// A shard whose report is MISSING contributes nothing, deliberately. The merged report then holds
// fewer tests than the catalog declares, and observeGalleryRun's reconciliation reports it as
// declared-but-not-run -- so a shard that died silently fails the gate rather than shrinking the
// suite into a smaller, quietly passing one. Throwing here instead would lose the surviving shards'
// results and say less about what happened.
export function mergeShardReports(reports) {
  const merged = { suites: [], stats: { expected: 0, unexpected: 0, flaky: 0, skipped: 0 } }
  for (const report of reports) {
    if (!report) continue
    merged.suites.push(...(report.suites ?? []))
    for (const key of Object.keys(merged.stats)) merged.stats[key] += report.stats?.[key] ?? 0
  }
  return merged
}
