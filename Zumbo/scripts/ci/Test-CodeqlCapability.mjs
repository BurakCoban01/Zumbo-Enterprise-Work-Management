import assert from 'node:assert/strict';
import { appendFileSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  CODEQL_APPLICABLE,
  CODEQL_AVAILABLE,
  CODEQL_EXTERNAL_UNAVAILABLE,
  CODEQL_NOT_APPLICABLE,
  evaluateCodeqlCapability
} from './codeql-capability.mjs';
import { applicationRoot } from '../repository-layout.mjs';

const policyPath = 'docs/quality/codeql-capability-policy.json';
const evidencePath = 'artifacts/quality/QA-001-codeql-capability.json';
const policy = JSON.parse(readFileSync(resolve(applicationRoot, policyPath), 'utf8'));
const evidence = JSON.parse(readFileSync(resolve(applicationRoot, evidencePath), 'utf8'));

assert.equal(policy.schemaVersion, 1);
assert.equal(policy.capability, 'GitHubCodeScanning');
assert.deepEqual(policy.codeql.languages, ['csharp', 'javascript-typescript']);
assert.deepEqual(policy.codeql.jobs, {
  csharp: 'codeql-csharp',
  'javascript-typescript': 'codeql-javascript-typescript'
});
assert.equal(policy.codeql.queries, 'security-extended');
assert.equal(policy.variable.name, 'GITHUB_CODE_SECURITY_ENABLED');
assert.equal(policy.variable.enabledValue, 'true');
assert.equal(policy.states.available.name, CODEQL_AVAILABLE);
assert.equal(policy.states.available.applicability, CODEQL_APPLICABLE);
assert.equal(policy.states.available.expectedCodeqlResult, 'success');
assert.equal(policy.states.unavailable.name, CODEQL_EXTERNAL_UNAVAILABLE);
assert.equal(policy.states.unavailable.applicability, CODEQL_NOT_APPLICABLE);
assert.equal(policy.states.unavailable.expectedCodeqlResult, 'skipped');
assert.equal(policy.states.unavailable.codeqlPassed, false);
assert.deepEqual(policy.mandatoryAlternativeJobs, ['security-containers', 'frontend', 'backend-core']);

const fixtures = [
  {
    name: 'public repository enables CodeQL',
    input: { repositoryPrivate: false, codeSecurityVariable: '' },
    expected: { enabled: true, state: CODEQL_AVAILABLE, applicability: CODEQL_APPLICABLE, expectedCodeqlResult: 'success', variable: 'unset' }
  },
  {
    name: 'audited variable enables private CodeQL',
    input: { repositoryPrivate: true, codeSecurityVariable: 'true' },
    expected: { enabled: true, state: CODEQL_AVAILABLE, applicability: CODEQL_APPLICABLE, expectedCodeqlResult: 'success', variable: 'true' }
  },
  {
    name: 'private repository without Code Security is externally unavailable',
    input: { repositoryPrivate: true, codeSecurityVariable: '' },
    expected: { enabled: false, state: CODEQL_EXTERNAL_UNAVAILABLE, applicability: CODEQL_NOT_APPLICABLE, expectedCodeqlResult: 'skipped', variable: 'unset' }
  },
  {
    name: 'explicit false remains externally unavailable',
    input: { repositoryPrivate: 'true', codeSecurityVariable: 'false' },
    expected: { enabled: false, state: CODEQL_EXTERNAL_UNAVAILABLE, applicability: CODEQL_NOT_APPLICABLE, expectedCodeqlResult: 'skipped', variable: 'false' }
  }
];
for (const fixture of fixtures) assert.deepEqual(evaluateCodeqlCapability(fixture.input), fixture.expected, fixture.name);
assert.throws(() => evaluateCodeqlCapability({ repositoryPrivate: 'unknown', codeSecurityVariable: '' }), /privacy is ambiguous/);
assert.throws(() => evaluateCodeqlCapability({ repositoryPrivate: true, codeSecurityVariable: 'yes' }), /is ambiguous/);
assert.throws(() => evaluateCodeqlCapability({ repositoryPrivate: true, codeSecurityVariable: 'TRUE' }), /is ambiguous/);

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'QA-001');
assert.equal(evidence.policy.path, policyPath);
assert.equal(evidence.repository.private, true);
assert.equal(evidence.repository.visibility, 'private');
assert.equal(evidence.repository.ownerType, 'User');
assert.equal(evidence.repository.plan, 'free');
assert.equal(evidence.variable.name, policy.variable.name);
assert.equal(evidence.variable.observedValue, 'unset');
const evidenceDecision = evaluateCodeqlCapability({
  repositoryPrivate: evidence.repository.private,
  codeSecurityVariable: evidence.variable.observedValue
});
assert.equal(evidence.decision.enabled, evidenceDecision.enabled);
assert.equal(evidence.decision.state, evidenceDecision.state);
assert.equal(evidence.decision.applicability, evidenceDecision.applicability);
assert.equal(evidence.decision.expectedCodeqlResult, evidenceDecision.expectedCodeqlResult);
assert.deepEqual(evidence.decision.expectedLanguageJobResults, {
  'codeql-csharp': evidenceDecision.expectedCodeqlResult,
  'codeql-javascript-typescript': evidenceDecision.expectedCodeqlResult
});
assert.equal(evidence.decision.codeqlPassed, false);
assert.equal(evidence.observation.codeScanningApiHttpStatus, 403);
assert.equal(evidence.observation.sarifUploadSucceeded, false);

if (process.argv.includes('--runtime')) {
  const actual = evaluateCodeqlCapability({
    repositoryPrivate: process.env.ZUMBO_REPOSITORY_PRIVATE,
    codeSecurityVariable: process.env.ZUMBO_CODE_SECURITY_VARIABLE
  });
  assert.equal(actual.enabled, evidence.decision.enabled, 'Runtime capability differs from reviewed evidence.');
  assert.equal(actual.state, evidence.decision.state, 'Runtime capability state differs from reviewed evidence.');
  assert.equal(actual.applicability, evidence.decision.applicability, 'Runtime applicability differs from reviewed evidence.');
  assert.ok(process.env.GITHUB_OUTPUT, 'GITHUB_OUTPUT is required in runtime mode.');
  appendFileSync(process.env.GITHUB_OUTPUT, [
    `enabled=${actual.enabled}`,
    `state=${actual.state}`,
    `applicability=${actual.applicability}`,
    `expected_codeql_result=${actual.expectedCodeqlResult}`,
    ''
  ].join('\n'));
}

console.log(`CodeQL capability contract passed: ${fixtures.length} positive fixtures, 3 ambiguous fixtures rejected; current=${evidence.decision.applicability}.`);
