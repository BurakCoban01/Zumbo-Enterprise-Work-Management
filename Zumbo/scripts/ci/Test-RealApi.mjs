import assert from 'node:assert/strict';

const apiUrl = requireUrl('ZUMBO_API_URL');
const adminEmail = requireValue('ZUMBO_IDENTITY_ADMIN_EMAIL');
const bootstrapToken = requireValue('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN');
const stamp = `${Date.now().toString(36)}-${process.pid}`;
const organizationId = `ci-api-${stamp}`;

const ready = await fetch(`${apiUrl}/health/ready`);
assert.equal(ready.status, 200, 'Real-dependency API readiness failed.');

const anonymous = await fetch(`${apiUrl}/api/auth/sessions`);
assert.equal(anonymous.status, 401, 'Authenticated API route did not reject an anonymous request.');

const registration = await request('/api/auth/register', {
  method: 'POST',
  body: {
    username: `ciadmin${stamp.replace(/[^a-z0-9]/g, '')}`,
    email: adminEmail,
    password: 'Ci-only-P@ssword123',
    organizationId,
    bootstrapToken
  }
});
assert.equal(registration.response.status, 200, registration.payload.error?.message || 'Bootstrap registration failed.');
assert.equal(registration.payload.data.user.organizationId, organizationId);
assert.ok(registration.payload.data.user.roles.includes('SystemAdmin'));
assert.ok(registration.payload.data.accessToken);

const sessions = await request('/api/auth/sessions', {
  token: registration.payload.data.accessToken
});
assert.equal(sessions.response.status, 200, sessions.payload.error?.message || 'Authenticated session query failed.');
assert.ok(Array.isArray(sessions.payload.data));

console.log(`Real API acceptance passed for synthetic tenant ${organizationId}; Compose teardown owns final data cleanup.`);

async function request(path, { method = 'GET', token, body } = {}) {
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload };
}

function requireValue(name) {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function requireUrl(name) {
  const value = requireValue(name);
  const url = new URL(value);
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error(`${name} must be HTTP(S).`);
  return url.toString().replace(/\/$/, '');
}
