const sha256Pattern = /^[0-9a-f]{64}$/;

function requireCondition(condition, message, issues) {
  if (!condition) issues.push(message);
}

export function validateDesktopVisualReview(matrix, review) {
  const issues = [];
  requireCondition(matrix?.taskId === 'V3-QA-001', 'Matrix taskId must be V3-QA-001.', issues);
  requireCondition(matrix?.status === 'Passed', 'Automated desktop matrix must pass before visual approval.', issues);
  requireCondition(
    matrix?.completedMatrixCaptures === matrix?.expectedMatrixCaptures,
    'Automated matrix capture count is incomplete.',
    issues
  );
  requireCondition(Array.isArray(matrix?.captures) && matrix.captures.length > 0, 'Matrix captures are missing.', issues);
  requireCondition(review?.schemaVersion === 1, 'Visual review schemaVersion must be 1.', issues);
  requireCondition(review?.taskId === matrix?.taskId, 'Visual review taskId does not match the matrix.', issues);
  requireCondition(review?.runId === matrix?.runId, 'Visual review runId does not match the matrix.', issues);
  requireCondition(review?.status === 'Approved', 'Visual review status must be Approved.', issues);
  requireCondition(
    Number.isFinite(Date.parse(review?.reviewedAtUtc || '')),
    'Visual review reviewedAtUtc must be an ISO timestamp.',
    issues
  );
  requireCondition(Array.isArray(review?.captureReviews), 'Visual review captureReviews must be an array.', issues);

  const captures = Array.isArray(matrix?.captures) ? matrix.captures : [];
  const captureReviews = Array.isArray(review?.captureReviews) ? review.captureReviews : [];
  const captureByScreenshot = new Map();
  for (const capture of captures) {
    requireCondition(
      typeof capture.screenshot === 'string' && capture.screenshot.length > 0,
      'Every matrix capture must name a screenshot.',
      issues
    );
    requireCondition(
      sha256Pattern.test(capture.sha256 || ''),
      `Capture ${capture.screenshot || '(unnamed)'} has an invalid SHA-256.`,
      issues
    );
    requireCondition(!captureByScreenshot.has(capture.screenshot), `Duplicate matrix capture ${capture.screenshot}.`, issues);
    captureByScreenshot.set(capture.screenshot, capture);
  }

  const reviewByScreenshot = new Map();
  for (const item of captureReviews) {
    requireCondition(
      typeof item.screenshot === 'string' && item.screenshot.length > 0,
      'Every visual review item must name a screenshot.',
      issues
    );
    requireCondition(!reviewByScreenshot.has(item.screenshot), `Duplicate visual review ${item.screenshot}.`, issues);
    reviewByScreenshot.set(item.screenshot, item);
  }

  requireCondition(
    captureReviews.length === captures.length,
    `Visual review covers ${captureReviews.length}/${captures.length} captures.`,
    issues
  );
  for (const capture of captures) {
    const item = reviewByScreenshot.get(capture.screenshot);
    requireCondition(!!item, `Missing visual review for ${capture.screenshot}.`, issues);
    if (!item) continue;
    requireCondition(item.sha256 === capture.sha256, `Visual review hash mismatch for ${capture.screenshot}.`, issues);
    requireCondition(item.approved === true, `Visual review did not approve ${capture.screenshot}.`, issues);
    for (const field of ['criticalOverlap', 'textClipping', 'horizontalOverflow', 'unreadableContent']) {
      requireCondition(item[field] === false, `${capture.screenshot} has unresolved ${field}.`, issues);
    }
    requireCondition(typeof item.notes === 'string', `${capture.screenshot} review notes must be a string.`, issues);
  }
  for (const item of captureReviews) {
    requireCondition(captureByScreenshot.has(item.screenshot), `Visual review references unknown capture ${item.screenshot}.`, issues);
  }

  const expectedAdditionalStates = Array.isArray(matrix?.expectedAdditionalStates)
    ? matrix.expectedAdditionalStates
    : [];
  const passedAdditionalStates = new Set(
    (Array.isArray(matrix?.stateCoverage) ? matrix.stateCoverage : [])
      .filter(state => state.passed)
      .map(state => state.state)
  );
  for (const state of expectedAdditionalStates) {
    requireCondition(passedAdditionalStates.has(state), `Additional state ${state} did not pass.`, issues);
  }

  if (issues.length) {
    const error = new Error(`Desktop visual review is not acceptable:\n${issues.join('\n')}`);
    error.issues = issues;
    throw error;
  }
  return {
    passed: true,
    taskId: matrix.taskId,
    runId: matrix.runId,
    reviewedCaptures: captures.length,
    reviewedAdditionalStates: expectedAdditionalStates.length
  };
}
