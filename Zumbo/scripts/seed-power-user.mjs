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

let cookies = '', csrf = '', adminId = '', orgId = '';

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
  try { const r = await fn(); return r; }
  catch(e) { return null; }
}

function days(n) { const d = new Date(); d.setDate(d.getDate()+n); return d.toISOString(); }
function dateOnly(n) { const d = new Date(); d.setDate(d.getDate()+n); return d.toISOString().slice(0,10); }

// =====================================================
async function main() {
  console.log('🚀 Zumbo Power-User Veri Doldurma\n');

  // LOGIN
  const lr = await call('/api/browser-auth/login', { method:'POST', body: JSON.stringify({ usernameOrEmail: ADMIN_EMAIL, password: ADMIN_PASS }) });
  csrf = lr.csrfToken; adminId = lr.user.id; orgId = lr.user.organizationId;
  console.log(`✅ Admin: ${lr.user.username}\n`);

  // === KULLANICILAR OLUŞTUR ===
  console.log('👥 Kullanıcılar oluşturuluyor...');
  const teams = await call(`/api/teams?organizationId=${orgId}&pageSize=50`);
  const teamList = Array.isArray(teams) ? teams : (teams.items||[]);

  const users = [
    { name: 'ahmetyilmaz', email: 'ahmet.yilmaz@zumbo.local', pass: 'ZumboDev2026!', display: 'Ahmet Yılmaz', team: 0, role: 'Member' },
    { name: 'mehmetdemir', email: 'mehmet.demir@zumbo.local', pass: 'ZumboDev2026!', display: 'Mehmet Demir', team: 0, role: 'Member' },
    { name: 'aysekaya', email: 'ayse.kaya@zumbo.local', pass: 'ZumboDev2026!', display: 'Ayşe Kaya', team: 2, role: 'Member' },
    { name: 'fatmayilmaz', email: 'fatma.yilmaz@zumbo.local', pass: 'ZumboDev2026!', display: 'Fatma Yılmaz', team: 1, role: 'Member' },
    { name: 'mustafacelep', email: 'mustafa.celep@zumbo.local', pass: 'ZumboDev2026!', display: 'Mustafa Çelep', team: 3, role: 'Member' },
    { name: 'zeyneparslan', email: 'zeynep.arslan@zumbo.local', pass: 'ZumboDev2026!', display: 'Zeynep Arslan', team: 2, role: 'Member' },
    { name: 'emreozturk', email: 'emre.ozturk@zumbo.local', pass: 'ZumboDev2026!', display: 'Emre Öztürk', team: 0, role: 'Member' },
    { name: 'selinacar', email: 'selin.acar@zumbo.local', pass: 'ZumboDev2026!', display: 'Selin Acar', team: 1, role: 'Member' }
  ];

  const userMap = {};
  userMap[adminId] = { display: 'Local Admin', email: ADMIN_EMAIL };

  for (const u of users) {
    const team = teamList[u.team];
    if (!team) continue;

    // Davet oluştur
    const invite = await tryOp(`Davet: ${u.display}`, async () => {
      return call(`/api/teams/${team.id}/members`, { method:'POST', body: JSON.stringify({ email: u.email, role: u.role }) });
    });

    if (invite && invite.invitationToken) {
      // Kullanıcıyı kaydet
      const reg = await tryOp(`Kayıt: ${u.display}`, async () => {
        // Bu çağrı admin cookie'sini kullanmaz - yeni kullanıcı olarak
        const rr = await fetch(`${API}/api/browser-auth/register`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Origin: ORIGIN },
          body: JSON.stringify({ username: u.name, email: u.email, password: u.pass, organizationId: orgId, bootstrapToken: invite.invitationToken })
        });
        const rd = await rr.json();
        if (!rr.ok) throw new Error(rd?.error?.message || 'Register failed');
        return rd.data;
      });

      if (reg && reg.user) {
        userMap[reg.user.id] = { display: u.display, email: u.email };
        console.log(`  ✅ ${u.display} (${u.email})`);
      }
    } else {
      console.log(`  ⚠️  ${u.display}: davet alınamadı`);
    }
  }

  const userIds = Object.keys(userMap).filter(id => id !== adminId);
  console.log(`\n📊 Toplam kullanıcı: ${Object.keys(userMap).length}\n`);

  // === PROJELER VE PANOLAR ===
  const projects = await call(`/api/projects?organizationId=${orgId}&pageSize=50`);
  const projectList = Array.isArray(projects) ? projects : (projects.items||[]);
  const mainProjects = projectList.filter(p => ['ETC','FIN','DESIGN','OPS'].includes(p.key));

  // Her projeye üye ekle
  console.log('🔗 Proje üyeleri ekleniyor...');
  for (const proj of mainProjects) {
    for (let i = 0; i < userIds.length; i++) {
      const uid = userIds[i];
      const roles = ['Developer', 'Developer', 'Viewer', 'ProjectAdmin'];
      await tryOp(`Üye: ${userMap[uid].display} → ${proj.key}`, async () => {
        return call(`/api/projects/${proj.id}/members`, { method:'POST', body: JSON.stringify({ userId: uid, role: roles[i % roles.length] }) });
      });
    }
  }

  // === ÇOK SAYIDA İŞ ÖĞESİ OLUŞTUR ===
  console.log('\n📝 İş öğeleri oluşturuluyor...');

  const workItemTemplates = {
    ETC: [
      { type:'Epic', title:'Müşteri Hesap Yönetimi', desc:'Kullanıcı kayıt, giriş, profil yönetimi, şifre sıfırlama ve hesap doğrulama akışlarının tamamı.', priority:'High' },
      { type:'Epic', title:'Ürün Arama ve Filtreleme', desc:'Elasticsearch tabanlı gelişmiş arama, фасет filtreleme ve otomatik tamamlama.', priority:'High' },
      { type:'Story', title:'Kullanıcı giriş ekranı tasarımı', desc:'Modern, responsive giriş ekranı. Sosyal medya login butonları ve "beni hatırla" özelliği.', priority:'Medium' },
      { type:'Story', title:'Şifre sıfırlama akışı', desc:'E-posta tabanlı güvenli şifre sıfırlma. Token süresi 30 dakika, tek kullanımlık.', priority:'High' },
      { type:'Story', title:'Ürün değerlendirme sistemi', desc:'5 yıldızlı puanlama, yorum yazma, fotoğraf ekleme ve moderasyon.', priority:'Medium' },
      { type:'Task', title:'JWT token yenileme mantığı', desc:'Access token 15dk, refresh token 7 gün. Silent refresh mekanizması.', priority:'High' },
      { type:'Task', title:'E-posta şablonları hazırlanması', desc:'Hoş geldiniz, sipariş onayı, kargo bildirimi, şifre sıfırlama şablonları.', priority:'Medium' },
      { type:'Task', title:'Ürün resim optimizasyonu', desc:'WebP formatına dönüştürme, lazy loading ve thumbnail oluşturma.', priority:'Medium' },
      { type:'Bug', title:'Mobilde sepetten ürün silinmiyor', desc:'iOS Safari\'de swipe-to-delete çalışmıyor. Touch event\'i yakalanmıyor.', priority:'High' },
      { type:'Bug', title:'Arama sonuçları yavaş geliyor', desc:'2+ sanide yanıt süresi. Elasticsearch index yeniden yapılandırılmalı.', priority:'High' },
      { type:'Bug', title:'Kupon kodu büyük/küçük harf duyarlı', desc:'Kupon kodları case-sensitive olmamalı. Kullanıcılar küçük harfle girince çalışmıyor.', priority:'Medium' },
      { type:'Task', title:'Google Analytics entegrasyonu', desc:'E-ticaret event tracking: view_item, add_to_cart, begin_checkout, purchase.', priority:'Low' },
      { type:'Subtask', title:'Veritabanı index optimizasyonu', desc:'products tablosunda composite index eklenecek.', priority:'Medium' }
    ],
    FIN: [
      { type:'Epic', title:'Gerçek Zamanlı Finans Tablosu', desc:'WebSocket tabanlı canlı borsa ve döviz kuru göstergeleri.', priority:'High' },
      { type:'Epic', title:'Otomatik Mutabakat Sistemi', desc:'Banka ekstresi içe aktarma ve otomatik eşleştirme süreci.', priority:'High' },
      { type:'Story', title:'Bütçe vs Gerçekleşen karşılaştırma', desc:'Departman bazında bütçe ve gerçekleşen harcama yan yana karşılaştırma.', priority:'Medium' },
      { type:'Story', title:'Çoklu para birimi desteği', desc:'TRY, USD, EUR, GBP. Günlük kur otomatik güncelleme.', priority:'Medium' },
      { type:'Task', title:'PDF rapor şablonu tasarımı', desc:'Kurumsal kimliğe uygun, grafikli aylık finans raporu.', priority:'Medium' },
      { type:'Task', title:'Excel export fonksiyonu', desc:'Tüm finansal tablolar Excel\'e aktarılabilir olmalı.', priority:'Low' },
      { type:'Task', title:'Vergi hesaplama modülü', desc:'KDV, gelir vergisi ve stopaj hesaplama.', priority:'Medium' },
      { type:'Bug', title:'Yıl başı tarihlerinde rapor hatası', desc:'31 Aralık - 1 Ocak arası işlemler yanlış kategoriye düşüyor.', priority:'High' },
      { type:'Bug', title:'Ondalık sayı yuvarlama sorunu', desc:'Float precision hatası. Decimal tipine geçiş yapılmalı.', priority:'Medium' },
      { type:'Task', title:'Banka API entegrasyonu', desc:'Garanti, İş Bankası ve Akbank API\'leri için adapter pattern.', priority:'High' }
    ],
    DESIGN: [
      { type:'Epic', title:'Tema ve Marka Sistemi', desc:'Açık/koyu tema, marka renkleri, tipografi skalası ve spacing sistemi.', priority:'High' },
      { type:'Story', title:'Modal/Dialog bileşeni', desc:'Erişilebilir, keyboard navigasyonlu, focus trap\'li modal sistemi.', priority:'Medium' },
      { type:'Story', title:'Toast/bildirim bileşeni', desc:'Success, error, warning, info varyasyonları. Auto-dismiss ve Manuel kapatma.', priority:'Medium' },
      { type:'Story', title:'Breadcrumbs bileşeni', desc:'SEO uyumlu, yapılandırılmış veri destekli navigasyon breadcrumbs.', priority:'Low' },
      { type:'Task', title:'Renk kontrast denetimi', desc:'WCAG AA standartlarına uygun kontrast oranı kontrolü.', priority:'Medium' },
      { type:'Task', title:'Icon set genişletme', desc:'50+ yeni Lucide icon eklenmesi ve kategorilere ayrılması.', priority:'Low' },
      { type:'Bug', title:'Dropdown mobilde kapanmıyor', desc:'Dışarı tıklayınca kapanmıyor, backdrop eksik.', priority:'Medium' },
      { type:'Bug', title:'Tab sıralama animasyonu takılıyor', desc:'CSS transition\'da jitter var. transform: translate3d denenebilir.', priority:'Low' }
    ],
    OPS: [
      { type:'Epic', title:'Multi-Region Deployment', desc:'Avrupa ve Asya data center\'larında aktif-aktif deployment mimarisi.', priority:'High' },
      { type:'Epic', title:'Log Toplama ve Analiz', desc:'ELK stack tabanlı merkezi log yönetimi ve anomali tespiti.', priority:'High' },
      { type:'Story', title:'Blue-Green deployment stratejisi', desc:'Sıfır kesintili deployment için blue-green veya canary release.', priority:'High' },
      { type:'Story', title:'Otomatik ölçeklendirme kuralları', desc:'CPU > %70 ise scale out, < %30 ise scale in.', priority:'Medium' },
      { type:'Task', title:'Terraform modülleri yazımı', desc:'AWS/GCP altyapı kodu modular Terraform ile.', priority:'High' },
      { type:'Task', title:'Secret yönetimi migration', desc:'Environment variable\'lardan HashiCorp Vault\'a geçiş.', priority:'High' },
      { type:'Task', title:'Database connection pooling', desc:'PgBouncer konfigürasyonu ve connection limit tuning.', priority:'Medium' },
      { type:'Bug', title:'Memory leak production\'da', desc:'API container\'ları 48 saat sonra OOM ile crash veriyor.', priority:'High' },
      { type:'Bug', title:'Cron job çakışıyor', desc:'Aynı anda iki instance job\'u çalıştırıyor. Distributed lock gerekli.', priority:'Medium' },
      { type:'Task', title:'SSL sertifika otomasyonu', desc:'Let\'s Encrypt + cert-manager ile otomatik yenileme.', priority:'Low' }
    ]
  };

  const allUserIds = Object.keys(userMap);
  const allWorkItems = [];

  for (const proj of mainProjects) {
    const templates = workItemTemplates[proj.key] || [];
    const boards = await call(`/api/boards/by-project/${proj.id}`);
    const boardList = Array.isArray(boards) ? boards : (boards.items||[]);
    const boardId = boardList[0]?.id;

    for (const t of templates) {
      const assigneeIdx = Math.floor(Math.random() * allUserIds.length);
      const wi = await tryOp(`İş: [${proj.key}] ${t.title}`, async () => {
        return call('/api/work-items', { method:'POST', body: JSON.stringify({
          projectId: proj.id, boardId, type: t.type, title: t.title,
          description: t.desc, priority: t.priority,
          assigneeUserId: allUserIds[assigneeIdx],
          labels: t.type === 'Bug' ? ['bug','acil'] : (t.priority === 'High' ? ['önemli'] : [])
        })});
      });

      if (wi) {
        allWorkItems.push({ ...wi, projectId: proj.id, projectKey: proj.key });

        // Statü çeşitlendir
        const statusFlow = [
          ['To Do'],
          ['In Progress'],
          ['In Progress', 'Code Review'],
          ['In Progress', 'Code Review', 'Test'],
          ['In Progress', 'Code Review', 'Test', 'Done'],
          ['To Do'],
          ['In Progress']
        ];
        const flowIdx = allWorkItems.length % statusFlow.length;
        for (const st of statusFlow[flowIdx]) {
          await tryOp(`Statü: ${t.title} → ${st}`, async () => {
            return call(`/api/work-items/${wi.id}/status`, { method:'PATCH', body: JSON.stringify({ status: st }) });
          });
        }

        // Bitiş tarihi
        const dueOffset = [-2, 1, 3, 7, 14, 21, 30, 45][allWorkItems.length % 8];
        await tryOp(`Tarih: ${t.title}`, async () => {
          return call(`/api/work-items/${wi.id}`, { method:'PUT', body: JSON.stringify({
            title: t.title, description: t.desc, priority: t.priority, dueDate: days(dueOffset)
          })});
        });

        // Yorum
        const commentTemplates = [
          'Bu görev üzerinde çalışmaya başladım. İlk taslak yakında.',
          'Teknik yaklaşım netleşti, implementasyona geçiyorum.',
          'Code review için hazır, inceleyebilir misiniz?',
          'Test ortamında doğrulandı, canlıya almaya hazır.',
          'Bir engel var: bağımlılığın tamamlanması gerekiyor.',
          'Performans test sonuçları beklentilerin altında, optimizasyon lazım.',
          'Müşteri geri bildirimi alındı, küçük revizyonlar yapılacak.',
          'Dokümantasyon güncellendi, PR açıldı.'
        ];
        const cmtIdx = allWorkItems.length % commentTemplates.length;
        await tryOp(`Yorum: ${t.title}`, async () => {
          return call(`/api/work-items/${wi.id}/comments`, { method:'POST', body: JSON.stringify({ body: commentTemplates[cmtIdx] }) });
        });
        // İkinci yorum
        const replyIdx = (cmtIdx + 3) % commentTemplates.length;
        await tryOp(`Yanıt: ${t.title}`, async () => {
          return call(`/api/work-items/${wi.id}/comments`, { method:'POST', body: JSON.stringify({ body: commentTemplates[replyIdx] }) });
        });

        // İş günlüğü
        const wlHours = [1.5, 2, 3, 4, 5, 6, 8, 10, 12];
        await tryOp(`İş günlüğü: ${t.title}`, async () => {
          return call(`/api/work-items/${wi.id}/worklogs`, { method:'POST', body: JSON.stringify({
            userId: allUserIds[assigneeIdx], hours: wlHours[allWorkItems.length % wlHours.length],
            note: 'Geliştirme ve test çalışması'
          })});
        });

        // Checklist
        const checklists = [
          ['Gereksinim analizi', 'Tasarım onayı', 'Implementasyon', 'Test', 'Dokümantasyon'],
          ['Kod yazımı', 'Birim testi', 'Code review', 'Entegrasyon testi'],
          ['Sorumlu kişi atandı', 'Zaman tahmini yapıldı', 'Bağımlılıklar belirlendi', 'Risk analizi']
        ];
        for (const item of checklists[allWorkItems.length % 3]) {
          await tryOp(`Checklist: ${item}`, async () => {
            return call(`/api/work-items/${wi.id}/checklist`, { method:'POST', body: JSON.stringify({ text: item }) });
          });
        }

        // İzle + Oy
        await tryOp(`İzle: ${t.title}`, async () => {
          return call(`/api/work-items/${wi.id}/watch`, { method:'PUT', body: JSON.stringify({ watching: true }) });
        });
      }
    }
  }

  console.log(`\n📊 Toplam yeni iş öğesi: ${allWorkItems.length}`);

  // === İLİŞKİLER (Blocks/Relates) ===
  console.log('\n🔗 İş öğesi ilişkileri...');
  for (let i = 0; i < allWorkItems.length - 1; i += 3) {
    const a = allWorkItems[i];
    const b = allWorkItems[i + 1];
    if (a && b && a.projectId === b.projectId) {
      await tryOp(`Blocks: ${a.title} → ${b.title}`, async () => {
        return call(`/api/work-items/${a.id}/relations`, { method:'POST', body: JSON.stringify({
          relatedWorkItemId: b.id, relationType: 'Blocks'
        })});
      });
    }
    const c = allWorkItems[i + 2];
    if (a && c && a.projectId === c.projectId) {
      await tryOp(`Relates: ${a.title} ↔ ${c.title}`, async () => {
        return call(`/api/work-items/${a.id}/relations`, { method:'POST', body: JSON.stringify({
          relatedWorkItemId: c.id, relationType: 'RelatesTo'
        })});
      });
    }
  }

  // === PARENT-CHILD ===
  console.log('\n👪 Parent-child ilişkileri...');
  for (const proj of mainProjects) {
    const projItems = allWorkItems.filter(wi => wi.projectKey === proj.key);
    const epics = projItems.filter(wi => wi.type === 'Epic');
    const others = projItems.filter(wi => wi.type !== 'Epic' && wi.type !== 'Subtask');
    for (const epic of epics) {
      for (let i = 0; i < Math.min(others.length, 4); i++) {
        const child = others[(others.indexOf(epic) + i + 1) % others.length];
        if (child) {
          await tryOp(`Parent: ${child.title} → ${epic.title}`, async () => {
            return call(`/api/work-items/${child.id}/parent`, { method:'PATCH', body: JSON.stringify({ parentId: epic.id }) });
          });
        }
      }
    }
  }

  // === SPRINT'E DAHA FAZLA ÖĞE PLANLA ===
  console.log('\n📅 Sprint planlama...');
  for (const proj of mainProjects) {
    const sprints = await tryOp(`Sprintler: ${proj.key}`, async () => {
      return call(`/api/sprints?projectId=${proj.id}&pageSize=20`);
    });
    const sprintList = Array.isArray(sprints) ? sprints : (sprints?.items||[]);
    const activeSprint = sprintList.find(s => s.state === 'Active' || s.status === 'Active');
    const plannedSprint = sprintList.find(s => s.state === 'Planned' || s.status === 'Planned');

    const projItems = allWorkItems.filter(wi => wi.projectKey === proj.key);

    // Aktif sprint'e daha fazla öğe ekle
    if (activeSprint) {
      for (let i = 0; i < Math.min(projItems.length, 8); i++) {
        await tryOp(`Planla (aktif): ${projItems[i].title}`, async () => {
          return call(`/api/sprints/${activeSprint.id}/items/${projItems[i].id}`, { method:'PUT', body:'{}' });
        });
      }
    }
    // Planlı sprint'e öğeler ekle
    if (plannedSprint) {
      for (let i = 8; i < Math.min(projItems.length, 15); i++) {
        if (projItems[i]) {
          await tryOp(`Planla (gelecek): ${projItems[i].title}`, async () => {
            return call(`/api/sprints/${plannedSprint.id}/items/${projItems[i].id}`, { method:'PUT', body:'{}' });
          });
        }
      }
    }
  }

  // === DAHA FAZLA SPRINT ===
  console.log('\n🏃 Ek sprint\'ler...');
  for (const proj of mainProjects) {
    // Tamamlanmış sprint (geçmiş)
    await tryOp(`Sprint 3 (tamamlanmış): ${proj.key}`, async () => {
      return call('/api/sprints', { method:'POST', body: JSON.stringify({
        projectId: proj.id, name: `${proj.key} Sprint 3`, goal: 'Stabilizasyon ve hata düzeltme',
        startDate: dateOnly(-35), endDate: dateOnly(-21)
      })});
    });
    await tryOp(`Sprint 4 (tamamlanmış): ${proj.key}`, async () => {
      return call('/api/sprints', { method:'POST', body: JSON.stringify({
        projectId: proj.id, name: `${proj.key} Sprint 4`, goal: 'Performans iyileştirme',
        startDate: dateOnly(-21), endDate: dateOnly(-7)
      })});
    });
  }

  // === ONAY TALEPLERİ ===
  console.log('\n✅ Onay talepleri...');
  for (const proj of mainProjects) {
    const projItems = allWorkItems.filter(wi => wi.projectKey === proj.key && (wi.status === 'Code Review' || wi.status === 'Test'));
    for (const item of projItems.slice(0, 2)) {
      await tryOp(`Onay: ${item.title}`, async () => {
        return call(`/api/work-items/${item.id}/approvals`, { method:'POST', body: JSON.stringify({
          approverUserId: adminId, note: 'Kod incelemesi için onay talebi'
        })});
      });
    }
  }

  // === KAYDEDILMİŞ BOARD GÖRÜNÜMLERİ ===
  console.log('\n💾 Kaydedilmiş pano görünümleri...');
  for (const proj of mainProjects) {
    const boards = await call(`/api/boards/by-project/${proj.id}`);
    const boardList = Array.isArray(boards) ? boards : (boards.items||[]);
    if (boardList[0]) {
      await tryOp(`Görünüm: ${proj.key} Yüksek Öncelik`, async () => {
        return call(`/api/boards/${boardList[0].id}/views`, { method:'POST', body: JSON.stringify({
          name: 'Yüksek Öncelikli İşler', isShared: true,
          filters: { priority: 'High' }
        })});
      });
      await tryOp(`Görünüm: ${proj.key} Bug'lar`, async () => {
        return call(`/api/boards/${boardList[0].id}/views`, { method:'POST', body: JSON.stringify({
          name: 'Tüm Hatalar', isShared: true,
          filters: { type: 'Bug' }
        })});
      });
    }
  }

  // === DEPARTMAN ÜYELERİ ===
  console.log('\n🏢 Departman üyeleri...');
  const deptRes = await tryOp('Departmanlar', async () => call(`/api/organizations/${orgId}/departments`));
  const depts = Array.isArray(deptRes) ? deptRes : (deptRes?.items||[]);
  for (let i = 0; i < depts.length && i < userIds.length; i++) {
    await tryOp(`Üye: ${userMap[userIds[i]].display} → ${depts[i].name}`, async () => {
      return call(`/api/organizations/${orgId}/departments/${depts[i].id}/members`, {
        method:'POST', body: JSON.stringify({ userId: userIds[i] })
      });
    });
  }

  console.log('\n🎉 Power-user veri doldurma tamamlandı!');
  console.log(`📊 Özet:`);
  console.log(`   Kullanıcı: ${Object.keys(userMap).length}`);
  console.log(`   Yeni iş öğesi: ${allWorkItems.length}`);
  console.log(`   Proje: ${mainProjects.length}`);
}

main().catch(err => { console.error('❌', err.message); process.exit(1); });
