export function parseNodeTestCount(stdout, suiteId) {
  const summaries = [
    ...stdout.matchAll(/^[ \t]*ℹ[ \t]+tests[ \t]+(\d+)[ \t]*\r?$/gm),
    ...stdout.matchAll(/^[ \t]*#[ \t]+tests[ \t]+(\d+)[ \t]*\r?$/gm),
  ]

  if (summaries.length !== 1) {
    throw new Error(`${suiteId}: expected exactly one Node test summary ("ℹ tests N" or "# tests N"); found ${summaries.length}`)
  }

  const count = Number(summaries[0][1])
  if (!Number.isSafeInteger(count) || count <= 0) {
    throw new Error(`${suiteId}: Node test summary reported invalid test count ${summaries[0][1]}; expected a positive safe integer`)
  }

  return count
}
