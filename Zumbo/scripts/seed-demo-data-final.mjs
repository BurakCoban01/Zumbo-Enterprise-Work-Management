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

async function tryOp(label, fn) {
  try { const r = await fn(); console.log(`  ✅ ${label}`); return r; }
  catch (e) { console.log(`  ⚠️  ${label}: ${e.message}`); return null; }
}

function dateStr(days) {
  const d = new Date(); d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

async function main() {
  console.log('🌱 Zumbo Demo Veri — Son Düzeltmeler\n');
  const r = await api('/api/browser-auth/login', { method: 'POST', body: JSON.stringify({
    usernameOrEmail: env.ZUMBO_IDENTITY_ADMIN_EMAIL, password: ADMIN_PASSWORD
  })});
  csrfToken = r.csrfToken; adminUserId = r.user.id; orgId = r.user.organizationId;

  const projectList = await api(`/api/projects?organizationId=${orgId}&pageSize=50`);
  const projects = Array.isArray(projectList) ? projectList : (projectList.items || []);
  const teamList = await api(`/api/teams?organizationId=${orgId}&pageSize=50`);
  const teams = Array.isArray(teamList) ? teamList : (teamList.items || []);

  // Mevcut statü isimleri
  const statuses = ['To Do', 'In Progress', 'Blocked', 'Code Review', 'Test', 'Done'];

  for (const project of projects) {
    const pid = project.id;
    const pkey = project.key;
    console.log(`\n📦 ${project.name} (${pkey})`);

    // --- BOARD KOLONLARI ---
    const boardsRes = await api(`/api/boards/by-project/${pid}`);
    const boards = Array.isArray(boardsRes) ? boardsRes : (boardsRes.items || []);
    for (const board of boards) {
      // Kolonların varlığını kontrol et, yoksa oluştur
      const existingCols = board.columns || [];
      if (existingCols.length > 0) {
        console.log(`  ℹ️  ${board.name}: zaten ${existingCols.length} kolon var`);
        continue;
      }
      const colDefs = [
        { name: 'To Do', category: 'Todo', wipLimit: 0, statusNames: ['To Do'] },
        { name: 'In Progress', category: 'InProgress', wipLimit: 5, statusNames: ['In Progress'] },
        { name: 'Blocked', category: 'InProgress', wipLimit: 3, statusNames: ['Blocked'] },
        { name: 'Code Review', category: 'InProgress', wipLimit: 3, statusNames: ['Code Review'] },
        { name: 'Test', category: 'InProgress', wipLimit: 3, statusNames: ['Test'] },
        { name: 'Done', category: 'Done', wipLimit: 0, statusNames: ['Done'] }
      ];
      for (const col of colDefs) {
        await tryOp(`Kolon: ${col.name}`, async () => {
          return api(`/api/boards/${board.id}/columns`, { method: 'POST', body: JSON.stringify(col) });
        });
      }
    }

    // --- İŞ ÖĞELERİ ---
    const wiRes = await api(`/api/work-items?projectId=${pid}&pageSize=100`);
    const items = Array.isArray(wiRes) ? wiRes : (wiRes.items || []);
    if (items.length === 0) { console.log('  (iş öğesi yok)'); continue; }

    // Statüleri çeşitlendir (sadece geçerli geçişler: To Do → In Progress → Code Review → Test → Done)
    const validStatuses = ['To Do', 'In Progress', 'Code Review', 'Test', 'Done'];
    for (let i = 0; i < items.length; i++) {
      const wi = items[i];
      const targetStatus = validStatuses[i % validStatuses.length];
      // Statü güncelle (sadece geçerli geçişler)
      if (wi.status !== targetStatus) {
        let steps = [targetStatus];
        if (wi.status === 'To Do' && ['Code Review', 'Test'].includes(targetStatus)) {
          steps = ['In Progress', targetStatus];
        }
        for (const st of steps) {
          await tryOp(`Statü: ${wi.title} → ${st}`, async () => {
            return api(`/api/work-items/${wi.id}/status`, { method: 'PATCH', body: JSON.stringify({ status: st }) });
          });
        }
      }
      // Takım
      if (teams.length > 0 && !wi.teamId) {
        await tryOp(`Takım: ${wi.title}`, async () => {
          return api(`/api/work-items/${wi.id}/team`, { method: 'PATCH', body: JSON.stringify({ teamId: teams[i % teams.length].id }) });
        });
      }
      // İzle + Oyla
      await tryOp(`İzle: ${wi.title}`, async () => {
        return api(`/api/work-items/${wi.id}/watch`, { method: 'PUT', body: JSON.stringify({ watching: true }) });
      });
      if (i % 2 === 0) {
        await tryOp(`Oy: ${wi.title}`, async () => {
          return api(`/api/work-items/${wi.id}/vote`, { method: 'PUT', body: JSON.stringify({ voted: true }) });
        });
      }
    }

    // Parent-child (Epic'leri bul, child'ları bağla)
    const epics = items.filter(wi => wi.type === 'Epic');
    const nonEpics = items.filter(wi => wi.type !== 'Epic');
    if (epics.length > 0 && nonEpics.length > 0) {
      for (let i = 0; i < Math.min(nonEpics.length, 3); i++) {
        const child = nonEpics[i];
        const parent = epics[i % epics.length];
        await tryOp(`Parent: ${child.title}`, async () => {
          return api(`/api/work-items/${child.id}/parent`, { method: 'PATCH', body: JSON.stringify({ parentId: parent.id }) });
        });
      }
    }

    // --- SPRINT ---
    let s1 = null;
    await tryOp('Sprint 1 (Aktif)', async () => {
      s1 = await api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 1`, goal: 'Çekirdek özelliklerin teslimi',
        startDate: dateStr(-7), endDate: dateStr(7)
      })});
      return s1;
    });
    if (s1) {
      // Önce planla, SONRA başlat
      for (let i = 0; i < Math.min(items.length, 5); i++) {
        await tryOp(`Planla: ${items[i].title}`, async () => api(`/api/sprints/${s1.id}/items/${items[i].id}`, { method: 'PUT', body: '{}' }));
      }
      await tryOp('Sprint başlat', async () => api(`/api/sprints/${s1.id}/start`, { method: 'POST', body: '{}' }));
    }
    await tryOp('Sprint 2 (Planlı)', async () => {
      return api('/api/sprints', { method: 'POST', body: JSON.stringify({
        projectId: pid, name: `${pkey} Sprint 2`, goal: 'İyileştirme ve optimizasyon',
        startDate: dateStr(8), endDate: dateStr(22)
      })});
    });

    // --- ONAY ---
    const reviewItem = items.find(wi => wi.status === 'Code Review' || wi.status === 'Test');
    if (reviewItem) {
      await tryOp(`Onay: ${reviewItem.title}`, async () => {
        return api(`/api/work-items/${reviewItem.id}/approvals`, { method: 'POST', body: JSON.stringify({
          approverUserId: adminUserId, note: 'İnceleme için onay talebi'
        })});
      });
    }

    // --- YAYIN ---
    // Versiyonları getir (try/catch ile, endpoint olmayabilir)
    let versions = [];
    try {
      const versionsRes = await api(`/api/projects/${pid}/versions`);
      versions = Array.isArray(versionsRes) ? versionsRes : (versionsRes.items || []);
    } catch (e) { /* endpoint yoksa atla */ }
    if (versions.length > 0) {
      await tryOp('Yayın', async () => {
        return api(`/api/projects/${pid}/releases`, { method: 'POST', body: JSON.stringify({
          versionId: versions[0].id, name: 'İlk Kararlı Sürüm', scheduledAt: new Date().toISOString()
        })});
      });
    }

    // --- İŞ OTOMASYONU ---
    if (boards.length > 0) {
      let tpl = null;
      await tryOp('İş Şablonu', async () => {
        tpl = await api('/api/work-items/templates', { method: 'POST', body: JSON.stringify({
          projectId: pid, boardId: boards[0].id, name: 'Haftalık Durum Raporu',
          title: 'Haftalık Proje Durum Raporu', type: 'Task', priority: 'Low',
          description: 'Her hafta proje durumunu özetleyen rapor hazırlanmalı.',
          dueAfterDays: 7, labels: ['rapor', 'rutin']
        })});
        return tpl;
      });
      if (tpl) {
        await tryOp('Yineleme', async () => {
          return api('/api/work-items/recurrences', { method: 'POST', body: JSON.stringify({
            projectId: pid, templateId: tpl.id, frequency: 'Weekly', interval: 1,
            startAtUtc: new Date(Date.now() + 86400000).toISOString(),
            endAtUtc: new Date(Date.now() + 90*86400000).toISOString(),
            maxOccurrences: 12
          })});
        });
      }
    }
  }

  console.log('\n🎉 Son düzeltmeler tamamlandı!');
}

main().catch(err => { console.error('❌ Hata:', err.message); process.exit(1); });
