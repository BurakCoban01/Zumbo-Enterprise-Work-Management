#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
const ADMIN_EMAIL = env.ZUMBO_IDENTITY_ADMIN_EMAIL;
const ADMIN_PASS = process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || 'Deneme12345!';
const ORIGIN = 'http://127.0.0.1:58177';
const ORG = 'local-dev';

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

async function tryOp(label, fn) {
  try { await fn(); process.stdout.write(`  ✅ ${label}\n`); return true; }
  catch(e) { return false; }
}

function days(n) { const d = new Date(); d.setDate(d.getDate()+n); return d.toISOString(); }

async function main() {
  console.log('🌐 Zumbo Power-User Faz 5 — Tüm Detaylar\n');

  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASS }) });
  csrf = lr.csrfToken; adminId = lr.user.id;
  console.log(`✅ Admin: ${lr.user.username}\n`);

  // === KULLANICILARI AL ===
  const usersRes = await call(`/api/auth/users?organizationId=${ORG}&pageSize=50`);
  const users = Array.isArray(usersRes) ? usersRes : (usersRes.items || []);
  console.log(`👤 ${users.length} kullanıcı bulundu`);
  const userIds = users.map(u => u.id).filter(id => id !== adminId);

  // === DEPARTMAN ÜYELERİ + POZİSYONLAR ===
  console.log('\n🏢 Departman üyeleri ve pozisyonlar...');
  // Departmanları yeniden oluştur (ID'leri yakalamak için)
  const deptDefs = [
    { name: 'Teknoloji' },
    { name: 'Ürün Yönetimi' },
    { name: 'Operasyonlar' },
    { name: 'Mühendislik', parent: 'Teknoloji' },
    { name: 'Kalite Güvence', parent: 'Teknoloji' }
  ];
  const deptMap = {};
  for (const d of deptDefs) {
    await tryOp(`Departman: ${d.name}`, async () => {
      const body = { name: d.name };
      if (d.parent && deptMap[d.parent]) body.parentDepartmentId = deptMap[d.parent];
      const r = await call(`/api/organizations/${ORG}/departments`, { method:'POST', body: JSON.stringify(body) });
      if (r && r.id) deptMap[d.name] = r.id;
    });
  }

  const positions = [
    { dept: 'Teknoloji', title: 'Teknoloji Direktörü' },
    { dept: 'Teknoloji', title: 'Yazılım Mimarı' },
    { dept: 'Mühendislik', title: 'Senior Backend Geliştirici' },
    { dept: 'Mühendislik', title: 'Frontend Geliştirici' },
    { dept: 'Mühendislik', title: 'Full Stack Geliştirici' },
    { dept: 'Ürün Yönetimi', title: 'Ürün Yöneticisi' },
    { dept: 'Ürün Yönetimi', title: 'İş Analisti' },
    { dept: 'Operasyonlar', title: 'Operasyon Sorumlusu' },
    { dept: 'Kalite Güvence', title: 'QA Mühendisi' },
    { dept: 'Kalite Güvence', title: 'Test Otomasyon Uzmanı' }
  ];

  for (let i = 0; i < positions.length && i < userIds.length; i++) {
    const pos = positions[i];
    const uid = userIds[i];
    const deptId = deptMap[pos.dept];
    if (deptId) {
      await tryOp(`Üye+Pozisyon: ${pos.dept}/${pos.title}`, async () => {
        await call(`/api/organizations/${ORG}/departments/${deptId}/members`, {
          method:'POST', body: JSON.stringify({ userId: uid, position: pos.title })
        });
      });
    }
  }

  // === ROLLERİ KULLANICILARA ATA ===
  console.log('\n🎭 Rol atamaları...');
  const rolesRes = await call(`/api/auth/roles?organizationId=${ORG}`);
  const roles = Array.isArray(rolesRes) ? rolesRes : (rolesRes.items || []);
  const devRole = roles.find(r => r.name === 'Developer');
  const obsRole = roles.find(r => r.name === 'Observer');

  if (devRole) {
    for (let i = 0; i < Math.min(userIds.length, 5); i++) {
      await tryOp(`Developer rolü: ${userIds[i].slice(0,8)}`, async () => {
        await call(`/api/auth/users/${userIds[i]}/roles`, {
          method:'PUT', body: JSON.stringify({ roleIds: [devRole.id] })
        });
      });
    }
  }
  if (obsRole) {
    for (let i = 5; i < userIds.length; i++) {
      await tryOp(`Observer rolü: ${userIds[i].slice(0,8)}`, async () => {
        await call(`/api/auth/users/${userIds[i]}/roles`, {
          method:'PUT', body: JSON.stringify({ roleIds: [obsRole.id] })
        });
      });
    }
  }

  // === İŞ ÖĞELERİNİN TÜM ALANLARINI DOLDUR ===
  console.log('\n📋 İş öğeleri tam detaylandırılıyor...');
  const projects = await call(`/api/projects?organizationId=${ORG}&pageSize=50`);
  const projectList = Array.isArray(projects) ? projects : (projects.items || []);

  const estimateByType = { Epic: 21, Story: 8, Task: 5, Bug: 3, Subtask: 2 };

  for (const proj of projectList) {
    const wiRes = await call(`/api/work-items?projectId=${proj.id}&pageSize=200`);
    const items = Array.isArray(wiRes) ? wiRes : (wiRes.items || []);

    for (const wi of items) {
      const estimate = estimateByType[wi.type] || 5;

      // Planning (estimate)
      await tryOp(`Estimate: ${wi.title?.slice(0,30)}`, async () => {
        await call(`/api/work-items/${wi.id}/planning`, {
          method:'PATCH', body: JSON.stringify({ estimatePoints: estimate })
        });
      });

      // Custom fields (her tip için farklı)
      const customFields = [
        { fieldKey: 'kabul_kriterleri', textValue: 'Görev tamamlandı ve test edildi\nKod incelemesi onaylandı\nDokümantasyon güncellendi' },
        { fieldKey: 'teknik_notlar', textValue: 'Backend: .NET 8, Frontend: AngularJS\nVeritabanı: MongoDB\nÖnbellek: Redis' },
        { fieldKey: 'musteri_etkisi', optionKey: wi.type === 'Bug' ? 'Yuksek' : 'Orta' },
        { fieldKey: 'test_onceligi', optionKey: wi.priority === 'High' ? 'Kritik' : 'Normal' }
      ];
      await tryOp(`Özel alanlar: ${wi.title?.slice(0,30)}`, async () => {
        await call(`/api/work-items/${wi.id}/custom-fields`, {
          method:'PUT', body: JSON.stringify({ values: customFields })
        });
      });

      // Ek yorum (tartışma)
      const discussions = [
        '@ahmetyilmaz bu görevi inceleyebilir misin? Teknik bir engel var.',
        'Risk değerlendirmesi yapıldı. Düşük riskli, mevcut mimariyle uyumlu.',
        'Müşteri bu özelliği acil istiyor, teslimat tarihi netleşmeli.',
        'Bağımlılıklar kontrol edildi, engel yok. Başlayabiliriz.',
        'Performans testi gerekli, büyük veri setlerinde davranışı belirsiz.',
        'Tasarım ekibinden onay bekleniyor, mock\'lar hazır.'
      ];
      await tryOp(`Tartışma: ${wi.title?.slice(0,30)}`, async () => {
        await call(`/api/work-items/${wi.id}/comments`, {
          method:'POST', body: JSON.stringify({ body: discussions[Math.floor(Math.random()*discussions.length)] })
        });
      });

      // Ek iş günlüğü
      const wlHours = [1, 2, 3, 4, 6, 8];
      const wlNotes = [
        'Kod yazımı ve debug',
        'Birim testleri yazıldı',
        'Entegrasyon testi',
        'Dokümantasyon',
        'Code review geri bildirimleri uygulandı'
      ];
      const wlUser = userIds[Math.floor(Math.random()*userIds.length)] || adminId;
      await tryOp(`İş günlüğü+: ${wi.title?.slice(0,30)}`, async () => {
        await call(`/api/work-items/${wi.id}/worklogs`, {
          method:'POST', body: JSON.stringify({
            userId: wlUser,
            hours: wlHours[Math.floor(Math.random()*wlHours.length)],
            note: wlNotes[Math.floor(Math.random()*wlNotes.length)]
          })
        });
      });

      // İkinci etiket seti
      const extraLabels = ['dokümantasyon', 'refactor', 'performans', 'güvenlik', 'ux', 'api'];
      const extraLabel = extraLabels[Math.floor(Math.random()*extraLabels.length)];
      await tryOp(`Etiket+: ${extraLabel}`, async () => {
        await call(`/api/work-items/${wi.id}/labels`, {
          method:'POST', body: JSON.stringify({ label: extraLabel })
        });
      });
    }
  }

  // === İŞ ÖĞELERİNİ KULLANICILARA DAĞIT ===
  console.log('\n🎯 İş öğeleri kullanıcılara dağıtılıyor...');
  for (const proj of projectList) {
    const wiRes = await call(`/api/work-items?projectId=${proj.id}&pageSize=200`);
    const items = Array.isArray(wiRes) ? wiRes : (wiRes.items || []);
    for (let i = 0; i < items.length; i++) {
      const wi = items[i];
      const assignee = userIds[i % userIds.length];
      if (assignee && assignee !== wi.assigneeUserId) {
        await tryOp(`Ata: ${wi.title?.slice(0,25)}`, async () => {
          await call(`/api/work-items/${wi.id}/assignee`, {
            method:'PATCH', body: JSON.stringify({ assigneeUserId: assignee })
          });
        });
      }
    }
  }

  // === WEBHOOK ENTEGRASYONU ===
  console.log('\n🔔 Webhook entegrasyonu...');
  await tryOp('Webhook: Slack bildirim', async () => {
    await call('/api/integrations/webhooks', {
      method:'POST', body: JSON.stringify({
        name: 'Slack Bildirim Kanalı',
        url: 'https://hooks.slack.com/services/demo/local/zumbo',
        events: ['WorkItemCreated', 'WorkItemStatusChanged', 'SprintCompleted'],
        isActive: true
      })
    });
  });

  // === BİLDİRİM TERCİHLERİ (her kullanıcı için) ===
  console.log('\n📬 Bildirim tercihleri...');
  // Admin için zaten ayarlandı, kullanıcılar kendi tercihleridir

  console.log('\n🎉 Faz 5 tamamlandı!');
  console.log('\n📊 Nihai veri seti hazır.');
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
