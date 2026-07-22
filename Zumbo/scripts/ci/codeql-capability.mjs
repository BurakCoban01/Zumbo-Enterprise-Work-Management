export const CODEQL_AVAILABLE = 'Available';
export const CODEQL_EXTERNAL_UNAVAILABLE = 'ExternalPlatformUnavailable';
export const CODEQL_APPLICABLE = 'Applicable';
export const CODEQL_NOT_APPLICABLE = 'NotApplicableExternalPlatform';
export const CODEQL_LANGUAGE_JOBS = Object.freeze([
  'codeql-csharp',
  'codeql-javascript-typescript'
]);
export const REQUIRED_CI_JOBS = Object.freeze([
  'ci-contract',
  'backend-core',
  'provider-mongo',
  'provider-postgresql',
  'external-dependencies',
  'frontend',
  'runtime-browser',
  'security-containers'
]);

export function evaluateCodeqlCapability({ repositoryPrivate, codeSecurityVariable }) {
  const isPrivate = parseRepositoryPrivate(repositoryPrivate);
  const variable = parseCodeSecurityVariable(codeSecurityVariable);
  const enabled = !isPrivate || variable === 'true';
  return enabled
    ? {
        enabled: true,
        state: CODEQL_AVAILABLE,
        applicability: CODEQL_APPLICABLE,
        expectedCodeqlResult: 'success',
        variable
      }
    : {
        enabled: false,
        state: CODEQL_EXTERNAL_UNAVAILABLE,
        applicability: CODEQL_NOT_APPLICABLE,
        expectedCodeqlResult: 'skipped',
        variable
      };
}

export function validateCiSummary(input) {
  const failures = [];
  for (const job of REQUIRED_CI_JOBS) {
    const result = input.requiredJobResults?.[job];
    if (result !== 'success') failures.push(`${job} must be success, found ${display(result)}.`);
  }
  if (input.capabilityJobResult !== 'success') {
    failures.push(`codeql-capability must be success, found ${display(input.capabilityJobResult)}.`);
  }
  if (input.capabilityEnabled === 'true') {
    if (input.capabilityState !== CODEQL_AVAILABLE) failures.push('Enabled CodeQL requires the Available state.');
    if (input.applicability !== CODEQL_APPLICABLE) failures.push('Enabled CodeQL requires Applicable applicability.');
    for (const job of CODEQL_LANGUAGE_JOBS) {
      const result = input.codeqlJobResults?.[job];
      if (result !== 'success') failures.push(`Enabled CodeQL job ${job} must be success, found ${display(result)}.`);
    }
  } else if (input.capabilityEnabled === 'false') {
    if (input.capabilityState !== CODEQL_EXTERNAL_UNAVAILABLE) failures.push('Unavailable CodeQL requires ExternalPlatformUnavailable.');
    if (input.applicability !== CODEQL_NOT_APPLICABLE) failures.push('Unavailable CodeQL requires NotApplicableExternalPlatform.');
    for (const job of CODEQL_LANGUAGE_JOBS) {
      const result = input.codeqlJobResults?.[job];
      if (result !== 'skipped') failures.push(`Unavailable CodeQL job ${job} must be conditionally skipped, found ${display(result)}.`);
    }
  } else {
    failures.push(`CodeQL capability enabled output is ambiguous: ${display(input.capabilityEnabled)}.`);
  }
  return failures;
}

function parseRepositoryPrivate(value) {
  if (value === true || value === 'true') return true;
  if (value === false || value === 'false') return false;
  throw new Error(`Repository privacy is ambiguous: ${display(value)}.`);
}

function parseCodeSecurityVariable(value) {
  if (value === undefined || value === null || value === '' || value === 'unset' || value === 'false') {
    return value === 'false' ? 'false' : 'unset';
  }
  if (value === 'true') return 'true';
  throw new Error(`GITHUB_CODE_SECURITY_ENABLED is ambiguous: ${display(value)}.`);
}

function display(value) {
  return value === undefined || value === null || value === '' ? '<missing>' : `'${value}'`;
}
