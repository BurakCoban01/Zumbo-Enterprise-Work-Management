import { randomUUID } from 'node:crypto';

function slug(value, fallback) {
  const normalized = String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 24);
  return normalized || fallback;
}

export function createRunContext(taskId, browserName, now = Date.now(), uuid = randomUUID()) {
  const task = slug(taskId, 'e2e');
  const browser = slug(browserName, 'browser');
  const nonce = slug(uuid, 'run').replaceAll('-', '').slice(0, 10);
  const runId = `${task}-${browser}-${Number(now).toString(36)}-${nonce}`;

  return Object.freeze({
    taskId: String(taskId),
    browser,
    runId,
    tenants: Object.freeze({
      desktop: `${runId}-desktop`,
      mobile: `${runId}-mobile`
    })
  });
}

export function createCleanupLedger() {
  const entries = [];
  const keys = new Set();
  let completedResult = null;

  return {
    add(key, action) {
      if (completedResult) throw new Error('Cleanup ledger has already run.');
      if (typeof action !== 'function') throw new TypeError('Cleanup action must be a function.');
      if (keys.has(key)) return false;
      keys.add(key);
      entries.push({ key, action });
      return true;
    },

    async run() {
      if (completedResult) return completedResult;
      const results = [];
      for (const entry of [...entries].reverse()) {
        try {
          const detail = await entry.action();
          results.push({ key: entry.key, passed: true, detail: detail ?? null });
        } catch (error) {
          results.push({
            key: entry.key,
            passed: false,
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
      completedResult = Object.freeze({
        attempted: results.length,
        passed: results.filter(result => result.passed).length,
        failed: results.filter(result => !result.passed).length,
        results: Object.freeze(results)
      });
      return completedResult;
    }
  };
}
