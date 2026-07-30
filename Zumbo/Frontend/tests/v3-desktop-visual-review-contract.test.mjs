import assert from 'node:assert/strict';
import test from 'node:test';
import { validateDesktopVisualReview } from './v3-desktop-visual-review-contract.mjs';

const firstHash = 'a'.repeat(64);
const secondHash = 'b'.repeat(64);

function matrix() {
  return {
    taskId: 'V3-QA-001',
    runId: 'qa-run-1',
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

function review() {
  return {
    schemaVersion: 1,
    taskId: 'V3-QA-001',
    runId: 'qa-run-1',
    status: 'Approved',
    reviewedAtUtc: '2026-07-29T20:00:00Z',
    captureReviews: [
      approvedCapture('normal.png', firstHash),
      approvedCapture('offline.png', secondHash)
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

test('complete hash-bound explicit visual review passes', () => {
  assert.deepEqual(validateDesktopVisualReview(matrix(), review()), {
    passed: true,
    taskId: 'V3-QA-001',
    runId: 'qa-run-1',
    reviewedCaptures: 2,
    reviewedAdditionalStates: 1
  });
});

test('review cannot approve an incomplete or stale capture set', () => {
  const candidate = review();
  candidate.captureReviews.pop();
  candidate.captureReviews[0].sha256 = secondHash;
  assert.throws(
    () => validateDesktopVisualReview(matrix(), candidate),
    error => error.issues.some(issue => issue.includes('covers 1/2'))
      && error.issues.some(issue => issue.includes('hash mismatch'))
      && error.issues.some(issue => issue.includes('Missing visual review'))
  );
});

test('review cannot approve visual defects or an unpassed adverse state', () => {
  const candidateMatrix = matrix();
  candidateMatrix.stateCoverage[0].passed = false;
  const candidateReview = review();
  candidateReview.captureReviews[1].criticalOverlap = true;
  candidateReview.captureReviews[1].approved = false;
  assert.throws(
    () => validateDesktopVisualReview(candidateMatrix, candidateReview),
    error => error.issues.some(issue => issue.includes('did not approve'))
      && error.issues.some(issue => issue.includes('criticalOverlap'))
      && error.issues.some(issue => issue.includes('Additional state offline did not pass'))
  );
});

test('review is rejected before the automated matrix passes', () => {
  const candidateMatrix = matrix();
  candidateMatrix.status = 'Blocked';
  assert.throws(
    () => validateDesktopVisualReview(candidateMatrix, review()),
    error => error.issues.includes('Automated desktop matrix must pass before visual approval.')
  );
});
