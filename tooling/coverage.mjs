import {execFileSync} from 'node:child_process'
import {copyFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync} from 'node:fs'
import path from 'node:path'

export const coverageEnabled = (env = process.env) => env.HARBORLINE_GATE_COVERAGE === '1'
const attr = (text, name) => new RegExp(`\\b${name}\\s*=\\s*(["'])(.*?)\\1`).exec(text)?.[2]
const walk = dir => readdirSync(dir, {withFileTypes: true}).flatMap(entry => { const file = path.join(dir, entry.name); return entry.isDirectory() ? walk(file) : entry.isFile() ? [file] : [] })
export function coverageSummary(xml, root, sourcePrefix) {
  const documents = Array.isArray(xml) ? xml : [xml]
  const lines = new Map()
  const overlappingBranchFiles = new Set()
  const tracked = new Set(execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, '-C', root, 'ls-files'], {encoding: 'utf8'}).trim().split(/\r?\n/))
  // Cobertura filenames are relative to the report's <source> roots (coverlet writes the project's source
  // root, e.g. projections/dotnet/), not to the repository; map through every root before giving up.
  const sourceRoots = documents.flatMap(document => [...document.matchAll(/<source>([^<]*)<\/source>/g)].map(m => m[1].trim())).filter(Boolean)
  const map = filename => {
    const normalized = filename.replaceAll('\\', '/').replace(/^\.\//, '')
    const candidates = [normalized, path.relative(root, filename).replaceAll('\\', '/'),
      ...sourceRoots.map(source => path.relative(root, path.resolve(root, source, normalized)).replaceAll('\\', '/'))]
    return candidates.find(candidate => tracked.has(candidate))
  }
  for (const [documentIndex, document] of documents.entries()) for (const match of document.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)) {
    const reported = attr(match[1], 'filename'); if (!reported) throw new Error('Cobertura class has no filename')
    const filename = map(reported) ?? reported
    for (const line of match[2].matchAll(/<line\b([^>]*)\/?\s*>/g)) {
      const number = attr(line[1], 'number'), hits = attr(line[1], 'hits')
      if (!/^\d+$/.test(number ?? '') || !/^\d+$/.test(hits ?? '')) throw new Error(`invalid Cobertura line in ${filename}`)
      const key = `${filename}:${number}`
      const branch = attr(line[1], 'condition-coverage')
      const counts = branch ? /\((\d+)\/(\d+)\)/.exec(branch) : null
      if (branch && !counts) throw new Error(`invalid Cobertura branch in ${filename}`)
      const previous = lines.get(key)
      if (previous?.documentIndex !== undefined && previous.documentIndex !== documentIndex
        && previous.validBranches > 0 && Number(counts?.[2] ?? 0) > 0) overlappingBranchFiles.add(filename)
      lines.set(key, {
        filename,
        documentIndex,
        hits: Math.max(Number(hits), previous?.hits ?? 0),
        coveredBranches: Math.max(Number(counts?.[1] ?? 0), previous?.coveredBranches ?? 0),
        validBranches: Math.max(Number(counts?.[2] ?? 0), previous?.validBranches ?? 0),
      })
    }
  }
  const values = [...lines.values()].filter(line => !sourcePrefix || map(line.filename)?.startsWith(sourcePrefix))
  const paths = [...new Set(values.map(line => line.filename))].sort()
  const overlappingBranchReports = [...overlappingBranchFiles].some(file => !sourcePrefix || map(file)?.startsWith(sourcePrefix))
  return {
    coveredLines: values.filter(line => line.hits > 0).length,
    validLines: values.length,
    coveredBranches: overlappingBranchReports ? null : values.reduce((sum, line) => sum + line.coveredBranches, 0),
    validBranches: overlappingBranchReports ? null : values.reduce((sum, line) => sum + line.validBranches, 0),
    mappedPaths: paths.map(map).filter(Boolean).sort(),
    unmappedPaths: paths.filter(file => !map(file)),
  }
}
export function copyCoberturaReport({root, resultsDirectory, suite, sourcePrefix}) {
  const reports = walk(resultsDirectory).filter(file => /^(coverage\.cobertura|cobertura-coverage)\.xml$/i.test(path.basename(file)))
  if (!reports.length) throw new Error(`expected a Cobertura report under ${resultsDirectory}, found 0`)
  const sourceReports = reports.sort((a, b) => a.localeCompare(b))
  const coverageDirectory = path.join(root, 'artifacts', 'quality', 'coverage', suite)
  const artifactPaths = sourceReports.map((report, index) => path.join(coverageDirectory, 'reports', `${index + 1}-${path.basename(report)}`))
  artifactPaths.forEach((target, index) => { mkdirSync(path.dirname(target), {recursive: true}); copyFileSync(sourceReports[index], target) })
  const relativeArtifacts = artifactPaths.map(target => path.relative(root, target).replaceAll('\\', '/'))
  const summary = {suite, sourcePrefix, artifactPath: relativeArtifacts[0], artifactPaths: relativeArtifacts, sourceReports: sourceReports.map(report => path.relative(root, report).replaceAll('\\', '/')), ...coverageSummary(sourceReports.map(report => readFileSync(report, 'utf8')), root, sourcePrefix)}
  if (sourcePrefix && !summary.validLines) throw new Error(`${suite} coverage contains no lines under ${sourcePrefix}`)
  writeFileSync(path.join(coverageDirectory, 'coverage-summary.json'), `${JSON.stringify(summary, null, 2)}\n`)
  console.error(`${suite} coverage — lines ${summary.coveredLines}/${summary.validLines} | branches ${summary.validBranches === null ? 'unavailable (overlapping reports)' : `${summary.coveredBranches}/${summary.validBranches}`}`)
  return summary
}
