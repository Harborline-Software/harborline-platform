export const reactPackageIdentity = '@harborline-software/ui-react'
export const reactAggregateModuleId = 'hlp.ui.button'

export function parseReactAggregateContributions(source) {
  const match = source.match(/const contributions = \[\r?\n(?<body>[\s\S]*?)\r?\n\]/)
  if (!match?.groups?.body) throw new Error('React aggregate contribution list is missing or has an unsupported shape')

  const moduleIds = []
  for (const rawLine of match.groups.body.split(/\r?\n/)) {
    const line = rawLine.trim()
    if (line.length === 0 || line.startsWith('//')) continue
    const entry = line.match(/^\['(?<name>[^']+)', resolve\(root, '\.\.\/(?<moduleId>hlp\.ui\.[^']+)'\), (?:true|false)\],$/)
    if (!entry?.groups || entry.groups.name !== entry.groups.moduleId.slice('hlp.ui.'.length)) {
      throw new Error(`React aggregate contribution entry has an unsupported shape: ${line}`)
    }
    moduleIds.push(entry.groups.moduleId)
  }

  const duplicates = moduleIds.filter((moduleId, index) => moduleIds.indexOf(moduleId) !== index)
  if (duplicates.length > 0) throw new Error(`React aggregate contribution list contains duplicate modules: ${[...new Set(duplicates)].join(', ')}`)

  // Contributions wired OUTSIDE the tuple list: top-level module-root consts
  // (e.g. `const contextRoot = resolve(root, '../hlp.ui.context-menu')`) whose
  // exports the build appends directly. Both mechanisms are real contributions.
  for (const rootConst of source.matchAll(/^const \w+Root = resolve\(root, '\.\.\/(hlp\.ui\.[a-z0-9-]+)'\)$/gm)) {
    if (!moduleIds.includes(rootConst[1])) moduleIds.push(rootConst[1])
  }
  return moduleIds
}

export function npmPackageContributionErrors(catalog, aggregateBuildSource) {
  let explicitBuildModules
  try {
    explicitBuildModules = parseReactAggregateContributions(aggregateBuildSource)
  } catch (error) {
    return [`catalog-npm-contributions-match-aggregate-build: ${error.message}`]
  }

  // A module claims the package either by CONTRIBUTING to it (packageContribution,
  // plain identity string) or by OWNING it as the aggregate host (react artifact.id).
  const claimsIdentity = react =>
    react?.packageContribution === reactPackageIdentity || react?.artifact?.id === reactPackageIdentity
  const claimedModules = Object.entries(catalog.modules ?? {})
    .filter(([, module]) => claimsIdentity(module.projections?.react))
    .map(([moduleId]) => moduleId)
    .sort()
  const builtModules = [...new Set([reactAggregateModuleId, ...explicitBuildModules])].sort()
  const claimed = new Set(claimedModules)
  const built = new Set(builtModules)

  return [
    ...claimedModules
      .filter(moduleId => !built.has(moduleId))
      .map(moduleId => `catalog-npm-contributions-match-aggregate-build: ${moduleId} claims ${reactPackageIdentity} but is absent from the aggregate build`),
    ...builtModules
      .filter(moduleId => !claimed.has(moduleId))
      .map(moduleId => `catalog-npm-contributions-match-aggregate-build: ${moduleId} is present in the aggregate build but does not claim ${reactPackageIdentity}`),
  ]
}
