import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url'
const here=path.dirname(fileURLToPath(import.meta.url)); const ledger=JSON.parse(fs.readFileSync(path.join(here,'pilot-cases.json'),'utf8'))
const declared=ledger.files.reduce((n,f)=>n+f[1],0), expanded=ledger.files.reduce((n,f)=>n+f[2],0)
if (ledger.pin!=='3410883405996dc8e4871418a441f52970d9994e' || ledger.files.length!==42 || declared!==357 || expanded!==384) throw new Error(`Pilot ledger mismatch: ${ledger.files.length}/${declared}/${expanded}`)
const live=ledger.files.filter(f=>f[3]); if (live.length!==2 || live.some(f=>f[1]!==1||f[2]!==1)) throw new Error('live battery gate mismatch')
console.log(`Pilot ledger reconciled: ${ledger.files.length}/${declared}/${expanded}; ${live.length} live rows excluded from deterministic execution`)
