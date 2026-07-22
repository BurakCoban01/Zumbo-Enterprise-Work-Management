import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend } from './build-frontend.mjs';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const password = 'P@ssword123';
const stamp = Date.now().toString(36);
const email = `platform006-${stamp}@zumbo.local`;
const organizationId = `platform006-org-${stamp}`;
const outputDirectory = resolve(import.meta.dirname, '../../artifacts/runtime/platform006-browser');
await mkdir(outputDirectory, { recursive: true });
await buildFrontend();

async function register() {
  const response = await fetch(`${apiBaseUrl}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      username: `platform006-${stamp}`,
      email,
      password,
      organizationId
    })
  });
  const payload = await response.json();
  assert.ok(response.ok, payload.error?.message || `Registration failed with ${response.status}`);
}

await register();
const frontendUrl = new URL(frontendBaseUrl);
const server = await startStaticServer(resolve(import.meta.dirname, '../dist'), {
  host: frontendUrl.hostname,
  port: Number(frontendUrl.port)
});
const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
});
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();
const failures = [];
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});

try {
  await page.goto(`${server.origin}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('input[autocomplete="username"]').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('form').getByRole('button', { name: /giri/i }).click();
  await page.locator('.side-nav').waitFor({ state: 'visible' });
  await page.getByTitle('Ayarlar').click();
  const privacySurface = page.locator('.danger-zone');
  await privacySurface.waitFor({ state: 'visible' });
  await privacySurface.screenshot({ path: resolve(outputDirectory, 'privacy-settings.png') });

  const result = await page.evaluate(async ({ apiBaseUrl, password }) => {
    const exportResponse = await fetch(`${apiBaseUrl}/api/auth/privacy/export.ndjson`, {
      credentials: 'include'
    });
    const reader = exportResponse.body.getReader();
    const decoder = new window.TextDecoder();
    let text = '';
    let chunks = 0;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      chunks++;
      text += decoder.decode(value, { stream: true });
    }
    text += decoder.decode();
    const lines = text.split('\n').filter(Boolean);
    const profile = JSON.parse(lines[0]);

    const jobResponse = await fetch(`${apiBaseUrl}/api/auth/privacy/anonymization-jobs`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-Token': sessionStorage.getItem('zumbo.csrfToken') || ''
      },
      body: JSON.stringify({ password, confirmation: 'ANONYMIZE' })
    });
    const receiptPayload = await jobResponse.json();
    const receipt = receiptPayload.data;
    let status;
    let statusCode;
    for (let attempt = 0; attempt < 100; attempt++) {
      const statusResponse = await fetch(
        `${apiBaseUrl}/api/auth/privacy/jobs/${receipt.job.id}/status`,
        { headers: { 'X-Privacy-Status-Token': receipt.statusToken } });
      statusCode = statusResponse.status;
      const statusPayload = await statusResponse.json();
      status = statusPayload.data;
      if (status?.state === 'Completed') break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    const wrongTokenResponse = await fetch(
      `${apiBaseUrl}/api/auth/privacy/jobs/${receipt.job.id}/status`,
      { headers: { 'X-Privacy-Status-Token': 'wrong-token' } });
    const staleSessionResponse = await fetch(`${apiBaseUrl}/api/auth/users`, {
      credentials: 'include'
    });
    return {
      exportStatus: exportResponse.status,
      exportContentType: exportResponse.headers.get('content-type'),
      exportFormat: exportResponse.headers.get('x-zumbo-export-format'),
      exportChunks: chunks,
      exportLines: lines.length,
      firstKind: profile.kind,
      jobCreateStatus: jobResponse.status,
      initialState: receipt.job.state,
      statusTokenExposed: Boolean(receipt.statusToken),
      finalStatusCode: statusCode,
      finalState: status?.state,
      progressPercent: status?.progressPercent,
      wrongTokenStatus: wrongTokenResponse.status,
      staleSessionStatus: staleSessionResponse.status
    };
  }, { apiBaseUrl, password });

  assert.equal(result.exportStatus, 200);
  assert.match(result.exportContentType, /^application\/x-ndjson/);
  assert.equal(result.exportFormat, 'ndjson-v1');
  assert.ok(result.exportChunks >= 1);
  assert.ok(result.exportLines >= 1);
  assert.equal(result.firstKind, 'profile');
  assert.equal(result.jobCreateStatus, 201);
  assert.equal(result.initialState, 'Pending');
  assert.equal(result.statusTokenExposed, true);
  assert.equal(result.finalStatusCode, 200);
  assert.equal(result.finalState, 'Completed');
  assert.equal(result.progressPercent, 100);
  assert.equal(result.wrongTokenStatus, 404);
  assert.equal(result.staleSessionStatus, 401);
  assert.deepEqual(failures, []);

  await writeFile(
    resolve(outputDirectory, 'result.json'),
    `${JSON.stringify({ passed: true, browser: 'chromium', ...result }, null, 2)}\n`);
  console.log('PLATFORM-006 browser privacy workflow passed.');
} catch (error) {
  await page.screenshot({ path: resolve(outputDirectory, 'failure.png'), fullPage: true }).catch(() => {});
  await writeFile(resolve(outputDirectory, 'result.json'), `${JSON.stringify({
    passed: false,
    error: error.stack || error.message,
    failures
  }, null, 2)}\n`);
  throw error;
} finally {
  await context.close();
  await browser.close();
  await server.close();
}
