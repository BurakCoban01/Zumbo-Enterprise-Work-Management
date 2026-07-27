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
  try { await fn(); process.stdout.write(`✅ ${label}  `); return true; }
  catch(e) { process.stdout.write(`⚠️ ${label}  `); return false; }
}

async function main() {
  console.log('🏢 Departman üyeleri + pozisyonlar\n');

  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASS }) });
  csrf = lr.csrfToken; adminId = lr.user.id;

  // Kullanıcıları al
  const usersRes = await call(`/api/auth/users?search=`);
  const users = Array.isArray(usersRes) ? usersRes : (usersRes.items || []);
  const userIds = users.map(u => u.id).filter(id => id !== adminId);
  console.log(`👤 ${users.length} kullanıcı\n`);

  // Departman ID'leri (MongoDB'den doğruladık)
  const depts = {
    'Teknoloji': '7931ba33f63b42a9a7c22672cc371652',
    'Ürün Yönetimi': '469b2e1102324b9f93e867cf258f24f1',
    'Operasyonlar': 'df7ac8b21b434a57b08313d224a1f2b3',
    'Mühendislik': '4ed445cd25104f399b88d39aa7d8978f',
    'Kalite Güvence': '6ff1e18a6d8b4c8488bfb6e8f9eb6cae'
  };

  // Pozisyon atamaları (kullanıcı → departman → pozisyon)
  const assignments = [
    { uid: 0, dept: 'Teknoloji', pos: 'Teknoloji Direktörü' },
    { uid: 1, dept: 'Mühendislik', pos: 'Senior Backend Geliştirici' },
    { uid: 2, dept: 'Mühendislik', pos: 'Frontend Geliştirici' },
    { uid: 3, dept: 'Ürün Yönetimi', pos: 'Ürün Yöneticisi' },
    { uid: 4, dept: 'Kalite Güvence', pos: 'QA Mühendisi' },
    { uid: 5, dept: 'Ürün Yönetimi', pos: 'İş Analisti' },
    { uid: 6, dept: 'Mühendislik', pos: 'Full Stack Geliştirici' },
    { uid: 7, dept: 'Kalite Güvence', pos: 'Test Otomasyon Uzmanı' }
  ];

  for (const a of assignments) {
    const uid = userIds[a.uid];
    const deptId = depts[a.dept];
    if (uid && deptId) {
      await tryOp(`${a.dept}/${a.pos}`, async () => {
        await call(`/api/organizations/${ORG}/departments/${deptId}/members`, {
          method:'POST', body: JSON.stringify({ userId: uid, position: a.pos })
        });
      });
    }
  }

  // Roller
  console.log('\n\n🎭 Rol atamaları');
  const rolesRes = await call(`/api/auth/roles?organizationId=${ORG}`);
  const roles = Array.isArray(rolesRes) ? rolesRes : (rolesRes.items || []);
  const devRole = roles.find(r => r.name === 'Developer');
  const obsRole = roles.find(r => r.name === 'Observer');

  if (devRole) {
    for (let i = 0; i < Math.min(5, userIds.length); i++) {
      await tryOp(`Developer: ${userIds[i].slice(0,8)}`, async () => {
        await call(`/api/auth/users/${userIds[i]}/roles`, {
          method:'PUT', body: JSON.stringify({ roleIds: [devRole.id] })
        });
      });
    }
  }
  if (obsRole) {
    for (let i = 5; i < userIds.length; i++) {
      await tryOp(`Observer: ${userIds[i].slice(0,8)}`, async () => {
        await call(`/api/auth/users/${userIds[i]}/roles`, {
          method:'PUT', body: JSON.stringify({ roleIds: [obsRole.id] })
        });
      });
    }
  }

  console.log('\n\n✅ Tamamlandı!');
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
