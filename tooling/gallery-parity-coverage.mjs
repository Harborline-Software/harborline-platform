// Visual-parity coverage, counted per SURFACE rather than per module.
//
// All 59 modules declare at least one visual-parity scenario, so coverage looks complete when it is
// counted by module. It is not: a module can declare parity on its calmest scenario — its two theme
// scenarios — and leave the complicated ones uncompared, which is how hlp.ui.scheduler's month and
// agenda views diverged in plain sight (control ticket 147). This computes, for every scenario,
// whether parity is actually asserted on it, and holds the uncompared set against an exact register
// so that adding a scenario without parity is a red gate rather than a silent gap.
const parityCaseSuffix = '.quality.visual-parity'

export function declaresVisualParity(scenario) {
  return (scenario.sourceQualityCaseIds ?? []).some(id => id.endsWith(parityCaseSuffix))
}

export function galleryParityCoverage(catalogs, register) {
  const errors = []
  const registered = new Map(Object.entries(register?.modules ?? {}))
  const modules = []
  for (const catalog of catalogs) {
    const compared = catalog.scenarios.filter(declaresVisualParity).map(scenario => scenario.id).sort()
    const uncompared = catalog.scenarios.filter(scenario => !declaresVisualParity(scenario)).map(scenario => scenario.id).sort()
    const row = registered.get(catalog.moduleId)
    registered.delete(catalog.moduleId)
    const declared = [...(row?.uncompared ?? [])].sort()
    // Exact, both directions: an unregistered uncompared surface is a new gap, and a registered id
    // that is now compared (or gone) is a stale row that has to be deleted.
    for (const id of uncompared) {
      if (!declared.includes(id)) errors.push(`${catalog.moduleId}: scenario is not compared for visual parity and is not registered as such: ${id}`)
    }
    for (const id of declared) {
      if (!uncompared.includes(id)) errors.push(`${catalog.moduleId}: registered uncompared scenario is compared or no longer exists: ${id}`)
    }
    if (uncompared.length && !row?.reason) errors.push(`${catalog.moduleId}: uncompared scenarios are registered without a reason`)
    modules.push({
      moduleId: catalog.moduleId,
      scenarios: catalog.scenarios.length,
      compared,
      uncompared,
      comparesOnlyThemeScenarios: compared.length > 0 && compared.every(id => /\.(?:theme|rtl-theme|locale-theme|themes|theme-light|theme-dark)[^.]*$/.test(id)),
    })
  }
  for (const moduleId of registered.keys()) errors.push(`visual parity coverage register names an unknown module: ${moduleId}`)
  return {
    schemaVersion: 1,
    totals: {
      modules: modules.length,
      scenarios: modules.reduce((total, module) => total + module.scenarios, 0),
      compared: modules.reduce((total, module) => total + module.compared.length, 0),
      uncompared: modules.reduce((total, module) => total + module.uncompared.length, 0),
      modulesComparingOnlyThemeScenarios: modules.filter(module => module.comparesOnlyThemeScenarios).length,
    },
    modules,
    errors,
  }
}
