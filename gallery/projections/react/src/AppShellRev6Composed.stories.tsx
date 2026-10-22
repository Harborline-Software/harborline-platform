import { useEffect, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { AppShellRev6TabHost, type AppShellRev6Intent, type AppShellRev6Presentation, type AppShellRev6TabKind } from '@harborline-software/ui-react'
import { presentation, runScenario, transition } from '../../../../projections/shared/ui/hlp.ui.app-shell/rev6/tab-identity-state.mjs'
import './AppShellRev6Composed.css'

// The frame and records are gallery fixtures. Only the tab host and its state model are rev6 code.
const records = [
  { id: 'R-1042', title: 'Record 1042', status: 'Open', owner: 'A. Rivera', type: 'Type A', updated: 'Today', place: 'Site 1' },
  { id: 'R-1038', title: 'Record 1038', status: 'In review', owner: 'J. Kim', type: 'Type B', updated: 'Yesterday', place: 'Site 1' },
  { id: 'R-1031', title: 'Record 1031', status: 'Open', owner: 'S. Patel', type: 'Type A', updated: 'Mon', place: 'Site 1' },
  { id: 'R-1027', title: 'Record 1027', status: 'Closed', owner: 'A. Rivera', type: 'Type C', updated: '2 Oct', place: 'Site 1' },
  { id: 'R-1019', title: 'Record 1019', status: 'Open', owner: 'M. Osei', type: 'Type B', updated: '30 Sep', place: 'Site 1' },
  { id: 'R-1012', title: 'Record 1012', status: 'In review', owner: 'J. Kim', type: 'Type A', updated: '28 Sep', place: 'Site 1' },
] as const
type RecordFixture = (typeof records)[number]
type IconName = 'chat' | 'grid' | 'inbox' | 'folder' | 'apps' | 'check' | 'chart' | 'settings' | 'search' | 'share' | 'panel' | 'filter' | 'sort' | 'plus' | 'chevron' | 'close' | 'spark' | 'pin' | 'list'
const paths: Record<IconName, string> = {
  chat: 'M4 5h16v11H9l-5 4V5Z', grid: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z',
  inbox: 'M3 5h18v13H3V5Zm0 8h5l2 3h4l2-3h5', folder: 'M3 6h7l2 2h9v11H3V6Z',
  apps: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z', check: 'M4 12l5 5L20 6',
  chart: 'M4 20V10m5 10V5m5 15v-8m5 8V8M2 20h20', settings: 'M12 3v3m0 12v3M3 12h3m12 0h3M5.6 5.6l2.1 2.1m8.6 8.6 2.1 2.1m0-12.8-2.1 2.1M7.7 16.3l-2.1 2.1M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14Zm5-2 5 5', share: 'M12 16V3m0 0L8 7m4-4 4 4M4 13v7h16v-7',
  panel: 'M3 4h18v16H3V4Zm12 0v16', filter: 'M3 5h18l-7 8v6l-4 2v-8L3 5Z', sort: 'M7 4v16m0-16L4 7m3-3 3 3M17 20V4m0 16-3-3m3 3 3-3',
  plus: 'M12 4v16M4 12h16', chevron: 'm6 9 6 6 6-6', close: 'M5 5l14 14M19 5 5 19',
  spark: 'm12 2 1.8 7.2L21 11l-7.2 1.8L12 20l-1.8-7.2L3 11l7.2-1.8L12 2Z',
  pin: 'M8 3h8l-1 6 3 3v2H6v-2l3-3-1-6Zm4 11v7', list: 'M8 5h13M8 12h13M8 19h13M3 5h1M3 12h1M3 19h1',
}
function Icon({ name, size = 16 }: { name: IconName; size?: number }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={paths[name]} /></svg>
}

const initialEvents = [
  { type: 'identity', owner: { person: 'alice', tenant: 'north', workspace: 'work' } },
  { type: 'navigate', destination: 'records' },
  { type: 'open', kind: 'Assistant', target: 'conversation-a' },
  { type: 'pin', id: JSON.stringify(['Assistant', 'conversation-a']) },
  { type: 'open', kind: 'Details', target: 'R-1042', preview: true },
]

function ComposedHarness() {
  const [state, setState] = useState(() => runScenario(initialEvents))
  const [selected, setSelected] = useState<string>('R-1042')
  const [status, setStatus] = useState('All')
  const [layout, setLayout] = useState<'Grid' | 'Board'>('Grid')
  const [query, setQuery] = useState('')
  const [panelOpen, setPanelOpen] = useState(true)
  const [toolsOpen, setToolsOpen] = useState(false)
  const [notice, setNotice] = useState('')
  const view = presentation(state)
  const active = view?.tabs.find(tab => tab.id === view.active)
  const activeRecord = records.find(record => record.id === active?.target)
  const filtered = records.filter(record => (status === 'All' || record.status === status) && `${record.id} ${record.title} ${record.owner}`.toLowerCase().includes(query.toLowerCase()))
  const dispatch = (event: object) => setState(current => transition(current, event))
  const selectRecord = (record: RecordFixture) => {
    setSelected(record.id)
    setPanelOpen(true)
    dispatch({ type: 'open', kind: 'Details', target: record.id, preview: true })
  }
  const openTool = (kind: AppShellRev6TabKind) => {
    dispatch({ type: 'open', kind, target: kind === 'Assistant' ? 'conversation-a' : selected })
    setToolsOpen(false)
  }
  // This gallery adapter acknowledges a requested save locally. It does not persist or authorize a write.
  useEffect(() => {
    const request = view?.saveRequest
    if (!request) return
    const timer = window.setTimeout(() => {
      dispatch({ type: 'saved', ...request })
      setNotice('Demo save acknowledged locally. No server write occurred.')
    }, 700)
    return () => window.clearTimeout(timer)
  }, [view?.saveRequest])
  useEffect(() => {
    if (!notice) return
    const timer = window.setTimeout(() => setNotice(''), 4200)
    return () => window.clearTimeout(timer)
  }, [notice])

  return <div className="hl-r6-demo">
    <nav className="hl-r6-rail" aria-label="Sample workspace rail">
      <span className="hl-r6-tenant" aria-label="Example organization">EO</span>
      <span className="hl-r6-rail-icon" title="Conversations"><Icon name="chat" /></span>
      <span className="hl-r6-rail-icon is-current" title="Records"><Icon name="grid" /></span>
      <span className="hl-r6-rail-icon" title="My work"><Icon name="inbox" /><b>2</b></span>
      <span className="hl-r6-rail-rule" />
      <span className="hl-r6-rail-icon" title="Pages"><Icon name="folder" /></span>
      <span className="hl-r6-rail-icon" title="Browse"><Icon name="apps" /></span>
      <span className="hl-r6-rail-icon" title="Tasks"><Icon name="check" /></span>
      <span className="hl-r6-rail-rule" />
      <span className="hl-r6-rail-icon" title="Reports"><Icon name="chart" /></span>
      <span className="hl-r6-rail-spacer" />
      <span className="hl-r6-rail-icon" title="Settings"><Icon name="settings" /></span>
      <span className="hl-r6-avatar" aria-label="Sample user A. Rivera">AR</span>
    </nav>
    <aside className="hl-r6-sidebar" aria-label="Sample records navigation">
      <strong className="hl-r6-sidebar-title">Records</strong>
      <div className="hl-r6-scope"><span><i className="hl-r6-scope-dot" aria-hidden="true" /> Site 1</span><span>Last used</span></div>
      <div className="hl-r6-scope-link">Everywhere</div>
      <div className="hl-r6-side-label">Views</div>
      <div className="hl-r6-side-current">All records</div>
      <div className="hl-r6-side-item">Mine</div>
      <div className="hl-r6-side-item">Recently changed</div>
      <div className="hl-r6-side-label hl-r6-side-label-space">Pinned</div>
      <div className="hl-r6-side-item">Mine, open</div>
      <div className="hl-r6-mock-note"><strong>Gallery mock</strong><span>Rail, records, search and Assistant are sample UI. Tab state is live; no backend, AI or persistence is connected.</span></div>
    </aside>
    <main className="hl-r6-workspace">
      <header className="hl-r6-topbar">
        <div className="hl-r6-location"><strong>Records</strong><small>Site 1 › Records</small></div>
        <label className="hl-r6-search"><Icon name="search" /><span className="hl-r6-sr">Search sample records</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Ask or search Site 1" /><kbd>Ctrl K</kbd></label>
        <button className="hl-r6-share" onClick={() => setNotice('Sharing is simulated in this gallery.')}><Icon name="share" /> Share</button>
        <button className="hl-r6-panel-toggle" aria-label={panelOpen ? 'Hide sample side panel' : 'Show sample side panel'} aria-pressed={panelOpen} onClick={() => setPanelOpen(open => !open)}><Icon name="panel" /></button>
      </header>
      <div className="hl-r6-contextbar">
        <div className="hl-r6-segments" role="group" aria-label="Filter sample records by status">{['Open', 'In review', 'Closed', 'All'].map(value => <button key={value} aria-pressed={status === value} onClick={() => setStatus(value)}>{value}</button>)}</div>
        <div className="hl-r6-context-actions">
          <button title="Show all sample records" aria-label="Show all sample records" onClick={() => { setStatus('All'); setQuery('') }}><Icon name="filter" /></button>
          <button title="Reverse sample records" aria-label="Reverse sample records" onClick={() => setNotice('Sort is illustrative in this gallery.')}><Icon name="sort" /></button>
          <div className="hl-r6-view-switch" role="group" aria-label="Sample record view"><button aria-pressed={layout === 'Grid'} onClick={() => setLayout('Grid')}>Grid</button><button aria-pressed={layout === 'Board'} onClick={() => setLayout('Board')}>Board</button></div>
          <button className="hl-r6-new" onClick={() => setNotice('Record creation is simulated in this gallery.')}><Icon name="plus" size={13} /> New record <Icon name="chevron" size={12} /></button>
        </div>
      </div>
      {layout === 'Grid' ? <div className="hl-r6-table-scroll"><table className="hl-r6-table"><thead><tr><th><span className="hl-r6-check" /></th><th>ID</th><th>Record</th><th>Status</th><th>Owner</th><th>Type</th><th>Updated</th></tr></thead><tbody>{filtered.map(record => <tr key={record.id} className={selected === record.id ? 'is-selected' : ''} onClick={() => selectRecord(record)}><td><span className="hl-r6-check" /></td><td>{record.id}</td><td><button className="hl-r6-record-link" onClick={event => { event.stopPropagation(); selectRecord(record) }}>{record.title}</button></td><td><span className="hl-r6-status">{record.status}</span></td><td>{record.owner}</td><td>{record.type}</td><td>{record.updated}</td></tr>)}</tbody></table>{filtered.length === 0 && <p className="hl-r6-empty">No sample records match this view.</p>}</div> : <div className="hl-r6-board">{['Open', 'In review', 'Closed'].map(column => <section key={column}><h2>{column} <span>{filtered.filter(record => record.status === column).length}</span></h2>{filtered.filter(record => record.status === column).map(record => <button key={record.id} onClick={() => selectRecord(record)}><small>{record.id}</small><strong>{record.title}</strong><span>{record.owner}</span></button>)}</section>)}</div>}
      <footer className="hl-r6-list-footer"><span>{filtered.length} sample records <span className="hl-r6-footer-hint">Select a row to retarget the preview tab</span></span><span>Local fixture · no live data</span></footer>
      <div className="hl-r6-mobile-note">Gallery mock · no backend, persistence or AI connected.</div>
    </main>
    {panelOpen && <aside className="hl-r6-panel" aria-label="Rev6 tab host with sample record details">
      <div className="hl-r6-native"><AppShellRev6TabHost shellId="gallery-rev6-composed" owner={state.owner} presentationOwner={state.owner} presentation={view as AppShellRev6Presentation | null} onIntent={(intent: AppShellRev6Intent) => dispatch(intent)} /></div>
      <div className="hl-r6-panel-actions"><button aria-label="Open sample tab" aria-expanded={toolsOpen} onClick={() => setToolsOpen(open => !open)}><Icon name="plus" size={13} /></button><button aria-label="Hide sample side panel" onClick={() => setPanelOpen(false)}><Icon name="close" size={13} /></button></div>
      {toolsOpen && <div className="hl-r6-tools" role="group" aria-label="Sample tab kinds">{(['Details', 'Log', 'Timeline', 'Files', 'Assistant'] as const).map(kind => <button key={kind} onClick={() => openTool(kind)}>{kind}</button>)}</div>}
      {activeRecord ? <div className="hl-r6-details"><dl><div><dt>Status</dt><dd><span className="hl-r6-status">{activeRecord.status}</span></dd></div><div><dt>Owner</dt><dd>{activeRecord.owner}</dd></div><div><dt>Type</dt><dd>{activeRecord.type}</dd></div><div><dt>Updated</dt><dd>{activeRecord.updated}</dd></div><div><dt>Place</dt><dd>{activeRecord.place}</dd></div></dl></div> : <div className="hl-r6-details hl-r6-tool-details"><Icon name={active?.kind === 'Assistant' ? 'spark' : 'list'} size={20} /><p>{active ? `${active.kind} for ${active.target}` : 'Select a record to preview its details.'}</p><small>Representative content is mocked for this gallery.</small></div>}
      <div className="hl-r6-ask"><Icon name="spark" size={13} /><span>Ask about {activeRecord?.title ?? 'this view'}</span><kbd>Ctrl J</kbd></div>
    </aside>}
    {notice && <div className="hl-r6-toast" role="status">{notice}</div>}
  </div>
}

const meta = { title: 'Experimental/App Shell Rev6 Composed Mock', component: ComposedHarness, parameters: { layout: 'fullscreen', docs: { description: { story: 'Gallery-only frame and sample records around the real rev6 tab host and shared state. No backend, persistence or AI is connected; rev5 remains live.' } } } } satisfies Meta<typeof ComposedHarness>
export default meta
type Story = StoryObj<typeof meta>
export const RecordsWithTabHost: Story = {}
