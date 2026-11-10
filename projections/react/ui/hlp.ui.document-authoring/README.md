# Documents authoring contribution

React and Blazor editors use the host's shared shell. They own local drafting only. The host supplies identity/revision, catalogues, admission refusals, an already-resolved DocumentPageFlow and a preview callback wired to DocumentRuntime.PreviewAsync. The host owns envelope/record binding/surface pin, numeric admission, persistence and server authorization. Do not connect this callback to issuance.

Text lines carry LayoutTextRun's text or fieldPath/fallback shape; masters carry LayoutMasterVariant's region slots and blocks carry Layout break controls. The host composes them into its pinned Layout surface. Geometry and live page-flow production are not implemented here. Block labels and per-run format selection remain held. The server's semantic content is escaped and never formatted or interpreted as HTML by the client.

Changes carry a local generation, never a persisted revision. Denials must match identity, revision and generation. Incoming identity/revision changes wait behind Keep/Discard when dirty. Read-only disables every authoring, preview and pending decision. The host must still reauthorize every server request. Samples are explicitly labelled, and the request exposes preview only.

Oracle: conformance/hlp.ui.document-authoring/fixtures.yaml is a literal contract fixture shared by both native suites. The Blazor test-only host composes the authored Layout surface and calls DocumentRuntime with no published lookup or issued store. It exercises both en-US and fr-FR while the UI locale differs. Gallery previews are explicitly labelled contract fixtures, not a live server.
