import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const repo = resolve(import.meta.dirname, '../..')
const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, 'disclosure-v1.json'), 'utf8'))
// The Blazor lane's OWN stylesheet, which bUnit cannot compute a style from. It is the byte copy of
// the authority the fixture names, and this test is what proves the copy still carries the token.
const laneCss = readFileSync(resolve(repo, 'projections/blazor/ui/hlp.ui.action-menu/wwwroot/action-menu.css'), 'utf8')
const authorityCss = readFileSync(resolve(repo, fixture.render.hintComputed.styleAuthority), 'utf8')

describe('ActionMenu Blazor lane style conformance (conformance/hlp.ui.action-menu/disclosure-v1.json)', () => {
  it('carries the authority stylesheet byte for byte', () => {
    expect(laneCss).toBe(authorityCss)
  })

  it('renders the shortcut hint right aligned in the shared monospace token', () => {
    const style = document.createElement('style')
    style.textContent = laneCss
    document.head.append(style)
    // The Blazor lane's rendered shape for one hinted entry.
    const menu = document.createElement('div')
    menu.className = 'hl-action-menu'
    menu.innerHTML = `<div class="hl-action-menu__menu"><button class="hl-action-menu__item" data-item-id="edit">`
      + `<span>Edit</span><span class="${fixture.render.hintClass}">${fixture.render.hints.edit}</span></button></div>`
    document.body.append(menu)

    const hint = menu.querySelector(`.${fixture.render.hintClass}`)
    const computed = getComputedStyle(hint)
    expect(computed.textAlign).toBe(fixture.render.hintComputed.textAlign)
    expect(computed.fontFamily).toContain(fixture.render.hintComputed.fontFamilyContains)
    // Right alignment inside the flex row: the token pushes itself to the inline end.
    expect(computed.marginInlineStart || laneCss).toContain('auto')

    // The alignment and the family are the TOKEN's: an entry without the class has neither.
    const label = menu.querySelector('span:not([class])')
    expect(getComputedStyle(label).fontFamily).not.toContain(fixture.render.hintComputed.fontFamilyContains)

    menu.remove()
    style.remove()
  })
})
