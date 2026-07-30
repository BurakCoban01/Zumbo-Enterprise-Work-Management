import assert from 'node:assert/strict';
import test from 'node:test';
import { realBrowserTestTimeoutMs } from './v3-desktop-real-suite-config.mjs';

test('ordinary real-browser scenarios keep the bounded four-minute timeout', () => {
  assert.equal(realBrowserTestTimeoutMs('v3-project-overview-real-browser.mjs', {}), 240_000);
});

test('desktop acceptance matrix receives a bounded thirty-minute timeout', () => {
  assert.equal(realBrowserTestTimeoutMs('v3-desktop-acceptance-real-browser.mjs', {}), 1_800_000);
});

test('ordinary and matrix timeout overrides remain independent', () => {
  const environment = {
    ZUMBO_QA_TEST_TIMEOUT_MS: '300000',
    ZUMBO_QA_MATRIX_TEST_TIMEOUT_MS: '2400000'
  };
  assert.equal(realBrowserTestTimeoutMs('v3-reporting-views-real-browser.mjs', environment), 300_000);
  assert.equal(realBrowserTestTimeoutMs('v3-desktop-acceptance-real-browser.mjs', environment), 2_400_000);
});

test('invalid, unbounded and subsecond timeout values are rejected', () => {
  for (const value of ['not-a-number', '999', '7200001', '1500.5']) {
    assert.throws(
      () => realBrowserTestTimeoutMs('v3-project-overview-real-browser.mjs', {
        ZUMBO_QA_TEST_TIMEOUT_MS: value
      }),
      RangeError
    );
  }
  assert.throws(
    () => realBrowserTestTimeoutMs('v3-desktop-acceptance-real-browser.mjs', {
      ZUMBO_QA_MATRIX_TEST_TIMEOUT_MS: 'Infinity'
    }),
    RangeError
  );
});
