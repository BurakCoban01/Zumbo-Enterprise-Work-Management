import assert from 'node:assert/strict';
import { frontendBaseUrl } from './environment.mjs';

const apiBaseUrl = (process.env.ZUMBO_SCALE_GATEWAY_URL || 'http://127.0.0.1:58089').replace(/\/$/, '');
const origin = new URL(frontendBaseUrl).origin;
const results = [];

for (let request = 1; request <= 11; request += 1) {
  const response = await fetch(`${apiBaseUrl}/api/browser-auth/login`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Origin: origin
    },
    body: JSON.stringify({
      usernameOrEmail: 'sec006-unknown-user@zumbo.invalid',
      password: 'P@ssword123'
    })
  });
  const payload = await response.json();
  results.push({
    request,
    status: response.status,
    instance: response.headers.get('x-zumbo-instance-id'),
    remaining: Number(response.headers.get('ratelimit-remaining')),
    errorCode: payload.error?.code
  });
}

assert.deepEqual(results.slice(0, 10).map(result => result.status), Array(10).fill(401));
assert.equal(results[10].status, 429);
assert.equal(results[10].errorCode, 'RATE_LIMIT_EXCEEDED');
assert.deepEqual(results.slice(0, 10).map(result => result.remaining), [9, 8, 7, 6, 5, 4, 3, 2, 1, 0]);
const instanceCounts = results.slice(0, 10).reduce((counts, result) => ({
  ...counts,
  [result.instance]: (counts[result.instance] || 0) + 1
}), {});
assert.equal(instanceCounts['api-1'], 5);
assert.equal(instanceCounts['api-2'], 5);

console.log(JSON.stringify({
  allowedAttempts: 10,
  rejectedAttempt: 11,
  instanceCounts,
  finalStatus: results[10].status,
  finalErrorCode: results[10].errorCode
}));
