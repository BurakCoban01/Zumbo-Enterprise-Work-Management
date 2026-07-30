#!/usr/bin/env node
/**
 * Zumbo — İkinci Organizasyon (Acme Teknoloji) Power-User Doldurma
 *
 * Bu script, sıfırdan yeni bir organizasyonu tüm detaylarıyla doldurur:
 * - Organizasyon + departman hiyerarşisi
 * - Kullanıcılar (davet + kayıt akışıyla)
 * - Takımlar + üyeler
 * - Projeler + panolar + board kolonları
 * - İş öğeleri (tüm tiplerde, tüm alanlar dolu)
 * - Sprint'ler (aktif + planlı + tamamlanmış)
 * - Yorumlar, iş günlükleri, checklist, etiketler, ilişkiler
 * - Proje kataloğu (sürüm, yayın, kilometre, bileşen)
 * - İş otomasyonu (şablon + yineleme)
 * - Roller
 *
 * ÖNEMLİ: Statü geçişleri mevcut workflow'a uymalı:
 *   To Do → In Progress → Code Review → Test → Done
 *   (To Do → Done izin verilmeyebilir)
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
const ORIGIN = 'http://127.0.0.1:58177';
const ADMIN_EMAIL = env.ZUMBO_IDENTITY_ADMIN_EMAIL;
const ADMIN_PASS = process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || 'Deneme12345!';
const USER_PASS = process.env.ZUMBO_USER_PASSWORD || 'AcmeDev2026!';

// Yeni organizasyon
const ORG_ID = 'acme-tech';
const ORG_NAME = 'Acme Teknoloji';

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

// Register yeni kullanıcı (admin cookie kullanmadan)
async function registerUser(username, email, pass, orgId, token) {
  const r = await fetch(`${API}/api/browser-auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Origin: ORIGIN },
    body: JSON.stringify({ username, email, password: pass, organizationId: orgId, bootstrapToken: token })
  });
  const d = await r.json();
  if (!r.ok) throw new Error(d?.error?.message || 'Register failed');
  return d.data;
}

async function tryOp(label, fn) {
  try { const r = await fn(); process.stdout.write(`✅ ${label}  `); return r; }
  catch(e) { process.stdout.write(`⚠️ ${label}  `); return null; }
}

function days(n) { const d = new Date(); d.setDate(d.getDate()+n); return d.toISOString(); }
function dateOnly(n) { return days(n).slice(0, 10); }

async function main() {
  console.log(`🚀 ${ORG_NAME} — Power-User Doldurma\n`);

  // === LOGIN ===
  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASS }) });
  csrf = lr.csrfToken; adminId = lr.user.id;
  console.log(`✅ Admin: ${lr.user.username}\n`);

  // === 1. DEPARTMANLAR ===
  console.log('🏢 Departmanlar...');
  const deptDefs = [
    { name: 'Yazılım Geliştirme' },
    { name: 'Ürün ve Tasarım' },
    { name: 'Satış ve Pazarlama' },
    { name: 'Backend Ekibi', parent: 'Yazılım Geliştirme' },
    { name: 'Mobil Ekibi', parent: 'Yazılım Geliştirme' },
    { name: 'Web Ekibi', parent: 'Yazılım Geliştirme' },
    { name: 'UI/UX', parent: 'Ürün ve Tasarım' }
  ];
  const deptMap = {};
  for (const d of deptDefs) {
    await tryOp(d.name, async () => {
      const body = { name: d.name };
      if (d.parent && deptMap[d.parent]) body.parentDepartmentId = deptMap[d.parent];
      const r = await call(`/api/organizations/${ORG_ID}/departments`, { method:'POST', body: JSON.stringify(body) });
      if (r && r.id) deptMap[d.name] = r.id;
    });
  }

  // === 2. TAKIMLAR ===
  console.log('\n👥 Takımlar...');
  const teamDefs = [
    { name: 'Backend Takımı', desc: 'API ve servis geliştirme' },
    { name: 'Mobil Takım', desc: 'iOS ve Android uygulama geliştirme' },
    { name: 'Web Takımı', desc: 'Web arayüzü geliştirme' },
    { name: 'Ürün Takımı', desc: 'Ürün yönetimi ve tasarım' }
  ];
  const teamMap = {};
  for (const t of teamDefs) {
    await tryOp(t.name, async () => {
      const r = await call('/api/teams', { method:'POST', body: JSON.stringify({ name: t.name, description: t.desc, organizationId: ORG_ID, ownerUserId: adminId }) });
      teamMap[t.name] = r?.id;
    });
  }

  // === 3. KULLANICILAR (davet + kayıt) ===
  console.log('\n👤 Kullanıcılar...');
  const userDefs = [
    { name: 'acmebackend', email: 'ali.veli@acme.local', display: 'Ali Veli', team: 'Backend Takımı', dept: 'Backend Ekibi', pos: 'Senior Backend Developer' },
    { name: 'acmemobile', email: 'ayse.yildiz@acme.local', display: 'Ayşe Yıldız', team: 'Mobil Takım', dept: 'Mobil Ekibi', pos: 'Mobil Geliştirici' },
    { name: 'acmeweb', email: 'mehmet.coskun@acme.local', display: 'Mehmet Coşkun', team: 'Web Takımı', dept: 'Web Ekibi', pos: 'Frontend Geliştirici' },
    { name: 'acmeproduct', email: 'fatma.demir@acme.local', display: 'Fatma Demir', team: 'Ürün Takımı', dept: 'Ürün ve Tasarım', pos: 'Ürün Yöneticisi' },
    { name: 'acmedesign', email: 'can.ozkan@acme.local', display: 'Can Özkan', team: 'Ürün Takımı', dept: 'UI/UX', pos: 'UI/UX Tasarımcı' },
    { name: 'acmeqa', email: 'zeynep.acar@acme.local', display: 'Zeynep Acar', team: 'Backend Takımı', dept: 'Yazılım Geliştirme', pos: 'QA Mühendisi' },
    { name: 'acmedevops', email: 'burak.sahin@acme.local', display: 'Burak Şahin', team: 'Backend Takımı', dept: 'Yazılım Geliştirme', pos: 'DevOps Mühendisi' }
  ];

  const userMap = {}; // email → userId
  for (const u of userDefs) {
    const teamId = teamMap[u.team];
    if (!teamId) continue;

    // Davet
    let token = null;
    await tryOp(`Davet: ${u.display}`, async () => {
      const r = await call(`/api/teams/${teamId}/members`, { method:'POST', body: JSON.stringify({ email: u.email, role: 'Member' }) });
      token = r?.invitationToken;
    });

    // Kayıt
    if (token) {
      await tryOp(`Kayıt: ${u.display}`, async () => {
        const r = await registerUser(u.name, u.email, USER_PASS, ORG_ID, token);
        userMap[u.email] = r.user.id;
      });
    }
  }
  const userIds = Object.values(userMap);
  console.log(`\n  ${userIds.length} kullanıcı oluşturuldu`);

  // === 4. DEPARTMAN ÜYELERİ ===
  console.log('\n🏢 Departman üyeleri...');
  for (const u of userDefs) {
    const uid = userMap[u.email];
    const deptId = deptMap[u.dept];
    if (uid && deptId) {
      await tryOp(`${u.dept}/${u.pos}`, async () => {
        await call(`/api/organizations/${ORG_ID}/departments/${deptId}/members`, {
          method:'POST', body: JSON.stringify({ userId: uid, position: u.pos })
        });
      });
    }
  }

  // === 5. PROJELER ===
  console.log('\n📁 Projeler...');
  const projectDefs = [
    { key: 'ACME', name: 'Acme SaaS Platformu', desc: 'Çoklu kiracılı SaaS uygulaması: müşteri yönetimi, abonelik, faturalandırma' },
    { key: 'MOB', name: 'Acme Mobil Uygulama', desc: 'iOS ve Android mobil uygulama: sipariş, bildirim, offline destek' },
    { key: 'INFRA', name: 'Altyapı ve DevOps', desc: 'Kubernetes, CI/CD, izleme, log yönetimi' },
    { key: 'CRM', name: 'CRM Entegrasyonu', desc: 'Salesforce/HubSpot entegrasyonu, müşteri veri senkronizasyonu' }
  ];
  const projectMap = {};
  for (const p of projectDefs) {
    await tryOp(p.name, async () => {
      const r = await call('/api/projects', { method:'POST', body: JSON.stringify({
        organizationId: ORG_ID, key: p.key, name: p.name, description: p.desc, ownerUserId: adminId, visibility: 'Internal'
      })});
      projectMap[p.key] = r?.id;
    });
  }

  // === 6. PROJE ÜYELERİ + TAKIM BAĞLAMA ===
  console.log('\n🔗 Proje üyeleri ve takım bağlama...');
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;
    // Tüm kullanıcıları üye yap
    for (const uid of userIds) {
      await tryOp(`Üye: ${p.key}`, async () => {
        await call(`/api/projects/${pid}/members`, { method:'POST', body: JSON.stringify({ userId: uid, role: 'Developer' }) });
      });
    }
    // Tüm takımları bağla
    for (const tid of Object.values(teamMap)) {
      await tryOp(`Takım: ${p.key}`, async () => {
        await call(`/api/projects/${pid}/teams`, { method:'POST', body: JSON.stringify({ teamId: tid }) });
      });
    }
  }

  // === 7. PANOLAR + KOLONLAR ===
  console.log('\n📋 Panolar...');
  const boardMap = {};
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;
    let boardId = null;
    await tryOp(`Pano: ${p.key}`, async () => {
      const r = await call('/api/boards', { method:'POST', body: JSON.stringify({ projectId: pid, name: `${p.key} Kanban`, type: 'Kanban' }) });
      boardId = r?.id;
      boardMap[p.key] = boardId;
    });
    // Varsayılan kolonlar zaten oluşuyor (5 kolon), kontrol et
    if (boardId) {
      await call(`/api/boards/by-project/${pid}`).then(boards => {
        const bl = Array.isArray(boards) ? boards : (boards.items || []);
        if (bl[0] && bl[0].columns && bl[0].columns.length === 0) {
          // Kolon ekle (nadir durum)
        }
      }).catch(() => {});
    }
  }

  // === 8. İŞ ÖĞELERİ ===
  console.log('\n📝 İş öğeleri...');
  const allUserIds = [adminId, ...userIds];
  const estimateByType = { Epic: 21, Story: 8, Task: 5, Bug: 3, Subtask: 2 };
  const allWorkItems = [];

  const workItemData = {
    ACME: [
      { type:'Epic', title:'Çoklu Kiracılı Mimari', desc:'Veritabanı isolation stratejisi (shared DB, tenant schema), connection routing, tenant resolution middleware.', priority:'High' },
      { type:'Epic', title:'Abonelik ve Faturalandırma', desc:'Stripe entegrasyonu, plan yönetimi, proration, invoice generation, dunning yönetimi.', priority:'High' },
      { type:'Story', title:'Kullanıcı profil sayfası', desc:'Avatar, kişisel bilgiler, şirket bilgileri, tercihler. Düzenleme ve kaydetme.', priority:'Medium' },
      { type:'Story', title:'Rol bazlı erişim kontrolü', desc:'RBAC: SuperAdmin, OrgAdmin, User, Viewer. İzin matrisi ve UI gating.', priority:'High' },
      { type:'Task', title:'PostgreSQL partitioning', desc:'Tenant bazlı tablo partitioning. Performance benchmark gerekli.', priority:'Medium' },
      { type:'Task', title:'Redis önbellek stratejisi', desc:'Hot data caching, tag-based invalidation, distributed lock.', priority:'Medium' },
      { type:'Bug', title:'Tenant geçişinde veri sızıntısı', desc:'Aynı connection pool\'da tenant isolation kırılıyor. Separate connection pool gerekli.', priority:'High' },
      { type:'Bug', title:'Login sonrası yönlendirme hatası', desc:'Auth sonrası /dashboard yerine /login\'e dönüyor. Token timing issue.', priority:'Medium' },
      { type:'Task', title:'Webhook altyapısı', desc:'Event-driven webhook gönderimi, retry, dead-letter queue.', priority:'Low' },
      { type:'Story', title:'Audit log arayüzü', desc:'Filtrelenebilir, export edilebilir audit log viewer. Tarih/kullanıcı/işlem bazlı arama.', priority:'Medium' },
      { type:'Subtask', title:'Migration script yazımı', desc:'Existing single-tenant data\'yı multi-tenant\'a migrate etme.', priority:'Medium' }
    ],
    MOB: [
      { type:'Epic', title:'Offline Mod Desteği', desc:'Çevrimdışı veri senkronizasyonu, conflict resolution, optimistic UI updates.', priority:'High' },
      { type:'Story', title:'Push bildirim entegrasyonu', desc:'FCM (Android) ve APNs (iOS) entegrasyonu. Topic-based ve targeted push.', priority:'High' },
      { type:'Story', title:'Biyometrik giriş', desc:'Face ID / Touch ID desteği. Secure enclave key storage.', priority:'Medium' },
      { type:'Task', title:'React Native upgrade', desc:'RN 0.74\'e yükseltme. Breaking changes fix, native module güncelleme.', priority:'Medium' },
      { type:'Task', title:'App Store optimizasyonu', desc:'ASO: anahtar kelimeler, screenshot\'lar, açıklama optimizasyonu.', priority:'Low' },
      { type:'Bug', title:'Android crash - Samsung cihazlar', desc:'Samsung S24\'te belirli ekranda crash. Native module null pointer.', priority:'High' },
      { type:'Bug', title:'iOS push ses gelmiyor', desc:'Push notification geldi ama ses çalmıyor. Sound permission kontrolü.', priority:'Medium' },
      { type:'Story', title:'Karanlık mod desteği', desc:'Sistem temasını takip eden dark mode. Tüm ekranlarda destek.', priority:'Medium' }
    ],
    INFRA: [
      { type:'Epic', title:'Kubernetes Migration', desc:'Docker Compose\'tan production K8s\'e geçiş. Helm chart\'ları, ingress, cert-manager.', priority:'High' },
      { type:'Story', title:'Auto-scaling yapılandırması', desc:'HPA: CPU/memory/queue depth bazlı. KEDA for event-driven scaling.', priority:'High' },
      { type:'Task', title:'Prometheus + Grafana setup', desc:'Metrik toplama, dashboard\'lar, alert rules (Slack integration).', priority:'Medium' },
      { type:'Task', title:'ELK Stack kurulumu', desc:'Filebeat → Logstash → Elasticsearch → Kibana. Index lifecycle policy.', priority:'Medium' },
      { type:'Bug', title:'Pod OOM Kills', desc:'Java heap memory çok yüksek. JVM args tuning gerekli (-Xmx).', priority:'High' },
      { type:'Task', title:'Backup stratejisi', desc:'Velero ile K8s backup. PITR for PostgreSQL. S3 lifecycle policy.', priority:'Medium' },
      { type:'Story', title:'Zero-downtime deployment', desc:'Canary release, feature flags, automatic rollback.', priority:'High' }
    ],
    CRM: [
      { type:'Epic', title:'Salesforce Entegrasyonu', desc:'Bidirectional sync: contact, account, opportunity, activity. Bulk API + REST API.', priority:'High' },
      { type:'Story', title:'HubSpot contact sync', desc:'Real-time webhook + batch sync. Deduplication logic.', priority:'Medium' },
      { type:'Task', title:'OAuth flow implementation', desc:'Salesforce ve HubSpot için OAuth 2.0 PKCE flow. Token refresh.', priority:'Medium' },
      { type:'Bug', title:'Rate limit exceeded', desc:'Salesforce API limit\'i aşılıyor. Bulk API + caching gerekli.', priority:'Medium' },
      { type:'Story', title:'Customer 360 dashboard', desc:'Tüm platformlardan müşteri verisi aggregation. Tek görünüm.', priority:'Medium' },
      { type:'Task', title:'Data quality rules', desc:'Validation, enrichment, deduplication rules engine.', priority:'Low' }
    ]
  };

  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    const bid = boardMap[p.key];
    if (!pid || !bid) continue;
    const items = workItemData[p.key] || [];

    for (const t of items) {
      const assignee = allUserIds[Math.floor(Math.random() * allUserIds.length)];
      const teamId = Object.values(teamMap)[Math.floor(Math.random() * Object.keys(teamMap).length)];

      const wi = await tryOp(`[${p.key}] ${t.title}`, async () => {
        return await call('/api/work-items', { method:'POST', body: JSON.stringify({
          projectId: pid, boardId: bid, type: t.type, title: t.title,
          description: t.desc, priority: t.priority, assigneeUserId: assignee,
          teamId, labels: t.type === 'Bug' ? ['bug','acil'] : (t.priority === 'High' ? ['önemli'] : [])
        })});
      });

      if (wi) {
        allWorkItems.push({ ...wi, projectKey: p.key, projectId: pid });

        // Estimate
        await tryOp('', async () => {
          await call(`/api/work-items/${wi.id}/planning`, { method:'PATCH', body: JSON.stringify({ estimatePoints: estimateByType[t.type] || 5 }) });
        });

        // Bitiş tarihi
        const dueOffset = [-3, -1, 1, 3, 7, 14, 21, 30][allWorkItems.length % 8];
        await tryOp('', async () => {
          await call(`/api/work-items/${wi.id}`, { method:'PUT', body: JSON.stringify({
            title: t.title, description: t.desc, priority: t.priority, dueDate: days(dueOffset)
          })});
        });

        // Statü (geçerli geçişlerle)
        const statusPaths = [
          ['To Do'],
          ['In Progress'],
          ['In Progress', 'Code Review'],
          ['In Progress', 'Code Review', 'Test'],
          ['In Progress', 'Code Review', 'Test', 'Done'],
          ['To Do'], ['In Progress']
        ];
        const flow = statusPaths[allWorkItems.length % statusPaths.length];
        for (const st of flow) {
          await tryOp('', async () => {
            await call(`/api/work-items/${wi.id}/status`, { method:'PATCH', body: JSON.stringify({ status: st }) });
          });
        }

        // Checklist
        const checklists = [
          ['Gereksinim analizi', 'Tasarım', 'Implementasyon', 'Test', 'Dokümantasyon'],
          ['Kod yazımı', 'Birim testi', 'Code review', 'Entegrasyon testi']
        ];
        for (const item of checklists[allWorkItems.length % 2]) {
          await tryOp('', async () => {
            await call(`/api/work-items/${wi.id}/checklist`, { method:'POST', body: JSON.stringify({ text: item }) });
          });
        }

        // Yorumlar
        const comments = [
          'Tasarım hazır, geliştirmeye başlıyorum.',
          'PR açıldı, review bekliyorum.',
          'Test ortamında doğrulandı.',
          'Bir bağımlılık var, bekliyorum.',
          'Performans testi gerekli.',
          'Müşteri geri bildirimi alındı.'
        ];
        for (let c = 0; c < 2; c++) {
          await tryOp('', async () => {
            await call(`/api/work-items/${wi.id}/comments`, { method:'POST', body: JSON.stringify({ body: comments[(allWorkItems.length + c) % comments.length] }) });
          });
        }

        // İş günlüğü
        const wlHours = [2, 3, 4, 6, 8];
        await tryOp('', async () => {
          await call(`/api/work-items/${wi.id}/worklogs`, { method:'POST', body: JSON.stringify({
            userId: assignee, hours: wlHours[allWorkItems.length % wlHours.length], note: 'Geliştirme çalışması'
          })});
        });

        // İzle
        await tryOp('', async () => {
          await call(`/api/work-items/${wi.id}/watch`, { method:'PUT', body: JSON.stringify({ watching: true }) });
        });
      }
    }
  }
  console.log(`\n  ${allWorkItems.length} iş öğesi oluşturuldu`);

  // === 9. İLİŞKİLER + PARENT-CHILD ===
  console.log('\n🔗 İlişkiler...');
  for (const p of projectDefs) {
    const projItems = allWorkItems.filter(wi => wi.projectKey === p.key);
    const epics = projItems.filter(wi => wi.type === 'Epic');
    const others = projItems.filter(wi => wi.type !== 'Epic' && wi.type !== 'Subtask');

    // Parent-child
    for (const epic of epics) {
      for (let i = 0; i < Math.min(others.length, 3); i++) {
        const child = others[(others.indexOf(epic) + i + 1) % others.length];
        if (child) {
          await tryOp(`Parent`, async () => {
            await call(`/api/work-items/${child.id}/parent`, { method:'PATCH', body: JSON.stringify({ parentId: epic.id }) });
          });
        }
      }
    }
    // Blocks
    for (let i = 0; i < projItems.length - 1; i += 2) {
      await tryOp(`Blocks`, async () => {
        await call(`/api/work-items/${projItems[i].id}/relations`, { method:'POST', body: JSON.stringify({
          relatedWorkItemId: projItems[i + 1].id, relationType: 'Blocks'
        })});
      });
    }
  }

  // === 10. SPRINT'LER ===
  console.log('\n🏃 Sprint\'ler...');
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;

    // Aktif sprint
    let s1 = null;
    await tryOp(`Sprint 1 (Aktif): ${p.key}`, async () => {
      s1 = await call('/api/sprints', { method:'POST', body: JSON.stringify({
        projectId: pid, name: `${p.key} Sprint 1`, goal: 'MVP çekirdek özellikler',
        startDate: dateOnly(-5), endDate: dateOnly(9)
      })});
    });

    const projItems = allWorkItems.filter(wi => wi.projectKey === p.key);
    if (s1) {
      // Önce planla, sonra başlat
      for (let i = 0; i < Math.min(projItems.length, 6); i++) {
        await tryOp(`Planla`, async () => {
          await call(`/api/sprints/${s1.id}/items/${projItems[i].id}`, { method:'PUT', body:'{}' });
        });
      }
      await tryOp(`Başlat`, async () => {
        await call(`/api/sprints/${s1.id}/start`, { method:'POST', body:'{}' });
      });
    }

    // Planlı sprint
    await tryOp(`Sprint 2 (Planlı): ${p.key}`, async () => {
      await call('/api/sprints', { method:'POST', body: JSON.stringify({
        projectId: pid, name: `${p.key} Sprint 2`, goal: 'İyileştirme',
        startDate: dateOnly(10), endDate: dateOnly(24)
      })});
    });

    // Tamamlanmış sprint
    await tryOp(`Sprint 3 (Tamamlandı): ${p.key}`, async () => {
      await call('/api/sprints', { method:'POST', body: JSON.stringify({
        projectId: pid, name: `${p.key} Sprint 3`, goal: 'Stabilizasyon',
        startDate: dateOnly(-33), endDate: dateOnly(-19)
      })});
    });
  }

  // === 11. PROJE KATALOĞU ===
  console.log('\n📦 Proje kataloğu...');
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    if (!pid) continue;

    await tryOp(`Sürüm: ${p.key}`, async () => {
      await call(`/api/projects/${pid}/versions`, { method:'POST', body: JSON.stringify({ name: 'v1.0.0' }) });
    });
    await tryOp(`Sürüm: ${p.key} v1.1`, async () => {
      await call(`/api/projects/${pid}/versions`, { method:'POST', body: JSON.stringify({ name: 'v1.1.0' }) });
    });
    await tryOp(`Kilometre: ${p.key}`, async () => {
      await call(`/api/projects/${pid}/milestones`, { method:'POST', body: JSON.stringify({
        name: 'GA Release', dueAt: days(45)
      })});
    });
    await tryOp(`Bileşen: ${p.key}`, async () => {
      await call(`/api/projects/${pid}/components`, { method:'POST', body: JSON.stringify({
        name: 'Backend Service', description: 'API katmanı'
      })});
    });
    await tryOp(`Bileşen: ${p.key}`, async () => {
      await call(`/api/projects/${pid}/components`, { method:'POST', body: JSON.stringify({
        name: 'Frontend', description: 'Kullanıcı arayüzü'
      })});
    });
  }

  // === 12. İŞ OTOMASYONU ===
  console.log('\n🔄 İş otomasyonu...');
  for (const p of projectDefs) {
    const pid = projectMap[p.key];
    const bid = boardMap[p.key];
    if (!pid || !bid) continue;

    let tplId = null;
    await tryOp(`Şablon: ${p.key}`, async () => {
      const t = await call('/api/work-items/templates', { method:'POST', body: JSON.stringify({
        projectId: pid, boardId: bid, name: 'Haftalık Sprint Review',
        title: 'Sprint Değerlendirme Toplantısı', type: 'Task', priority: 'Medium',
        description: 'Sprint demo ve retrospektif hazırlığı.', dueAfterDays: 14, labels: ['toplantı','rutin']
      })});
      tplId = t?.id;
    });

    if (tplId) {
      await tryOp(`Yineleme: ${p.key}`, async () => {
        await call('/api/work-items/recurrences', { method:'POST', body: JSON.stringify({
          projectId: pid, templateId: tplId, frequency: 'Weekly', interval: 2,
          startAtUtc: days(1), endAtUtc: days(90), maxOccurrences: 6
        })});
      });
    }
  }

  // === 13. ROLLER ===
  console.log('\n🎭 Roller...');
  await tryOp('Rol: Acme Developer', async () => {
    await call('/api/auth/roles', { method:'POST', body: JSON.stringify({
      name: 'AcmeDeveloper', organizationId: ORG_ID,
      permissions: ['WorkItemCreate','WorkItemView','WorkItemEdit','BoardView','ProjectView']
    })});
  });
  await tryOp('Rol: Acme Viewer', async () => {
    await call('/api/auth/roles', { method:'POST', body: JSON.stringify({
      name: 'AcmeViewer', organizationId: ORG_ID,
      permissions: ['WorkItemView','BoardView','ProjectView']
    })});
  });

  // === 14. KAYDEDILMIŞ BOARD GÖRÜNÜMLERİ ===
  console.log('\n💾 Kaydedilmiş görünümler...');
  for (const p of projectDefs) {
    const bid = boardMap[p.key];
    if (!bid) continue;
    await tryOp(`Görünüm: ${p.key}`, async () => {
      await call(`/api/boards/${bid}/views`, { method:'POST', body: JSON.stringify({
        name: 'Yüksek Öncelikli', isShared: true, filters: { priority: 'High' }
      })});
    });
  }

  console.log('\n\n🎉 Acme Teknoloji dolduruldu!');
  console.log(`📊 Özet:`);
  console.log(`   Organizasyon: ${ORG_NAME} (${ORG_ID})`);
  console.log(`   Departman: ${Object.keys(deptMap).length}`);
  console.log(`   Takım: ${Object.keys(teamMap).length}`);
  console.log(`   Kullanıcı: ${userIds.length}`);
  console.log(`   Proje: ${Object.keys(projectMap).length}`);
  console.log(`   İş öğesi: ${allWorkItems.length}`);
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
