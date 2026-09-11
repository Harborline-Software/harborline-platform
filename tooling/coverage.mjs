import {execFileSync} from 'node:child_process'
import {copyFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync} from 'node:fs'
import path from 'node:path'

export const coverageEnabled = (env = process.env) => env.HARBORLINE_GATE_COVERAGE === '1'
const attr = (text, name) => new RegExp(`\\b${name}\\s*=\\s*(["'])(.*?)\\1`).exec(text)?.[2]
const walk = dir => readdirSync(dir, {withFileTypes: true}).flatMap(entry => { const file = path.join(dir, entry.name); return entry.isDirectory() ? walk(file) : entry.isFile() ? [file] : [] })
export function coverageSummary(xml, root) {
  const documents = Array.isArray(xml) ? xml : [xml]
  const lines = new Map()
  for (const document of documents) for (const match of document.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)) {
    const filename = attr(match[1], 'filename'); if (!filename) throw new Error('Cobertura class has no filename')
    for (const line of match[2].matchAll(/<line\b([^>]*)\/?\s*>/g)) {
      const number = attr(line[1], 'number'), hits = attr(line[1], 'hits')
      if (!/^\d+$/.test(number ?? '') || !/^\d+$/.test(hits ?? '')) throw new Error(`invalid Cobertura line in ${filename}`)
      const key = `${filename}:${number}`; lines.set(key, {filename, hits: Math.max(Number(hits), lines.get(key)?.hits ?? 0)})
    }
  }
  const tracked = new Set(execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, '-C', root, 'ls-files'], {encoding: 'utf8'}).trim().split(/\r?\n/))
  const paths = [...new Set([...lines.values()].map(line => line.filename))].sort()
  // Cobertura filenames are relative to the report's <source> roots (coverlet writes the project's source
  // root, e.g. projections/dotnet/), not to the repository; map through every root before giving up.
  const sourceRoots = documents.flatMap(document => [...document.matchAll(/<source>([^<]*)<\/source>/g)].map(m => m[1].trim())).filter(Boolean)
  const map = filename => {
    const normalized = filename.replaceAll('\\', '/').replace(/^\.\//, '')
    const candidates = [normalized, path.relative(root, filename).replaceAll('\\', '/'),
      ...sourceRoots.map(source => path.relative(root, path.resolve(root, source, normalized)).replaceAll('\\', '/'))]
    return candidates.find(candidate => tracked.has(candidate))
  }
  return {coveredLines: [...lines.values()].filter(line => line.hits > 0).length, validLines: lines.size, mappedPaths: paths.map(map).filter(Boolean).sort(), unmappedPaths: paths.filter(file => !map(file))}
}
export function copyCoberturaReport({root, resultsDirectory, suite}) {
  const reports = walk(resultsDirectory).filter(file => /^(coverage\.cobertura|cobertura-coverage)\.xml$/i.test(path.basename(file)))
  if (!reports.length) throw new Error(`expected a Cobertura report under ${resultsDirectory}, found 0`)
  const sourceReports = reports.sort((a, b) => a.localeCompare(b))
  const coverageDirectory = path.join(root, 'artifacts', 'quality', 'coverage', suite)
  const artifactPaths = sourceReports.map((report, index) => path.join(coverageDirectory, 'reports', `${index + 1}-${path.basename(report)}`))
  artifactPaths.forEach((target, index) => { mkdirSync(path.dirname(target), {recursive: true}); copyFileSync(sourceReports[index], target) })
  const relativeArtifacts = artifactPaths.map(target => path.relative(root, target).replaceAll('\\', '/'))
  const summary = {suite, artifactPath: relativeArtifacts[0], artifactPaths: relativeArtifacts, sourceReports: sourceReports.map(report => path.relative(root, report).replaceAll('\\', '/')), ...coverageSummary(sourceReports.map(report => readFileSync(report, 'utf8')), root)}
  writeFileSync(path.join(coverageDirectory, 'coverage-summary.json'), `${JSON.stringify(summary, null, 2)}\n`)
  console.error(`${suite} coverage — covered ${summary.coveredLines} | valid ${summary.validLines}`)
  return summary
}
