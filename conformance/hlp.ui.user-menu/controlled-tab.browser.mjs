// Run after installing the React aggregate and gallery test dependencies:
// node conformance/hlp.ui.user-menu/controlled-tab.browser.mjs
// Native browser Tab is required: jsdom does not implement inert traversal.
import assert from 'node:assert/strict'
import {createRequire} from 'node:module'
import {readdirSync, existsSync} from 'node:fs'
import {resolve} from 'node:path'
const root = resolve(import.meta.dirname, '../..')
const requireUi = createRequire(resolve(root, 'projections/react/ui/hlp.ui.button/package.json'))
const requireGallery = createRequire(resolve(root, 'gallery/tests/package.json'))
const ui = resolve(root, 'projections/react/ui')
const alias = {react: resolve(requireUi.resolve('react'), '..'), 'react-dom/client': requireUi.resolve('react-dom/client')}
for (const id of readdirSync(ui)) {
  const index = resolve(ui, id, 'src/index.ts')
  if (existsSync(index)) alias[`@harborline-platform/${id}`] = index
}
const bundle = requireUi('esbuild').buildSync({bundle: true, write: false, platform: 'browser', alias,
  stdin: {loader: 'jsx', resolveDir: root, contents: `
import React from 'react';
import {createRoot} from 'react-dom/client';
import {UserMenu} from '@harborline-platform/hlp.ui.user-menu';
function App() {
  const [open, setOpen] = React.useState(true);
  window.closeMenu = () => setOpen(false);
  return <><UserMenu identity={{name:'Ada'}} open={open} onOpenChange={() => {window.closeRequested=true}}
    items={[{id:'profile',kind:'action',label:'Profile',onActivate:()=>{}}]}/><button id="after">After menu</button></>;
}
createRoot(document.getElementById('root')).render(<App/>);`},
}).outputFiles[0].text
const browser = await requireGallery('@playwright/test').chromium.launch({headless: true})
try {
  for (const key of ['Tab', 'Shift+Tab']) {
    const page = await browser.newPage()
    await page.setContent('<div id="root"></div>')
    await page.addScriptTag({content: bundle})
    await page.getByRole('menuitem', {name: 'Profile', exact: true}).focus()
    await page.keyboard.press(key)
    // Oracle: the existing nonmodal-menu contract exits forward to the next page control,
    // backward to the trigger. The host deliberately leaves open=true until this is checked.
    const destination = page.locator(key === 'Tab' ? '#after' : '.hl-user-menu__trigger')
    assert.equal(await destination.evaluate(e => e === document.activeElement), true, `${key} leaves pending menu`)
    assert.equal(await page.evaluate(() => window.closeRequested), true)
    await page.waitForFunction(() => !document.querySelector('.hl-user-menu__surface').inert)
    await page.getByRole('menuitem', {name: 'Profile', exact: true}).focus()
    assert.equal(await page.getByRole('menuitem', {name: 'Profile', exact: true}).evaluate(e => e === document.activeElement), true, 'host-retained menu becomes focusable again')
    await destination.focus()
    await page.evaluate(() => window.closeMenu())
    await page.getByRole('menu').waitFor({state: 'detached'})
    assert.equal(await destination.evaluate(e => e === document.activeElement), true, 'delayed close preserves outside focus')
    await page.close()
  }
  console.log('PASS: forward/backward Tab, retained panel restoration, and delayed controlled closure')
} finally { await browser.close() }
