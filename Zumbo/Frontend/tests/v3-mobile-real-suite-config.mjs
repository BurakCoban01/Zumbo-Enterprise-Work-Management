const defaultTimeoutMs = 240_000;
const matrixTimeoutMs = 1_800_000;
const minimumTimeoutMs = 1_000;
const maximumTimeoutMs = 7_200_000;
const matrixTestFile = 'v3-mobile-acceptance-real-browser.mjs';

function boundedTimeout(name, value, fallback) {
  const parsed = Number(value ?? fallback);
  if (!Number.isInteger(parsed) || parsed < minimumTimeoutMs || parsed > maximumTimeoutMs) {
    throw new RangeError(
      `${name} must be an integer between ${minimumTimeoutMs} and ${maximumTimeoutMs} milliseconds.`
    );
  }
  return parsed;
}

export function mobileRealBrowserTestTimeoutMs(testFile, environment = process.env) {
  const ordinary = boundedTimeout('ZUMBO_QA_TEST_TIMEOUT_MS', environment.ZUMBO_QA_TEST_TIMEOUT_MS, defaultTimeoutMs);
  if (testFile !== matrixTestFile) return ordinary;
  return boundedTimeout(
    'ZUMBO_QA_MOBILE_MATRIX_TEST_TIMEOUT_MS',
    environment.ZUMBO_QA_MOBILE_MATRIX_TEST_TIMEOUT_MS,
    matrixTimeoutMs
  );
}
