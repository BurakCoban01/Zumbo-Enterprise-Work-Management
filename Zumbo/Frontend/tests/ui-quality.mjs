import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const baseUrl = process.env.ZUMBO_FRONTEND_URL || 'http://127.0.0.1:5177';
const executablePath = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/playwright');
const backendEnvPath = resolve(import.meta.dirname, '../../Backend/.env');
const backendEnv = Object.fromEntries(readFileSync(backendEnvPath, 'utf8')
  .split(/\r?\n/)
  .filter(line => line && !line.trimStart().startsWith('#') && line.includes('='))
  .map(line => {
    const separator = line.indexOf('=');
    return [line.slice(0, separator).trim(), line.slice(separator + 1).trim()];
  }));
const adminEmail = process.env.ZUMBO_IDENTITY_ADMIN_EMAIL || backendEnv.ZUMBO_IDENTITY_ADMIN_EMAIL;
const adminBootstrapToken = process.env.ZUMBO_IDENTITY_BOOTSTRAP_TOKEN || backendEnv.ZUMBO_IDENTITY_BOOTSTRAP_TOKEN;
assert.ok(adminEmail && adminBootstrapToken, 'Local SystemAdmin bootstrap settings are required for the role-management E2E');
await mkdir(outputDir, { recursive: true });
process.on('uncaughtException', error => {
  writeFileSync(resolve(outputDir, 'result.json'), JSON.stringify({ passed: false, error: error.stack || error.message }, null, 2));
  process.exit(1);
});
process.on('unhandledRejection', error => {
  writeFileSync(resolve(outputDir, 'result.json'), JSON.stringify({ passed: false, error: error.stack || String(error) }, null, 2));
  process.exit(1);
});

const browser = await chromium.launch({ executablePath, headless: true });
const failures = [];
const expectedHttpResponses = [];
let diagnosticPage = null;

async function attachDiagnostics(page, name) {
  page.on('pageerror', error => failures.push(`${name} page error: ${error.message}`));
  page.on('response', response => {
    const expected = expectedHttpResponses.find(item =>
      !item.seen && item.status === response.status() && response.url().includes(item.urlPart));
    if (expected) {
      expected.seen = true;
      return;
    }
    if (response.status() >= 400) failures.push(`${name} HTTP ${response.status()}: ${response.url()}`);
  });
  page.on('console', message => {
    if (message.type() === 'error'
      && !message.text().includes('net::ERR_INTERNET_DISCONNECTED')
      && !message.text().includes('Failed to load resource')) {
      failures.push(`${name} console error: ${message.text()}`);
    }
  });
}

function expectHttpResponse(status, urlPart) {
  const expected = { status, urlPart, seen: false };
  expectedHttpResponses.push(expected);
  return expected;
}

async function assertNoOverflow(page, selector = 'body') {
  const overflow = await page.locator(selector).evaluate(element => ({
    horizontal: element.scrollWidth - element.clientWidth,
    vertical: element.scrollHeight - element.clientHeight
  }));
  assert.ok(overflow.horizontal <= 1, `${selector} has ${overflow.horizontal}px horizontal overflow`);
}

async function assertPwa(page) {
  const manifest = await page.locator('link[rel="manifest"]').getAttribute('href');
  assert.ok(manifest, 'manifest link is missing');
  const manifestPayload = await page.evaluate(async href => (await fetch(href)).json(), manifest);
  assert.equal(manifestPayload.display, 'standalone');
  assert.ok(manifestPayload.icons.some(icon => icon.sizes === '192x192' && icon.type === 'image/png'));
  assert.ok(manifestPayload.icons.some(icon => icon.sizes === '512x512' && icon.type === 'image/png'));
  assert.ok(manifestPayload.icons.some(icon => icon.purpose.includes('maskable')));
  await page.waitForFunction(() => navigator.serviceWorker && navigator.serviceWorker.ready);
}

async function cachedUrls(page) {
  return page.evaluate(async () => {
    const urls = [];
    for (const cacheName of await caches.keys()) {
      const cache = await caches.open(cacheName);
      urls.push(...(await cache.keys()).map(request => request.url));
    }
    return urls;
  });
}

async function apiRequest(path, method, body, token) {
  const response = await fetch(`http://localhost:5088${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  return { response, payload, data: payload.data };
}

try {
  const desktop = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'light' });
  const page = await desktop.newPage();
  diagnosticPage = page;
  await attachDiagnostics(page, 'desktop');
  await page.goto(`${baseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  const desktopRegistrationRequest = page.waitForRequest(request =>
    request.method() === 'POST' && request.url().endsWith('/api/auth/register'));
  await page.locator('.desktop-login form').getByRole('button', { name: 'Demo çalışma alanı oluştur' }).click();
  const desktopDemoPassword = (await desktopRegistrationRequest).postDataJSON().password;
  assert.ok(desktopDemoPassword, 'Desktop demo registration did not include a generated password');
  await page.locator('.task').first().waitFor({ timeout: 30_000 });
  const originalProjectId = await page.locator('.task').first().getAttribute('data-project-id');
  assert.ok(originalProjectId, 'Initial task did not expose its project relationship');
  const collaborator = await page.evaluate(async stamp => {
    const currentUser = JSON.parse(localStorage.getItem('zumbo.currentUser'));
    const response = await fetch('http://localhost:5088/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: `collaborator${stamp}`,
        email: `collaborator${stamp}@zumbo.local`,
        password: 'P@ssword123',
        organizationId: currentUser.organizationId
      })
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.error?.message || 'Collaborator registration failed');
    return payload.data.user;
  }, Date.now());
  const workspaceUser = await page.evaluate(() => JSON.parse(localStorage.getItem('zumbo.currentUser')));
  let adminAuth = await apiRequest('/api/auth/register', 'POST', {
    username: 'local-system-admin',
    email: adminEmail,
    password: 'P@ssword123',
    organizationId: 'local-system-administration',
    bootstrapToken: adminBootstrapToken
  });
  if (adminAuth.response.status === 409) {
    adminAuth = await apiRequest('/api/auth/login', 'POST', {
      usernameOrEmail: adminEmail,
      password: 'P@ssword123'
    });
  }
  assert.ok(adminAuth.response.ok, adminAuth.payload.error?.message || 'SystemAdmin authentication failed');
  const roleGrant = await apiRequest(`/api/auth/users/${workspaceUser.id}/roles`, 'PUT', {
    roles: ['User', 'OrganizationAdmin']
  }, adminAuth.data.accessToken);
  assert.ok(roleGrant.response.ok, roleGrant.payload.error?.message || 'OrganizationAdmin grant failed');
  const elevatedSession = await apiRequest('/api/auth/login', 'POST', {
    usernameOrEmail: workspaceUser.username,
    password: desktopDemoPassword
  });
  assert.ok(elevatedSession.response.ok, elevatedSession.payload.error?.message || 'Elevated user login failed');
  await page.evaluate(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    localStorage.setItem('zumbo.accessToken', auth.accessToken);
    localStorage.setItem('zumbo.refreshToken', auth.refreshToken);
  }, elevatedSession.data);
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('.task').first().waitFor({ timeout: 30_000 });
  await page.locator('.side-nav').waitFor();
  assert.ok(await page.locator('.side-nav svg').count() >= 5, 'Lucide navigation icons did not render');
  await assertNoOverflow(page);
  await assertPwa(page);
  await page.waitForFunction(() => Number(document.querySelector('.summary-strip strong')?.textContent || 0) >= 1);
  await page.screenshot({ path: resolve(outputDir, 'desktop-light.png'), fullPage: true });

  const epicTitle = `UI Epic ${String(Date.now()).slice(-6)}`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).click();
  await page.locator('#new-task-title').fill(epicTitle);
  await page.locator('#new-task-type').selectOption('Epic');
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev oluşturuldu.' }).waitFor();
  await page.locator('.task').filter({ hasText: epicTitle }).waitFor();

  const firstTask = page.locator('.task').first();
  const firstTitle = await firstTask.locator('h2').innerText();
  await firstTask.locator('input[type="checkbox"]').click();
  await page.locator('.bulk-toolbar').waitFor();
  await firstTask.press('Alt+ArrowRight');
  await page.waitForFunction(title => {
    const lanes = Array.from(document.querySelectorAll('.column-lane'));
    return lanes.length > 1 && lanes[1].textContent.includes(title);
  }, firstTitle, { timeout: 15_000 });

  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.locator('.inspector').waitFor();
  assert.match(page.url(), /#section=board(?:&|$)/, 'Board section deep link was not written');
  assert.match(page.url(), /[?&]task=/, 'Task detail deep link was not written');
  await page.locator('#task-description').fill('Playwright yaşam döngüsü doğrulaması');
  await page.locator('#task-parent').selectOption({ label: epicTitle });
  await page.getByRole('button', { name: 'Ayrıntıları kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ayrıntıları kaydedildi.' }).waitFor();
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('.inspector').waitFor({ timeout: 15_000 });
  assert.equal(await page.locator('#task-description').inputValue(), 'Playwright yaşam döngüsü doğrulaması');
  assert.equal((await page.locator('#task-parent option:checked').innerText()).trim(), epicTitle);
  await page.getByLabel('İlişki türü').selectOption('RelatesTo');
  await page.getByLabel('İlişkili görev').selectOption({ label: epicTitle });
  await page.getByRole('button', { name: 'Görev ilişkisi ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ilişkisi eklendi.' }).waitFor();
  await page.getByRole('button', { name: 'Görev ilişkisini kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ilişkisi kaldırıldı.' }).waitFor();
  await page.locator('#task-parent').selectOption('');
  await page.getByRole('button', { name: 'Ayrıntıları kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ayrıntıları kaydedildi.' }).waitFor();
  await page.getByLabel('Yeni etiket').fill('playwright');
  await page.getByRole('button', { name: 'Etiket ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Etiket eklendi.' }).waitFor();
  await page.locator('.editable-labels').getByRole('button', { name: 'Etiketi kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Etiket kaldırıldı.' }).waitFor();
  await page.locator('.inspector-section').filter({ hasText: 'Yorumlar' }).locator('textarea').fill('Yaşam döngüsü yorumu');
  await page.getByRole('button', { name: 'Yorum ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum eklendi.' }).waitFor();
  const commentRow = page.locator('.comment-row').last();
  await commentRow.getByRole('button', { name: 'Yorumu düzenle' }).click();
  await commentRow.getByLabel('Yorum metni').fill('Güncellenmiş yaşam döngüsü yorumu');
  await commentRow.getByRole('button', { name: 'Yorumu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum güncellendi.' }).waitFor();
  await page.locator('.comment-row').last().getByRole('button', { name: 'Yorumu sil' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum silindi.' }).waitFor();
  await page.getByLabel('İş günlüğü saati').fill('1.5');
  await page.getByLabel('İş günlüğü notu').fill('Playwright doğrulaması');
  await page.getByRole('button', { name: 'İş günlüğü ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'İş günlüğü eklendi.' }).waitFor();
  await page.screenshot({ path: resolve(outputDir, 'desktop-task-detail.png'), fullPage: true });
  await page.getByRole('button', { name: 'Görev detayını kapat' }).click();

  await page.getByRole('button', { name: 'Kart alanlarını yapılandır' }).click();
  await page.locator('.card-config-menu').waitFor();
  await page.locator('.card-config-menu').getByText('Bitiş tarihi').click();
  await page.getByRole('button', { name: 'Kart alanlarını yapılandır' }).click();
  await page.getByRole('button', { name: 'Kolonu daralt' }).first().click();
  await page.locator('.column-lane.collapsed').first().waitFor();
  await page.getByRole('button', { name: 'Kolonu genişlet' }).first().click();

  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.getByRole('button', { name: 'Görevi arşivle' }).click();
  await page.locator('.inspector').waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Arşiv' }).click();
  const archivedRow = page.locator('.archive-list article').filter({ hasText: firstTitle });
  await archivedRow.waitFor();
  await archivedRow.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedRow.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Pano' }).click();
  await page.locator('.task').filter({ hasText: firstTitle }).waitFor();

  const managementStamp = String(Date.now()).slice(-6);
  const teamName = `UI Ekip ${managementStamp}`;
  const renamedTeam = `${teamName} Güncel`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Ekip', exact: true }).click();
  await page.locator('#new-team-name').fill(teamName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip oluşturuldu.' }).waitFor();
  await page.getByRole('button', { name: 'Ekipler', exact: true }).click();
  await page.locator('.entity-list').getByText(teamName, { exact: true }).click();
  assert.match(page.url(), /[?&]team=/, 'Team deep link was not written');
  const teamDeepLink = page.url();
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#team-name').waitFor();
  assert.equal(await page.locator('#team-name').inputValue(), teamName, 'Team deep link did not survive reload');
  assert.equal(page.url(), teamDeepLink, 'Team deep link changed during reload');
  await page.locator('.invite-row input[type="email"]').fill(collaborator.email);
  await page.locator('.invite-row').getByRole('button', { name: 'Davet et' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip daveti oluşturuldu.' }).waitFor();
  const invitedMember = page.locator('.member-manage-row').filter({ hasText: collaborator.email });
  const teamMemberRemoval = page.waitForResponse(response =>
    response.url().includes('/members/') && response.request().method() === 'DELETE');
  await invitedMember.getByRole('button', { name: 'Ekip üyesini kaldır' }).click();
  assert.equal((await teamMemberRemoval).status(), 200, 'Team member removal failed');
  await page.locator('.timeline').filter({ hasText: 'TeamMemberRemoved' }).waitFor();
  await page.locator('#team-name').fill(renamedTeam);
  await page.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip kaydedildi.' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Arşivle', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedTeam = page.locator('.archive-group').filter({ hasText: 'Ekipler' }).locator('article').filter({ hasText: renamedTeam });
  await archivedTeam.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedTeam.waitFor({ state: 'detached' });

  const projectName = `UI Proje ${managementStamp}`;
  const renamedProject = `${projectName} Güncel`;
  const boardName = `UI Pano ${managementStamp}`;
  const renamedBoard = `${boardName} Güncel`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Proje', exact: true }).click();
  await page.locator('#new-project-key').fill(`UI${managementStamp}`);
  await page.locator('#new-project-name').fill(projectName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje oluşturuldu.' }).waitFor();
  assert.match(page.url(), /[?&]project=/, 'Project deep link was not written');
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#project-name').waitFor();
  assert.equal(await page.locator('#project-name').inputValue(), projectName, 'Project deep link did not survive reload');
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('#new-board-name').fill(boardName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano oluşturuldu.' }).waitFor();
  assert.match(page.url(), /[?&]board=/, 'Board deep link was not written');
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#board-name').waitFor();
  assert.equal(await page.locator('#board-name').inputValue(), boardName, 'Board deep link did not survive reload');
  await page.locator('#board-name').fill(renamedBoard);
  await page.getByRole('button', { name: 'Panoyu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kaydedildi.' }).waitFor();
  await page.getByRole('button', { name: 'Panoyu arşivle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedBoard = page.locator('.archive-group').filter({ hasText: 'Panolar' }).locator('article').filter({ hasText: renamedBoard });
  await archivedBoard.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedBoard.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Projeler', exact: true }).click();
  await page.locator('#project-name').fill(renamedProject);
  await page.getByLabel('Proje üyesi').locator('option').filter({ hasText: collaborator.email }).waitFor({ state: 'attached' });
  await page.getByLabel('Proje üyesi').selectOption({ label: `${collaborator.username} · ${collaborator.email}` });
  await page.getByRole('button', { name: 'Üye ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje üyesi eklendi.' }).waitFor();
  let projectMember = page.locator('.member-manage-row').filter({ hasText: collaborator.id });
  const currentProjectRole = await projectMember.getByLabel('Proje üyesi rolü').inputValue();
  const nextProjectRole = currentProjectRole === 'Viewer' ? 'Developer' : 'Viewer';
  const projectRoleResponse = page.waitForResponse(response =>
    response.url().includes(`/members/${collaborator.id}/role`) && response.request().method() === 'PATCH');
  await projectMember.getByLabel('Proje üyesi rolü').selectOption(nextProjectRole);
  assert.equal((await projectRoleResponse).status(), 200, 'Project member role update failed');
  projectMember = page.locator('.member-manage-row').filter({ hasText: collaborator.id });
  const projectMemberRemoval = page.waitForResponse(response =>
    response.url().includes(`/members/${collaborator.id}`) && response.request().method() === 'DELETE');
  await projectMember.getByRole('button', { name: 'Proje üyesini kaldır' }).click();
  assert.equal((await projectMemberRemoval).status(), 200, 'Project member removal failed');
  await page.locator('.timeline').filter({ hasText: 'ProjectMemberRemoved' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje kaydedildi.' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Arşivle', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedProject = page.locator('.archive-group').filter({ hasText: 'Projeler' }).locator('article').filter({ hasText: renamedProject });
  await archivedProject.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedProject.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Projeler', exact: true }).click();
  await page.locator(`[data-project-id="${originalProjectId}"]`).click();
  const inProgressColumn = page.locator('.configuration-row').filter({ has: page.getByLabel('Kolon adı', { exact: true }) }).nth(1);
  await inProgressColumn.locator('input[type="number"]').fill('1');
  await inProgressColumn.getByRole('button', { name: 'Kolonu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kolon ayarları kaydedildi.' }).waitFor();
  const newColumnForm = page.locator('form.configuration-row');
  await newColumnForm.getByLabel('Yeni kolon adı').fill('UI Review');
  await newColumnForm.getByLabel('Yeni kolon WIP limiti').fill('4');
  await newColumnForm.getByRole('button', { name: 'Kolon ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kolonu eklendi.' }).waitFor();
  const customColumn = page.locator('.configuration-row').filter({ has: page.getByLabel('Kolon adı', { exact: true }) }).last();
  await customColumn.getByRole('button', { name: 'Kolonu kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kolonu kaldırıldı.' }).waitFor();
  await page.locator('.workflow-row.transition-row').first().waitFor();
  await page.locator('.workflow-row.transition-row').nth(1).locator('input[type="checkbox"]').nth(2).check();
  await page.getByRole('button', { name: "Workflow'u kaydet" }).click();
  await page.locator('.toast.success').filter({ hasText: 'Workflow kaydedildi.' }).waitFor();
  await page.locator('.timeline').filter({ hasText: 'BoardColumnDeleted' }).waitFor();
  await page.locator('.timeline').filter({ hasText: 'WorkflowUpdated' }).waitFor();
  await assertNoOverflow(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-project-management.png'), fullPage: true });
  await page.getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('.task').first().waitFor();
  const wipConflict = expectHttpResponse(409, `/api/work-items/`);
  const wipResponse = page.waitForResponse(response =>
    response.status() === 409 && response.url().includes('/api/work-items/') && response.url().endsWith('/status'));
  await page.locator('.task').filter({ hasText: epicTitle }).press('Alt+ArrowRight');
  await wipResponse;
  await page.locator('.toast.error').filter({ hasText: 'WIP limiti dolu' }).waitFor();
  assert.ok(wipConflict.seen, 'Expected WIP conflict response was not observed');
  assert.ok(await page.locator('.column-lane').first().locator('.task').filter({ hasText: epicTitle }).isVisible(), 'Optimistic WIP move was not rolled back');
  const viewName = `UI Görünüm ${managementStamp}`;
  const renamedView = `${viewName} Güncel`;
  await page.getByPlaceholder('Görünüm adı').fill(viewName);
  await page.locator('.save-view').getByRole('button', { name: 'Kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm kaydedildi.' }).waitFor();
  await page.locator('#saved-view').selectOption({ label: viewName });
  await page.getByPlaceholder('Görünüm adı').fill(renamedView);
  await page.locator('.save-view').getByRole('button', { name: 'Kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm kaydedildi.' }).waitFor();
  await page.locator('#saved-view').selectOption({ label: renamedView });
  await page.getByRole('button', { name: 'Kayıtlı görünümü sil' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm silindi.' }).waitFor();
  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.getByRole('button', { name: 'Onay iste' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Geçiş onayı istendi.' }).waitFor();
  await page.locator('.approval-row').filter({ hasText: 'Pending' }).waitFor();
  await page.getByRole('button', { name: 'Görev detayını kapat' }).click();

  await page.getByRole('button', { name: 'Ayarlar', exact: true }).click();
  await page.locator('.settings-view[data-settings-ready="true"]').waitFor();
  await page.getByRole('tab', { name: 'Organizasyon' }).click();
  await page.getByLabel('Organizasyon adı').fill('UI Organizasyonu');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Organizasyon kaydedildi.' }).waitFor();
  await page.getByLabel('Yeni departman adı').fill('Platform');
  await page.getByRole('button', { name: 'Departman ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Departman eklendi.' }).waitFor();
  await page.getByLabel('Departman', { exact: true }).selectOption({ label: 'Platform' });
  await page.getByLabel('Departman üyesi').selectOption({ label: `${collaborator.username} · ${collaborator.email}` });
  await page.getByLabel('Departman pozisyonu').fill('Developer');
  await page.getByRole('button', { name: 'Üye ata' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Departman üyesi atandı.' }).waitFor();
  const departmentMemberRemoval = page.waitForResponse(response =>
    response.url().includes('/departments/') && response.url().includes('/members/') && response.request().method() === 'DELETE');
  await page.getByRole('button', { name: 'Departman üyesini kaldır' }).click();
  assert.equal((await departmentMemberRemoval).status(), 200, 'Department member removal failed');
  await page.locator('.timeline').filter({ hasText: 'DepartmentMemberRemoved' }).waitFor();
  await page.getByRole('tab', { name: 'Rol ve izinler' }).click();
  const roleName = `UI Reviewer ${managementStamp}`;
  const renamedRole = `${roleName} Lead`;
  const roleCreateForm = page.locator('.role-create-form');
  await roleCreateForm.getByLabel('Yeni rol adı').fill(roleName);
  await roleCreateForm.getByText('AuditReadAll', { exact: true }).click();
  await roleCreateForm.getByRole('button', { name: 'Rol oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Özel rol oluşturuldu.' }).waitFor();
  let roleRow = page.locator(`.role-definition[data-role-name="${roleName}"]`);
  await roleRow.getByLabel('Rol adı').fill(renamedRole);
  await roleRow.getByText('BoardView', { exact: true }).click();
  await roleRow.getByRole('button', { name: 'Rolü kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Rol güncellendi.' }).waitFor();
  const collaboratorRoleRow = page.locator('.user-role-row').filter({ hasText: collaborator.email });
  await collaboratorRoleRow.getByText(renamedRole, { exact: true }).click();
  await collaboratorRoleRow.getByRole('button', { name: 'Rolleri kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kullanıcı rolleri güncellendi.' }).waitFor();
  await collaboratorRoleRow.getByText(renamedRole, { exact: true }).click();
  await collaboratorRoleRow.getByRole('button', { name: 'Rolleri kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kullanıcı rolleri güncellendi.' }).waitFor();
  roleRow = page.locator(`.role-definition[data-role-name="${renamedRole}"]`);
  await roleRow.getByRole('button', { name: 'Rolü kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Rol kaldırıldı.' }).waitFor();
  await roleRow.waitFor({ state: 'detached' });
  await assertNoOverflow(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-access-management.png'), fullPage: true });
  await page.getByRole('tab', { name: 'Hesap ve güvenlik' }).click();
  await page.getByLabel('API anahtarı adı').fill('Playwright');
  await page.getByLabel('API anahtarı parolası').fill(desktopDemoPassword);
  await page.locator('.api-key-form').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'API anahtarı oluşturuldu' }).waitFor();
  assert.match(await page.locator('.secret-output').filter({ hasText: 'Yeni API anahtarı' }).locator('code').innerText(), /^zmb_/);
  await page.locator('.settings-band').filter({ hasText: 'API anahtarları' }).getByRole('button', { name: 'API anahtarını iptal et' }).click();
  await page.locator('.toast.success').filter({ hasText: 'API anahtarı iptal edildi.' }).waitFor();
  await page.locator('.secret-output').filter({ hasText: 'Yeni API anahtarı' }).waitFor({ state: 'detached' });
  await assertNoOverflow(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-settings.png'), fullPage: true });
  await page.getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('.task').first().waitFor();

  await page.keyboard.press('Control+K');
  await page.locator('.command-palette').waitFor();
  await page.waitForTimeout(250);
  await page.screenshot({ path: resolve(outputDir, 'desktop-command.png') });
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Temayı değiştir' }).click();
  await page.locator('body.theme-dark').waitFor();
  await page.screenshot({ path: resolve(outputDir, 'desktop-dark.png'), fullPage: true });

  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('body.theme-dark').waitFor();
  const ownerAccessToken = await page.evaluate(() => localStorage.getItem('zumbo.accessToken'));
  const viewerGrant = await apiRequest(`/api/projects/${originalProjectId}/members`, 'POST', {
    userId: collaborator.id,
    role: 'Viewer'
  }, ownerAccessToken);
  assert.ok(viewerGrant.response.ok, viewerGrant.payload.error?.message || 'Viewer project grant failed');
  const viewerAuth = await apiRequest('/api/auth/login', 'POST', {
    usernameOrEmail: collaborator.username,
    password: 'P@ssword123'
  });
  assert.ok(viewerAuth.response.ok, viewerAuth.payload.error?.message || 'Viewer login failed');
  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await viewerContext.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    localStorage.setItem('zumbo.accessToken', auth.accessToken);
    localStorage.setItem('zumbo.refreshToken', auth.refreshToken);
  }, viewerAuth.data);
  const viewerPage = await viewerContext.newPage();
  await attachDiagnostics(viewerPage, 'viewer-permission');
  await viewerPage.goto(`${baseUrl}/desktop-bulma/index.html#section=projects&project=${originalProjectId}`, { waitUntil: 'networkidle' });
  await viewerPage.getByText('Viewer rolüyle bu proje salt okunur görüntüleniyor.').waitFor();
  assert.ok(await viewerPage.locator('#project-name').isDisabled(), 'Viewer could edit the project name');
  assert.equal(await viewerPage.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).count(), 0, 'Viewer received a project save command');
  const viewerRemoval = await apiRequest(`/api/projects/${originalProjectId}/members/${collaborator.id}`, 'DELETE', undefined, ownerAccessToken);
  assert.ok(viewerRemoval.response.ok, viewerRemoval.payload.error?.message || 'Viewer project removal failed');
  await viewerPage.reload({ waitUntil: 'networkidle' });
  await viewerPage.getByText('pano ve iş öğelerine erişmek için proje üyeliği gerekir').waitFor();
  assert.equal(await viewerPage.locator('.board-management button').count(), 0, 'Removed member retained board access');
  await viewerContext.close();
  await desktop.close();

  const offlineShell = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const offlinePage = await offlineShell.newPage();
  await offlinePage.goto(`${baseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await assertPwa(offlinePage);
  await offlinePage.reload({ waitUntil: 'networkidle' });
  await offlineShell.setOffline(true);
  await offlinePage.reload({ waitUntil: 'domcontentloaded' });
  assert.equal(await offlinePage.title(), 'Zumbo Desktop');
  await offlineShell.setOffline(false);
  await offlineShell.close();

  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, colorScheme: 'light' });
  const mobilePage = await mobile.newPage();
  await attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${baseUrl}/mobile-ionic/index.html`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('button', { name: 'Demo kullanıcı oluştur' }).click();
  await mobilePage.locator('.metric-band').waitFor({ timeout: 30_000 });
  const mobileCollaborator = await mobilePage.evaluate(async stamp => {
    const currentUser = JSON.parse(localStorage.getItem('zumbo.currentUser'));
    const response = await fetch('http://localhost:5088/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: `mobilecollab${stamp}`,
        email: `mobilecollab${stamp}@zumbo.local`,
        password: 'P@ssword123',
        organizationId: currentUser.organizationId
      })
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.error?.message || 'Mobile collaborator registration failed');
    return payload.data.user;
  }, Date.now());
  await assertNoOverflow(mobilePage);
  await assertPwa(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-light.png'), fullPage: true });
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Çalışma' }).click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  const mobileStamp = String(Date.now()).slice(-6);
  const mobileTeamName = `Mobil Ekip ${mobileStamp}`;
  const renamedMobileTeam = `${mobileTeamName} Güncel`;
  await mobilePage.locator('.workspace-segments').getByText('Ekipler', { exact: true }).click();
  await mobilePage.getByLabel('Yeni mobil ekip adı').fill(mobileTeamName);
  await mobilePage.getByRole('button', { name: 'Ekip oluştur' }).click();
  const mobileTeamItem = mobilePage.locator('ion-item').filter({ hasText: mobileTeamName });
  await mobileTeamItem.waitFor();
  await mobileTeamItem.click();
  await mobilePage.waitForURL(/#\/teams\//);
  await mobilePage.getByLabel('Mobil ekip adı', { exact: true }).fill(renamedMobileTeam);
  const mobileTeamSave = mobilePage.waitForResponse(response => response.url().includes('/api/teams/') && response.request().method() === 'PUT');
  await mobilePage.getByRole('button', { name: 'Ekibi kaydet' }).click();
  assert.equal((await mobileTeamSave).status(), 200, 'Mobile team update failed');
  await mobilePage.getByLabel('Mobil ekip davet e-postası').fill(mobileCollaborator.email);
  await mobilePage.getByRole('button', { name: 'Davet et' }).click();
  const mobileInvitedMember = mobilePage.locator('.mobile-member-row').filter({ hasText: mobileCollaborator.email });
  await mobileInvitedMember.waitFor();
  await mobilePage.locator('[data-team-saving="false"]').waitFor();
  await mobileInvitedMember.getByRole('button', { name: 'Mobil ekip üyesini kaldır' }).click();
  await mobileInvitedMember.waitFor({ state: 'detached' });
  await mobilePage.getByRole('button', { name: 'Arşivle' }).click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  await mobilePage.locator('.workspace-segments').getByText('Arşiv', { exact: true }).click();
  const archivedMobileTeam = mobilePage.locator('.mobile-archive-row').filter({ hasText: renamedMobileTeam });
  await archivedMobileTeam.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileTeam.waitFor({ state: 'detached' });

  const mobileProjectName = `Mobil Proje ${mobileStamp}`;
  const renamedMobileProject = `${mobileProjectName} Güncel`;
  const mobileBoardName = `Mobil Pano ${mobileStamp}`;
  await mobilePage.locator('.workspace-segments').getByText('Projeler', { exact: true }).click();
  await mobilePage.getByLabel('Yeni mobil proje anahtarı').fill(`M${mobileStamp}`);
  await mobilePage.getByLabel('Yeni mobil proje adı').fill(mobileProjectName);
  await mobilePage.getByRole('button', { name: 'Proje oluştur' }).click();
  const mobileProjectItem = mobilePage.locator('ion-item').filter({ hasText: mobileProjectName });
  await mobileProjectItem.waitFor();
  await mobileProjectItem.click();
  await mobilePage.waitForURL(/#\/projects\//);
  await mobilePage.getByLabel('Mobil proje adı', { exact: true }).fill(renamedMobileProject);
  await mobilePage.getByRole('button', { name: 'Projeyi kaydet' }).click();
  await mobilePage.getByLabel('Yeni mobil pano adı').fill(mobileBoardName);
  await mobilePage.getByRole('button', { name: 'Pano oluştur' }).click();
  let mobileBoardRow = mobilePage.locator('.mobile-entity-row').filter({ has: mobilePage.getByLabel('Mobil pano adı', { exact: true }) }).first();
  await mobilePage.locator('[data-project-saving="false"]').waitFor();
  await mobileBoardRow.getByLabel('Mobil pano adı', { exact: true }).fill(`${mobileBoardName} Güncel`);
  await mobileBoardRow.getByRole('button', { name: 'Kaydet' }).click();
  mobileBoardRow = mobilePage.locator('.mobile-entity-row').filter({ has: mobilePage.getByLabel('Mobil pano adı', { exact: true }) }).last();
  await mobileBoardRow.getByRole('button', { name: 'Arşivle' }).click();
  const archivedMobileBoard = mobilePage.locator('.mobile-archive-row').filter({ hasText: `${mobileBoardName} Güncel` });
  await archivedMobileBoard.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileBoard.waitFor({ state: 'detached' });
  await assertNoOverflow(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-management.png'), fullPage: true });
  await mobilePage.getByRole('button', { name: 'Arşivle' }).first().click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  await mobilePage.locator('.workspace-segments').getByText('Arşiv', { exact: true }).click();
  const archivedMobileProject = mobilePage.locator('.mobile-archive-row').filter({ hasText: renamedMobileProject });
  await archivedMobileProject.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileProject.waitFor({ state: 'detached' });
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Profil' }).click();
  await mobilePage.waitForURL(/#\/app\/profile$/);
  await mobilePage.getByRole('button', { name: 'Temayı değiştir' }).click();
  await mobilePage.waitForFunction(() => document.body.classList.contains('theme-dark'));
  await mobilePage.locator('.profile img').waitFor();
  await mobilePage.getByRole('button', { name: 'Çıkış yap' }).waitFor();
  await mobilePage.waitForTimeout(350);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-dark.png'), fullPage: true });
  await mobile.close();

  const mobileOfflineShell = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const mobileOfflinePage = await mobileOfflineShell.newPage();
  const mobileOfflineErrors = [];
  mobileOfflinePage.on('pageerror', error => mobileOfflineErrors.push(error.message));
  mobileOfflinePage.on('console', message => {
    if (message.type() === 'error' && !message.text().includes('ERR_INTERNET_DISCONNECTED')) {
      mobileOfflineErrors.push(message.text());
    }
  });
  await mobileOfflinePage.goto(`${baseUrl}/mobile-ionic/index.html`, { waitUntil: 'networkidle' });
  await assertPwa(mobileOfflinePage);
  await mobileOfflinePage.reload({ waitUntil: 'networkidle' });
  const mobileCachedUrls = await cachedUrls(mobileOfflinePage);
  assert.ok(mobileCachedUrls.some(url => url.includes('ionic.bundle.min.js')), 'Ionic runtime was not cached');
  assert.ok(mobileCachedUrls.some(url => url.includes('ionic.min.css')), 'Ionic stylesheet was not cached');
  await mobileOfflineShell.setOffline(true);
  await mobileOfflinePage.reload({ waitUntil: 'domcontentloaded' });
  await mobileOfflinePage.waitForTimeout(2_000);
  assert.ok(
    await mobileOfflinePage.getByRole('button', { name: 'Demo kullanıcı oluştur' }).isVisible(),
    `Mobile offline shell did not render: ${mobileOfflineErrors.join(' | ')}`
  );
  await mobileOfflineShell.setOffline(false);
  await mobileOfflineShell.close();
} catch (error) {
  if (diagnosticPage && !diagnosticPage.isClosed()) {
    await diagnosticPage.screenshot({ path: resolve(outputDir, 'failure.png'), fullPage: true }).catch(() => {});
    const state = await diagnosticPage.evaluate(() => ({
      url: location.href,
      lanes: Array.from(document.querySelectorAll('.column-lane')).map(lane => lane.innerText),
      toasts: Array.from(document.querySelectorAll('.toast')).map(toast => toast.innerText),
      feedback: document.querySelector('.status-banner')?.innerText || null
    })).catch(() => null);
    const stateDetail = `UI state: ${JSON.stringify(state)}`;
    error.message += `\n${stateDetail}`;
    error.stack += `\n${stateDetail}`;
  }
  if (failures.length) {
    const failureDetail = `Diagnostics: ${failures.join(' | ')}`;
    error.message += `\n${failureDetail}`;
    error.stack += `\n${failureDetail}`;
  }
  throw error;
} finally {
  await browser.close();
}

assert.deepEqual(failures, [], failures.join('\n'));
writeFileSync(resolve(outputDir, 'result.json'), JSON.stringify({ passed: true, checks: ['desktop', 'mobile', 'themes', 'command', 'keyboard', 'deep-link', 'team-deep-link-reload', 'project-deep-link-reload', 'board-deep-link-reload', 'permission-loss-state', 'card-config', 'column-collapse', 'column-management', 'wip-conflict-rollback', 'workflow-management', 'saved-view-lifecycle', 'task-lifecycle', 'task-hierarchy-lifecycle', 'task-relation-lifecycle', 'task-approval-request', 'comment-lifecycle', 'label-lifecycle', 'worklog-lifecycle', 'team-lifecycle', 'team-invite-lifecycle', 'project-lifecycle', 'project-member-lifecycle', 'board-lifecycle', 'audit-timeline', 'organization-lifecycle', 'department-member-lifecycle', 'role-permission-lifecycle', 'mobile-team-lifecycle', 'mobile-team-invite-lifecycle', 'mobile-project-lifecycle', 'mobile-board-lifecycle', 'api-key-lifecycle', 'pwa'] }, null, 2));
console.log('UI quality checks passed: desktop/mobile, themes, keyboard, lifecycle, board configuration and PWA shell.');
