#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/).filter(l => l.trim() && l.includes('='))
  .map(l => { const s = l.indexOf('='); return [l.slice(0, s).trim(), l.slice(s + 1).trim()]; }));

const API = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
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
  try { await fn(); return true; }
  catch(e) { return false; }
}

async function main() {
  console.log('🔧 Custom field şemaları ve değerleri\n');

  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({
    usernameOrEmail: env.ZUMBO_IDENTITY_ADMIN_EMAIL,
    password: process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || 'Deneme12345!'
  })});
  csrf = lr.csrfToken; adminId = lr.user.id;

  const projects = await call(`/api/projects?organizationId=${ORG}&pageSize=50`);
  const projectList = Array.isArray(projects) ? projects : (projects.items || []);

  // Her proje için custom field şeması tanımla
  for (const proj of projectList) {
    if (!['ETC','FIN','DESIGN','OPS'].includes(proj.key)) continue;

    process.stdout.write(`\n📦 ${proj.key}: `);

    // Şema tanımla
    const defined = await tryOp('şema', async () => {
      await call(`/api/work-item-schemas/${proj.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          customFields: [
            { key: 'kabul_kriterleri', name: 'Kabul Kriterleri', type: 'Text', required: false, position: 1 },
            { key: 'teknik_notlar', name: 'Teknik Notlar', type: 'Text', required: false, position: 2 },
            { key: 'musteri_etkisi', name: 'Müşteri Etkisi', type: 'Option', required: false, position: 3,
              options: ['Dusuk', 'Orta', 'Yuksek', 'Kritik'] },
            { key: 'test_onceligi', name: 'Test Önceliği', type: 'Option', required: false, position: 4,
              options: ['Normal', 'Onemli', 'Kritik'] },
            { key: 'hedef_tarih', name: 'Hedef Tarih', type: 'Date', required: false, position: 5 }
          ],
          issueTypes: [
            { key: 'Epic', name: 'Epic', description: 'Büyük kapsamlı iş', hierarchyLevel: '1', active: true, position: 1 },
            { key: 'Story', name: 'Story', description: 'Kullanıcı hikayesi', hierarchyLevel: '2', active: true, position: 2 },
            { key: 'Task', name: 'Task', description: 'Teknik görev', hierarchyLevel: '2', active: true, position: 3 },
            { key: 'Bug', name: 'Bug', description: 'Hata bildirimi', hierarchyLevel: '2', active: true, position: 4 },
            { key: 'Subtask', name: 'Subtask', description: 'Alt görev', hierarchyLevel: '3', active: true, position: 5 }
          ]
        })
      });
    });
    if (defined) process.stdout.write('✅ '); else process.stdout.write('⚠️ ');

    // İş öğelerine custom field değerleri ekle
    const wiRes = await call(`/api/work-items?projectId=${proj.id}&pageSize=200`);
    const items = Array.isArray(wiRes) ? wiRes : (wiRes.items || []);

    for (const wi of items) {
      const impact = wi.type === 'Bug' ? 'Yuksek' : (wi.priority === 'High' ? 'Yuksek' : 'Orta');
      const testPri = wi.priority === 'High' ? 'Kritik' : 'Normal';
      const targetDate = new Date();
      targetDate.setDate(targetDate.getDate() + Math.floor(Math.random() * 30) + 7);

      await tryOp('', async () => {
        await call(`/api/work-items/${wi.id}/custom-fields`, {
          method: 'PUT',
          body: JSON.stringify({
            values: [
              { fieldKey: 'kabul_kriterleri', textValue: 'Görev tamamlandı ve test edildi\nKod incelemesi onaylandı\nDokümantasyon güncellendi' },
              { fieldKey: 'musteri_etkisi', optionKey: impact },
              { fieldKey: 'test_onceligi', optionKey: testPri },
              { fieldKey: 'hedef_tarih', dateValue: targetDate.toISOString().slice(0,10) }
            ]
          })
        });
      });
    }
    process.stdout.write(`${items.length} öğe\n`);
  }

  console.log('\n✅ Custom fields tamamlandı!');
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
