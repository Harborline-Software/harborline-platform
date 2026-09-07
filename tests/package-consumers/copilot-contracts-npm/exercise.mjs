import { buildManifest, parseProposal } from '@harborline-software/copilot-contracts'
const specs=[{id:'draft.edit',summary:'Edit a draft',argsHint:'{}',argsSchema:x=>({ok:true,args:x}),classification:{tier:'ap'},undoable:true}]
if (!parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:'draft.edit',args:{}},'forms.builder',specs).ok) throw new Error('packed parser failed')
if (!buildManifest('forms.builder',specs).includes('draft.edit')) throw new Error('packed manifest failed')

