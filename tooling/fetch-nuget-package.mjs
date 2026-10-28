import { writeFileSync } from 'node:fs'
import { setTimeout } from 'node:timers/promises'
import { inspect } from 'node:util'

export async function fetchNugetPackageBytes(url) {
  for (let attempt = 1; attempt <= 2; attempt += 1) {
    const started = performance.now()
    let response
    try {
      response = await fetch(url)
    } catch (error) {
      console.error(`[nuget-get] attempt ${attempt} failed after ${Math.round(performance.now() - started)}ms\n${inspect(error, { depth: null, colors: false })}`)
      // Retry the observed pre-response connection failure once, within the caller's existing budget.
      if (attempt !== 1 || !(error instanceof TypeError)
        || !(error.cause instanceof AggregateError) || error.cause.code !== 'ETIMEDOUT') throw error
      await setTimeout(250)
      continue
    }
    console.error(`[nuget-get] attempt ${attempt} received HTTP ${response.status} after ${Math.round(performance.now() - started)}ms`)
    if (!response.ok) throw new Error(`${response.status} ${url}`)
    return Buffer.from(await response.arrayBuffer())
  }
}

if (import.meta.main) {
  const [url, destination] = process.argv.slice(2)
  writeFileSync(destination, await fetchNugetPackageBytes(url))
}
