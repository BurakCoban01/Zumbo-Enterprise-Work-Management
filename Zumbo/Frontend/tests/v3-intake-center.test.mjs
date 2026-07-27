import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/intake-core.js'));
const apiClient = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');
const desktopApp = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const desktopFeature = await readFile(resolve(root, 'desktop-bulma/intake-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/intake-center.css'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileFeature = await readFile(resolve(root, 'mobile-ionic/intake-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/intake-center.css'), 'utf8');
const backendContracts = await readFile(
  resolve(root, '../Backend/src/Zumbo.Modules.WorkItems/IntakeContracts.cs'),
  'utf8'
);
const backendEndpoints = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Endpoints/IntakeEndpoints.cs'),
  'utf8'
);

function draft() {
  return core.newDraft(
    { id: 'project-1' },
    [{ id: 'board-1', name: 'Talep panosu' }]
  );
}

test('intake draft normalizes the versioned backend contract without silent field loss', () => {
  const model = draft();
  model.name = 'Destek talebi';
  model.definition.fields.push({
    key: 'kategori',
    label: 'Kategori',
    type: 'Choice',
    required: true,
    helpText: 'Bir seçenek belirleyin',
    optionsText: 'Erişim\nHata\nerişim'
  });
  model.definition.mapping.priorityFieldKey = 'kategori';

  assert.equal(core.validateDraft(model), null);
  const request = core.requestFor(model);
  assert.equal(request.projectId, 'project-1');
  assert.equal(request.definition.boardId, 'board-1');
  assert.deepEqual(request.definition.fields.at(-1).options, ['Erişim', 'Hata']);
  assert.equal(request.definition.mapping.titleFieldKey, 'baslik');
  assert.match(backendContracts, /CreateIntakeFormRequest/);
  assert.match(backendContracts, /IntakeFieldMappingRequest/);
});

test('intake validation rejects invalid title and non-bijective custom mappings', () => {
  const model = draft();
  model.name = 'Form';
  model.definition.fields[0].required = false;
  assert.match(core.validateDraft(model), /Başlık eşlemesi/);

  model.definition.fields[0].required = true;
  model.definition.mapping.customFields = [
    { intakeFieldKey: 'baslik', workItemFieldKey: 'customer' },
    { intakeFieldKey: 'aciklama', workItemFieldKey: 'customer' }
  ];
  assert.match(core.validateDraft(model), /bire bir/);
});

test('submission projection preserves typed values and enforces attachment budgets before upload', () => {
  const form = {
    fields: [
      { key: 'baslik', label: 'Başlık', type: 'Text', required: true },
      { key: 'onay', label: 'Onay', type: 'Checkbox', required: false },
      { key: 'dosya', label: 'Dosya', type: 'Attachment', required: true }
    ]
  };
  const model = core.submissionModel(form);
  assert.match(core.validateSubmission(form, model), /Başlık/);
  model.values.baslik = 'VPN erişimi';
  model.values.onay = true;
  model.files.dosya = [{ name: 'kanıt.txt', size: 1024 }];
  assert.equal(core.validateSubmission(form, model), null);
  assert.deepEqual(core.submissionPayload(form, model).values, [
    { fieldKey: 'baslik', value: 'VPN erişimi' },
    { fieldKey: 'onay', value: 'true' }
  ]);

  model.files.dosya = [{ name: 'large.bin', size: core.limits.attachmentBytes + 1 }];
  assert.match(core.validateSubmission(form, model), /10 MB/);
});

test('desktop surface exposes normal, read-only, public, triage and idempotent submission paths', () => {
  assert.match(desktopApp, /value: 'intake'/);
  assert.match(desktopFeature, /\/api\/intake\/forms\?projectId=/);
  assert.match(desktopFeature, /\/submissions/);
  assert.match(desktopFeature, /newIdempotencyKey\(\)/);
  assert.match(desktopFeature, /applyPublicIntakeLocation/);
  assert.match(desktopHtml, /workMode === 'intake'/);
  assert.match(desktopHtml, /Intake ve triage merkezi/);
  assert.match(desktopHtml, /vm\.canManageIntake\(\)/);
  assert.match(desktopHtml, /vm\.triageIntakeSubmission/);
  assert.match(desktopHtml, /class="public-intake-shell"/);
  assert.match(desktopCss, /grid-template-columns: minmax\(250px, 320px\) minmax\(0, 1fr\)/);
  assert.match(apiClient, /kind: 'intake-forms'/);
  assert.match(backendEndpoints, /RequireRateLimiting\("intake-public"\)/);
});

test('mobile surface has protected project parity and an unprotected public form state', () => {
  assert.match(mobileApp, /state\('public-intake', \{ url:/);
  assert.match(mobileApp, /state\('project-intake', protectedState/);
  assert.match(mobileFeature, /controller\('MobileIntakeController'/);
  assert.match(mobileFeature, /controller\('PublicIntakeController'/);
  assert.match(mobileFeature, /contentTypeUndefined: true/);
  assert.match(mobileHtml, /aria-label="Mobil intake çalışma alanları"/);
  assert.match(mobileHtml, /templates\/public-intake\.html/);
  assert.match(mobileHtml, /vm\.triage\(submission,state\.id\)/);
  assert.match(mobileCss, /min-height: 44px/);
  assert.match(mobileCss, /@media \(max-width: 360px\)/);
});
