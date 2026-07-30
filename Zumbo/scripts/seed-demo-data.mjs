#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const envPath = resolve(import.meta.dirname, '../Backend/.env');
const env = Object.fromEntries(readFileSync(envPath, 'utf8')
  .split(/\r?\n/)
  .filter(l => l.trim() && l.includes('='))
  .map(l => {
    const s = l.indexOf('=');
    return [l.slice(0, s).trim(), l.slice(s + 1).trim()];
  }));

const API_BASE = env.ZUMBO_API_URL || 'http://127.0.0.1:58089';
const ADMIN_EMAIL = env.ZUMBO_IDENTITY_ADMIN_EMAIL;
const ADMIN_PASSWORD = process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || 'Deneme12345!';

let cookies = '';
let csrfToken = '';
let adminUserId = '';

async function api(path, options = {}) {
  const url = `${API_BASE}${path}`;
  const headers = {
    'Content-Type': 'application/json',
    'Origin': 'http://127.0.0.1:58177',
    ...options.headers
  };
  if (cookies) headers['Cookie'] = cookies;
  if (csrfToken) headers['X-CSRF-Token'] = csrfToken;

  const res = await fetch(url, { ...options, headers });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const msg = data?.error?.message || res.statusText;
    throw new Error(`API ${res.status} ${path}: ${msg}`);
  }

  // Handle cookies properly using getSetCookie()
  const setCookieHeaders = res.headers.getSetCookie ? res.headers.getSetCookie() : [];
  if (setCookieHeaders.length > 0) {
    const merged = {};
    if (cookies) {
      cookies.split(';').forEach(c => {
        const p = c.trim().split('=');
        if (p[0]) merged[p[0].trim()] = (p[1] || '').trim();
      });
    }
    setCookieHeaders.forEach(sc => {
      const pair = sc.split(';')[0].trim().split('=');
      if (pair[0]) merged[pair[0].trim()] = (pair[1] || '').trim();
    });
    cookies = Object.entries(merged).map(([k, v]) => `${k}=${v}`).join('; ');
  }
  return data?.data !== undefined ? data.data : data;
}

async function login() {
  const result = await api('/api/browser-auth/login', {
    method: 'POST',
    body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASSWORD })
  });
  csrfToken = result.csrfToken || '';
  adminUserId = result.user?.id || '';
  console.log(`✅ Giriş başarılı (kullanıcı: ${result.user?.username}, ID: ${adminUserId})`);
  return result;
}

async function getOrg() {
  return api('/api/organizations/local-dev');
}

async function createTeam(organizationId, name, description, ownerUserId) {
  return api('/api/teams', {
    method: 'POST',
    body: JSON.stringify({ organizationId, name, description, ownerUserId })
  });
}

async function createProject(organizationId, key, name, description, ownerUserId) {
  return api('/api/projects', {
    method: 'POST',
    body: JSON.stringify({ organizationId, key, name, description, ownerUserId, visibility: 'Internal' })
  });
}

async function createBoard(projectId, name, type) {
  return api('/api/boards', {
    method: 'POST',
    body: JSON.stringify({ projectId, name, type })
  });
}

async function createWorkItem(projectId, boardId, type, title, description, priority) {
  const body = {
    projectId,
    boardId,
    type,
    title,
    description,
    priority,
    labels: []
  };
  return api('/api/work-items', {
    method: 'POST',
    body: JSON.stringify(body)
  });
}

async function addComment(workItemId, text) {
  return api(`/api/work-items/${workItemId}/comments`, {
    method: 'POST',
    body: JSON.stringify({ body: text })
  });
}

async function main() {
  console.log('🌱 Zumbo Demo Veri Doldurma');
  console.log(`📌 API: ${API_BASE}`);
  console.log(`👤 Admin: ${ADMIN_EMAIL}\n`);

  await login();
  const orgId = 'local-dev';

  // --- TAKIMLAR ---
  console.log('\n--- Takımlar oluşturuluyor ---');
  const teams = {};
  const teamData = [
    { name: 'Mühendislik Takımı', desc: 'Backend ve frontend geliştirme ekibi' },
    { name: 'Ürün Takımı', desc: 'Ürün yönetimi ve iş analizi ekibi' },
    { name: 'Tasarım Takımı', desc: 'UI/UX ve görsel tasarım ekibi' },
    { name: 'DevOps Takımı', desc: 'CI/CD, altyapı ve izleme ekibi' }
  ];
  for (const t of teamData) {
    try {
      const res = await createTeam(orgId, t.name, t.desc, adminUserId);
      teams[t.name] = res.id;
      console.log(`  ✅ ${t.name}`);
    } catch (e) { console.log(`  ⚠️  ${t.name}: ${e.message}`); }
  }

  // --- PROJELER ---
  console.log('\n--- Projeler oluşturuluyor ---');
  const projects = {};
  const projectData = [
    { key: 'ETC', name: 'E-Ticaret Platformu', desc: 'Kapsamlı e-ticaret sistemi: ürün kataloğu, sepet, ödeme, sipariş yönetimi' },
    { key: 'FIN', name: 'Finans Yönetim Paneli', desc: 'Bütçe takibi, gider raporları ve finansal gösterge paneli' },
    { key: 'DESIGN', name: 'Tasarım Sistemi', desc: 'Yeniden kullanılabilir bileşen kütüphanesi ve dokümantasyon' },
    { key: 'OPS', name: 'Operasyon Otomasyonu', desc: 'İş süreci otomasyonu ve devreye alma boru hatları' }
  ];
  for (const p of projectData) {
    try {
      const res = await createProject(orgId, p.key, p.name, p.desc, adminUserId);
      projects[p.key] = res.id;
      console.log(`  ✅ ${p.name} (${p.key})`);
    } catch (e) { console.log(`  ⚠️  ${p.name}: ${e.message}`); }
  }

  // --- PANOLAR ---
  console.log('\n--- Panolar oluşturuluyor ---');
  const boards = {};
  for (const key of Object.keys(projects)) {
    try {
      const res = await createBoard(projects[key], `${key} Geliştirme Panosu`, 'Kanban');
      boards[key] = res.id;
      console.log(`  ✅ ${key} Geliştirme Panosu`);
    } catch (e) { console.log(`  ⚠️  ${key} Pano: ${e.message}`); }
  }

  // --- İŞ ÖĞELERİ ---
  console.log('\n--- İş öğeleri oluşturuluyor ---');
  const items = [
    { proj: 'ETC', type: 'Epic', title: 'Ödeme Sağlayıcı Entegrasyonu', desc: 'Stripe, PayTR ve iyzico ödeme sağlayıcılarının API entegrasyonu. Webhook doğrulama ve hata yönetimi dahil.', priority: 'High' },
    { proj: 'ETC', type: 'Story', title: 'Ürün Katalog Servisi', desc: 'Ürün listeleme, arama, filtreleme ve kategori yönetimi için REST API servisi.', priority: 'High' },
    { proj: 'ETC', type: 'Task', title: 'Sepet Yönetimi', desc: 'Misafir ve üye sepeti yönetimi. Çoklu satıcı desteği ve stok kontrolü.', priority: 'Medium' },
    { proj: 'ETC', type: 'Bug', title: 'Sipariş Onay E-postası Gelmiyor', desc: 'Sipariş tamamlandığında müşteriye onay e-postası gönderilmiyor. SMTP loglarında hata görülüyor.', priority: 'High' },
    { proj: 'ETC', type: 'Task', title: 'Kupon ve İndirim Motoru', desc: 'Dinamik kupon oluşturma, süre sınırlı kampanyalar ve müşteri segmentasyonu.', priority: 'Low' },

    { proj: 'FIN', type: 'Epic', title: 'Bütçe Planlama Modülü', desc: 'Yıllık ve departman bazlı bütçe oluşturma, onay akışı ve revizyon takibi.', priority: 'High' },
    { proj: 'FIN', type: 'Story', title: 'Gider Raporlama', desc: 'PDF ve Excel çıktılı aylık gider raporları. Grafiksel gösterim ve trend analizi.', priority: 'Medium' },
    { proj: 'FIN', type: 'Task', title: 'Fatura Eşleştirme', desc: 'Gelen faturaların otomatik sipariş eşleştirmesi ve muhasebe entegrasyonu.', priority: 'Medium' },
    { proj: 'FIN', type: 'Bug', title: 'Para Birimi Dönüşüm Hatası', desc: 'Çoklu para biriminde tutarlar yanlış yuvarlanıyor, sente kadar hassasiyet gerekli.', priority: 'High' },

    { proj: 'DESIGN', type: 'Epic', title: 'Bileşen Kütüphanesi Altyapısı', desc: 'AngularJS uyumlu modüler bileşen sistemi. Tema değişkenleri ve dokümantasyon altyapısı.', priority: 'High' },
    { proj: 'DESIGN', type: 'Story', title: 'Form Bileşenleri', desc: 'Input, select, checkbox, radio, date picker ve validasyon göstergeleri.', priority: 'Medium' },
    { proj: 'DESIGN', type: 'Task', title: 'Veri Tablosu Bileşeni', desc: 'Sayfalama, sıralama, filtreleme ve satır içi düzenleme destekli tablo.', priority: 'Medium' },
    { proj: 'DESIGN', type: 'Bug', title: 'Mobil Görünüm Bozuluyor', desc: '320px genişlikte kart bileşenleri taşma yapıyor, responsive düzeltme gerekli.', priority: 'Medium' },

    { proj: 'OPS', type: 'Epic', title: 'CI/CD Boru Hattı Kurulumu', desc: 'GitHub Actions tabanlı otomatik derleme, test ve dağıtım boru hattı.', priority: 'High' },
    { proj: 'OPS', type: 'Story', title: 'İzleme ve Uyarı Sistemi', desc: 'Prometheus metrik toplama, Grafana panoları ve Slack/e-posta uyarıları.', priority: 'High' },
    { proj: 'OPS', type: 'Task', title: 'Yedekleme Otomasyonu', desc: 'Günlük veritabanı yedekleri, retention politikası ve restore testleri.', priority: 'Medium' },
    { proj: 'OPS', type: 'Task', title: 'Güvenlik Taraması', desc: 'Bağımlılık zafiyet taraması, SAST ve konteyner imaj denetimi.', priority: 'Medium' }
  ];

  const created = {};
  for (const item of items) {
    try {
      const res = await createWorkItem(projects[item.proj], boards[item.proj], item.type, item.title, item.desc, item.priority);
      created[item.title] = res.id;
      console.log(`  ✅ [${item.proj}] ${item.title}`);
    } catch (e) { console.log(`  ⚠️  [${item.proj}] ${item.title}: ${e.message}`); }
  }

  // --- YORUMLAR ---
  console.log('\n--- Yorumlar ekleniyor ---');
  const commentTargets = [
    { title: 'Ödeme Sağlayıcı Entegrasyonu', text: 'Stripe sandbox ortamında testler başarılı. Prodüksiyon anahtarı güvenli kasada saklanmalı.' },
    { title: 'Bütçe Planlama Modülü', text: 'Finans departmanından onay akışı gereksinimleri alındı, taslak döküman paylaşıldı.' },
    { title: 'Bileşen Kütüphanesi Altyapısı', text: 'Tema değişkenleri için CSS custom properties yaklaşımı benimsendi. Bulma değişkenleriyle uyumlu.' },
    { title: 'CI/CD Boru Hattı Kurulumu', text: 'Docker multi-stage build doğrulandı. İmaj boyutu 180MB, hedef 150MB altında olmalı.' }
  ];
  for (const ct of commentTargets) {
    if (created[ct.title]) {
      try {
        await addComment(created[ct.title], ct.text);
        console.log(`  ✅ Yorum: ${ct.title}`);
      } catch (e) { console.log(`  ⚠️  Yorum hatası: ${e.message}`); }
    }
  }

  console.log('\n🎉 Demo veri doldurma tamamlandı!');
  console.log('📊 Özet:');
  console.log(`   Takım: ${Object.keys(teams).length}`);
  console.log(`   Proje: ${Object.keys(projects).length}`);
  console.log(`   Pano: ${Object.keys(projects).length}`);
  console.log(`   İş öğesi: ${Object.keys(created).length}`);
  console.log(`   Yorum: ${commentTargets.length}`);
  console.log('\n💡 http://127.0.0.1:58177/desktop-bulma/index.html adresinden giriş yapın.');
}

main().catch(err => {
  console.error('❌ Hata:', err.message);
  process.exit(1);
});