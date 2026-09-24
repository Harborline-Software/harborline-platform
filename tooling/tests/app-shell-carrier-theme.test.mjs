import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import test from 'node:test'

const files = {
  spec: 'specs/modules/ui/hlp.ui.app-shell/style.css',
  react: 'projections/react/ui/hlp.ui.app-shell/src/style.css',
  blazor: 'projections/blazor/ui/hlp.ui.app-shell/wwwroot/app-shell.css',
}

test('app shell ships one dark-first Carrier semantic theme to both projections', async () => {
  const css = Object.fromEntries(await Promise.all(Object.entries(files).map(async ([lane, path]) => [lane, await readFile(path, 'utf8')])))
  assert.equal(css.react, css.spec)
  assert.equal(css.blazor, css.spec)

  for (const [lane, source] of Object.entries(css)) {
    assert.match(source, /--color-background:\s*#121212/iu, `${lane} owns Carrier's dark background`)
    assert.match(source, /--color-primary:\s*#7ab8ff/iu, `${lane} owns Carrier's dark interactive blue`)
    assert.match(source, /--font-ui:\s*['"]Inter['"]/iu, `${lane} exposes the Carrier UI family`)
    assert.match(source, /--font-mono:\s*['"]JetBrains Mono['"]/iu, `${lane} exposes the Carrier identifier family`)
    assert.match(source, /--hl-background:\s*var\(--color-background\)/u, `${lane} maps the existing shell contract to Carrier semantics`)
    assert.match(source, /\.hl-app-shell[^{}]*\{[^{}]*font-family:\s*var\(--font-ui\)/su, `${lane} consumes the UI family`)
    assert.match(source, /\.hl-app-shell__key-hint[^{}]*\{[^{}]*font-family:\s*var\(--font-mono\)/su, `${lane} consumes the mono family`)
    assert.match(source, /html:has\(\.hl-app-shell\)[^{}]*\{[^{}]*margin:\s*0/su, `${lane} removes host-page framing`)
    assert.match(source, /\.hl-app-shell \.hl-app-layout__rail[^{}]*\{[^{}]*background:\s*var\(--color-muted\)/su, `${lane} uses the Carrier muted rail`)
    assert.match(source, /\.hl-app-shell__dock[^{}]*\{[^{}]*gap:\s*8px[^{}]*padding:\s*8px/su, `${lane} uses the Carrier dock canvas`)
  }
})

test('Blazor shell toolbar uses vector icons rather than font-dependent Unicode glyphs', async () => {
  const source = await readFile('projections/blazor/ui/hlp.ui.app-shell/HarborlineAppShell.razor', 'utf8')
  assert.doesNotMatch(source, /[☰▥⌕♧⋮↗⤢×⌄]/u)
  assert.match(source, /<svg[^>]*data-shell-icon="menu"/u)
  assert.match(source, /<svg[^>]*data-shell-icon="search"/u)
  assert.match(source, /<svg[^>]*data-shell-icon="notifications"/u)
  assert.match(source, /<svg[^>]*data-shell-icon="close"/u)
  assert.match(source, /<svg[^>]*data-shell-icon="chevron-down"/u)
})
