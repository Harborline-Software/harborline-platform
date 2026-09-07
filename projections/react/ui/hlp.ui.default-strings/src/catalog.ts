/**
 * @harborline-software/ui-react — localization catalog (the `en` default + the catalog shape).
 *
 * THE ARCHITECTURE (CIC-confirmed 2026-06-25; see _shared/research/i18n-audit-2026-06-25.md):
 *
 *   The library is **framework-agnostic**. It does NOT depend on react-i18next, FormatJS,
 *   or any i18n runtime. Instead it follows the React-Aria / MUI / Ant `ConfigProvider`
 *   pattern: a `HarborlineLocaleProvider` accepts a PLAIN strings object (this catalog's
 *   shape), and components resolve their built-in UI text by stable KEY via the
 *   `useHarborlineStrings()` hook.
 *
 *   Harborline App owns the real i18n runtime (react-i18next + ar-SA bundle). It maps
 *   its translated strings into THIS catalog shape and feeds the provider. The library
 *   never sees `t()`.
 *
 * RESOLUTION PRECEDENCE (highest wins) — see `useHarborlineStrings`:
 *   1. An explicit per-instance string prop on the component (e.g. `<Alert dismissLabel=…>`).
 *   2. The catalog value supplied via `HarborlineLocaleProvider`.
 *   3. This `en` default catalog (so the library renders correct English with ZERO config).
 *
 * KEY-NAMING CONVENTION:
 *   - Dot-namespaced, lowerCamelCase segments: `common.close`, `dataGrid.operator.contains`.
 *   - First segment = the family/cluster: `common` (cross-component primitives shared by
 *     many components — close/cancel/dismiss/loading), then per-cluster namespaces a
 *     cluster owns (`dataGrid.*`, `scheduler.*`, `forms.*`, `pagination.*`, …).
 *   - The catalog is a FLAT `Record<string, string>` keyed by the dotted string. Flat (not
 *     nested) so a key is one lookup, the type is trivial, and the app's i18next nested
 *     bundle flattens into it cleanly when a host maps its own strings onto these keys.
 *
 * INTERPOLATION:
 *   - Single-brace `{name}` placeholders, resolved by `useHarborlineStrings` from a vars map.
 *     This is the LIBRARY's neutral convention — deliberately NOT i18next's double-brace
 *     `{{name}}`. The app resolves its own i18next `{{}}` to a final string BEFORE handing
 *     it into the catalog, so by the time a string reaches the library it is already
 *     translated; library-side `{name}` interpolation only fills component-runtime values
 *     (counts, item names) the app can't know at translation time.
 *
 * HOW A CLUSTER ADDS KEYS:
 *   1. Add the `en` default(s) under your cluster's namespace in `defaultStrings` below
 *      (keep keys grouped + alphabetical within a cluster block).
 *   2. Add the matching `HarborlineStringKey` literal to the union type (it is derived from
 *      `defaultStrings`, so adding to the object IS adding to the type).
 *   3. In the component, read the string via `useHarborlineStrings()` + the key, allowing an
 *      explicit prop to override (precedence rule 1). Never inline a hardcoded UI string.
 *   4. The app adds the SAME dotted keys to its locale bundles (Harborline App en-US.ts / ar-SA.ts).
 */

/**
 * The `en` default catalog. This is the source-of-truth key set AND the zero-config
 * English fallback. Every built-in UI string the library renders lives here.
 *
 * EXEMPLAR SCOPE (this foundation PR): the shared dismiss/close/cancel/loading family,
 * migrated end-to-end across Alert / StatusBanner / Notification / Sonner / MaskedText to
 * prove the pattern. The remaining ~430 strings are deliberately NOT extracted yet — they
 * follow this same pattern in subsequent extraction waves.
 */
export const defaultStrings = {
  // ── common.* — cross-component primitives shared by many components ──────────
  /** Generic "close" affordance (dialogs, popovers, panels). */
  'common.close': 'Close',
  /** Generic "cancel" affordance (confirm dialogs, forms). */
  'common.cancel': 'Cancel',
  /** Generic "confirm" affordance (confirm dialogs). */
  'common.confirm': 'Confirm',
  /** Generic dismiss affordance (alerts, banners) — aria-label on the ✕ button. */
  'common.dismiss': 'Dismiss',
  /** Dismiss affordance specific to a transient notification/toast — aria-label. */
  'common.dismissNotification': 'Dismiss notification',
  /** Generic loading / busy status text. */
  'common.loading': 'Loading',
  /** MaskedText: aria-label for the masked value when it is hidden. */
  'common.hiddenValue': 'hidden value',
  /** MaskedText: aria-label for the toggle button when the value is hidden (action = reveal). */
  'common.showValue': 'Show value',
  /** MaskedText: aria-label for the toggle button when the value is shown (action = hide). */
  'common.hideValue': 'Hide value',
  /** Generic "remove" affordance (chips, uploaded items) — aria-label on the ✕ button. */
  'common.remove': 'Remove',
  /** Generic "clear" affordance (clear a field/value). */
  'common.clear': 'Clear',
  /** Generic "browse" affordance (open a file picker). */
  'common.browse': 'Browse…',
  /** Generic "select all" affordance (selection grids/lists) — aria-label / checkbox label. */
  'common.selectAll': 'Select all',
  /** Generic save action. */
  'common.save': 'Save',
  /** Generic delete action. */
  'common.delete': 'Delete',
  /** Generic apply action. */
  'common.apply': 'Apply',
  /** Generic back action. */
  'common.back': 'Back',
  /** Generic done action. */
  'common.done': 'Done',

  // ── buttons.* — built-in button labels and accessible names ─────────────────────
  'buttons.copy': 'Copy',
  'buttons.copied': 'Copied!',
  'buttons.export': 'Export',
  'buttons.moreActions': 'More actions',
  'buttons.moreOptions': 'More options',
  'buttons.action': 'action',
  'buttons.format.csv': 'CSV (.csv)',
  'buttons.format.xlsx': 'Excel (.xlsx)',
  'buttons.format.pdf': 'PDF (.pdf)',
  'buttons.format.json': 'JSON (.json)',
  'buttons.format.md': 'Markdown (.md)',

  // ── feedback.* — feedback, document, and guided-tour controls ───────────────
  'feedback.confirm': 'Confirm',
  'feedback.copy': 'Copy',
  'feedback.copied': 'Copied',
  'feedback.copyCode': 'Copy code',
  'feedback.removeItem': 'Remove {label}',
  'feedback.progress': 'Progress',
  'feedback.connected': 'Connected',
  'feedback.noInternet': 'No internet connection',
  'feedback.reconnecting': 'Reconnecting…',
  'feedback.syncWhenReconnected': 'Your changes will sync when reconnected.',
  'feedback.retry': 'Retry',
  'feedback.page': 'Page {page}',
  'feedback.zoomOut': 'Zoom out',
  'feedback.zoomIn': 'Zoom in',
  'feedback.zoomLevel': 'Zoom level: {zoom}',
  'feedback.openPdfNewTab': 'Open PDF in a new tab',
  'feedback.open': 'Open',
  'feedback.pdfViewer': 'PDF viewer',
  'feedback.noPdfSource': 'No PDF source provided',
  'feedback.pdfEditor': 'PDF editor: {document}',
  'feedback.pdfDocument': 'PDF document',
  'feedback.pdfEditingTools': 'PDF editing tools',
  'feedback.readMode': 'Read mode',
  'feedback.highlightText': 'Highlight text',
  'feedback.underlineText': 'Underline text',
  'feedback.stickyNote': 'Sticky note',
  'feedback.signDocument': 'Sign document',
  'feedback.fillForm': 'Fill form',
  'feedback.exportPdf': 'Export PDF',
  'feedback.exportComplete': 'Export complete',
  'feedback.exportFailed': 'Export failed',
  'feedback.loadingPdf': 'Loading PDF',
  'feedback.pdfContent': 'PDF content',
  'feedback.editNote': 'Edit note',
  'feedback.deleteNote': 'Delete note',
  'feedback.saveNote': 'Save note',
  'feedback.noteOnPage': 'Note: {note} on page {page}',
  'feedback.highlightOnPage': 'Highlight on page {page}',
  'feedback.underlineOnPage': 'Underline on page {page}',
  'feedback.pageNumberUnknown': 'Page number, {page} of —',
  'feedback.signatureArea': 'Signature area',
  'feedback.signatureProvided': 'Signature provided',
  'feedback.signatureEmpty': 'Signature area — empty',
  'feedback.tour': 'Tour',
  'feedback.goToStep': 'Go to step {step}',
  'feedback.previous': 'Previous',
  'feedback.next': 'Next',
  'feedback.finish': 'Finish',
  'feedback.stepProgress': 'Step {current} of {total}: {title}',

  // ── ai.* — assistant prompts, chat, speech, and conversation history ────────
  'ai.askQuestion': 'Ask a question…',
  'ai.ask': 'Ask AI…',
  'ai.thinking': 'Thinking…',
  'ai.noSuggestions': 'No suggestions',
  'ai.view.responses': 'Responses',
  'ai.view.prompt': 'Prompt',
  'ai.view.commands': 'Commands',
  'ai.actionNumber': 'Action {number}',
  'ai.send': 'Send',
  'ai.submit': 'Submit',
  'ai.cancelGeneration': 'Cancel',
  'ai.copy': 'Copy',
  'ai.discard': 'Discard',
  'ai.commands': 'Commands',
  'ai.speechToText': 'Speech to text',
  'ai.smartPaste': 'Auto Fill',
  'ai.smartPasteReading': 'Reading…',
  'ai.speechStart': 'Start speech to text',
  'ai.speechStop': 'Stop recording',
  'ai.attachedFiles': 'Attached files',
  'ai.removeAttachment': 'Remove {name}',
  'ai.characterCount': '{current} / {maximum}',
  'ai.chat.placeholder': 'Type a message…',
  'ai.chat.messageOptions': 'Message options',
  'ai.chat.messageActions': 'Message actions',
  'ai.chat.fileAttachmentsOne': '{count} file attachment',
  'ai.chat.fileAttachmentsOther': '{count} file attachments',
  'ai.chat.fileSize': '({size} KB)',
  'ai.chat.fileAction': '{action}: {name}',
  'ai.chat.uploading': 'Uploading…',
  'ai.chat.error': 'Error',
  'ai.chat.suggestedActions': 'Suggested actions',
  'ai.chat.userFallback': 'User',
  'ai.chat.userTyping': '{name} is typing…',
  'ai.chat.reply': 'In reply to {author}: {message}',
  'ai.chat.messageFallback': 'message',
  'ai.chat.pinnedMessages': 'Pinned messages',
  'ai.chat.unpinMessage': 'Unpin message from {name}',
  'ai.chat.conversation': 'Conversation',
  'ai.chat.deleted': 'This message was deleted.',
  'ai.chat.roleUser': 'user',
  'ai.chat.roleAssistant': 'assistant',
  'ai.chat.messageArticle': '{role} message',
  'ai.chat.messageArticleTime': '{role} message, {time}',
  'ai.chat.loadMore': 'Load more messages',
  'ai.chat.showMore': 'Show more',
  'ai.chat.showLess': 'Show less',
  'ai.chat.failedToSend': 'Failed to send',
  'ai.chat.retry': 'Retry',
  'ai.chat.aiTyping': 'AI is typing…',
  'ai.chat.scrollBottom': 'Scroll to bottom',
  'ai.chat.inputLabel': 'Message input',
  'ai.chat.status.pending': 'pending',
  'ai.chat.status.streaming': 'streaming',
  'ai.chat.status.sent': 'sent',
  'ai.chat.status.delivered': 'delivered',
  'ai.chat.status.read': 'read',
  'ai.chat.status.complete': 'complete',
  'ai.chat.status.error': 'error',
  'ai.chat.status.failed': 'failed',
  'ai.conversations.heading': 'Conversations',
  'ai.conversations.new': 'New conversation',
  'ai.conversations.empty': 'No conversations yet.',
  'ai.conversations.rename': 'Rename',
  'ai.conversations.delete': 'Delete',
  'ai.conversations.cancel': 'Cancel',
  'ai.conversations.actionsFor': 'Actions for {title}',
  'ai.conversations.renameTitle': 'Rename: {title}',
  'ai.conversations.deleteTitle': 'Delete: {title}',

  // ── badges.* — badge, code-image, and count-indicator family ───────────────────
  /** Barcode: default image accessible name (interpolates {value}). */
  'badges.barcode': 'Barcode: {value}',
  /** QRCode: default image accessible name (interpolates {value}). */
  'badges.qrCode': 'QR code: {value}',
  /** NumberBadge: default count announcement, singular (interpolates {count}). */
  'badges.notificationCountOne': '{count} notification',
  /** NumberBadge: default count announcement, plural (interpolates {count}). */
  'badges.notificationCountOther': '{count} notifications',

  // ── charts.* — chart names, controls, legends, and AT summaries ─────────────
  'charts.areaChart': 'Area chart',
  'charts.barChart': 'Bar chart',
  'charts.boxPlotChart': 'Box plot chart',
  'charts.bubbleChart': 'Bubble chart',
  'charts.bulletChart': 'Bullet chart',
  'charts.candlestickChart': 'Candlestick chart',
  'charts.chart': 'Chart',
  'charts.columnChart': 'Column chart',
  'charts.donutChart': 'Donut chart',
  'charts.drilldownChart': 'Drilldown chart',
  'charts.funnelChart': 'Funnel chart',
  'charts.heatmapChart': 'Heatmap chart',
  'charts.lineChart': 'Line chart',
  'charts.ohlcChart': 'OHLC chart',
  'charts.pieChart': 'Pie chart',
  'charts.polarChart': 'Polar chart',
  'charts.pyramidChart': 'Pyramid chart',
  'charts.radarAreaChart': 'Radar area chart',
  'charts.radarChart': 'Radar chart',
  'charts.radarColumnChart': 'Radar column chart',
  'charts.rangeAreaChart': 'Range area chart',
  'charts.rangeBarChart': 'Range bar chart',
  'charts.sankeyDiagram': 'Sankey diagram',
  'charts.sankeyFlowDiagram': 'Sankey flow diagram',
  'charts.scatterChart': 'Scatter chart',
  'charts.scatterLineChart': 'Scatter line chart',
  'charts.stockChart': 'Stock chart',
  'charts.waterfallChart': 'Waterfall chart',
  'charts.chartType': 'Chart type',
  'charts.type.line': 'Line',
  'charts.type.bar': 'Bar',
  'charts.type.area': 'Area',
  'charts.type.scatter': 'Scatter',
  'charts.type.pie': 'Pie',
  'charts.type.donut': 'Donut',
  'charts.type.funnel': 'Funnel',
  'charts.back': 'Back',
  'charts.actual': 'Actual',
  'charts.target': 'Target',
  'charts.series': 'Series',
  'charts.outliers': '{series} outliers',
  'charts.value': 'Value',
  'charts.waterfallRange': 'Waterfall: {start} to {end}',
  'charts.waterfallItem': 'Waterfall: {item}',
  'charts.item': 'Item',
  'charts.delta': 'Delta',
  'charts.runningTotal': 'Running Total',
  'charts.sankeyLink': '{source} to {target}: {value}',

  // ── dataGrid.* — the data-grid / table family ───────────────────────────────────
  // Filter operators — the canonical label set (DataGrid OperatorMenu + Filter builder).
  // A locale catalog maps these keys; the symbolic number/date variants are separate keys
  // so a locale can keep `>`/`>=` symbolic OR spell them out per its conventions.
  /** Filter operator: substring match. */
  'dataGrid.operator.contains': 'Contains',
  /** Filter operator: equality. */
  'dataGrid.operator.eq': 'Equals',
  /** Filter operator: inequality. */
  'dataGrid.operator.neq': 'Not equals',
  /** Filter operator: greater-than (spelled out — used by the DataGrid OperatorMenu). */
  'dataGrid.operator.gt': 'Greater than',
  /** Filter operator: less-than (spelled out — used by the DataGrid OperatorMenu). */
  'dataGrid.operator.lt': 'Less than',
  /** Filter operator: greater-than-or-equal (spelled out). */
  'dataGrid.operator.gte': 'Greater than or equal',
  /** Filter operator: less-than-or-equal (spelled out). */
  'dataGrid.operator.lte': 'Less than or equal',
  /** Filter operator: prefix match. */
  'dataGrid.operator.startswith': 'Starts with',
  /** Filter operator: suffix match. */
  'dataGrid.operator.endswith': 'Ends with',
  /** Filter operator: value is null. */
  'dataGrid.operator.isnull': 'Is null',
  /** Filter operator: value is not null. */
  'dataGrid.operator.isnotnull': 'Is not null',
  /** Filter operator: value is empty string. */
  'dataGrid.operator.isempty': 'Is empty',
  /** Filter operator: value is not empty string. */
  'dataGrid.operator.isnotempty': 'Is not empty',
  /** Filter operator (number, symbolic): greater-than. */
  'dataGrid.operator.gtSymbol': '>',
  /** Filter operator (number, symbolic): greater-than-or-equal. */
  'dataGrid.operator.gteSymbol': '>=',
  /** Filter operator (number, symbolic): less-than. */
  'dataGrid.operator.ltSymbol': '<',
  /** Filter operator (number, symbolic): less-than-or-equal. */
  'dataGrid.operator.lteSymbol': '<=',
  /** Filter operator (date): equality. */
  'dataGrid.operator.dateOn': 'On',
  /** Filter operator (date): strictly after. */
  'dataGrid.operator.dateAfter': 'After',
  /** Filter operator (date): strictly before. */
  'dataGrid.operator.dateBefore': 'Before',
  /** Filter operator (date): on or after. */
  'dataGrid.operator.dateOnOrAfter': 'On or after',
  /** Filter operator (date): on or before. */
  'dataGrid.operator.dateOnOrBefore': 'On or before',
  /** Filter operator (boolean): is. */
  'dataGrid.operator.boolIs': 'Is',
  /** Filter operator (boolean): is not. */
  'dataGrid.operator.boolIsNot': 'Is not',

  // Column menu (sort / hide / filter / chooser) -------------------------------------
  /** DataGrid column menu: sort ascending. */
  'dataGrid.columnMenu.sortAsc': 'Sort ascending',
  /** DataGrid column menu: sort descending. */
  'dataGrid.columnMenu.sortDesc': 'Sort descending',
  /** DataGrid column menu: clear sort. */
  'dataGrid.columnMenu.clearSort': 'Clear sort',
  /** DataGrid column menu: hide this column. */
  'dataGrid.columnMenu.hideColumn': 'Hide column',
  /** DataGrid column menu: open the per-column filter. */
  'dataGrid.columnMenu.filter': 'Filter…',
  /** DataGrid column menu: open the column chooser. */
  'dataGrid.columnMenu.columns': 'Columns…',
  /** DataGrid: per-column menu trigger aria-label (interpolates {column}). */
  'dataGrid.columnMenuLabel': '{column} column menu',
  /** DataGrid: column-visibility chooser dialog aria-label. */
  'dataGrid.columnVisibility': 'Column visibility',
  /** DataGrid: column-chooser heading. */
  'dataGrid.columnsHeading': 'Columns',
  /** DataGrid: lock-indicator title on a locked column. */
  'dataGrid.columnLocked': 'Column locked',
  /** DataGrid: drag-handle title on a reorderable column. */
  'dataGrid.dragToReorder': 'Drag to reorder',

  // Filter row / operator menu -------------------------------------------------------
  /** DataGrid: operator-menu aria-label (interpolates {column}). */
  'dataGrid.filterOperatorFor': 'Filter operator for {column}',
  /** DataGrid: filter-value input aria-label (interpolates {column}). */
  'dataGrid.filterValueFor': 'Filter value for {column}',
  /** DataGrid: per-column filter-value input placeholder. */
  'dataGrid.filterPlaceholder': 'Filter…',
  /** DataGrid: clear-this-column-filter menu item. */
  'dataGrid.clearFilter': 'Clear filter',

  // Selection / row actions ----------------------------------------------------------
  /** DataGrid: select-row checkbox aria-label (interpolates {row}). */
  'dataGrid.selectRow': 'Select row {row}',
  /** DataGrid: bulk-action-bar selected-count, singular (interpolates {count}). */
  'dataGrid.selectedOne': '{count} selected',
  /** DataGrid: bulk-action-bar selected-count, plural (interpolates {count}). */
  'dataGrid.selectedOther': '{count} selected',
  /** DataGrid: edit-row button aria-label. */
  'dataGrid.editRow': 'Edit row',
  /** DataGrid: edit-cell button aria-label (interpolates {column}). */
  'dataGrid.editColumn': 'Edit {column}',
  /** DataGrid: save-row-edits button aria-label. */
  'dataGrid.saveRowEdits': 'Save row edits',
  /** DataGrid: cancel-row-edits button aria-label. */
  'dataGrid.cancelRowEdits': 'Cancel row edits',
  /** DataGrid: pending-changes region aria-label. */
  'dataGrid.pendingChanges': 'Pending changes',
  /** DataGrid: save-row-edits button text. */
  'dataGrid.save': 'Save',
  /** DataGrid: cancel-row-edits button text. */
  'dataGrid.cancel': 'Cancel',
  /** DataGrid: edit-row button text. */
  'dataGrid.edit': 'Edit',
  /** DataGrid: commit-batch-changes button text. */
  'dataGrid.commit': 'Commit',
  /** DataGrid: discard-batch-changes button text. */
  'dataGrid.discard': 'Discard',
  /** DataGrid: pending-changes count, singular (interpolates {count}). */
  'dataGrid.pendingCountOne': '{count} pending change',
  /** DataGrid: pending-changes count, plural (interpolates {count}). */
  'dataGrid.pendingCountOther': '{count} pending changes',
  /** DataGrid: grid-toolbar region aria-label. */
  'dataGrid.toolbar': 'Grid toolbar',
  /** DataGrid: bulk-actions region aria-label. */
  'dataGrid.bulkActions': 'Bulk actions',
  /** DataGrid: active-sorts chip-bar aria-label. */
  'dataGrid.activeSorts': 'Active sorts',
  /** DataGrid: remove-sort chip button aria-label (interpolates {column}). */
  'dataGrid.removeSort': 'Remove {column} sort',

  // Empty / pagination (built-in pager inside DataGrid) ------------------------------
  /** DataGrid: default empty-state text (the en byte-identical literal includes the period). */
  'dataGrid.noResults': 'No results.',
  'dataGrid.collapseGroup': 'Collapse group',
  'dataGrid.collapseRow': 'Collapse row',
  'dataGrid.editingRow': 'Editing row',
  'dataGrid.expandGroup': 'Expand group',
  'dataGrid.expandRow': 'Expand row',

  // Diagram -------------------------------------------------------------------------
  'diagram.reset': 'Reset',
  'diagram.resetZoomAndPan': 'Reset zoom and pan',

  // Gantt ---------------------------------------------------------------------------
  'gantt.day': 'Day',
  'gantt.editTask': 'Edit task',
  'gantt.end': 'End',
  'gantt.expand': 'Expand',
  'gantt.month': 'Month',
  'gantt.name': 'Name',
  'gantt.progress': 'Progress (%)',
  'gantt.save': 'Save',
  'gantt.start': 'Start',
  'gantt.title': 'Title',
  'gantt.week': 'Week',
  'gantt.weekNumber': 'Week {week}',
  'gantt.zoom': 'Zoom',

  // OrgChart ------------------------------------------------------------------------
  'orgChart.addChild': 'Add child',
  'orgChart.collapse': 'Collapse',
  'orgChart.collapseGroup': 'Collapse group',
  'orgChart.edit': 'Edit',
  'orgChart.expand': 'Expand',
  'orgChart.expandGroup': 'Expand group',
  'orgChart.nodeActions': 'Node actions',
  /** OrgChart: selection-checkbox label (interpolates {title}). */
  'orgChart.selectNode': 'Select {title}',

  // Spreadsheet ---------------------------------------------------------------------
  'spreadsheet.addSheet': 'Add sheet',
  'spreadsheet.alignCenter': 'Align center',
  'spreadsheet.alignLeft': 'Align left',
  'spreadsheet.alignRight': 'Align right',
  'spreadsheet.allBorders': 'All borders',
  'spreadsheet.bold': 'Bold',
  'spreadsheet.border': 'Border',
  'spreadsheet.bottomBorder': 'Bottom border',
  'spreadsheet.cellReference': 'Cell reference',
  'spreadsheet.defaultFont': 'Default',
  'spreadsheet.define': 'Define',
  'spreadsheet.defineNamedRange': 'Define named range',
  'spreadsheet.delete': 'Delete',
  'spreadsheet.deleteColumn': 'Delete column',
  'spreadsheet.deleteNamedRange': 'Delete named range {name}',
  'spreadsheet.deleteRow': 'Delete row',
  'spreadsheet.export': 'Export',
  'spreadsheet.fillColor': 'Fill color',
  'spreadsheet.fontFamily': 'Font family',
  'spreadsheet.fontSize': 'Font size',
  'spreadsheet.formula': 'Formula',
  'spreadsheet.formulaBar': 'Formula bar',
  'spreadsheet.formulaOrValue': 'Formula or value',
  'spreadsheet.import': 'Import',
  'spreadsheet.insertColumnRight': 'Insert column right',
  'spreadsheet.insertRowBelow': 'Insert row below',
  'spreadsheet.italic': 'Italic',
  'spreadsheet.name': 'Name',
  'spreadsheet.navigateToNamedRange': 'Navigate to named range',
  'spreadsheet.noBorder': 'No border',
  'spreadsheet.outerBorder': 'Outer border',
  'spreadsheet.redo': 'Redo',
  'spreadsheet.referenceExample': 'Ref (e.g. A1:B3)',
  'spreadsheet.rename': 'Rename',
  'spreadsheet.textColor': 'Text color',
  'spreadsheet.underline': 'Underline',
  'spreadsheet.undo': 'Undo',

  // ── pagination.* — Pager + DataGrid built-in pager ─────────────────────────────
  /** Pagination: nav landmark aria-label. */
  'pagination.label': 'Pagination',
  /** Pagination: previous-page button aria-label. */
  'pagination.previousPage': 'Previous page',
  /** Pagination: next-page button aria-label. */
  'pagination.nextPage': 'Next page',
  /** Pagination: rows-per-page label + select aria-label. */
  'pagination.rowsPerPage': 'Rows per page',
  /** Pagination: page-number button aria-label (interpolates {page}). */
  'pagination.page': 'Page {page}',
  /** Pagination: jump-to-page input default aria-label (Pager `inputAriaLabel` default). */
  'pagination.pageInputLabel': 'Page',
  /** Pagination: page-list ordered-list aria-label. */
  'pagination.pageList': 'Page list',
  /** Pagination: no-results summary (when total is 0). */
  'pagination.noResults': 'No results',
  /** Pagination: range summary (interpolates {from}, {to}, {total}). */
  'pagination.showingRange': 'Showing {from}–{to} of {total}',
  /** Pagination: prev-next-mode "Page X of Y" summary (interpolates {page}, {total}). */
  'pagination.pageOf': 'Page {page} of {total}',
  /** Pagination: input-mode "of Y" suffix (interpolates {total}). */
  'pagination.ofTotal': 'of {total}',

  // ── transfer.* — Transfer (dual-list) ──────────────────────────────────────────
  /** Transfer: select-all-items checkbox aria-label. */
  'transfer.selectAllItems': 'Select all items',
  /** Transfer: search-list input aria-label. */
  'transfer.searchList': 'Search list',
  /** Transfer: move-selected-right button aria-label. */
  'transfer.moveRight': 'Move selected items right',
  /** Transfer: move-selected-left button aria-label. */
  'transfer.moveLeft': 'Move selected items left',
  /** Transfer: default accessible/visible title for the source list. */
  'transfer.availableItems': 'Available items',
  /** Transfer: default accessible/visible title for the target list. */
  'transfer.selectedItems': 'Selected items',

  // ── treeList.* — TreeList ──────────────────────────────────────────────────────
  /** TreeList: select-all-rows checkbox aria-label. */
  'treeList.selectAllRows': 'Select all rows',
  /** TreeList: expand-row toggle aria-label (collapsed → action is expand). */
  'treeList.expand': 'Expand',
  /** TreeList: collapse-row toggle aria-label (expanded → action is collapse). */
  'treeList.collapse': 'Collapse',

  // ── masterDetail.* — MasterDetail ──────────────────────────────────────────────
  /** MasterDetail: master-list region default aria-label (prop-overridable). */
  'masterDetail.itemsList': 'Items list',
  /** MasterDetail: detail-pane region aria-label. */
  'masterDetail.itemDetail': 'Item detail',

  // ── listBox.* — ListBox ────────────────────────────────────────────────────────
  /** ListBox: remove-item button aria-label. */
  'listBox.remove': 'Remove',
  /** ListBox: move-item-up button aria-label. */
  'listBox.moveUp': 'Move up',
  /** ListBox: move-item-down button aria-label. */
  'listBox.moveDown': 'Move down',
  /** ListBox: default accessible name for the listbox surface. */
  'listBox.items': 'Items',

  // ── selectionBasket.* — SelectionBasket (the generic curated-batch tray) ────────
  /** SelectionBasket: default heading for the tray. Deliberately domain-free. */
  'selectionBasket.title': 'Selection',
  /** SelectionBasket: empty-state text when nothing has been collected yet. */
  'selectionBasket.empty': 'Nothing selected yet',
  /** SelectionBasket: visually-hidden count, singular (interpolates {count}). */
  'selectionBasket.countOne': '{count} selected',
  /** SelectionBasket: visually-hidden count, plural (interpolates {count}). */
  'selectionBasket.countOther': '{count} selected',

  // ── pivotGrid.* — PivotGrid ────────────────────────────────────────────────────
  /** PivotGrid: empty-config placeholder (no rows/columns/measures yet). */
  'pivotGrid.configurePlaceholder': 'Configure rows, columns, and measures',
  /** PivotGrid: fields-panel heading. */
  'pivotGrid.fields': 'Fields',
  /** PivotGrid: filter/sort-panel heading. */
  'pivotGrid.filterSort': 'Filter / Sort',
  /** PivotGrid: add-field-to-rows button title. */
  'pivotGrid.addToRows': 'Add to Rows',
  /** PivotGrid: add-field-to-columns button title. */
  'pivotGrid.addToColumns': 'Add to Columns',
  /** PivotGrid: add-field-to-measures button title. */
  'pivotGrid.addToMeasures': 'Add to Measures',
  /** PivotGrid: rows drop-zone label. */
  'pivotGrid.rows': 'Rows',
  /** PivotGrid: columns drop-zone label. */
  'pivotGrid.columns': 'Columns',
  /** PivotGrid: measures drop-zone label. */
  'pivotGrid.measures': 'Measures',
  /** PivotGrid: grand-total header/cell label. */
  'pivotGrid.total': 'Total',
  /** PivotGrid: subtotal-row label (interpolates {member}). */
  'pivotGrid.subtotal': '{member} — Subtotal',
  /** PivotGrid: sort-control label prefix. */
  'pivotGrid.sort': 'Sort:',
  /** PivotGrid: ascending-sort button label. */
  'pivotGrid.sortAsc': '↑ Asc',
  /** PivotGrid: descending-sort button label. */
  'pivotGrid.sortDesc': '↓ Desc',
  /** PivotGrid: no-sort button label. */
  'pivotGrid.sortNone': 'None',
  /** PivotGrid: filter-values control label. */
  'pivotGrid.filterValues': 'Filter values:',
  /** PivotGrid: clear-filter-values button. */
  'pivotGrid.clear': 'Clear',
  /** PivotGrid: has-active-filter indicator aria-label. */
  'pivotGrid.hasFilter': 'has filter',
  /** PivotGrid: remove-field-from-zone button aria-label (interpolates {label}, {zone}). */
  'pivotGrid.removeFromZone': 'Remove {label} from {zone}',
  /** PivotGrid: hierarchical row collapse button aria-label. */
  'pivotGrid.collapse': 'Collapse',
  /** PivotGrid: hierarchical row expand button aria-label. */
  'pivotGrid.expand': 'Expand',

  // ── taskBoard.* — TaskBoard (Kanban) ───────────────────────────────────────────
  /** TaskBoard: low-priority card label/aria. */
  'taskBoard.priorityLow': 'Low priority',
  /** TaskBoard: medium-priority card label/aria. */
  'taskBoard.priorityMedium': 'Medium priority',
  /** TaskBoard: high-priority card label/aria. */
  'taskBoard.priorityHigh': 'High priority',
  /** TaskBoard: critical-priority card label/aria. */
  'taskBoard.priorityCritical': 'Critical priority',
  /** TaskBoard: column-name input aria-label. */
  'taskBoard.columnName': 'Column name',
  /** TaskBoard: edit-column button aria-label. */
  'taskBoard.editColumn': 'Edit column',
  /** TaskBoard: delete-column button aria-label. */
  'taskBoard.deleteColumn': 'Delete column',
  /** TaskBoard: add-card button aria-label. */
  'taskBoard.addCard': 'Add card',
  /** TaskBoard: add-column button aria-label. */
  'taskBoard.addColumn': 'Add column',
  /** TaskBoard: add-column button text. */
  'taskBoard.addColumnText': '+ Add column',

  // ── filterBuilder.* — Filter (composite filter builder) ─────────────────────────
  /** Filter builder: remove-filter-row button aria-label. */
  'filterBuilder.removeFilter': 'Remove filter',
  /** Filter builder: remove-group button aria-label. */
  'filterBuilder.removeGroup': 'Remove group',
  /** Filter builder: logic-toggle group aria-label. */
  'filterBuilder.logic': 'Filter logic',
  /**
   * Filter builder: field-picker `<select>` accessible name. The control has no visible
   * label (the row is a compact field/operator/value strip), so this IS its accessible
   * name — without it axe `select-name` fails (critical, WCAG 4.1.2) on any populated row.
   */
  'filterBuilder.field': 'Filter field',
  /** Filter builder: operator-picker `<select>` accessible name (see `filterBuilder.field`). */
  'filterBuilder.operator': 'Filter operator',
  /** Filter builder: value control accessible name (see `filterBuilder.field`). */
  'filterBuilder.value': 'Filter value',
  /**
   * Filter builder: AND logic toggle text. en default is the rendered ALL-CAPS form (the
   * component no longer force-`.toUpperCase()`s it so a locale can supply its own casing;
   * the visual `uppercase` Tailwind class still styles it). Accessible name = this value.
   */
  'filterBuilder.and': 'AND',
  /** Filter builder: OR logic toggle text (see `filterBuilder.and`). */
  'filterBuilder.or': 'OR',
  /** Filter builder: add-filter-row button text. */
  'filterBuilder.addFilter': '+ Add filter',
  /** Filter builder: add-group button text. */
  'filterBuilder.addGroup': '+ Add group',
  /** Filter builder: value input placeholder. */
  'filterBuilder.valuePlaceholder': 'Value…',
  /** Filter builder: boolean true option. */
  'filterBuilder.true': 'True',
  /** Filter builder: boolean false option. */
  'filterBuilder.false': 'False',

  // ── structural.* — list-toolbar structural controls ────────────────────────────
  /** SearchInput: zero-config search placeholder and accessible name. */
  'structural.search.placeholder': 'Search…',
  /** SearchInput: clear-current-search button accessible name. */
  'structural.search.clear': 'Clear search',
  /** ListToolbar: clear all active search/filter values. */
  'structural.listToolbar.clearFilters': 'Clear filters',
  /** ListToolbar: accessible name for the grouped list controls. */
  'structural.listToolbar.label': 'List filters and actions',
  /** ColumnVisibilityMenu: trigger accessible name. */
  'structural.columnVisibility.label': 'Column visibility',
  /** ColumnVisibilityMenu: singular hidden-column count in the trigger accessible name. */
  'structural.columnVisibility.hiddenCount.one': '{count} hidden',
  /** ColumnVisibilityMenu: plural hidden-column count in the trigger accessible name. */
  'structural.columnVisibility.hiddenCount.other': '{count} hidden',
  /** ColumnVisibilityMenu: visible trigger label. */
  'structural.columnVisibility.columns': 'Columns',
  /** SavedViewsMenu: trigger accessible name. */
  'structural.savedViews.label': 'Saved views',
  /** SavedViewsMenu: compact visible trigger label. */
  'structural.savedViews.views': 'Views',
  /** SavedViewsMenu: heading above the new-view form. */
  'structural.savedViews.saveCurrent': 'Save current view',
  /** SavedViewsMenu: new-view name input placeholder. */
  'structural.savedViews.namePlaceholder': 'View name…',
  /** SavedViewsMenu: submit-new-view button. */
  'structural.savedViews.save': 'Save',
  /** SavedViewsMenu: heading above the saved-view list. */
  'structural.savedViews.saved': 'Saved',
  /** SavedViewsMenu: delete-view button accessible name (interpolates {name}). */
  'structural.savedViews.delete': 'Delete view {name}',
  /** SavedViewsMenu: empty-state message. */
  'structural.savedViews.empty': 'No saved views yet.',
  /** SortControl: group accessible name. */
  'structural.sort.label': 'Sort',
  /** SortControl: field-select accessible name. */
  'structural.sort.by': 'Sort by',
  /** SortControl: ascending-state toggle accessible name. */
  'structural.sort.ascendingToggle': 'Sort ascending, click to toggle',
  /** SortControl: descending-state toggle accessible name. */
  'structural.sort.descendingToggle': 'Sort descending, click to toggle',

  // ── forms.* — the forms cluster (labels, placeholders, aria-labels, empty states) ──
  // FieldWrapper / FloatingLabel / Label suffixes -------------------------------------
  /** Suffix appended to a label for an optional field (lowercase parenthetical). */
  'forms.optionalSuffix': '(optional)',
  /** Suffix appended to a label for an optional field (capitalized parenthetical). */
  'forms.optionalSuffixCapitalized': '(Optional)',
  /** sr-only suffix marking a required field (announced after the visual asterisk). */
  'forms.requiredSuffix': '(required)',

  // AddressForm ----------------------------------------------------------------------
  /** AddressForm: street-address line-1 label. */
  'forms.address.street': 'Street address',
  /** AddressForm: street-address line-2 label. */
  'forms.address.street2': 'Apt, suite, unit, etc.',
  /** AddressForm: city label. */
  'forms.address.city': 'City',
  /** AddressForm: state/province label. */
  'forms.address.state': 'State',
  /** AddressForm: postal-code label. */
  'forms.address.zip': 'ZIP code',
  /** AddressForm: country label. */
  'forms.address.country': 'Country',
  /** AddressForm: street-address line-1 example placeholder. */
  'forms.address.streetPlaceholder': '123 Main St',
  /** AddressForm: street-address line-2 example placeholder. */
  'forms.address.street2Placeholder': 'Unit 4B',
  /** AddressForm: city example placeholder. */
  'forms.address.cityPlaceholder': 'Seattle',
  /** AddressForm: country example placeholder. */
  'forms.address.countryPlaceholder': 'United States',

  // CreditCardField ------------------------------------------------------------------
  /** CreditCardField: card-number label. */
  'forms.creditCard.number': 'Card number',
  /** CreditCardField: composite label for form-level validation. */
  'forms.creditCard.group': 'Payment card',
  /** CreditCardField: cardholder-name label. */
  'forms.creditCard.name': 'Name on card',
  /** CreditCardField: expiry label. */
  'forms.creditCard.expiry': 'Expiry',
  /** CreditCardField: CVC label. */
  'forms.creditCard.cvc': 'CVC',
  /** CreditCardField: cardholder-name example placeholder. */
  'forms.creditCard.namePlaceholder': 'Jane Smith',
  /** CreditCardField: expiry example placeholder. */
  'forms.creditCard.expiryPlaceholder': 'MM/YY',
  /** NumberFormatField: generic amount label for validation composition. */
  'forms.numberFormat.amount': 'Amount',

  // Shared field-picker defaults -----------------------------------------------------
  /** Generic select-only field placeholder. */
  'forms.selectPlaceholder': 'Select…',
  /** Generic search placeholder inside a picker. */
  'forms.searchPlaceholder': 'Search…',
  /** Editable combo-box placeholder. */
  'forms.selectOrTypePlaceholder': 'Select or type…',
  /** Search/filter input placeholder inside a picker. */
  'forms.filterPlaceholder': 'Filter...',
  /** Filter input accessible name inside a picker. */
  'forms.filterOptions': 'Filter options',
  /** Search input accessible name inside a picker. */
  'forms.searchOptions': 'Search options',
  /** SearchField: clear-search action accessible name. */
  'forms.clearSearch': 'Clear search',
  /** Picker list accessible name. */
  'forms.options': 'Options',
  /** Picker trigger accessible name. */
  'forms.selectOptions': 'Select options',
  /** DropDownTree: default trigger placeholder. */
  'forms.treeSelectPlaceholder': 'Select...',
  /** MultiSelectTree: default trigger placeholder. */
  'forms.multiTreeSelectPlaceholder': 'Select items...',
  /** MultiColumnComboBox: default trigger placeholder. */
  'forms.multiColumnSelectPlaceholder': 'Select...',
  /** SearchableSelect: default trigger placeholder. */
  'forms.searchableSelectPlaceholder': 'Select an option',
  /** Picker trigger state while loading. */
  'forms.loadingOptions': 'Loading options',
  /** Legacy drop-down loading text, including its visible ellipsis. */
  'forms.loading': 'Loading...',
  /** Picker trigger action while closed. */
  'forms.openOptions': 'Open options',
  /** Picker trigger action while open. */
  'forms.closeOptions': 'Close options',
  /** Generic empty result for a searchable field picker. */
  'forms.noResults': 'No results found',
  /** Generic empty result when a picker has no available options. */
  'forms.noOptions': 'No options found.',

  // Color controls ------------------------------------------------------------------
  /** ColorGradient: hue slider accessible name. */
  'forms.color.hue': 'Hue',
  /** ColorGradient: opacity slider accessible name. */
  'forms.color.opacity': 'Opacity',
  /** ColorGradient: hexadecimal color input accessible name. */
  'forms.color.hex': 'Hex color',
  /** ColorPalette: palette group accessible name. */
  'forms.color.palette': 'Color palette',
  /** ColorPicker: default trigger placeholder. */
  'forms.color.pick': 'Pick a color',
  /** FlatColorPicker: gradient tab label. */
  'forms.color.gradient': 'Gradient',
  /** FlatColorPicker: palette tab label. */
  'forms.color.paletteView': 'Palette',

  // ESignatureField ------------------------------------------------------------------
  /** ESignatureField: default acceptance checkbox label. */
  'forms.esign.acceptance': 'I agree that this electronic signature is legally binding',
  /** ESignatureField: tab label for the draw method. */
  'forms.esign.methodDraw': 'Draw',
  /** ESignatureField: tab label for the type method. */
  'forms.esign.methodType': 'Type',
  /** ESignatureField: tab label for the upload method. */
  'forms.esign.methodUpload': 'Upload image',
  /** ESignatureField: aria-label on the method tablist. */
  'forms.esign.methodTablistLabel': 'Signature method',
  /** ESignatureField: aria-label for the single-method (no-tablist) capture region. */
  'forms.esign.singleCaptureLabel': 'Signature capture — {method}',
  /** ESignatureField: canvas role=img aria-label for the draw area. */
  'forms.esign.drawAreaLabel': 'Signature drawing area',
  /** ESignatureField: canvas fallback text content for AT that reads canvas children. */
  'forms.esign.drawAreaFallback':
    'Signature drawing area. Use a mouse or touch screen to draw your signature. For keyboard access, switch to the Type tab.',
  /** ESignatureField: sr-only draw-panel instructions. */
  'forms.esign.drawInstructions':
    'Draw your signature in the box above. Use the Clear button to start over. If you are using a keyboard or screen reader, switch to the Type tab to enter your name instead.',
  /** ESignatureField: polite keyboard hint when both draw + type are available. */
  'forms.esign.drawKeyboardHint':
    'Keyboard users: activate the Type tab to enter your name as a signature.',
  /** ESignatureField: type-panel input label. */
  'forms.esign.typeInputLabel': 'Your name (as signature)',
  /** ESignatureField: type-panel input placeholder. */
  'forms.esign.typeInputPlaceholder': 'Type your full name',
  /** ESignatureField: empty-state text for the typed-signature preview. */
  'forms.esign.typePreviewPlaceholder': 'Your signature will appear here',
  /** ESignatureField: aria-label on the clear-typed-signature button. */
  'forms.esign.clearTypedLabel': 'Clear typed signature',
  /** ESignatureField: assertive live announcement on completion. */
  'forms.esign.completeAnnouncement': 'Signature accepted. Your signature has been recorded.',
  /** ESignatureField: live preview announcement while typing (interpolates {name}). */
  'forms.esign.typePreviewAnnouncement': 'Signature preview: {name}',
  /** ESignatureField: upload-region aria-label. */
  'forms.esign.uploadRegionLabel': 'Upload signature image',
  /** ESignatureField: sr-only upload instructions. */
  'forms.esign.uploadInstructions':
    'Upload a PNG, JPEG, WebP, or SVG image of your handwritten signature.',
  /** ESignatureField: upload drop-zone prompt. */
  'forms.esign.uploadPrompt': 'Drag and drop a signature image, or browse to select',
  /** ESignatureField: hidden file input aria-label. */
  'forms.esign.uploadInputLabel': 'Choose signature image file',
  /** ESignatureField: uploaded-image preview alt text. */
  'forms.esign.uploadPreviewAlt': 'Uploaded signature image preview',
  /** ESignatureField: aria-label on the remove-uploaded-image button. */
  'forms.esign.removeUploadLabel': 'Remove uploaded signature image',
  /** ESignatureField: error when an unsupported file type is chosen. */
  'forms.esign.uploadTypeError': 'File must be a PNG, JPEG, WebP, or SVG image.',
  /** ESignatureField: live announcement when an upload is rejected for type. */
  'forms.esign.uploadRejectedAnnouncement':
    'File rejected: invalid file type. Please upload a PNG, JPEG, WebP, or SVG image.',
  /** ESignatureField: live announcement when an upload succeeds (interpolates {name}). */
  'forms.esign.uploadAcceptedAnnouncement': 'Signature image uploaded: {name}',
  /** ESignatureField: signer-information section heading. */
  'forms.esign.signerInfoHeading': 'Signer information',
  /** ESignatureField: signer full-name field label. */
  'forms.esign.signerName': 'Full name',
  /** ESignatureField: signer title field label. */
  'forms.esign.signerTitle': 'Title',
  /** ESignatureField: signer date field label. */
  'forms.esign.signerDate': 'Date',
  /** ESignatureField: sr-only acceptance-checkbox hint. */
  'forms.esign.acceptanceHint':
    'By checking this box, you are confirming that you intend for this to be your electronic signature.',
  /** ESignatureField: completed-state badge text. */
  'forms.esign.completeBadge': 'Signature accepted',
  /** ESignatureField: clear-and-re-sign button text. */
  'forms.esign.clearAndReSign': 'Clear and re-sign',
  /** ESignatureField: aria-label on the clear-and-re-sign button. */
  'forms.esign.clearAndReSignLabel': 'Clear signature and sign again',
  /** ESignatureField: "Signing:" context-banner prefix. */
  'forms.esign.signingPrefix': 'Signing:',

  // FileManager ----------------------------------------------------------------------
  /** FileManager: home/root breadcrumb button. */
  'forms.fileManager.home': 'Home',
  /** FileManager: new-folder toolbar button. */
  'forms.fileManager.newFolder': '+ Folder',
  /** FileManager: upload toolbar button. */
  'forms.fileManager.upload': 'Upload',
  /** FileManager: delete toolbar button (interpolates {count}). */
  'forms.fileManager.delete': 'Delete ({count})',
  /** FileManager: switch-to-grid-view button. */
  'forms.fileManager.viewGrid': 'Grid',
  /** FileManager: switch-to-list-view button. */
  'forms.fileManager.viewList': 'List',
  /** FileManager: window.prompt text for a new folder name. */
  'forms.fileManager.newFolderPrompt': 'Folder name:',
  /** FileManager: aria-label / caption on the files table & grid. */
  'forms.fileManager.filesLabel': 'Files',
  /** FileManager: name column header. */
  'forms.fileManager.colName': 'Name',
  /** FileManager: size column header. */
  'forms.fileManager.colSize': 'Size',
  /** FileManager: modified column header. */
  'forms.fileManager.colModified': 'Modified',
  /** FileManager: per-entry rename button. */
  'forms.fileManager.rename': 'Rename',
  /** FileManager: empty-folder placeholder. */
  'forms.fileManager.emptyFolder': 'Empty folder',

  // DocumentUploadZone / ImageUploader ----------------------------------------------
  /** DocumentUploadZone: file input accessible name. */
  'forms.documentUpload.chooseFiles': 'Choose files to upload',
  /** DocumentUploadZone: multiple-file region accessible name. */
  'forms.documentUpload.uploadFiles': 'Upload files',
  /** DocumentUploadZone: single-file region accessible name. */
  'forms.documentUpload.uploadFile': 'Upload a file',
  /** DocumentUploadZone: idle drop prompt. */
  'forms.documentUpload.prompt': 'Drag & drop or click to upload',
  /** DocumentUploadZone: active drop prompt. */
  'forms.documentUpload.dropPrompt': 'Drop to upload',
  /** DocumentUploadZone: maximum-size-per-file hint. */
  'forms.documentUpload.maxPerFile': 'Max {size}',
  /** DocumentUploadZone: accepted-types hint. */
  'forms.documentUpload.accepted': 'Accepted file types: {types}',
  /** DocumentUploadZone: maximum-file-size hint. */
  'forms.documentUpload.maxSize': 'Maximum file size: {size}',
  /** DocumentUploadZone: uploaded-files heading. */
  'forms.documentUpload.uploadedFiles': 'Uploaded files',
  /** DocumentUploadZone: remove-file accessible name. */
  'forms.documentUpload.removeFile': 'Remove {name}',
  /** DocumentUploadZone: uploaded-file count, singular. */
  'forms.documentUpload.countOne': '{count} file uploaded',
  /** DocumentUploadZone: uploaded-file count, plural. */
  'forms.documentUpload.countOther': '{count} files uploaded',
  /** ImageUploader: idle prompt. */
  'forms.imageUpload.prompt': 'Click or drag to upload image',
  /** ImageUploader: preview image alternative text. */
  'forms.imageUpload.previewAlt': 'Uploaded preview',
  /** ImageUploader: remove-image accessible name. */
  'forms.imageUpload.remove': 'Remove image',
  /** ImageUploader: maximum-size hint. */
  'forms.imageUpload.maxSize': 'Maximum size: {size}',
  /** ImageUploader: file-size validation error. */
  'forms.imageUpload.tooLarge': 'File must be under {size}',

  // Upload ---------------------------------------------------------------------------
  /** Upload: select-files action button. */
  'forms.upload.selectFiles': 'Select files',
  /** Upload: upload action button. */
  'forms.upload.upload': 'Upload',
  /** Upload: per-file cancel button + aria-label. */
  'forms.upload.cancel': 'Cancel',
  /** Upload: per-file success status text. */
  'forms.upload.done': 'Done',
  /** Upload: per-file remove button aria-label. */
  'forms.upload.remove': 'Remove',

  // DropZone -------------------------------------------------------------------------
  /** DropZone: root aria-label. */
  'forms.dropZone.label': 'Drop files here or click to browse',
  /** DropZone: idle prompt. */
  'forms.dropZone.idlePrompt': 'Drag & drop files or click to browse',
  /** DropZone: active drag-over prompt. */
  'forms.dropZone.dropPrompt': 'Drop files here',
  /** DropZone: accepted-types prefix (interpolates {accept}). */
  'forms.dropZone.accepted': 'Accepted: {accept}',
  /** DropZone: successful selection status, singular (interpolates {count}). */
  'forms.dropZone.selectedOne': '{count} file selected',
  /** DropZone: successful selection status, plural (interpolates {count}). */
  'forms.dropZone.selectedOther': '{count} files selected',
  /** DropZone: max-size prefix (interpolates {size}). */
  'forms.dropZone.maxSize': 'Max size: {size}MB',
  /** DropZone: oversize error, singular (interpolates {count}, {limit}). */
  'forms.dropZone.oversizeOne': '{count} file exceeds the {limit} MB limit',
  /** DropZone: oversize error, plural (interpolates {count}, {limit}). */
  'forms.dropZone.oversizeOther': '{count} files exceed the {limit} MB limit',

  // NumericTextBox -------------------------------------------------------------------
  /** NumericTextBox: aria-label on the increment spinner. */
  'forms.numeric.increment': 'Increment',
  /** NumericTextBox: aria-label on the decrement spinner. */
  'forms.numeric.decrement': 'Decrement',
  /** NumberStepper / NumericInput: decrease-value accessible name. */
  'forms.numeric.decrease': 'Decrease',
  /** NumberStepper / NumericInput: increase-value accessible name. */
  'forms.numeric.increase': 'Increase',
  /** CurrencyField: default numeric-entry placeholder. */
  'forms.currency.placeholder': '0.00',
  /** CurrencyField: generic label for localized examples and host composition. */
  'forms.currency.label': 'Amount',

  // Calendar -------------------------------------------------------------------------
  /** Calendar: root group aria-label. */
  'forms.calendar.label': 'Calendar',
  /** DatePicker: open-calendar trigger accessible name. */
  'forms.calendar.open': 'Open calendar',
  /** DateTimePicker: open-picker trigger accessible name. */
  'forms.calendar.openDateTime': 'Open date and time picker',
  /** DateTimePicker: popover accessible name. */
  'forms.calendar.dateTime': 'Date and time',
  /** Calendar: previous-month nav aria-label. */
  'forms.calendar.previousMonth': 'Previous month',
  /** Calendar: next-month nav aria-label. */
  'forms.calendar.nextMonth': 'Next month',
  /** Calendar: previous-year nav aria-label. */
  'forms.calendar.previousYear': 'Previous year',
  /** Calendar: next-year nav aria-label. */
  'forms.calendar.nextYear': 'Next year',
  /** Calendar: month-selection region accessible name. */
  'forms.calendar.selectMonth': 'Select month',
  /** Calendar: month-selector accessible name (interpolates {year}). */
  'forms.calendar.monthSelectorFor': 'Month selector for {year}',
  /** Calendar: months grid accessible name (interpolates {year}). */
  'forms.calendar.monthsFor': 'Months for {year}',
  /** MonthYearPicker: root accessible name. */
  'forms.calendar.monthYearPicker': 'Month and year picker',
  /** DateRangePicker: root accessible name. */
  'forms.calendar.dateRange': 'Select date range',
  /** DateRangePicker: default start-date placeholder. */
  'forms.calendar.startPlaceholder': 'Start date',
  /** DateRangePicker: default end-date placeholder. */
  'forms.calendar.endPlaceholder': 'End date',
  /** DateRangePicker: live announcement after choosing a start date. */
  'forms.calendar.startSelected': 'Start date selected: {date}',
  /** DateRangePicker: live announcement after choosing the full range. */
  'forms.calendar.rangeSelected': 'Range selected: {start} to {end}',
  /** Calendar: month/year view toggle aria-label. */
  'forms.calendar.changeView': 'Change month/year view',
  /** Calendar: go-to-today button aria-label. */
  'forms.calendar.goToToday': 'Go to today',
  /** Calendar: today button text. */
  'forms.calendar.today': 'Today',
  /** Calendar: week-number column header (short). */
  'forms.calendar.weekShort': 'Wk',
  /** Calendar: week-number column aria-label. */
  'forms.calendar.week': 'Week',
  /** Calendar: range start-date live announcement (interpolates {date}). */
  'forms.calendar.startDateSet': 'Start date set: {date}',
  /** Calendar: range end-date live announcement, singular (interpolates {date}). */
  'forms.calendar.endDateSetOne': 'End date set: {date}. {count} day selected.',
  /** Calendar: range end-date live announcement, plural (interpolates {date}, {count}). */
  'forms.calendar.endDateSetOther': 'End date set: {date}. {count} days selected.',

  // Time and duration controls -------------------------------------------------------
  /** Date/time picker: set value to the current instant. */
  'forms.time.now': 'Now',
  /** TimeSpinner: root accessible name. */
  'forms.time.enter': 'Enter time',
  /** TimePicker: trigger accessible name. */
  'forms.time.open': 'Open time picker',
  /** TimePicker: popover accessible name. */
  'forms.time.label': 'Time',
  /** TimeSpinner: hours list accessible name. */
  'forms.time.hours': 'Hours',
  /** TimeSpinner: minutes list accessible name. */
  'forms.time.minutes': 'Minutes',
  /** TimeSpinner: seconds list accessible name. */
  'forms.time.seconds': 'Seconds',
  /** TimeSpinner: period list accessible name. */
  'forms.time.period': 'AM or PM',
  /** DurationField: hour field label. */
  'forms.duration.hours': 'Hours',
  /** DurationField: minute field label. */
  'forms.duration.minutes': 'Minutes',
  /** DurationField: second field label. */
  'forms.duration.seconds': 'Seconds',
  /** DurationField: abbreviated hour unit. */
  'forms.duration.hoursShort': 'hr',
  /** DurationField: abbreviated minute unit. */
  'forms.duration.minutesShort': 'min',
  /** DurationField: abbreviated second unit. */
  'forms.duration.secondsShort': 'sec',

  // Editor --------------------------------------------------------------------------
  /** Editor: toolbar accessible name. */
  'forms.editor.toolbar': 'Formatting',
  /** Editor: bold command. */
  'forms.editor.bold': 'Bold',
  /** Editor: italic command. */
  'forms.editor.italic': 'Italic',
  /** Editor: underline command. */
  'forms.editor.underline': 'Underline',
  /** Editor: strikethrough command. */
  'forms.editor.strikethrough': 'Strikethrough',
  /** Editor: bullet-list command. */
  'forms.editor.bulletList': 'Bulleted list',
  /** Editor: numbered-list command. */
  'forms.editor.numberedList': 'Numbered list',
  /** Editor: increase-indent command. */
  'forms.editor.increaseIndent': 'Increase indent',
  /** Editor: decrease-indent command. */
  'forms.editor.decreaseIndent': 'Decrease indent',
  /** Editor: undo command. */
  'forms.editor.undo': 'Undo',
  /** Editor: redo command. */
  'forms.editor.redo': 'Redo',
  /** Editor: clear-formatting command. */
  'forms.editor.clearFormatting': 'Clear formatting',
  /** Editor: default editable-area placeholder. */
  'forms.editor.placeholder': 'Type here…',

  // Multi-value / selector controls -------------------------------------------------
  /** MultiSelect: hidden selected-item count, singular. */
  'forms.multiSelect.moreOne': '+{count} more',
  /** MultiSelect: hidden selected-item count, plural. */
  'forms.multiSelect.moreOther': '+{count} more',
  /** MultiStepForm: progress-region accessible name. */
  'forms.multiStep.progress': 'Form progress',
  /** MultiStepForm: completed-step accessible name. */
  'forms.multiStep.stepComplete': 'Step {step}: {title} (complete)',
  /** MultiStepForm: current-step accessible name. */
  'forms.multiStep.stepCurrent': 'Step {step}: {title} (current)',
  /** MultiStepForm: upcoming-step accessible name. */
  'forms.multiStep.stepUpcoming': 'Step {step}: {title} (upcoming)',
  /** PinField: default accessible name. */
  'forms.pin.label': 'PIN',
  /** PinField: per-digit accessible name. */
  'forms.pin.digit': '{label} digit {position}',
  /** PropertySelector: picker accessible name. */
  'forms.propertySelector.label': 'Select property',
  /** PropertySelector: search placeholder. */
  'forms.propertySelector.search': 'Search properties…',
  /** PropertySelector: search input accessible name. */
  'forms.propertySelector.searchLabel': 'Search properties',
  /** PropertySelector: empty result. */
  'forms.propertySelector.empty': 'No properties found',
  /** PropertySelector: unit count, singular. */
  'forms.propertySelector.unitsOne': '{count} unit',
  /** PropertySelector: unit count, plural. */
  'forms.propertySelector.unitsOther': '{count} units',
  /** RangeSlider: default accessible name. */
  'forms.range.label': 'Range',
  /** RangeSlider: minimum thumb suffix. */
  'forms.range.minimum': '{label} minimum',
  /** RangeSlider: maximum thumb suffix. */
  'forms.range.maximum': '{label} maximum',
  /** TagInput: default placeholder. */
  'forms.tagInput.placeholder': 'Add tag…',
  /** InlineEdit: default empty-value placeholder. */
  'forms.inlineEdit.placeholder': 'Click to edit',
  /** InlineEdit: edit action accessible name. */
  'forms.inlineEdit.edit': 'Edit {label}',
  /** TextBox: reveal-password action accessible name. */
  'forms.textBox.showPassword': 'Show password',
  /** TextBox: conceal-password action accessible name. */
  'forms.textBox.hidePassword': 'Hide password',

  // Recurrence / schedule -----------------------------------------------------------
  /** RecurrenceField: repeat interval prefix. */
  'forms.recurrence.every': 'Every',
  /** RecurrenceField: no-repeat frequency. */
  'forms.recurrence.once': 'Does not repeat',
  /** RecurrenceField: daily frequency. */
  'forms.recurrence.daily': 'Daily',
  /** RecurrenceField: weekly frequency. */
  'forms.recurrence.weekly': 'Weekly',
  /** RecurrenceField: monthly frequency. */
  'forms.recurrence.monthly': 'Monthly',
  /** RecurrenceField: yearly frequency. */
  'forms.recurrence.yearly': 'Yearly',
  /** RecurrenceField: repeat interval input accessible name. */
  'forms.recurrence.interval': 'Repeat interval',
  /** RecurrenceField: day-selection group accessible name. */
  'forms.recurrence.days': 'Repeat on days',
  /** RecurrenceField: day interval unit, singular. */
  'forms.recurrence.dayOne': 'day',
  /** RecurrenceField: day interval unit, plural. */
  'forms.recurrence.dayOther': 'days',
  /** RecurrenceField: week interval unit, singular. */
  'forms.recurrence.weekOne': 'week',
  /** RecurrenceField: week interval unit, plural. */
  'forms.recurrence.weekOther': 'weeks',
  /** RecurrenceField: month interval unit, singular. */
  'forms.recurrence.monthOne': 'month',
  /** RecurrenceField: month interval unit, plural. */
  'forms.recurrence.monthOther': 'months',
  /** RecurrenceField: year interval unit, singular. */
  'forms.recurrence.yearOne': 'year',
  /** RecurrenceField: year interval unit, plural. */
  'forms.recurrence.yearOther': 'years',
  /** RecurrenceField: end-date field label. */
  'forms.recurrence.endDate': 'End date',
  /** ScheduleField: date field label. */
  'forms.schedule.date': 'Date',
  /** ScheduleField: start-time field label. */
  'forms.schedule.startTime': 'Start time',
  /** ScheduleField: end-time field label. */
  'forms.schedule.endTime': 'End time',
  /** ScheduleField: all-day checkbox label. */
  'forms.schedule.allDay': 'All day',

  // Rating ---------------------------------------------------------------------------
  /** Rating: per-star aria-label, singular (interpolates {count}). */
  'forms.rating.starOne': '{count} star',
  /** Rating: per-star aria-label, plural (interpolates {count}). */
  'forms.rating.starOther': '{count} stars',
  /** Rating: container aria-label summary (interpolates {value}, {max}). */
  'forms.rating.summary': 'Rating: {value} of {max}',

  // Signature ------------------------------------------------------------------------
  /** Signature: aria-label on the clear button (more specific than the visible "Clear"). */
  'forms.signature.clearLabel': 'Clear signature',
  /** Signature: drawing surface accessible name. */
  'forms.signature.padLabel': 'Signature pad',
  /** Signature: canvas fallback text for AT and non-canvas user agents. */
  'forms.signature.canvasFallback':
    'Signature drawing area. Draw with a mouse or touch screen, or use the typed signature field for keyboard access.',
  /** Signature: instructions linked to the canvas and typed-name input. */
  'forms.signature.keyboardInstructions':
    'Draw your signature with a mouse or touch screen, or type your full name below.',
  /** SignatureStub: empty-state prompt. */
  'forms.signature.signHere': 'Sign here',
  /** ValidationSummary: default heading. */
  'forms.validationSummary.title': 'Please fix the following errors',
  /** Wizard: progress-region accessible name. */
  'forms.wizard.steps': 'Wizard steps',
  /** Wizard: back action. */
  'forms.wizard.back': 'Back',
  /** Wizard: next action. */
  'forms.wizard.next': 'Next',
  /** Wizard: default completion action. */
  'forms.wizard.finish': 'Finish',

  // ── forms.validation.* — LOCALIZABLE validation-error templates (ADR 0055) ─────────
  // The forms engine emits each ValidationError with a stable `code` (the JSON-Schema keyword
  // or an engine code) + `params`; the SchemaForm renderer resolves the matching key below and
  // interpolates `params` + the field's already-resolved LABEL (passed as `{field}`). When a
  // code has no template here the renderer falls back to the engine's English `message`.
  //
  // The en values are deliberately the SAME English the engine would otherwise emit, so the
  // en render is unchanged; a translated catalog supplies the localized form per locale.
  /** Validation: a required field is empty (interpolates {field} = the field's label). */
  'forms.validation.required': '{field} is required.',
  /** Validation: value is the wrong JSON type (interpolates {field}). */
  'forms.validation.type': '{field} has the wrong type.',
  /** Validation: number below the minimum (interpolates {field}, {min}). */
  'forms.validation.minimum': '{field} must be at least {min}.',
  /** Validation: number above the maximum (interpolates {field}, {max}). */
  'forms.validation.maximum': '{field} must be at most {max}.',
  /** Validation: number not strictly above the exclusive minimum (interpolates {field}, {min}). */
  'forms.validation.exclusiveMinimum': '{field} must be greater than {min}.',
  /** Validation: number not strictly below the exclusive maximum (interpolates {field}, {max}). */
  'forms.validation.exclusiveMaximum': '{field} must be less than {max}.',
  /** Validation: text shorter than minLength (interpolates {field}, {min}). */
  'forms.validation.minLength': '{field} must be at least {min} characters.',
  /** Validation: text longer than maxLength (interpolates {field}, {max}). */
  'forms.validation.maxLength': '{field} must be at most {max} characters.',
  /** Validation: value not one of the allowed enum options (interpolates {field}). */
  'forms.validation.enum': '{field} is not one of the allowed values.',
  /** Validation: value not equal to the required const (interpolates {field}). */
  'forms.validation.const': '{field} has an invalid value.',
  /** Validation: text does not match the required pattern (interpolates {field}). */
  'forms.validation.pattern': '{field} is not in the expected format.',
  /** Validation: value does not match the required format, e.g. date/email (interpolates {field}). */
  'forms.validation.format': '{field} is not in the expected format.',
  /** Validation: number not a multiple of the required step (interpolates {field}, {multiple}). */
  'forms.validation.multipleOf': '{field} must be a multiple of {multiple}.',
  /** Validation: array shorter than minItems (interpolates {field}, {min}). */
  'forms.validation.minItems': '{field} must have at least {min} items.',
  /** Validation: array longer than maxItems (interpolates {field}, {max}). */
  'forms.validation.maxItems': '{field} must have at most {max} items.',
  /** Validation: an unexpected extra property was submitted (additionalProperties: false). */
  'forms.validation.additional-properties': 'This field is not allowed.',
  /** Validation: generic fallback when the engine reports no keyword-level detail. */
  'forms.validation.invalid': '{field} is invalid.',

  // ── scheduler.* — Scheduler day/week now-line + anchored-opening affordances (2026-07-06) ──
  /** Scheduler: 'Now' toolbar button — scrolls the day/week grid back to the current-time line
   *  when it has scrolled off-screen. */
  'scheduler.now': 'Now',
  /** Scheduler: sr-only accessible text on the now-line indicator (interpolates {time} = the
   *  localized current time, e.g. '9:41 AM'). */
  'scheduler.nowSrText': 'Current time, {time}',
  /** Scheduler: accessible label on the Day/Week grid body when it is genuinely scrollable
   *  (e.g. 7 narrow day columns at phone width) — #133 slice 4 ScrollAffordance treatment. */
  'scheduler.weekGridScrollable': 'Calendar grid, scrollable',
  'scheduler.agenda': 'Agenda',
  'scheduler.allDay': 'All day',
  'scheduler.day': 'Day',
  'scheduler.deleteEvent': 'Delete event',
  'scheduler.deleteRecurringEvent': 'Delete recurring event',
  'scheduler.deleteRecurrenceQuestion': 'Do you want to delete only this event, or all events in the series?',
  'scheduler.deleteSeries': 'Delete all events in the series',
  'scheduler.deleteThisEvent': 'Delete this event',
  'scheduler.editEvent': 'Edit event',
  'scheduler.editOccurrence': 'Edit this occurrence',
  'scheduler.editRecurringEvent': 'Edit recurring event',
  'scheduler.editRecurrenceQuestion': 'Do you want to change only this event, or all events in the series?',
  'scheduler.editSeries': 'Edit all events in the series',
  'scheduler.editThisEvent': 'Edit this event',
  'scheduler.end': 'End',
  'scheduler.eventTitle': 'Event title',
  'scheduler.month': 'Month',
  'scheduler.more': '+{count} more',
  'scheduler.newEvent': 'New event',
  'scheduler.next': 'Next',
  'scheduler.noUpcomingEvents': 'No upcoming events',
  'scheduler.previous': 'Previous',
  'scheduler.recurringEvent': 'Recurring event',
  'scheduler.recurringEventModified': 'Recurring event (modified)',
  'scheduler.save': 'Save',
  'scheduler.start': 'Start',
  'scheduler.today': 'Today',
  'scheduler.week': 'Week',
  // ── scrollAffordance.* — useScrollAffordance debounced SR position announcement (#133 slice 4
  // fix-forward, 2026-07-07 — SA-I18N-01). Every consumer of the ScrollAffordance primitive
  // (Scheduler week grid, BuilderTopBar, DecisionTableEditor, …) shares these; the caller-supplied
  // `ariaLabel` on the wrapper stays per-consumer, but the POSITION announcement is the hook's own
  // and must be localizable for every adopter, not just ones that translate their own label. ──
  /** Position announcement when the region holds a single visible item (interpolates {index},
   *  {total}), e.g. 'Scrollable, showing 3 of 7'. */
  'scrollAffordance.showingOne': 'Scrollable, showing {index} of {total}',
  /** Position announcement when multiple items are visible at once (interpolates {start}, {end},
   *  {total}), e.g. 'Scrollable, showing 2 to 4 of 7'. */
  'scrollAffordance.showingRange': 'Scrollable, showing {start} to {end} of {total}',
  /** Generic (no `itemCount`) announcement once scrolled to the end — no hidden content ahead. */
  'scrollAffordance.endReached': 'Scrollable region, end reached',
  /** Generic (no `itemCount`) announcement at the start edge — more content is available ahead. */
  'scrollAffordance.moreAvailable': 'Scrollable region, more content available, use arrow keys to scroll',
  /** Generic (no `itemCount`) mid-scroll announcement (interpolates {percent}). */
  'scrollAffordance.percentScrolled': 'Scrollable region, {percent}% scrolled, use arrow keys to scroll',
  // ── chrome.* — app chrome: guarded controls (GuardedControl) + consequence grammar (2026-07-06) ──
  // Guard grammar (§AD.2 composable-guard ruling). {action} = the control's human label,
  // {seconds} = the live arm-countdown; both interpolated at runtime by the component.
  /** GuardedControl covered-state SR hint — announces the guarded control is locked. */
  'chrome.guard.covered.hint': 'Guarded — press to unlock',
  /** GuardedControl armed-state hint — announced + shown with the live arm countdown. */
  'chrome.guard.armed.hint': 'Armed — press to {action}. {seconds}s left.',
  /** GuardedControl spring-loaded auto-re-cover announcement. */
  'chrome.guard.recovered.announce': 'Re-guarded.',
  /** GuardedControl heavy-composition (dual/launch) fallback — the governed pending flow (B10) is not wired yet. */
  'chrome.guard.pending.unavailable': 'This guarded action needs approval steps that are not available yet.',
} as const

/**
 * The catalog shape the provider accepts. A partial map of any known key → string; any
 * key the app omits falls back to `defaultStrings` (so an app can override just the keys
 * it has translated). `Partial` is intentional — incremental translation is the norm.
 */
export type HarborlineStringCatalog = Partial<Record<HarborlineStringKey, string>>

/** The union of every known string key. Derived from `defaultStrings` — single source of truth. */
export type HarborlineStringKey = keyof typeof defaultStrings

/**
 * Interpolate `{name}` placeholders in a resolved string from a vars map.
 * Unknown placeholders are left intact (so a missing var is visible, not silently blanked).
 * Values are stringified; numbers are NOT locale-formatted here — formatting is the caller's
 * job via `Intl.*` (see `lib/format` seam) before passing the var in.
 */
export function interpolate(
  template: string,
  vars?: Record<string, string | number>,
): string {
  if (!vars) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in vars ? String(vars[name]) : match,
  )
}
