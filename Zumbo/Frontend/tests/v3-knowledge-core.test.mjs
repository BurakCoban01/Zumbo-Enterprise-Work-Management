import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/knowledge-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/knowledge-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/knowledge-center.css'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/knowledge-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/knowledge-center.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

test('knowledge core emits structured Markdown without unsafe link targets', () => {
  const blocks = core.parseMarkdown([
    '# Release notes',
    '',
    '- **Ready**',
    '- [Runbook](/runbooks/release)',
    '',
    '`npm test` and [unsafe](javascript:alert(1))',
    '',
    '```sh',
    'pnpm test',
    '```'
  ].join('\n'));

  assert.equal(blocks[0].type, 'heading');
  assert.equal(blocks[1].type, 'list');
  assert.deepEqual(blocks[1].items[1][0], {
    type: 'link',
    text: 'Runbook',
    href: '/runbooks/release'
  });
  assert.equal(blocks[2].segments[0].type, 'code');
  assert.equal(blocks[2].segments.some((segment) => segment.type === 'link'), false);
  assert.match(blocks[2].segments.map((segment) => segment.text).join(''), /unsafe/);
  assert.equal(blocks[3].type, 'code');
  assert.equal(core.safeLink('data:text/html,unsafe'), null);
  assert.equal(core.safeLink('https://docs.example.test/release'), 'https://docs.example.test/release');
});

test('knowledge core limits editable scopes and normalizes version payloads', () => {
  const scopes = core.scopeOptions([
    {
      id: 'project-owner',
      key: 'OWN',
      name: 'Owned project',
      members: [{ userId: 'user-1', role: 'ProjectOwner' }]
    },
    {
      id: 'project-viewer',
      key: 'VIEW',
      name: 'Viewer project',
      members: [{ userId: 'user-1', role: 'Viewer' }]
    }
  ], [{
    name: 'Product portfolio',
    canEdit: false,
    initiatives: [
      { id: 'initiative-owned', name: 'Owned initiative', ownerUserId: 'user-1' },
      { id: 'initiative-hidden', name: 'Hidden initiative', ownerUserId: 'user-2' }
    ]
  }], 'user-1');

  assert.deepEqual(scopes.map((scope) => scope.key), [
    'Project:project-owner',
    'Initiative:initiative-owned'
  ]);

  const value = core.draft(scopes[0]);
  value.title = ' Release readiness ';
  value.contentMarkdown = '# Gate';
  value.tagsText = 'Release, release, Security';
  value.workItemIds = ['work-1', 'work-1'];
  value.userIds = ['user-2', 'user-2'];
  value.changeSummary = ' Initial version ';
  assert.deepEqual(core.createPayload(value, scopes[0]), {
    title: 'Release readiness',
    contentMarkdown: '# Gate',
    tags: ['Release', 'Security'],
    workItemIds: ['work-1'],
    userIds: ['user-2'],
    changeSummary: 'Initial version',
    scopeType: 'Project',
    scopeId: 'project-owner'
  });
});

test('desktop and mobile expose version, link, comment, search and archive workflows safely', () => {
  for (const source of [desktopSource, mobileSource]) {
    assert.match(source, /\/api\/knowledge-documents\?page=/);
    assert.match(source, /scope-link-options/);
    assert.match(source, /\/versions\//);
    assert.match(source, /\/comments/);
    assert.match(source, /\/resolve/);
    assert.match(source, /apiClient\.delete\('\/api\/knowledge-documents\//);
  }

  const combined = [desktopSource, desktopHtml, mobileSource, mobileHtml].join('\n');
  assert.doesNotMatch(combined, /ng-bind-html|\$sce|innerHTML/);
  assert.match(desktopHtml, /vm\.activeSection === 'knowledge'/);
  assert.match(desktopHtml, /<knowledge-segments segments=/);
  assert.match(desktopCss, /grid-template-columns:/);
  assert.match(mobileHtml, /templates\/knowledge\.html/);
  assert.match(mobileHtml, /ui-sref="knowledge-center"/);
  assert.match(mobileHtml, /<mobile-knowledge-segments segments=/);
  assert.match(mobileCss, /min-height:\s*44px/);
  assert.match(apiClientSource, /capacity-plans\|knowledge-documents/);
});
