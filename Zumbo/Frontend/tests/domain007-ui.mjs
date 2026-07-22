import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/playwright/chromium');
await mkdir(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
});
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'light' });
const page = await context.newPage();
const failures = [];
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});

try {
  await page.goto(`${frontendBaseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('.desktop-login form').getByRole('button', { name: 'Demo çalışma alanı oluştur' }).click();
  const firstTask = page.locator('.task').first();
  await firstTask.waitFor({ timeout: 30_000 });
  const projectId = await firstTask.getAttribute('data-project-id');
  assert.ok(projectId, 'Demo görevinin proje kimliği bulunamadı.');

  const schemaResult = await page.evaluate(async ({ apiUrl, id }) => {
    const headers = { 'X-CSRF-Token': sessionStorage.getItem('zumbo.csrfToken') };
    const currentResponse = await fetch(`${apiUrl}/api/work-item-schemas/${id}`, {
      credentials: 'include',
      headers
    });
    const current = await currentResponse.json();
    const schema = {
      issueTypes: [
        {
          key: 'Task',
          name: 'Task',
          description: 'Standard task',
          hierarchyLevel: 'Standard',
          active: true,
          position: 0
        },
        {
          key: 'Incident',
          name: 'Incident',
          description: 'Operational incident',
          hierarchyLevel: 'Standard',
          active: true,
          position: 1
        }
      ],
      customFields: [
        {
          key: 'severity',
          name: 'Severity',
          type: 'Select',
          required: true,
          indexed: true,
          maxLength: null,
          minimum: null,
          maximum: null,
          options: ['Critical', 'High', 'Medium', 'Low'],
          appliesToIssueTypes: ['Incident'],
          position: 0
        },
        {
          key: 'customer',
          name: 'Customer',
          type: 'Text',
          required: false,
          indexed: true,
          maxLength: 100,
          minimum: null,
          maximum: null,
          options: null,
          appliesToIssueTypes: ['Incident'],
          position: 1
        }
      ],
      layouts: [
        { issueTypeKey: 'Task', fieldKeys: [] },
        { issueTypeKey: 'Incident', fieldKeys: ['severity', 'customer'] }
      ]
    };
    const writeHeaders = {
      ...headers,
      'Content-Type': 'application/json'
    };
    if (current.data.version > 0) writeHeaders['If-Match'] = `"${current.data.version}"`;
    const response = await fetch(`${apiUrl}/api/work-item-schemas/${id}`, {
      method: 'PUT',
      credentials: 'include',
      headers: writeHeaders,
      body: JSON.stringify(schema)
    });
    return { status: response.status, payload: await response.json() };
  }, { apiUrl: apiBaseUrl, id: projectId });
  assert.equal(schemaResult.status, 200, JSON.stringify(schemaResult.payload));

  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('.task').first().waitFor({ timeout: 30_000 });
  await page.locator('.create-button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Görev' }).click();
  await page.locator('#new-task-type').selectOption('Incident');
  await page.locator('#new-task-title').fill('DOMAIN007 UI incident');
  await page.locator('#new-custom-severity').selectOption({ label: 'Critical' });
  await page.locator('#new-custom-customer').fill('Acme');
  assert.equal(await page.locator('#new-custom-severity option:checked').textContent(), 'Critical');
  assert.equal(await page.locator('#new-custom-customer').inputValue(), 'Acme');
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();

  const createdTask = page.locator('.task', { hasText: 'DOMAIN007 UI incident' });
  await createdTask.waitFor({ timeout: 30_000 });
  const taskId = await createdTask.getAttribute('data-work-item-id');
  assert.ok(taskId, 'Oluşturulan görevin kimliği bulunamadı.');
  await createdTask.click();
  await page.locator('#task-custom-severity').waitFor();
  assert.equal(await page.locator('#task-custom-severity option:checked').textContent(), 'Critical');
  assert.equal(await page.locator('#task-custom-customer').inputValue(), 'Acme');
  await assertNoHorizontalOverflow(page);
  await page.screenshot({
    path: resolve(outputDir, 'domain007-desktop.png'),
    fullPage: true
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/tasks/${taskId}`, { waitUntil: 'networkidle' });
  await page.getByText('Özel alanlar', { exact: true }).waitFor({ timeout: 30_000 });
  await page.getByText('Critical', { exact: true }).waitFor();
  await page.getByText('Acme', { exact: true }).waitFor();
  await assertNoHorizontalOverflow(page);
  await page.screenshot({
    path: resolve(outputDir, 'domain007-mobile.png'),
    fullPage: true
  });

  assert.deepEqual(failures, []);
  await writeFile(
    resolve(outputDir, 'domain007-result.json'),
    JSON.stringify({ passed: true, projectId, taskId }, null, 2));
} catch (error) {
  await writeFile(
    resolve(outputDir, 'domain007-result.json'),
    JSON.stringify({ passed: false, error: error.stack || error.message, failures }, null, 2));
  throw error;
} finally {
  await browser.close();
}

async function assertNoHorizontalOverflow(targetPage) {
  const overflow = await targetPage.locator('body').evaluate(element => element.scrollWidth - element.clientWidth);
  assert.ok(overflow <= 1, `body yatay taşması ${overflow}px`);
}
