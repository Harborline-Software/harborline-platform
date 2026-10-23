import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { defaultStrings } from '@harborline-platform/hlp.ui.default-strings'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { Host } from './setup'

// Contract: the rail chrome's default copy comes from the catalog (navigation.layersRail.*), caller text
// overrides it, and no control or region is ever unnamed.
const French = { 'navigation.layersRail.region': 'Calques', 'navigation.layersRail.lenses': 'Lentilles', 'navigation.layersRail.outline': 'Plan', 'navigation.layersRail.exitLens': 'Quitter la lentille' }
const Arabic = { 'navigation.layersRail.region': 'الطبقات', 'navigation.layersRail.lenses': 'العدسات', 'navigation.layersRail.outline': 'المخطط', 'navigation.layersRail.exitLens': 'الخروج من العدسة' }
function chrome() {
  const result = {
    region: screen.getByRole('complementary').getAttribute('aria-label'),
    lenses: screen.getByRole('group').getAttribute('aria-label'),
    outline: screen.getByRole('tree').getAttribute('aria-label'),
    exitLens: document.querySelector('.hl-layers-rail__status-close')!.getAttribute('aria-label'),
  }
  for (const value of Object.values(result)) expect((value ?? '').trim()).not.toBe('')
  return result
}

describe('LayersRail catalog-backed copy', () => {
  it('takes its default copy from the default catalog without a provider', () => {
    render(<Host/>)
    expect(chrome()).toEqual({ region: defaultStrings['navigation.layersRail.region'], lenses: defaultStrings['navigation.layersRail.lenses'], outline: defaultStrings['navigation.layersRail.outline'], exitLens: defaultStrings['navigation.layersRail.exitLens'] })
  })

  it('uses the host catalog, follows a French to Arabic change, and caller text overrides it', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}><Host/></HarborlineLocaleProvider>)
    expect(chrome()).toEqual({ region: 'Calques', lenses: 'Lentilles', outline: 'Plan', exitLens: 'Quitter la lentille' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}><Host/></HarborlineLocaleProvider>)
    expect(chrome()).toEqual({ region: 'الطبقات', lenses: 'العدسات', outline: 'المخطط', exitLens: 'الخروج من العدسة' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}><Host labels={{ railRegion: 'Strata' }}/></HarborlineLocaleProvider>)
    expect(chrome().region).toBe('Strata')
  })

  it('uses a pseudo-localized catalog under en-XA', () => {
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'navigation.layersRail.region': '⟦ Ļåÿëřš ··· ⟧' }}><Host/></HarborlineLocaleProvider>)
    expect(chrome().region).toBe('⟦ Ļåÿëřš ··· ⟧')
  })

  it('never renders an empty name for a blank catalog value', () => {
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'navigation.layersRail.region': '  ' }}><Host/></HarborlineLocaleProvider>)
    expect(chrome().region).toBe('Layers')
  })
})
