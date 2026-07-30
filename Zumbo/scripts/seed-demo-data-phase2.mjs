#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API_BASE = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
const ADMIN_EMAIL = env.ZUMBO_IDENTITY_ADMIN_EMAIL;
const ADMIN_PASSWORD = process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || 'Deneme12345!';

let cookies = '', csrfToken = '', adminUserId = '', orgId = '';

async function api(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', 'Origin': 'http://127.0.0.1:58177', ...options.headers };
  if (cookies) headers['Cookie'] = cookies;
  if (csrfToken) headers['X-CSRF-Token'] = csrfToken;
  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(`API ${res.status} ${path}: ${data?.error?.message || res.statusText}`);
  const sc = res.headers.getSetCookie ? res.headers.getSetCookie() : [];
  if (sc.length) {
    const merged = {};
    if (cookies) cookies.split(';').forEach(c => { const p = c.trim().split('='); if (p[0]) merged[p[0].trim()] = (p[1]||'').trim(); });
    sc.forEach(s => { const pair = s.split(';')[0].trim().split('='); if (pair[0]) merged[pair[0].trim()] = (pair[1]||'').trim(); });
    cookies = Object.entries(merged).map(([k,v]) => `${k}=${v}`).join('; ');
  }
  return data?.data !== undefined ? data.data : data;
}

async function login() {
  const r = await api('/api/browser-auth/login', { method: 'POST', body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASSWORD }) });
  csrfToken = r.csrfToken || ''; adminUserId = r.user?.id || ''; orgId = r.user?.organizationId || 'local-dev';
  console.log(`✅ Giriş: ${r.user?.username} (org: ${orgId})`);
}

// --- Yardımcı fonksiyonlar ---
async function getProjects() {
  const r = await api(`/api/projects?organizationId=${orgId}&pageSize=50`);
  return Array.isArray(r) ? r : (r.items || r.data || []);
}
async function getBoards(projectId) { return (await api(`/api/boards/by-project/${projectId}`)).items || []; }
async function getWorkItems(projectId) {
  const r = await api(`/api/work-items?projectId=${projectId}&pageSize=100`);
  return r.items || r.data?.items || (Array.isArray(r) ? r : []);
}
async function getTeams() {
  const r = await api(`/api/teams?organizationId=${orgId}&pageSize=50`);
  return Array.isArray(r) ? r : (r.items || r.data || []);
}

async function tryCreate(label, fn) {
  try { const r = await fn(); console.log(`  ✅ ${label}`); return r; }
  catch (e) { console.log(`  ⚠️  ${label}: ${e.message}`); return null; }
}

// --- ANA AKIŞ ---
async function main() {
  console.log('🌱 Zumbo Demo Veri — Faz 2 (Tüm Bölümleri Doldurma)\n');
  await login();

  const projects = await getProjects();
  const teams = await getTeams();
  console.log(`📁 ${projects.length} proje, ${teams.length} takım bulundu\n`);

  // Her proje için workflow, board kolonları, sprint, katalog ve zenginleştirme
  for (const project of projects) {
    const pid = project.id || project._id;
    const pkey = project.key || project.Key;
    const pname = project.name || project.Name;
    console.log(`\n📦 Proje: ${pname} (${pkey})`);

    // --- WORKFLOW ---
    await tryCreate(`Workflow: ${pkey}`, async () => {
      return api(`/api/workflows/${pid}/draft`, {
        method: 'PUT',
        body: JSON.stringify({
          projectId: pid,
          name: `${pkey} İş Akışı`,
          statuses: [
            { name: 'Yapılacak', category: 'Todo', position: 1 },
            { name: 'Geliştiriliyor', category: 'InProgress', position: 2 },
            { name: 'İncelemede', category: 'InProgress', position: 3 },
            { name: 'Tamamlandı', category: 'Done', position: 4 }
          ],
          transitions: [
            { from: 'Yapılacak', to: 'Geliştiriliyor' },
            { from: 'Geliştiriliyor', to: 'İncelemede' },
            { from: 'İncelemede', to: 'Tamamlandı' },
            { from: 'Geliştiriliyor', to: 'Tamamlandı' },
            { from: 'İncelemede', to: 'Geliştiriliyor' },
            { from: 'Yapılacak', to: 'Tamamlandı' }
          ],
          issueTypeSchemes: []
        })
      });
    });

    await tryCreate(`Workflow yayınla: ${pkey}`, async () => {
      return api(`/api/workflows/${pid}/publish`, { method: 'POST', body: JSON.stringify({ projectId: pid }) });
    });

    // --- BOARD KOLONLARI ---
    const boards = await getBoards(pid);
    for (const board of boards) {
      const bid = board.id || board._id;
      const bname = board.name || board.Name;

      const columnDefs = [
        { name: 'Yapılacak', category: 'Todo', wipLimit: 0, statusNames: ['Yapılacak'] },
        { name: 'Geliştiriliyor', category: 'InProgress', wipLimit: 5, statusNames: ['Geliştiriliyor'] },
        { name: 'İncelemede', category: 'InProgress', wipLimit: 3, statusNames: ['İncelemede'] },
        { name: 'Tamamlandı', category: 'Done', wipLimit: 0, statusNames: ['Tamamlandı'] }
      ];

      for (const col of columnDefs) {
        await tryCreate(`Kolon: ${col.name} (${bname})`, async () => {
          return api(`/api/boards/${bid}/columns`, { method: 'POST', body: JSON.stringify(col) });
        });
      }
    }

    // --- İŞ ÖĞELERİ ZENGİNLEŞTİRME ---
    const items = await getWorkItems(pid);
    if (items.length === 0) { console.log('  (iş öğesi yok, atlanıyor)'); continue; }

    // Çeşitli statülere dağıt
    const statuses = ['Yapılacak', 'Geliştiriliyor', 'İncelemede', 'Tamamlandı', 'Yapılacak', 'Geliştiriliyor', 'Tamamlandı'];
    for (let i = 0; i < items.length; i++) {
      const wi = items[i];
      const wiId = wi.id || wi._id;
      const targetStatus = statuses[i % statuses.length];

      // Statü güncelle
      await tryCreate(`Statü: ${wi.title || wi.Title} → ${targetStatus}`, async () => {
        return api(`/api/work-items/${wiId}/status`, { method: 'PATCH', body: JSON.stringify({ status: targetStatus }) });
      });

      // Ata (admin'e)
      await tryCreate(`Atama: ${wi.title || wi.Title}`, async () => {
        return api(`/api/work-items/${wiId}/assignee`, { method: 'PATCH', body: JSON.stringify({ assigneeUserId: adminUserId }) });
      });

      // Takım bağla (ilk takıma)
      if (teams.length > 0) {
        await tryCreate(`Takım: ${wi.title || wi.Title}`, async () => {
          return api(`/api/work-items/${wiId}/team`, { method: 'PATCH', body: JSON.stringify({ teamId: teams[0].id || teams[0]._id }) });
        });
      }

      // Bitiş tarihi (bazılarına)
      if (i % 2 === 0) {
        const due = new Date();
        due.setDate(due.getDate() + (i <= 2 ? -(i+1) : i * 2)); // bazıları geçmiş, bazıları gelecek
        await tryCreate(`Bitiş tarihi: ${wi.title || wi.Title}`, async () => {
          return api(`/api/work-items/${wiId}`, { method: 'PUT', body: JSON.stringify({
            title: wi.title || wi.Title,
            dueDate: due.toISOString()
          })});
        });
      }

      // Etiketler
      const labelPool = ['önemli', 'acil', 'dokümantasyon', 'teknik-borç', 'müşteri-etkisi', 'ar-ge', 'güvenlik', 'performans'];
      const labels = [labelPool[i % labelPool.length], labelPool[(i + 3) % labelPool.length]];
      for (const label of labels) {
        await tryCreate(`Etiket: ${label}`, async () => {
          return api(`/api/work-items/${wiId}/labels`, { method: 'POST', body: JSON.stringify({ label }) });
        });
      }

      // Checklist
      const checklists = [
        ['Gereksinim analizi yapıldı', 'Tasarım onaylandı', 'Implementasyon tamamlandı', 'Test yazıldı'],
        ['Veritabanı şeması oluşturuldu', 'API endpoint yazıldı', 'Birim testi eklendi', 'Dokümantasyon güncellendi'],
        ['Mevcut durum incelendi', 'Kök neden tespit edildi', 'Düzeltme uygulandı', 'Regresyon testi yapıldı']
      ];
      const checklist = checklists[i % checklists.length];
      for (const item of checklist) {
        await tryCreate(`Checklist: ${item}`, async () => {
          return api(`/api/work-items/${wiId}/checklist`, { method: 'POST', body: JSON.stringify({ text: item }) });
        });
      }

      // Work log
      const hours = [2.5, 4, 1.5, 8, 3, 6, 2];
      await tryCreate(`İş günlüğü: ${hours[i % hours.length]}s`, async () => {
        return api(`/api/work-items/${wiId}/worklogs`, { method: 'POST', body: JSON.stringify({
          userId: adminUserId, hours: hours[i % hours.length], note: 'Geliştirme çalışması'
        })});
      });

      // İzlenme ve oylama
      if (i % 3 === 0) {
        await tryCreate(`İzle: ${wi.title || wi.Title}`, async () => {
          return api(`/api/work-items/${wiId}/watch`, { method: 'PUT' });
        });
      }
      if (i % 4 === 0) {
        await tryCreate(`Oy: ${wi.title || wi.Title}`, async () => {
          return api(`/api/work-items/${wiId}/vote`, { method: 'PUT' });
        });
      }

      // Yorumlar
      const comments = [
        'Bu iş öğesi üzerinde çalışmaya başladım, ilk taslak yakında paylaşacağım.',
        'Teknik detayları netleştirmemiz lazım. Tasarım ekibinden görüş alalım.',
        'Performans etkisi değerlendirilmeli, özellikle büyük veri setlerinde.',
        'Müşteri geri bildirimi doğrultusunda öncelik arttırıldı.'
      ];
      await tryCreate(`Yorum: ${wi.title || wi.Title}`, async () => {
        return api(`/api/work-items/${wiId}/comments`, { method: 'POST', body: JSON.stringify({ body: comments[i % comments.length] }) });
      });
    }

    // İlişkiler (parent-child + blocks)
    if (items.length >= 3) {
      // İlk öğeyi parent (Epic) yap, 2-3'ü child
      const parent = items[0];
      const parentId = parent.id || parent._id;
      for (let j = 1; j <= Math.min(2, items.length - 1); j++) {
        const child = items[j];
        const childId = child.id || child._id;
        await tryCreate(`Parent: ${child.title||child.Title} → ${parent.title||parent.Title}`, async () => {
          return api(`/api/work-items/${childId}/parent`, { method: 'PATCH', body: JSON.stringify({ parentId }) });
        });
      }
      // Blocks ilişkisi
      if (items.length >= 3) {
        const blocker = items[1]; const blocked = items[2];
        await tryCreate(`Blocks: ${blocker.title||blocker.Title} → ${blocked.title||blocked.Title}`, async () => {
          return api(`/api/work-items/${(blocker.id||blocker._id)}/relations`, { method: 'POST', body: JSON.stringify({
            relatedWorkItemId: blocked.id || blocked._id, relationType: 'Blocks'
          })});
        });
      }
    }

    // --- SPRINT ---
    const now = new Date();
    const sprintStart = new Date(now.getTime() - 4 * 86400000);
    const sprintEnd = new Date(now.getTime() + 10 * 86400000);
    let sprintId = null;
    await tryCreate(`Sprint: ${pkey} Sprint 1`, async () => {
      const sprint = await api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 1`, goal: `${pname} için ilk yineleme: çekirdek özelliklerin teslimi`,
        startDate: sprintStart.toISOString(), endDate: sprintEnd.toISOString()
      })});
      sprintId = sprint.id || sprint._id;
      return sprint;
    });

    // Sprint başlat
    if (sprintId) {
      await tryCreate(`Sprint başlat`, async () => {
        return api(`/api/sprints/${sprintId}/start`, { method: 'POST', body: JSON.stringify({}) });
      });
      // İlk birkaç iş öğesini sprint'e planla
      const planCount = Math.min(5, items.length);
      for (let i = 0; i < planCount; i++) {
        const wiId = items[i].id || items[i]._id;
        await tryCreate(`Sprint planla: ${items[i].title||items[i].Title}`, async () => {
          return api(`/api/sprints/${sprintId}/items/${wiId}`, { method: 'PUT', body: JSON.stringify({}) });
        });
      }
    }

    // İkinci sprint (Planned durumda - backlog için)
    const sprint2Start = new Date(now.getTime() + 11 * 86400000);
    const sprint2End = new Date(now.getTime() + 25 * 86400000);
    let sprint2Id = null;
    await tryCreate(`Sprint: ${pkey} Sprint 2 (Planlı)`, async () => {
      const sprint2 = await api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 2`, goal: `${pname} için ikinci yineleme: iyileştirme ve optimizasyon`,
        startDate: sprint2Start.toISOString(), endDate: sprint2End.toISOString()
      })});
      sprint2Id = sprint2.id || sprint2._id;
      return sprint2;
    });

    // --- PROJE KATALOĞU ---
    // Sürüm
    let versionId = null;
    await tryCreate(`Sürüm: v1.0.0`, async () => {
      const v = await api(`/api/projects/${pid}/versions`, { method: 'POST', body: JSON.stringify({ name: 'v1.0.0' }) });
      versionId = v.id || v._id; return v;
    });
    await tryCreate(`Sürüm: v1.1.0`, async () => {
      return api(`/api/projects/${pid}/versions`, { method: 'POST', body: JSON.stringify({ name: 'v1.1.0' }) });
    });

    // Yayın
    if (versionId) {
      await tryCreate(`Yayın: İlk Sürüm`, async () => {
        return api(`/api/projects/${pid}/releases`, { method: 'POST', body: JSON.stringify({
          versionId, name: 'İlk Kararlı Sürüm', scheduledAt: new Date(now.getTime() + 14*86400000).toISOString()
        })});
      });
    }

    // Kilometre taşı
    await tryCreate(`Kilometre: MVP`, async () => {
      return api(`/api/projects/${pid}/milestones`, { method: 'POST', body: JSON.stringify({
        name: 'MVP Teslimi', dueAt: new Date(now.getTime() + 30*86400000).toISOString()
      })});
    });
    await tryCreate(`Kilometre: Beta`, async () => {
      return api(`/api/projects/${pid}/milestones`, { method: 'POST', body: JSON.stringify({
        name: 'Beta Yayını', dueAt: new Date(now.getTime() + 60*86400000).toISOString()
      })});
    });

    // Bileşenler
    const componentNames = ['Backend API', 'Web Arayüzü', 'Veritabanı', 'Kimlik Doğrulama', 'Altyapı'];
    for (const cn of componentNames.slice(0, 3)) {
      await tryCreate(`Bileşen: ${cn}`, async () => {
        return api(`/api/projects/${pid}/components`, { method: 'POST', body: JSON.stringify({ name: cn, description: `${cn} bileşeni` }) });
      });
    }

    // Proje şablonu
    await tryCreate(`Şablon: Varsayılan`, async () => {
      return api(`/api/projects/${pid}/templates`, { method: 'POST', body: JSON.stringify({
        name: 'Standart Görev Şablonu', isDefault: true,
        defaultComponents: 'Backend API\nWeb Arayüzü\nVeritabanı'
      })});
    });

    // --- İŞ OTOMASYONU ---
    // İş şablonu
    let templateId = null;
    const boardId = boards.length > 0 ? (boards[0].id || boards[0]._id) : null;
    if (boardId) {
      await tryCreate(`İş Şablonu: Haftalık Bakım`, async () => {
        const t = await api('/api/work-items/templates', { method: 'POST', body: JSON.stringify({
          projectId: pid, boardId, name: 'Haftalık Bakım Görevi',
          title: 'Haftalık Sistem Bakımı ve Kontrolü',
          description: 'Her hafta tekrarlanan sistem bakım görevi. Log kontrolleri, disk alanı ve performans metrikleri incelenmeli.',
          type: 'Task', priority: 'Medium', dueAfterDays: 7,
          labels: ['bakım', 'rutin']
        })});
        templateId = t.id || t._id; return t;
      });

      // Yineleme planı
      if (templateId) {
        await tryCreate(`Yineleme: Haftalık Bakım`, async () => {
          return api('/api/work-items/recurrences', { method: 'POST', body: JSON.stringify({
            projectId: pid, templateId, frequency: 'Weekly', interval: 1,
            startAtUtc: new Date(now.getTime() + 86400000).toISOString(),
            endAtUtc: new Date(now.getTime() + 90*86400000).toISOString(),
            maxOccurrences: 12
          })});
        });
      }
    }
  }

  // --- DEPARTMANLAR ---
  console.log('\n🏢 Departmanlar oluşturuluyor...');
  const departments = [
    { name: 'Teknoloji', parent: null },
    { name: 'Yazılım Geliştirme', parent: 'Teknoloji' },
    { name: 'DevOps', parent: 'Teknoloji' },
    { name: 'Ürün Yönetimi', parent: null },
    { name: 'Operasyonlar', parent: null }
  ];
  const deptMap = {};
  for (const dept of departments) {
    await tryCreate(`Departman: ${dept.name}`, async () => {
      const parentId = dept.parent ? deptMap[dept.parent] : undefined;
      const body = { name: dept.name };
      if (parentId) body.parentDepartmentId = parentId;
      const r = await api(`/api/organizations/${orgId}/departments`, { method: 'POST', body: JSON.stringify(body) });
      deptMap[dept.name] = r.id || r._id;
      return r;
    });
  }

  // --- ÖZEL ROL ---
  console.log('\n👤 Özel roller oluşturuluyor...');
  await tryCreate(`Rol: Geliştirici`, async () => {
    return api('/api/auth/roles', { method: 'POST', body: JSON.stringify({
      name: 'Geliştirici', organizationId: orgId,
      permissions: ['WorkItemCreate', 'WorkItemView', 'WorkItemEdit', 'BoardView', 'ProjectView']
    })});
  });
  await tryCreate(`Rol: Gözlemci`, async () => {
    return api('/api/auth/roles', { method: 'POST', body: JSON.stringify({
      name: 'Gözlemci', organizationId: orgId,
      permissions: ['WorkItemView', 'BoardView', 'ProjectView']
    })});
  });

  // --- BİLDİRİM TERCİHLERİ ---
  console.log('\n🔔 Bildirim tercihleri ayarlanıyor...');
  await tryCreate('Bildirim tercihleri', async () => {
    return api('/api/notifications/preferences/me', { method: 'PUT', body: JSON.stringify({
      emailEnabled: true,
      inAppEnabled: true,
      mutedTypes: ['WorkItemCreated', 'SprintCompleted']
    })});
  });

  // --- PROJEYE TAKIM VE ÜYE BAĞLAMA ---
  console.log('\n🔗 Proje-takım bağlantıları...');
  const allProjects = await getProjects();
  const allTeams = await getTeams();
  for (const project of allProjects) {
    const pid = project.id || project._id;
    if (allTeams.length > 0) {
      await tryCreate(`Takım bağla: ${project.name||project.Name}`, async () => {
        return api(`/api/projects/${pid}/teams`, { method: 'POST', body: JSON.stringify({
          teamId: allTeams[0].id || allTeams[0]._id
        })});
      });
    }
  }

  console.log('\n🎉 Tüm bölümler dolduruldu!');
  console.log('💡 http://127.0.0.1:58177/desktop-bulma/index.html');
}

main().catch(err => { console.error('❌ Hata:', err.message); process.exit(1); });
