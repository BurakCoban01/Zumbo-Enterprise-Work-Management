import assert from 'node:assert/strict';
import test from 'node:test';
import { mobileRealBrowserTestTimeoutMs } from './v3-mobile-real-suite-config.mjs';

test('ordinary mobile real-browser scenarios keep the bounded four-minute timeout', () => {
  assert.equal(mobileRealBrowserTestTimeoutMs('v3-mobile-work-parity-real-browser.mjs', {}), 240_000);
});

test('mobile acceptance matrix receives a bounded thirty-minute timeout', () => {
  assert.equal(mobileRealBrowserTestTimeoutMs('v3-mobile-acceptance-real-browser.mjs', {}), 1_800_000);
});

test('ordinary and mobile matrix timeout overrides remain independent', () => {
  const environment = {
    ZUMBO_QA_TEST_TIMEOUT_MS: '300000',
    ZUMBO_QA_MOBILE_MATRIX_TEST_TIMEOUT_MS: '2400000'
  };
  assert.equal(mobileRealBrowserTestTimeoutMs('v3-mobile-ia-real-browser.mjs', environment), 300_000);
  assert.equal(mobileRealBrowserTestTimeoutMs('v3-mobile-acceptance-real-browser.mjs', environment), 2_400_000);
});

test('invalid, unbounded and subsecond mobile timeout values are rejected', () => {
  for (const value of ['not-a-number', '999', '7200001', '1500.5']) {
    assert.throws(
      () => mobileRealBrowserTestTimeoutMs('v3-mobile-work-parity-real-browser.mjs', {
        ZUMBO_QA_TEST_TIMEOUT_MS: value
      }),
      RangeError
    );
  }
  assert.throws(
    () => mobileRealBrowserTestTimeoutMs('v3-mobile-acceptance-real-browser.mjs', {
      ZUMBO_QA_MOBILE_MATRIX_TEST_TIMEOUT_MS: 'Infinity'
    }),
    RangeError
  );
});
