import assert from 'node:assert/strict';
import test from 'node:test';
import { validateMobileVisualReview } from './v3-mobile-visual-review-contract.mjs';

const firstHash = 'a'.repeat(64);
const secondHash = 'b'.repeat(64);

function matrix() {
  return {
    taskId: 'V3-QA-002',
    runId: 'mobile-qa-run-1',
    status: 'Passed',
    expectedMatrixCaptures: 1,
    completedMatrixCaptures: 1,
    expectedAdditionalStates: ['offline'],
    stateCoverage: [{ state: 'offline', passed: true }],
    captures: [
      { screenshot: 'normal.png', sha256: firstHash },
      { screenshot: 'offline.png', sha256: secondHash }
    ]
  };
}

function approvedCapture(screenshot, sha256) {
  return {
    screenshot,
    sha256,
    approved: true,
    criticalOverlap: false,
    textClipping: false,
    horizontalOverflow: false,
    unreadableContent: false,
    notes: ''
  };
}

function review() {
  return {
    schemaVersion: 1,
    taskId: 'V3-QA-002',
    runId: 'mobile-qa-run-1',
    status: 'Approved',
    reviewedAtUtc: '2026-07-29T20:00:00Z',
    captureReviews: [
      approvedCapture('normal.png', firstHash),
      approvedCapture('offline.png', secondHash)
    ]
  };
}

test('complete hash-bound mobile visual review passes', () => {
  assert.deepEqual(validateMobileVisualReview(matrix(), review()), {
    passed: true,
    taskId: 'V3-QA-002',
    runId: 'mobile-qa-run-1',
    reviewedCaptures: 2,
    reviewedAdditionalStates: 1
  });
});

test('mobile review cannot approve an incomplete or stale capture set', () => {
  const candidate = review();
  candidate.captureReviews.pop();
  candidate.captureReviews[0].sha256 = secondHash;
  assert.throws(
    () => validateMobileVisualReview(matrix(), candidate),
    error => error.issues.some(issue => issue.includes('covers 1/2'))
      && error.issues.some(issue => issue.includes('hash mismatch'))
      && error.issues.some(issue => issue.includes('Missing visual review'))
  );
});

test('mobile review cannot approve visual defects or an unpassed adverse state', () => {
  const candidateMatrix = matrix();
  candidateMatrix.stateCoverage[0].passed = false;
  const candidateReview = review();
  candidateReview.captureReviews[1].criticalOverlap = true;
  candidateReview.captureReviews[1].approved = false;
  assert.throws(
    () => validateMobileVisualReview(candidateMatrix, candidateReview),
    error => error.issues.some(issue => issue.includes('did not approve'))
      && error.issues.some(issue => issue.includes('criticalOverlap'))
      && error.issues.some(issue => issue.includes('Additional state offline did not pass'))
  );
});

test('mobile review is rejected before the automated matrix passes', () => {
  const candidateMatrix = matrix();
  candidateMatrix.status = 'Blocked';
  assert.throws(
    () => validateMobileVisualReview(candidateMatrix, review()),
    error => error.issues.includes('Automated mobile matrix must pass before visual approval.')
  );
});
