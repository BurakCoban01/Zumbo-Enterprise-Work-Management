#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const env = Object.fromEntries(readFileSync(resolve(import.meta.dirname, '../Backend/.env'), 'utf8')
  .split(/\r?\n/).filter(l => l.includes('=')).map(l => { const s=l.indexOf('='); return [l.slice(0,s).trim(), l.slice(s+1).trim()]; }));
const API = env.ZUMBO_API_URL; const ORIGIN = 'http://127.0.0.1:58177';
let cookies = '', csrf = '', adminId = '';

async function c(path, opts={}) {
  const h = {'Content-Type':'application/json',Origin:ORIGIN,...opts.headers};
  if(cookies)h.Cookie=cookies; if(csrf)h['X-CSRF-Token']=csrf;
  const r = await fetch(`${API}${path}`,{...opts,headers:h});
  const t = await r.text(); const d = t?JSON.parse(t):null;
  if(!r.ok) throw new Error(`${r.status}: ${d?.error?.message}`);
  const sc = r.headers.getSetCookie?.()||[];
  if(sc.length){const m={};if(cookies)cookies.split(';').forEach(x=>{const p=x.trim().split('=');if(p[0])m[p[0].trim()]=(p[1]||'').trim()});sc.forEach(x=>{const p=x.split(';')[0].trim().split('=');if(p[0])m[p[0].trim()]=(p[1]||'').trim()});cookies=Object.entries(m).map(([k,v])=>`${k}=${v}`).join('; ')}
  return d?.data!==undefined?d.data:d;
}
function days(n){const d=new Date();d.setDate(d.getDate()+n);return d.toISOString()}
function dateOnly(n){return days(n).slice(0,10)}

async function main() {
  const lr = await c('/api/browser-auth/login',{method:'POST',body:JSON.stringify({usernameOrEmail:'admin@acme.local',password:'AcmeAdmin2026!'})});
  csrf=lr.csrfToken; adminId=lr.user.id;
  console.log('✅ acmeadmin\n');

  const projects = await c(`/api/projects?organizationId=acme-tech&pageSize=50`);
  const pList = Array.isArray(projects)?projects:(projects.items||[]);
  const teams = await c(`/api/teams?organizationId=acme-tech&pageSize=50`);
  const tList = Array.isArray(teams)?teams:(teams.items||[]);
  const users = await c(`/api/auth/users?search=`);
  const uList = Array.isArray(users)?users:(users.items||[]);
  const allUids = [adminId, ...uList.map(u=>u.id).filter(id=>id!==adminId)];
  console.log(`Projects:${pList.length} Teams:${tList.length} Users:${uList.length}\n`);

  const estByType = {Epic:21,Story:8,Task:5,Bug:3,Subtask:2};

  const itemData = {
    ACME: [
      ['Epic','Abonelik Motoru','Stripe abonelik lifecycle, upgrade/downgrade, proration, dunning yönetimi','High'],
      ['Story','Onboarding sihirbazı','Yeni tenant için interaktif kurulum: şirket bilgileri, ekip daveti, ilk proje','Medium'],
      ['Task','API rate limiting','Tenant bazlı rate limiting. Redis token bucket algoritması.','High'],
      ['Bug','Webhook retry sonsuz döngü','Failed webhook exponential backoff yerine sonsuz retry yapıyor','High'],
      ['Story','İki faktörlü auth','TOTP based 2FA. QR code, recovery codes, trusted devices.','High'],
      ['Task','Database migration tool','Zero-downtime schema migration. Expand-contract pattern.','Medium'],
      ['Subtask','Tenant context middleware','Her istekte tenant resolution ve DB routing','Medium'],
      ['Story','Rapor ve analitik paneli','Özelleştirilebilir grafikler, export (PDF/CSV), scheduled reports','Medium'],
      ['Task','File upload servisi','S3 uyumlu depolama, virus scan, image processing (thumbnail, resize)','Low'],
      ['Bug','Session timeout erken','15dk token ama 5dk sonra logout oluyor. Clock skew kontrolü.','Medium']
    ],
    MOB: [
      ['Epic','Deep Link Sistemi','Universal links (iOS) + App links (Android). Branch.io entegrasyonu.','High'],
      ['Story','In-app purchase','StoreKit 2 (iOS) + Billing Library v6 (Android). Receipt validation server-side.','High'],
      ['Task','Crash reporting','Sentry integration, symbol upload, source maps.','Medium'],
      ['Bug','Keyboard toolbar overlap','Klavye açıldığında bottom toolbar overlay yapıyor','Medium'],
      ['Story','Offline cache sync','SQLite local cache + background sync queue. Conflict resolution.','High'],
      ['Task','Push notification segments','Kullanıcı segmentasyonu: dil, lokasyon, davranış bazlı push','Low'],
      ['Bug','iOS 17 button glitch','iOS 17\'de custom button component render sorunu','Medium']
    ],
    INFRA: [
      ['Epic','Service Mesh Implementasyonu','Istio service mesh: mTLS, traffic management, observability.','High'],
      ['Story','GDPR compliance tools','Data export, right to erasure, consent management.','High'],
      ['Task','Database read replicas','PostgreSQL streaming replication + read split.','Medium'],
      ['Bug','DNS resolution timeout','CoreDNS intermittent failures. NodeLocalDNS cache gerekli.','High'],
      ['Story','Chaos engineering','Gremlin/LitmusChaos ile dayanıklılık testleri.','Low'],
      ['Task','Cost optimization','Spot instances, cluster autoscaler tuning, resource requests right-sizing','Medium']
    ],
    CRM: [
      ['Epic','Real-time Customer Timeline','Tüm müşteri etkileşimleri tek timeline: email, call, meeting, purchase.','High'],
      ['Story','Lead scoring model','ML-based lead scoring. Features: engagement, firmographic, behavioral.','High'],
      ['Task','Email tracking','Open/click tracking pixel, unsubscribe management, bounce handling.','Medium'],
      ['Bug','Duplicate contacts','Aynı email farklı formatlarda kayıt. Normalization + merge tool.','Medium'],
      ['Story','Pipeline automation','Stage-based automation rules. Auto-assign, follow-up reminders.','Medium'],
      ['Task','Reporting API','Custom report builder. Group by, aggregate, scheduled email delivery.','Low']
    ]
  };

  let total = 0;
  for (const proj of pList) {
    const boards = await c(`/api/boards/by-project/${proj.id}`);
    const bList = Array.isArray(boards)?boards:(boards.items||[]);
    const bid = bList[0]?.id; if(!bid) continue;
    const items = itemData[proj.key]||[];

    // Mevcut öğeleri say
    const existing = await c(`/api/work-items?projectId=${proj.id}&pageSize=200`);
    const exList = Array.isArray(existing)?existing:(existing.items||[]);
    const exTitles = new Set(exList.map(w=>w.title));
    console.log(`${proj.key}: ${exList.length} mevcut, ${items.length} hedef`);

    for (const [type,title,desc,priority] of items) {
      if (exTitles.has(title)) continue;

      const uid = allUids[Math.floor(Math.random()*allUids.length)];

      try {
        const body = { projectId:proj.id,boardId:bid,type,title,description:desc,priority,assigneeUserId:uid };
        if (type === 'Subtask') body.parentId = exList[0]?.id;
        const wi = await c('/api/work-items',{method:'POST',body:JSON.stringify(body)});

        // Estimate
        await c(`/api/work-items/${wi.id}/planning`,{method:'PATCH',body:JSON.stringify({estimatePoints:estByType[type]||5})}).catch(()=>{});

        // Due date
        const due=[-2,1,3,7,14,21,30][total%7];
        await c(`/api/work-items/${wi.id}`,{method:'PUT',body:JSON.stringify({title,description:desc,priority,dueDate:days(due)})}).catch(()=>{});

        // Status (skip "To Do" as it's the default)
        const flows=[['In Progress'],['In Progress','Code Review'],['In Progress','Code Review','Test','Done'],[],['In Progress']];
        for(const st of flows[total%flows.length]) await c(`/api/work-items/${wi.id}/status`,{method:'PATCH',body:JSON.stringify({status:st})}).catch(()=>{});

        // Checklist
        for(const cl of ['Gereksinim','Tasarım','Kod','Test','Doküman']) await c(`/api/work-items/${wi.id}/checklist`,{method:'POST',body:JSON.stringify({text:cl})}).catch(()=>{});

        // Comments
        const cms=['Başladım, taslak yakında.','PR açıldı.','Test edildi, hazır.','Bağımlılık bekleniyor.','Review yapıldı.'];
        for(let i=0;i<2;i++) await c(`/api/work-items/${wi.id}/comments`,{method:'POST',body:JSON.stringify({body:cms[(total+i)%cms.length]})}).catch(()=>{});

        // Worklog + watch
        await c(`/api/work-items/${wi.id}/worklogs`,{method:'POST',body:JSON.stringify({userId:uid,hours:[2,3,4,6,8][total%5],note:'Geliştirme'})}).catch(()=>{});
        await c(`/api/work-items/${wi.id}/watch`,{method:'PUT',body:JSON.stringify({watching:true})}).catch(()=>{});

        total++; process.stdout.write('.');
      } catch(e) {
        console.log(`\n❌ ${title}: ${e.message}`);
      }
    }

    // Sprint planlama
    const allWi = await c(`/api/work-items?projectId=${proj.id}&pageSize=200`);
    const wiList = Array.isArray(allWi)?allWi:(allWi.items||[]);
    const sprints = await c(`/api/sprints?projectId=${proj.id}&pageSize=20`).catch(()=>[]);
    const sList = Array.isArray(sprints)?sprints:(sprints?.items||[]);
    const active = sList.find(s=>s.state==='Active'||s.status==='Active');
    if (active) {
      for(let i=0;i<Math.min(wiList.length,8);i++) await c(`/api/sprints/${active.id}/items/${wiList[i].id}`,{method:'PUT',body:'{}'}).catch(()=>{});
    }
  }

  console.log(`\n\n🎉 ${total} yeni iş öğesi oluşturuldu`);
}
main().catch(e=>{console.error('❌',e.message);process.exit(1)});
