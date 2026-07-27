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
  try { await fn(); console.log(`  ✅ ${label}`); }
  catch (e) { console.log(`  ⚠️  ${label}: ${e.message.slice(0,80)}`); }
}

function futureDate(days) {
  const d = new Date(); d.setDate(d.getDate() + days);
  return d.toISOString();
}

// --- İŞ ÖĞESİ İÇERİK KÜTÜPHANESİ ---
const contentByType = {
  Epic: {
    desc: (title, proj) => `## Hedef\n\n${title} kapsamında ${proj} projesi için temel özelliklerin tasarımı, geliştirilmesi ve canlıya alınması hedeflenmektedir. Bu epic, birden fazla kullanıcı hikayesi ve teknik görevi kapsayan üst düzey bir iş öğesidir.\n\n## Kapsam\n\n- İhtiyaç analizi ve gereksinim dokümanının hazırlanması\n- Teknik tasarım ve mimari kararların belgelenmesi\n- Geliştirme ve birim test süreçlerinin tamamlanması\n- Entegrasyon testleri ve kullanıcı kabul testleri\n- Canlıya alma ve izleme süreci\n\n## Başarı Kriterleri\n\n- Tüm alt görevler tamamlandı ve test edildi\n- Performans hedefleri karşılandı (yanıt süresi < 200ms)\n- Güvenlik denetimi geçildi\n- Kullanıcı dokümantasyonu hazır`,
    acceptance: 'Tüm alt görevler tamamlandı\nEntegrasyon testleri geçti\nPerformans hedefleri karşılandı\nGüvenlik denetimi tamamlandı',
    estimate: '21'
  },
  Story: {
    desc: (title, proj) => `## Kullanıcı Hikayesi\n\nBir kullanıcı olarak, ${title} özelliğini kullanabilmek istiyorum, böylece ${proj} üzerindeki işimi daha verimli yapabilirim.\n\n## Gereksinimler\n\n- Kullanıcı dostu ve sezgisel arayüz\n- Hızlı yanıt süresi (< 500ms)\n- Hata durumlarında anlaşılır geri bildirim\n- Mobil ve masaüstü uyumluluğu\n- Erişilebilirlik standartlarına uygunluk (WCAG 2.1 AA)\n\n## Teknik Notlar\n\n- REST API endpoint'leri tasarlanmalı\n- Veritabanı şeması güncellenmeli\n- Önbellekleme stratejisi belirlenmeli\n- Birim ve entegrasyon testleri yazılmalı`,
    acceptance: 'Kullanıcı özelliği sorunsuz kullanabiliyor\nHata durumları düzgün yönetiliyor\nYanıt süresi 500ms altında\nMobil ve masaüstünde çalışıyor',
    estimate: '8'
  },
  Task: {
    desc: (title, proj) => `## Görev Tanımı\n\n${title} görevi, ${proj} projesinin teknik gereksinimlerinin karşılanması için planlanmıştır.\n\n## Yapılacaklar\n\n1. Mevcut kod tabanı incelenecek ve etki analizi yapılacak\n2. Gerekli değişiklikler implement edilecek\n3. Birim testleri yazılacak ve en az %80 kod kapsama sağlanacak\n4. Kod incelemesi (code review) yapılacak\n5. CI/CD boru hattından geçecek\n6. Staging ortamında test edilecek\n\n## Teknik Detaylar\n\n- Programlama dili: C# / TypeScript\n- Framework: .NET 8 / AngularJS\n- Veritabanı: MongoDB\n- Test framework: xUnit / Jest`,
    acceptance: 'Kod yazıldı ve test edildi\nCode review onaylandı\nCI/CD pipeline geçti\nDokümantasyon güncellendi',
    estimate: '5'
  },
  Bug: {
    desc: (title, proj) => `## Hata Açıklaması\n\n${title} hatası tespit edilmiştir. Bu hata, ${proj} projesinde kullanıcı deneyimini olumsuz etkilemektedir.\n\n## Yeniden Üretme Adımları\n\n1. Sisteme giriş yapın\n2. İlgili modüle gidin\n3. Belirtilen işlemi gerçekleştirin\n4. Hata oluşur\n\n## Beklenen Davranış\n\nSistem hatasız çalışmalı ve doğru sonuç döndürmelidir.\n\n## Gerçekleşen Davranış\n\nSistem hata veriyor veya beklenmeyen sonuç üretiyor.\n\n## Etki Derecesi\n\n- **Öncelik:** Yüksek\n- **Etki:** Kullanıcı iş akışı kesintiye uğruyor\n- **Geçici Çözüm:** Belirli adımlarla atlanabilir`,
    acceptance: 'Hata düzeltildi\nRegresyon testi yazıldı\nTüm senaryolar test edildi',
    estimate: '3'
  },
  Subtask: {
    desc: (title, proj) => `## Alt Görev\n\n${title}, üst görevin bir parçası olarak planlanmıştır.\n\n## Detaylar\n\n- Üst görev ile uyumlu çalışmalı\n- Belirlenen standartlara uygun olmalı\n- Zaman çizelgesine uyulmalı\n\n## Çıktı\n\n- Tamamlanmış ve test edilmiş kod\n- Güncellenmiş dokümantasyon`,
    acceptance: 'Alt görev tamamlandı\nÜst görevle entegre çalışıyor',
    estimate: '2'
  }
};

async function main() {
  console.log('🌱 Zumbo Demo Veri — Detay Doldurma (Faz 4)\n');
  const r = await api('/api/browser-auth/login', { method: 'POST', body: JSON.stringify({
    usernameOrEmail: env.ZUMBO_IDENTITY_ADMIN_EMAIL, password: ADMIN_PASSWORD
  })});
  csrfToken = r.csrfToken; adminUserId = r.user.id; orgId = r.user.organizationId;
  console.log(`✅ Giriş: ${r.user.username}\n`);

  // Tüm projeleri al
  const projectList = await api(`/api/projects?organizationId=${orgId}&pageSize=50`);
  const projects = Array.isArray(projectList) ? projectList : (projectList.items || []);
  const projectMap = {};
  for (const p of projects) projectMap[p.id] = p.name;

  // Tüm iş öğelerini al
  const allWi = [];
  for (const project of projects) {
    try {
      const wiRes = await api(`/api/work-items?projectId=${project.id}&pageSize=100`);
      const items = Array.isArray(wiRes) ? wiRes : (wiRes.items || []);
      for (const wi of items) {
        allWi.push({ ...wi, projectName: project.name });
      }
    } catch(e) {}
  }
  console.log(`📋 ${allWi.length} iş öğesi bulundu\n`);

  let enriched = 0;
  for (const wi of allWi) {
    const type = wi.type || 'Task';
    const content = contentByType[type] || contentByType.Task;
    const projName = wi.projectName || 'proje';

    // Description doldur
    const newDesc = content.desc(wi.title, projName);
    let dueDate = wi.dueDate;
    if (!dueDate) {
      const offsets = [3, 7, 14, 21, 30, 45, 60];
      dueDate = futureDate(offsets[enriched % offsets.length]);
    }

    await tryOp(`Detay: ${wi.title}`, async () => {
      await api(`/api/work-items/${wi.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          title: wi.title,
          description: newDesc,
          priority: wi.priority || 'Medium',
          dueDate: dueDate
        })
      });
    });

    // Estimate ekle (custom field olarak veya planning endpoint ile)
    await tryOp(`Tahmini süre: ${wi.title}`, async () => {
      // Planning endpoint ile estimate set et
      await api(`/api/work-items/${wi.id}/planning`, {
        method: 'PATCH',
        body: JSON.stringify({
          estimate: content.estimate
        })
      });
    });

    // Kabul kriterleri - custom field olarak ekle
    await tryOp(`Kabul kriterleri: ${wi.title}`, async () => {
      await api(`/api/work-items/${wi.id}/custom-fields`, {
        method: 'PUT',
        body: JSON.stringify({
          customFields: [
            { name: 'Kabul Kriterleri', value: content.acceptance, type: 'Text' }
          ]
        })
      });
    });

    // Ek work log (zenginleştirilmiş)
    const workLogs = [
      { hours: 6.5, note: 'Tasarım dokümanı hazırlandı ve teknik ekip ile paylaşıldı.' },
      { hours: 8, note: 'Çekirdek implementasyon tamamlandı, birim testleri yazıldı.' },
      { hours: 4, note: 'Kod incelemesi yapıldı, geri bildirimler uygulandı.' },
      { hours: 3, note: 'Entegrasyon testleri eklendi, hatalar giderildi.' },
      { hours: 2, note: 'Dokümantasyon güncellendi, kullanıcı kılavuzu hazırlandı.' }
    ];
    const wl = workLogs[enriched % workLogs.length];
    await tryOp(`İş günlüğü: ${wl.hours}s - ${wi.title}`, async () => {
      await api(`/api/work-items/${wi.id}/worklogs`, {
        method: 'POST',
        body: JSON.stringify({ userId: adminUserId, hours: wl.hours, note: wl.note })
      });
    });

    // Yorum (zengin, tartışma tarzında)
    const comments = [
      'Güncel durum: Tasarım aşaması tamamlandı, geliştirmeye başlıyoruz. Öngörülen tamamlanma tarihi gelecek hafta sonu.',
      'Teknik borç açısından değerlendirme yaptım. Mevcut mimari bu özelliği destekliyor, ancak önbellekleme katmanı eklenmeli.',
      'QA ekibi test senaryolarını hazırladı. Toplam 12 test senaryosu mevcut, bunların 8\'i otomatikleştirilebilir.',
      'Müşteri geri bildirimi doğrultusunda önceliklendirme güncellendi. Bu görev bir sonraki sprint\'e çekilebilir.',
      'Bağımlılık analizi tamamlandı. Bu görev, diğer iki görev tamamlanmadan başlatılamayacak.'
    ];
    await tryOp(`Tartışma yorumu: ${wi.title}`, async () => {
      await api(`/api/work-items/${wi.id}/comments`, {
        method: 'POST',
        body: JSON.stringify({ body: comments[enriched % comments.length] })
      });
    });

    // İkinci yorum (yanıt niteliğinde)
    await tryOp(`Yanıt yorumu: ${wi.title}`, async () => {
      await api(`/api/work-items/${wi.id}/comments`, {
        method: 'POST',
        body: JSON.stringify({ body: 'Katkı için teşekkürler. Önerileri değerlendiriyorum, gerekirse güncelleme yapacağım.' })
      });
    });

    // Checklist öğelerini işaretle (bazılarını tamamlandı yap)
    // Önce mevcut checklist'i getir
    try {
      const detail = await api(`/api/work-items/${wi.id}`);
      const checklist = detail.checklist || [];
      for (let i = 0; i < checklist.length; i++) {
        const item = checklist[i];
        // İlk yarısını tamamlandı işaretle
        const isDone = i < Math.floor(checklist.length / 2);
        try {
          await api(`/api/work-items/${wi.id}/checklist/${item.id}`, {
            method: 'PATCH',
            body: JSON.stringify({ completed: isDone })
          });
        } catch(e) {}
      }
    } catch(e) {}

    enriched++;
  }

  console.log(`\n📊 ${enriched} iş öğesi detaylarıyla zenginleştirildi`);
  console.log('\n🎉 Detay doldurma tamamlandı!');
}

main().catch(err => { console.error('❌ Hata:', err.message); process.exit(1); });
