#!/usr/bin/env node
/**
 * Acme Teknoloji — Tam Power-User Doldurma
 * admin@acme.local ile giriş yapar ve tüm organizasyonu doldurur.
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
const ORIGIN = 'http://127.0.0.1:58177';
const ORG = 'acme-tech';
const ACME_EMAIL = 'admin@acme.local';
const ACME_PASS = 'AcmeAdmin2026!';
const USER_PASS = 'AcmeDev2026!';

let cookies = '', csrf = '', adminId = '';

async function call(path, opts = {}) {
  const h = { 'Content-Type': 'application/json', Origin: ORIGIN, ...opts.headers };
  if (cookies) h.Cookie = cookies;
  if (csrf) h['X-CSRF-Token'] = csrf;
  const r = await fetch(`${API}${path}`, { ...opts, headers: h });
  const t = await r.text();
  const d = t ? JSON.parse(t) : null;
  if (!r.ok) throw new Error(`${r.status} ${path}: ${d?.error?.message||r.statusText}`);
  const sc = r.headers.getSetCookie?.() || [];
  if (sc.length) {
    const m = {};
    if (cookies) cookies.split(';').forEach(c => { const p=c.trim().split('='); if(p[0])m[p[0].trim()]=(p[1]||'').trim(); });
    sc.forEach(s => { const p=s.split(';')[0].trim().split('='); if(p[0])m[p[0].trim()]=(p[1]||'').trim(); });
    cookies = Object.entries(m).map(([k,v])=>`${k}=${v}`).join('; ');
  }
  return d?.data !== undefined ? d.data : d;
}

async function registerUser(username, email, pass, orgId, token) {
  const r = await fetch(`${API}/api/browser-auth/register`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Origin: ORIGIN },
    body: JSON.stringify({ username, email, password: pass, organizationId: orgId, bootstrapToken: token })
  });
  const d = await r.json();
  if (!r.ok) throw new Error(d?.error?.message);
  return d.data;
}

let count = 0;
async function op(label, fn) {
  try { const r = await fn(); count++; if (count % 10 === 0) process.stdout.write('.'); return r; }
  catch(e) { return null; }
}

function days(n) { const d = new Date(); d.setDate(d.getDate()+n); return d.toISOString(); }
function dateOnly(n) { return days(n).slice(0, 10); }

async function main() {
  console.log(`🚀 Acme Teknoloji Tam Doldurma\n`);

  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({ usernameOrEmail: ACME_EMAIL, password: ACME_PASS }) });
  csrf = lr.csrfToken; adminId = lr.user.id;
  console.log(`✅ Acme Admin: ${lr.user.username} (org: ${lr.user.organizationId})\n`);

  // 1. DEPARTMANLAR
  console.log('🏢 Departmanlar');
  const deptDefs = [
    { name: 'Mühendislik' }, { name: 'Ürün' }, { name: 'Pazarlama' },
    { name: 'Backend', parent: 'Mühendislik' }, { name: 'Mobil', parent: 'Mühendislik' },
    { name: 'Web', parent: 'Mühendislik' }, { name: 'QA', parent: 'Mühendislik' }
  ];
  const deptMap = {};
  for (const d of deptDefs) {
    await op('', async () => {
      const body = { name: d.name };
      if (d.parent && deptMap[d.parent]) body.parentDepartmentId = deptMap[d.parent];
      const r = await call(`/api/organizations/${ORG}/departments`, { method:'POST', body: JSON.stringify(body) });
      if (r?.id) deptMap[d.name] = r.id;
    });
  }
  console.log(` ${Object.keys(deptMap).length} departman\n`);

  // 2. TAKIMLAR
  console.log('👥 Takımlar');
  const teamDefs = [
    { name: 'Platform Takımı', desc: 'Çekirdek platform geliştirme' },
    { name: 'Mobil Takım', desc: 'iOS ve Android geliştirme' },
    { name: 'Web Takımı', desc: 'Web arayüzü geliştirme' },
    { name: 'SRE Takımı', desc: 'Site reliability engineering' }
  ];
  const teamMap = {};
  for (const t of teamDefs) {
    await op('', async () => {
      const r = await call('/api/teams', { method:'POST', body: JSON.stringify({ name: t.name, description: t.desc, organizationId: ORG, ownerUserId: adminId }) });
      if (r?.id) teamMap[t.name] = r.id;
    });
  }
  console.log(` ${Object.keys(teamMap).length} takım\n`);

  // 3. KULLANICILAR (davet + kayıt)
  console.log('👤 Kullanıcılar');
  const userDefs = [
    { name: 'aliveli', email: 'ali.veli@acme.local', display: 'Ali Veli', team: 'Platform Takımı', dept: 'Backend', pos: 'Senior Backend Dev' },
    { name: 'aysedemir', email: 'ayse.demir@acme.local', display: 'Ayşe Demir', team: 'Mobil Takım', dept: 'Mobil', pos: 'Mobil Geliştirici' },
    { name: 'mehmetyilmaz', email: 'mehmet.yilmaz@acme.local', display: 'Mehmet Yılmaz', team: 'Web Takımı', dept: 'Web', pos: 'Frontend Dev' },
    { name: 'fatmakaya', email: 'fatma.kaya@acme.local', display: 'Fatma Kaya', team: 'Platform Takımı', dept: 'Ürün', pos: 'Ürün Yöneticisi' },
    { name: 'canozkan', email: 'can.ozkan@acme.local', display: 'Can Özkan', team: 'Web Takımı', dept: 'Web', pos: 'UI Tasarımcı' },
    { name: 'buraksahin', email: 'burak.sahin@acme.local', display: 'Burak Şahin', team: 'SRE Takımı', dept: 'Backend', pos: 'DevOps Mühendisi' },
    { name: 'selincetin', email: 'selin.cetin@acme.local', display: 'Selin Çetin', team: 'Platform Takımı', dept: 'QA', pos: 'QA Mühendisi' },
    { name: 'emreakyol', email: 'emre.akyol@acme.local', display: 'Emre Akyol', team: 'Mobil Takım', dept: 'Mobil', pos: 'iOS Developer' }
  ];

  const userMap = {};
  for (const u of userDefs) {
    const teamId = teamMap[u.team];
    if (!teamId) continue;

    let token = null;
    await op('', async () => {
      const r = await call(`/api/teams/${teamId}/members`, { method:'POST', body: JSON.stringify({ email: u.email, role: 'Member' }) });
      token = r?.invitationToken;
    });

    if (token) {
      await op('', async () => {
        const r = await registerUser(u.name, u.email, USER_PASS, ORG, token);
        userMap[u.email] = r.user.id;
      });
    }
  }
  const userIds = Object.values(userMap);
  console.log(` ${userIds.length} kullanıcı\n`);

  // 4. DEPARTMAN ÜYELERİ
  console.log('🏢 Departman üyeleri');
  for (const u of userDefs) {
    const uid = userMap[u.email];
    const deptId = deptMap[u.dept];
    if (uid && deptId) {
      await op('', async () => {
        await call(`/api/organizations/${ORG}/departments/${deptId}/members`, { method:'POST', body: JSON.stringify({ userId: uid, position: u.pos }) });
      });
    }
  }
  console.log('');

  // 5. PROJELER
  console.log('📁 Projeler');
  const projectDefs = [
    { key: 'ACME', name: 'Acme SaaS Platformu', desc: 'Çoklu kiracılı SaaS: müşteri yönetimi, abonelik, faturalandırma' },
    { key: 'MOB', name: 'Acme Mobil App', desc: 'iOS/Android: sipariş, bildirim, offline destek' },
    { key: 'INFRA', name: 'Altyapı ve DevOps', desc: 'Kubernetes, CI/CD, izleme, log yönetimi' },
    { key: 'CRM', name: 'CRM Entegrasyonu', desc: 'Salesforce/HubSpot entegrasyonu, veri senkronizasyonu' }
  ];
  const projectMap = {};
  for (const p of projectDefs) {
    await op('', async () => {
      const r = await call('/api/projects', { method:'POST', body: JSON.stringify({ organizationId: ORG, key: p.key, name: p.name, description: p.desc, ownerUserId: adminId, visibility: 'Internal' }) });
      if (r?.id) projectMap[p.key] = r.id;
    });
  }
  console.log(` ${Object.keys(projectMap).length} proje\n`);

  // 6. PROJE ÜYELERİ + TAKIM BAĞLAMA
  console.log('🔗 Proje üye+takım');
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;
    for (const uid of userIds) {
      await op('', async () => { await call(`/api/projects/${pid}/members`, { method:'POST', body: JSON.stringify({ userId: uid, role: 'Developer' }) }); });
    }
    for (const tid of Object.values(teamMap)) {
      await op('', async () => { await call(`/api/projects/${pid}/teams`, { method:'POST', body: JSON.stringify({ teamId: tid }) }); });
    }
  }
  console.log('');

  // 7. PANOLAR
  console.log('📋 Panolar');
  const boardMap = {};
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;
    await op('', async () => {
      const r = await call('/api/boards', { method:'POST', body: JSON.stringify({ projectId: pid, name: `${p.key} Kanban`, type: 'Kanban' }) });
      if (r?.id) boardMap[p.key] = r.id;
    });
  }
  console.log(` ${Object.keys(boardMap).length} pano\n`);

  // 8. İŞ ÖĞELERİ
  console.log('📝 İş öğeleri');
  const allUserIds = [adminId, ...userIds];
  const estimateByType = { Epic: 21, Story: 8, Task: 5, Bug: 3, Subtask: 2 };
  const allWI = [];

  const data = {
    ACME: [
      { type:'Epic', t:'Çoklu Kiracılı Mimari', d:'Veritabanı isolation, tenant routing, connection pooling. Her tenant\'ın verisi izole.', p:'High' },
      { type:'Epic', t:'Abonelik ve Faturalandırma', d:'Stripe entegrasyonu, plan yönetimi, proration, dunning. Otomatik invoice generation.', p:'High' },
      { type:'Story', t:'Kullanıcı profil sayfası', d:'Avatar, kişisel bilgiler, şirket bilgileri, tercihler. CRUD ve validation.', p:'Medium' },
      { type:'Story', t:'Rol bazlı erişim kontrolü', d:'RBAC: SuperAdmin, OrgAdmin, User, Viewer. İzin matrisi ve UI gating.', p:'High' },
      { type:'Task', t:'PostgreSQL partitioning', d:'Tenant bazlı tablo partitioning. Performance benchmark raporu gerekli.', p:'Medium' },
      { type:'Task', t:'Redis önbellek stratejisi', d:'Hot data cache, tag-based invalidation, distributed lock with Redlock.', p:'Medium' },
      { type:'Bug', t:'Tenant geçişinde veri sızıntısı', d:'Aynı connection pool\'da tenant isolation kırılıyor. Separate pool gerekli.', p:'High' },
      { type:'Bug', t:'Login sonrası yönlendirme hatası', d:'Auth sonrası /dashboard yerine /login\'e dönüyor. Token timing issue.', p:'Medium' },
      { type:'Task', t:'Webhook altyapısı', d:'Event-driven webhook gönderimi, retry with exponential backoff, dead-letter queue.', p:'Low' },
      { type:'Story', t:'Audit log arayüzü', d:'Filtrelenebilir audit log viewer. Tarih/kullanıcı/işlem bazlı arama ve export.', p:'Medium' },
      { type:'Subtask', t:'Migration script yazımı', d:'Existing single-tenant datayı multi-tenant\'a migrate etme scripti.', p:'Medium' },
      { type:'Story', t:'Dashboard widget sistemi', d:'Sürükle-bırak özelleştirilebilir dashboard. Chart.js entegrasyonu.', p:'Low' },
      { type:'Task', t:'E-posta şablonları', d:'Hoş geldiniz, şifre sıfırlma, fatura bildirimi, trial bitişi şablonları (Razor).', p:'Medium' }
    ],
    MOB: [
      { type:'Epic', t:'Offline Mod Desteği', d:'Çevrimdışı veri senkronizasyonu, conflict resolution (CRDT), optimistic UI.', p:'High' },
      { type:'Story', t:'Push bildirim entegrasyonu', d:'FCM (Android) ve APNs (iOS). Topic-based ve targeted push notifications.', p:'High' },
      { type:'Story', t:'Biyometrik giriş', d:'Face ID / Touch ID. Secure enclave key storage, fallback to PIN.', p:'Medium' },
      { type:'Task', t:'React Native upgrade', d:'RN 0.74\'e yükseltme. Breaking changes fix, native module güncelleme.', p:'Medium' },
      { type:'Task', t:'App Store optimizasyonu', d:'ASO: anahtar kelimeler, ekran görüntüleri, açıklama optimizasyonu.', p:'Low' },
      { type:'Bug', t:'Android crash Samsung cihazlar', d:'Samsung S24\'te belirli ekranda native crash. Null pointer in camera module.', p:'High' },
      { type:'Bug', t:'iOS push ses gelmiyor', d:'Push geldi ama ses çalmıyor. Sound permission ve Provisional entitlement kontrolü.', p:'Medium' },
      { type:'Story', t:'Karanlık mod', d:'Sistem temasını takip eden dark mode. Tüm ekranlarda destek, custom renkler.', p:'Medium' },
      { type:'Task', t:'Analytics entegrasyonu', d:'Firebase Analytics + Mixmarklet. Funnel tracking ve retention cohort.', p:'Low' },
      { type:'Story', t:'Çoklu dil desteği', d:'i18n: Türkçe, İngilizce, Almanca. RTL diller için hazırlık.', p:'Medium' }
    ],
    INFRA: [
      { type:'Epic', t:'Kubernetes Migration', d:'Docker Compose\'tan production K8s\'e geçiş. Helm chart\'ları, ingress, cert-manager.', p:'High' },
      { type:'Story', t:'Auto-scaling yapılandırması', d:'HPA: CPU/memory/queue depth. KEDA for event-driven scaling.', p:'High' },
      { type:'Task', t:'Prometheus + Grafana', d:'Metrik toplama, dashboard\'lar (latency, throughput, error rate), alert rules.', p:'Medium' },
      { type:'Task', t:'ELK Stack kurulumu', d:'Filebeat → Logstash → Elasticsearch → Kibana. ILM policy, index templates.', p:'Medium' },
      { type:'Bug', t:'Pod OOM Kills', d:'Java heap memory çok yüksek. JVM args tuning (-Xmx4g), memory requests/limits.', p:'High' },
      { type:'Task', t:'Backup stratejisi', d:'Velero ile K8s backup. PITR for PostgreSQL. S3 lifecycle + cross-region replication.', p:'Medium' },
      { type:'Story', t:'Zero-downtime deployment', d:'Canary release, feature flags (Unleash), automatic rollback on error rate spike.', p:'High' },
      { type:'Task', t:'Secret yönetimi', d:'HashiCorp Vault migration. Dynamic secrets, lease management, auto-rotation.', p:'High' },
      { type:'Bug', t:'SSL sertifika yenilenmiyor', d:'cert-manager Let\'s Encrypt renewal başarısız. DNS-01 challenge gerekli.', p:'Medium' }
    ],
    CRM: [
      { type:'Epic', t:'Salesforce Entegrasyonu', d:'Bidirectional sync: contact, account, opportunity, activity. Bulk + REST API.', p:'High' },
      { type:'Story', t:'HubSpot contact sync', d:'Real-time webhook + batch sync. Deduplication by email+domain.', p:'Medium' },
      { type:'Task', t:'OAuth flow implementation', d:'Salesforce ve HubSpot için OAuth 2.0 PKCE flow. Token refresh, secure storage.', p:'Medium' },
      { type:'Bug', t:'Rate limit exceeded', d:'Salesforce API limit aşılıyor. Bulk API + intelligent caching + request batching.', p:'Medium' },
      { type:'Story', t:'Customer 360 dashboard', d:'Tüm platformlardan müşteri verisi aggregation. Tek 360 görüş.', p:'Medium' },
      { type:'Task', t:'Data quality rules', d:'Validation, enrichment (Clearbit), deduplication rules engine.', p:'Low' },
      { type:'Bug', t:'Sync çakışması', d:'Aynı kayıt iki sistemde aynı anda güncellenince conflict. Last-write-wins + manual merge.', p:'High' }
    ]
  };

  for (const p of projectDefs) {
    const pid = projectMap[p.key]; const bid = boardMap[p.key];
    if (!pid || !bid) continue;
    const items = data[p.key] || [];

    for (const item of items) {
      const assignee = allUserIds[Math.floor(Math.random() * allUserIds.length)];
      const teamId = Object.values(teamMap)[Math.floor(Math.random() * Object.keys(teamMap).length)];

      const wi = await op('', async () => {
        return await call('/api/work-items', { method:'POST', body: JSON.stringify({
          projectId: pid, boardId: bid, type: item.type, title: item.t,
          description: item.d, priority: item.p, assigneeUserId: assignee, teamId,
          labels: item.type === 'Bug' ? ['bug','acil'] : (item.p === 'High' ? ['önemli'] : [])
        })});
      });

      if (wi) {
        allWI.push({ ...wi, projectKey: p.key, projectId: pid });

        // Estimate
        await op('', async () => { await call(`/api/work-items/${wi.id}/planning`, { method:'PATCH', body: JSON.stringify({ estimatePoints: estimateByType[item.type] || 5 }) }); });

        // Bitiş tarihi
        const dueOffset = [-3,-1,1,3,7,14,21,30][allWI.length % 8];
        await op('', async () => { await call(`/api/work-items/${wi.id}`, { method:'PUT', body: JSON.stringify({ title: item.t, description: item.d, priority: item.p, dueDate: days(dueOffset) }) }); });

        // Statü
        const flows = [['To Do'],['In Progress'],['In Progress','Code Review'],['In Progress','Code Review','Test'],['In Progress','Code Review','Test','Done'],['To Do'],['In Progress']];
        for (const st of flows[allWI.length % flows.length]) {
          await op('', async () => { await call(`/api/work-items/${wi.id}/status`, { method:'PATCH', body: JSON.stringify({ status: st }) }); });
        }

        // Checklist
        for (const c of ['Gereksinim analizi','Tasarım','Implementasyon','Test','Dokümantasyon']) {
          await op('', async () => { await call(`/api/work-items/${wi.id}/checklist`, { method:'POST', body: JSON.stringify({ text: c }) }); });
        }

        // Yorumlar
        const cmts = ['Tasarım hazır, geliştirmeye başlıyorum.','PR açıldı, review bekliyorum.','Test ortamında doğrulandı.','Bağımlılık bekleniyor.','Performans testi gerekli.','Müşteri geri bildirimi alındı.','Kod review yapıldı, düzeltmeler uygulandı.'];
        for (let c = 0; c < 2; c++) {
          await op('', async () => { await call(`/api/work-items/${wi.id}/comments`, { method:'POST', body: JSON.stringify({ body: cmts[(allWI.length+c) % cmts.length] }) }); });
        }

        // İş günlüğü
        await op('', async () => { await call(`/api/work-items/${wi.id}/worklogs`, { method:'POST', body: JSON.stringify({ userId: assignee, hours: [2,3,4,6,8][allWI.length%5], note: 'Geliştirme' }) }); });

        // İzle + Oy
        await op('', async () => { await call(`/api/work-items/${wi.id}/watch`, { method:'PUT', body: JSON.stringify({ watching: true }) }); });
        if (allWI.length % 2 === 0) await op('', async () => { await call(`/api/work-items/${wi.id}/vote`, { method:'PUT', body: JSON.stringify({ voted: true }) }); });
      }
    }
  }
  console.log(` ${allWI.length} iş öğesi\n`);

  // 9. İLİŞKİLER
  console.log('🔗 İlişkiler');
  for (const p of projectDefs) {
    const pi = allWI.filter(w => w.projectKey === p.key);
    const epics = pi.filter(w => w.type === 'Epic');
    const others = pi.filter(w => w.type !== 'Epic' && w.type !== 'Subtask');
    for (const e of epics) {
      for (let i = 0; i < Math.min(others.length, 3); i++) {
        const c = others[(others.indexOf(e)+i+1) % others.length];
        if (c) await op('', async () => { await call(`/api/work-items/${c.id}/parent`, { method:'PATCH', body: JSON.stringify({ parentId: e.id }) }); });
      }
    }
    for (let i = 0; i < pi.length-1; i += 2) {
      await op('', async () => { await call(`/api/work-items/${pi[i].id}/relations`, { method:'POST', body: JSON.stringify({ relatedWorkItemId: pi[i+1].id, relationType: 'Blocks' }) }); });
    }
  }
  console.log('');

  // 10. SPRINT'LER
  console.log('🏃 Sprint\'ler');
  for (const p of projectDefs) {
    const pid = projectMap[p.key]; if (!pid) continue;
    let s1 = null;
    await op('', async () => { s1 = await call('/api/sprints', { method:'POST', body: JSON.stringify({ projectId: pid, name: `${p.key} Sprint 1`, goal: 'MVP çekirdek', startDate: dateOnly(-5), endDate: dateOnly(9) }) }); });
    const pi = allWI.filter(w => w.projectKey === p.key);
    if (s1) {
      for (let i = 0; i < Math.min(pi.length, 6); i++) await op('', async () => { await call(`/api/sprints/${s1.id}/items/${pi[i].id}`, { method:'PUT', body:'{}' }); });
      await op('', async () => { await call(`/api/sprints/${s1.id}/start`, { method:'POST', body:'{}' }); });
    }
    await op('', async () => { await call('/api/sprints', { method:'POST', body: JSON.stringify({ projectId: pid, name: `${p.key} Sprint 2`, goal: 'İyileştirme', startDate: dateOnly(10), endDate: dateOnly(24) }) }); });
    await op('', async () => { await call('/api/sprints', { method:'POST', body: JSON.stringify({ projectId: pid, name: `${p.key} Sprint 3`, goal: 'Stabilizasyon', startDate: dateOnly(-33), endDate: dateOnly(-19) }) }); });
  }
  console.log('');

  // 11. PROJE KATALOĞU
  console.log('📦 Katalog');
  for (const p of projectDefs) {
    const pid = projectMap[p.key]; if (!pid) continue;
    await op('', async () => { await call(`/api/projects/${pid}/versions`, { method:'POST', body: JSON.stringify({ name: 'v1.0.0' }) }); });
    await op('', async () => { await call(`/api/projects/${pid}/versions`, { method:'POST', body: JSON.stringify({ name: 'v1.1.0' }) }); });
    await op('', async () => { await call(`/api/projects/${pid}/milestones`, { method:'POST', body: JSON.stringify({ name: 'GA Release', dueAt: days(45) }) }); });
    await op('', async () => { await call(`/api/projects/${pid}/components`, { method:'POST', body: JSON.stringify({ name: 'Backend', description: 'API katmanı' }) }); });
    await op('', async () => { await call(`/api/projects/${pid}/components`, { method:'POST', body: JSON.stringify({ name: 'Frontend', description: 'UI katmanı' }) }); });
  }
  console.log('');

  // 12. İŞ OTOMASYONU
  console.log('🔄 Otomasyon');
  for (const p of projectDefs) {
    const pid = projectMap[p.key]; const bid = boardMap[p.key]; if (!pid || !bid) continue;
    let tpl = null;
    await op('', async () => { tpl = await call('/api/work-items/templates', { method:'POST', body: JSON.stringify({ projectId: pid, boardId: bid, name: 'Haftalık Review', title: 'Sprint Review Hazırlığı', type: 'Task', priority: 'Medium', description: 'Sprint demo hazırlığı.', dueAfterDays: 14, labels: ['rutin'] }) }); });
    if (tpl) await op('', async () => { await call('/api/work-items/recurrences', { method:'POST', body: JSON.stringify({ projectId: pid, templateId: tpl.id, frequency: 'Weekly', interval: 2, startAtUtc: days(1), endAtUtc: days(90), maxOccurrences: 6 }) }); });
  }
  console.log('');

  // 13. ROLLER
  console.log('🎭 Roller');
  await op('', async () => { await call('/api/auth/roles', { method:'POST', body: JSON.stringify({ name: 'AcmeDeveloper', organizationId: ORG, permissions: ['WorkItemCreate','WorkItemView','WorkItemEdit','BoardView','ProjectView'] }) }); });
  await op('', async () => { await call('/api/auth/roles', { method:'POST', body: JSON.stringify({ name: 'AcmeViewer', organizationId: ORG, permissions: ['WorkItemView','BoardView','ProjectView'] }) }); });
  console.log('');

  // 14. BOARD GÖRÜNÜMLERİ
  console.log('💾 Görünümler');
  for (const p of projectDefs) {
    const bid = boardMap[p.key]; if (!bid) continue;
    await op('', async () => { await call(`/api/boards/${bid}/views`, { method:'POST', body: JSON.stringify({ name: 'Yüksek Öncelik', isShared: true, filters: { priority: 'High' } }) }); });
  }

  // 15. ONAY TALEPLERİ
  console.log('\n✅ Onaylar');
  for (const p of projectDefs) {
    const pi = allWI.filter(w => w.projectKey === p.key && (w.status === 'Code Review' || w.status === 'Test'));
    for (const item of pi.slice(0, 2)) {
      await op('', async () => { await call(`/api/work-items/${item.id}/approvals`, { method:'POST', body: JSON.stringify({ approverUserId: adminId, note: 'İnceleme için onay talebi' }) }); });
    }
  }

  console.log(`\n\n🎉 Acme Teknoloji dolduruldu!`);
  console.log(`📊 ${userIds.length} kullanıcı, ${Object.keys(projectMap).length} proje, ${allWI.length} iş öğesi`);
  console.log(`👤 Giriş: admin@acme.local / AcmeAdmin2026!`);
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
