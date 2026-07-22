import assert from 'node:assert/strict';
import {
  CODEQL_APPLICABLE,
  CODEQL_AVAILABLE,
  CODEQL_EXTERNAL_UNAVAILABLE,
  CODEQL_LANGUAGE_JOBS,
  CODEQL_NOT_APPLICABLE,
  REQUIRED_CI_JOBS,
  validateCiSummary
} from './codeql-capability.mjs';

const requiredJobResults = Object.fromEntries(REQUIRED_CI_JOBS.map(job => [job, 'success']));
const successfulCodeqlJobs = Object.fromEntries(CODEQL_LANGUAGE_JOBS.map(job => [job, 'success']));
const skippedCodeqlJobs = Object.fromEntries(CODEQL_LANGUAGE_JOBS.map(job => [job, 'skipped']));

const available = {
  requiredJobResults,
  capabilityJobResult: 'success',
  capabilityEnabled: 'true',
  capabilityState: CODEQL_AVAILABLE,
  applicability: CODEQL_APPLICABLE,
  codeqlJobResults: successfulCodeqlJobs
};
const unavailable = {
  requiredJobResults,
  capabilityJobResult: 'success',
  capabilityEnabled: 'false',
  capabilityState: CODEQL_EXTERNAL_UNAVAILABLE,
  applicability: CODEQL_NOT_APPLICABLE,
  codeqlJobResults: skippedCodeqlJobs
};

assert.deepEqual(validateCiSummary(available), []);
assert.deepEqual(validateCiSummary(unavailable), []);
assert.match(validateCiSummary({ ...available, codeqlJobResults: { ...successfulCodeqlJobs, 'codeql-csharp': 'skipped' } }).join('\n'), /codeql-csharp must be success/);
assert.match(validateCiSummary({ ...unavailable, codeqlJobResults: { ...skippedCodeqlJobs, 'codeql-csharp': 'success' } }).join('\n'), /codeql-csharp must be conditionally skipped/);
assert.match(validateCiSummary({ ...unavailable, codeqlJobResults: { ...skippedCodeqlJobs, 'codeql-javascript-typescript': 'failure' } }).join('\n'), /codeql-javascript-typescript must be conditionally skipped/);
assert.match(validateCiSummary({ ...unavailable, capabilityEnabled: '' }).join('\n'), /ambiguous/);
assert.match(validateCiSummary({ ...unavailable, capabilityState: CODEQL_AVAILABLE }).join('\n'), /ExternalPlatformUnavailable/);
assert.match(validateCiSummary({ ...available, capabilityJobResult: 'failure' }).join('\n'), /codeql-capability must be success/);
assert.match(validateCiSummary({ ...available, requiredJobResults: { ...requiredJobResults, 'security-containers': 'failure' } }).join('\n'), /security-containers must be success/);
assert.match(validateCiSummary({ ...available, requiredJobResults: { ...requiredJobResults, frontend: undefined } }).join('\n'), /frontend must be success, found <missing>/);
assert.match(validateCiSummary({ ...unavailable, codeqlJobResults: { ...skippedCodeqlJobs, 'codeql-csharp': undefined } }).join('\n'), /codeql-csharp must be conditionally skipped, found <missing>/);

if (process.argv.includes('--runtime')) {
  const actual = {
    requiredJobResults: Object.fromEntries(Object.keys(requiredJobResults).map(job => [job, process.env[`ZUMBO_RESULT_${job.replaceAll('-', '_').toUpperCase()}`]])),
    capabilityJobResult: process.env.ZUMBO_RESULT_CODEQL_CAPABILITY,
    capabilityEnabled: process.env.ZUMBO_CODEQL_ENABLED,
    capabilityState: process.env.ZUMBO_CODEQL_STATE,
    applicability: process.env.ZUMBO_CODEQL_APPLICABILITY,
    codeqlJobResults: {
      'codeql-csharp': process.env.ZUMBO_RESULT_CODEQL_CSHARP,
      'codeql-javascript-typescript': process.env.ZUMBO_RESULT_CODEQL_JAVASCRIPT_TYPESCRIPT
    }
  };
  const failures = validateCiSummary(actual);
  assert.deepEqual(failures, [], failures.join('\n'));
}

console.log('CI summary policy contract passed: 2 accepted paths and 9 rejected failure/ambiguity fixtures.');
