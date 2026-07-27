#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API_BASE = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
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

async function tryCreate(label, fn) {
  try { const r = await fn(); console.log(`  ✅ ${label}`); return r; }
  catch (e) { console.log(`  ⚠️  ${label}: ${e.message}`); return null; }
}

function dateStr(daysFromNow) {
  const d = new Date();
  d.setDate(d.getDate() + daysFromNow);
  return d.toISOString().slice(0, 10);
}

async function main() {
  console.log('🌱 Zumbo Demo Veri — Faz 3 (Düzeltme ve Tamamlama)\n');

  const r = await api('/api/browser-auth/login', { method: 'POST', body: JSON.stringify({
    usernameOrEmail: env.ZUMBO_IDENTITY_ADMIN_EMAIL, password: ADMIN_PASSWORD
  })});
  csrfToken = r.csrfToken; adminUserId = r.user.id; orgId = r.user.organizationId;
  console.log(`✅ Giriş: ${r.user.username}\n`);

  const projects = await api(`/api/projects?organizationId=${orgId}&pageSize=50`);
  const projectList = projects.items || projects;
  const teams = await api(`/api/teams?organizationId=${orgId}&pageSize=50`);
  const teamList = teams.items || teams;

  for (const project of projectList) {
    const pid = project.id;
    const pkey = project.key;
    const pname = project.name;
    console.log(`\n📦 ${pname} (${pkey})`);

    // --- TAKIMLARI ÖNCE PROJEYE BAĞLA ---
    for (const team of teamList) {
      await tryCreate(`Takım bağla: ${team.name}`, async () => {
        return api(`/api/projects/${pid}/teams`, { method: 'POST', body: JSON.stringify({ teamId: team.id }) });
      });
    }

    // --- WORKFLOW OLUŞTUR VE YAYINLA ---
    const statuses = [
      { name: 'Yapılacak', category: 'Todo' },
      { name: 'Geliştiriliyor', category: 'InProgress' },
      { name: 'İncelemede', category: 'InProgress' },
      { name: 'Tamamlandı', category: 'Done' }
    ];
    const transitions = [
      { fromStatus: 'Yapılacak', toStatus: 'Geliştiriliyor' },
      { fromStatus: 'Geliştiriliyor', toStatus: 'İncelemede' },
      { fromStatus: 'İncelemede', toStatus: 'Tamamlandı' },
      { fromStatus: 'Geliştiriliyor', toStatus: 'Tamamlandı' },
      { fromStatus: 'İncelemede', toStatus: 'Geliştiriliyor' },
      { fromStatus: 'Yapılacak', toStatus: 'Tamamlandı' }
    ];
    const issueTypeSchemes = [
      { issueType: 'Epic', defaultStatus: 'Yapılacak', statuses: ['Yapılacak','Geliştiriliyor','İncelemede','Tamamlandı'], doneStatuses: ['Tamamlandı'] },
      { issueType: 'Story', defaultStatus: 'Yapılacak', statuses: ['Yapılacak','Geliştiriliyor','İncelemede','Tamamlandı'], doneStatuses: ['Tamamlandı'] },
      { issueType: 'Task', defaultStatus: 'Yapılacak', statuses: ['Yapılacak','Geliştiriliyor','İncelemede','Tamamlandı'], doneStatuses: ['Tamamlandı'] },
      { issueType: 'Bug', defaultStatus: 'Yapılacak', statuses: ['Yapılacak','Geliştiriliyor','İncelemede','Tamamlandı'], doneStatuses: ['Tamamlandı'] },
      { issueType: 'Subtask', defaultStatus: 'Yapılacak', statuses: ['Yapılacak','Geliştiriliyor','İncelemede','Tamamlandı'], doneStatuses: ['Tamamlandı'] }
    ];

    await tryCreate(`Workflow taslak: ${pkey}`, async () => {
      return api(`/api/workflows/${pid}/draft`, {
        method: 'PUT',
        body: JSON.stringify({ projectId: pid, name: `${pkey} İş Akışı`, statuses, transitions, issueTypeSchemes })
      });
    });
    await tryCreate(`Workflow yayınla: ${pkey}`, async () => {
      return api(`/api/workflows/${pid}/publish`, { method: 'POST', body: JSON.stringify({ projectId: pid }) });
    });

    // --- İŞ ÖĞELERİ ---
    const wiRes = await api(`/api/work-items?projectId=${pid}&pageSize=100`);
    const items = wiRes.items || [];
    if (items.length === 0) { console.log('  (iş öğesi yok)'); continue; }

    // Statü güncelle, izle, oyla, takım ata
    const statusCycle = ['Yapılacak', 'Geliştiriliyor', 'İncelemede', 'Tamamlandı', 'Yapılacak', 'Geliştiriyor'];
    for (let i = 0; i < items.length; i++) {
      const wi = items[i];
      const status = statusCycle[i % statusCycle.length];
      await tryCreate(`Statü: ${wi.title} → ${status}`, async () => {
        return api(`/api/work-items/${wi.id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) });
      });
      if (teamList.length > 0) {
        await tryCreate(`Takım: ${wi.title}`, async () => {
          return api(`/api/work-items/${wi.id}/team`, { method: 'PATCH', body: JSON.stringify({ teamId: teamList[i % teamList.length].id }) });
        });
      }
      if (i % 2 === 0) {
        await tryCreate(`İzle: ${wi.title}`, async () => {
          return api(`/api/work-items/${wi.id}/watch`, { method: 'PUT', body: JSON.stringify({ watching: true }) });
        });
      }
      if (i % 3 === 0) {
        await tryCreate(`Oy: ${wi.title}`, async () => {
          return api(`/api/work-items/${wi.id}/vote`, { method: 'PUT', body: JSON.stringify({ voted: true }) });
        });
      }
    }

    // Parent-child: sadece Epic'leri parent yap
    const epics = items.filter(wi => wi.type === 'Epic');
    const nonEpics = items.filter(wi => wi.type !== 'Epic');
    if (epics.length > 0 && nonEpics.length > 0) {
      for (let i = 0; i < Math.min(nonEpics.length, 3); i++) {
        const child = nonEpics[i];
        const parent = epics[i % epics.length];
        await tryCreate(`Parent: ${child.title} → ${parent.title}`, async () => {
          return api(`/api/work-items/${child.id}/parent`, { method: 'PATCH', body: JSON.stringify({ parentId: parent.id }) });
        });
      }
    }

    // --- SPRINT ---
    let sprint1Id = null;
    await tryCreate(`Sprint 1 (Aktif): ${pkey}`, async () => {
      const sp = await api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 1`, goal: `${pname} çekirdek özelliklerin teslimi`,
        startDate: dateStr(-4), endDate: dateStr(10)
      })});
      sprint1Id = sp.id; return sp;
    });
    if (sprint1Id) {
      await tryCreate(`Sprint başlat`, async () => {
        return api(`/api/sprints/${sprint1Id}/start`, { method: 'POST', body: JSON.stringify({}) });
      });
      for (let i = 0; i < Math.min(items.length, 5); i++) {
        const wi = items[i];
        await tryCreate(`Planla: ${wi.title}`, async () => {
          return api(`/api/sprints/${sprint1Id}/items/${wi.id}`, { method: 'PUT', body: JSON.stringify({}) });
        });
      }
    }
    let sprint2Id = null;
    await tryCreate(`Sprint 2 (Planlı): ${pkey}`, async () => {
      const sp = await api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 2`, goal: `${pname} iyileştirme ve optimizasyon`,
        startDate: dateStr(11), endDate: dateStr(25)
      })});
      sprint2Id = sp.id; return sp;
    });

    // --- YAYIN ---
    const versionsRes = await api(`/api/projects/${pid}/versions`);
    const versions = versionsRes.items || versionsRes || [];
    if (versions.length > 0) {
      const v = versions[0];
      await tryCreate(`Yayın: ${pname}`, async () => {
        return api(`/api/projects/${pid}/releases`, { method: 'POST', body: JSON.stringify({
          versionId: v.id, name: 'İlk Kararlı Sürüm', scheduledAt: new Date().toISOString()
        })});
      });
    }

    // --- ONAY TALEBİ ---
    const reviewItems = items.filter(wi => wi.status === 'İncelemede');
    if (reviewItems.length > 0) {
      await tryCreate(`Onay talebi: ${reviewItems[0].title}`, async () => {
        return api(`/api/work-items/${reviewItems[0].id}/approvals`, { method: 'POST', body: JSON.stringify({
          approverUserId: adminUserId, note: 'Kod incelemesi için onay bekleniyor'
        })});
      });
    }

    // --- İŞ OTOMASYONU ---
    const boardsRes = await api(`/api/boards/by-project/${pid}`);
    const boards = boardsRes.items || [];
    if (boards.length > 0) {
      const bid = boards[0].id;
      let tplId = null;
      await tryCreate(`İş Şablonu: Sprint Review`, async () => {
        const t = await api('/api/work-items/templates', { method: 'POST', body: JSON.stringify({
          projectId: pid, boardId: bid, name: 'Sprint Değerlendirme Görevi',
          title: 'Sprint Değerlendirme ve Retrospektif', type: 'Task', priority: 'Medium',
          description: 'Sprint sonunda değerlendirme ve retrospektif toplantısı hazırlığı.',
          dueAfterDays: 14, labels: ['toplantı', 'sprint']
        })});
        tplId = t.id; return t;
      });
      if (tplId) {
        await tryCreate(`Yineleme: Sprint Review`, async () => {
          return api('/api/work-items/recurrences', { method: 'POST', body: JSON.stringify({
            projectId: pid, templateId: tplId, frequency: 'Weekly', interval: 2,
            startAtUtc: new Date(Date.now() + 86400000).toISOString(),
            endAtUtc: new Date(Date.now() + 90*86400000).toISOString(),
            maxOccurrences: 6
          })});
        });
      }
    }
  }

  // --- DEPARTMAN ALT ÖĞELER ---
  console.log('\n🏢 Departman alt öğeleri...');
  const deptRes = await api(`/api/organizations/${orgId}/departments`);
  const depts = deptRes.items || deptRes || [];
  const techDept = depts.find(d => d.name === 'Teknoloji');
  if (techDept) {
    for (const sub of ['Yazılım Gelistirme', 'DevOps']) {
      await tryCreate(`Alt departman: ${sub}`, async () => {
        return api(`/api/organizations/${orgId}/departments`, { method: 'POST', body: JSON.stringify({
          name: sub, parentDepartmentId: techDept.id
        })});
      });
    }
  }

  // --- ÖZEL ROLLER (ASCII isim) ---
  console.log('\n👤 Roller...');
  await tryCreate('Rol: Developer', async () => {
    return api('/api/auth/roles', { method: 'POST', body: JSON.stringify({
      name: 'Developer', organizationId: orgId,
      permissions: ['WorkItemCreate','WorkItemView','WorkItemEdit','BoardView','ProjectView']
    })});
  });
  await tryCreate('Rol: Observer', async () => {
    return api('/api/auth/roles', { method: 'POST', body: JSON.stringify({
      name: 'Observer', organizationId: orgId,
      permissions: ['WorkItemView','BoardView','ProjectView']
    })});
  });

  console.log('\n🎉 Faz 3 tamamlandı!');
  console.log('💡 http://127.0.0.1:58177/desktop-bulma/index.html');
}

main().catch(err => { console.error('❌ Hata:', err.message); process.exit(1); });
