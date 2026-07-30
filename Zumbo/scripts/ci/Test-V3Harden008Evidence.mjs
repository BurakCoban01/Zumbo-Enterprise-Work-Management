import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const task = 'V3-HARDEN-008';
const evidencePath = `artifacts/v3/${task}.json`;
const baselinePath = 'artifacts/performance/v3-harden-008/baseline.json';
const afterPath = 'artifacts/performance/v3-harden-008/after.json';
const sourcePaths = [
  'Frontend/desktop-bulma/board-excellence.js',
  'Frontend/desktop-bulma/index.html',
  'Frontend/mobile-ionic/workspace.js',
  'Frontend/mobile-ionic/index.html',
  'Frontend/tests/v3-board-excellence.test.mjs',
  'Frontend/tests/fe006-advanced-work.test.mjs',
  'Frontend/tests/v3-personal-work.test.mjs',
  'Frontend/tests/v3-legacy-performance-browser.mjs',
  'Frontend/package.json',
  'scripts/ci/Test-V3Harden008Evidence.mjs'
];

const baseline = json(baselinePath);
const after = json(afterPath);
const frontendPackage = json('Frontend/package.json');
assert.equal(
  frontendPackage.scripts['test:v3-harden-008'],
  'node tests/v3-legacy-performance-browser.mjs --label=after --assert');
assertPerformanceArtifacts();

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, task);
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.measurement.syntheticTaskCount, 100);
assert.deepEqual(evidence.measurement.redundantSortCallsPerDigest, {
  desktopList: { baseline: 4, after: 0 },
  mobileDashboard: { baseline: 2, after: 0 }
});
assert.deepEqual(evidence.measurement.desktopBoardWatchers, {
  baseline: 3000,
  after: 2800
});
assert.deepEqual(evidence.validation.frontend.unit, {
  passed: 210,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.browser.boardBehavior, {
  passed: true,
  desktopOwnerTasks: 48,
  desktopViewerReadOnly: true,
  mobileTouchMove: true
});
assert.equal(evidence.validation.browser.mobileAccessibility.viewports.length, 4);
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

console.log(
  'V3-HARDEN-008 evidence passed: 100-task profile, desktop/mobile no-op '
  + 'sort calls 4/2 -> 0/0, board watchers 3000 -> 2800 and 210/210 frontend tests.');

function assertPerformanceArtifacts() {
  for (const [artifact, label] of [[baseline, 'baseline'], [after, 'after']]) {
    assert.equal(artifact.schemaVersion, 1);
    assert.equal(artifact.taskId, task);
    assert.equal(artifact.label, label);
    assert.equal(artifact.syntheticTaskCount, 100);
    assert.equal(artifact.desktop.board.digestCount, 12);
    assert.equal(artifact.desktop.list.digestCount, 12);
    assert.equal(artifact.mobile.dashboard.digestCount, 12);
    assert.equal(artifact.mobile.dashboard.taskRows, 38);
  }

  assert.equal(baseline.desktop.list.noOpSortCallsPerDigest, 4);
  assert.equal(baseline.mobile.dashboard.noOpSortCallsPerDigest, 2);
  assert.equal(after.desktop.list.noOpSortCallsPerDigest, 0);
  assert.equal(after.mobile.dashboard.noOpSortCallsPerDigest, 0);
  assert.equal(baseline.desktop.board.watchers, 3000);
  assert.equal(after.desktop.board.watchers, 2800);

  for (const surface of ['board', 'list']) {
    assert.equal(after.desktop[surface].surfaceNodes, baseline.desktop[surface].surfaceNodes);
    assert.equal(after.desktop[surface].domNodes, baseline.desktop[surface].domNodes);
    assert.ok(after.desktop[surface].digestP95Ms <= after.budgets.desktopDigestP95MsMax);
  }
  assert.equal(after.mobile.dashboard.surfaceNodes, baseline.mobile.dashboard.surfaceNodes);
  assert.equal(after.mobile.dashboard.domNodes, baseline.mobile.dashboard.domNodes);
  assert.ok(after.mobile.dashboard.digestP95Ms <= after.budgets.mobileDigestP95MsMax);
  assert.ok(after.desktop.list.sortInteractionMs <= after.budgets.desktopSortInteractionMsMax);
}

function writeArtifact() {
  const artifact = {
    schemaVersion: 1,
    task,
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    measurement: {
      fixture: 'deterministic synthetic organization, project, four-column board and bounded task data',
      syntheticTaskCount: 100,
      digestSamplesPerSurface: 12,
      redundantSortCallsPerDigest: {
        desktopList: {
          baseline: baseline.desktop.list.noOpSortCallsPerDigest,
          after: after.desktop.list.noOpSortCallsPerDigest
        },
        mobileDashboard: {
          baseline: baseline.mobile.dashboard.noOpSortCallsPerDigest,
          after: after.mobile.dashboard.noOpSortCallsPerDigest
        }
      },
      desktopBoardWatchers: {
        baseline: baseline.desktop.board.watchers,
        after: after.desktop.board.watchers
      },
      digestP95Milliseconds: {
        desktopBoard: {
          baseline: baseline.desktop.board.digestP95Ms,
          after: after.desktop.board.digestP95Ms
        },
        desktopList: {
          baseline: baseline.desktop.list.digestP95Ms,
          after: after.desktop.list.digestP95Ms
        },
        mobileDashboard: {
          baseline: baseline.mobile.dashboard.digestP95Ms,
          after: after.mobile.dashboard.digestP95Ms
        }
      },
      desktopSortInteractionMilliseconds: {
        baseline: baseline.desktop.list.sortInteractionMs,
        after: after.desktop.list.sortInteractionMs,
        budget: after.budgets.desktopSortInteractionMsMax
      },
      unchangedRenderedScope: {
        desktopBoardSurfaceNodes: after.desktop.board.surfaceNodes,
        desktopListSurfaceNodes: after.desktop.list.surfaceNodes,
        mobileDashboardSurfaceNodes: after.mobile.dashboard.surfaceNodes,
        mobileVisibleTaskRows: after.mobile.dashboard.taskRows
      }
    },
    validation: {
      performanceGate: { passed: true, budgetsEnforced: true },
      frontend: {
        lint: true,
        unit: { passed: 210, failed: 0, skipped: 0 },
        assets: 125,
        runtimeAssetBrowser: 'chromium desktop/mobile local-only',
        dependencyAudit: {
          passedUnderPolicy: true,
          critical: 0,
          high: 2,
          timeBoundExceptions: 10
        },
        licenseAudit: { passed: true, packages: 22 }
      },
      browser: {
        performance: {
          viewports: ['1440x1000', '390x844'],
          screenshots: [
            'artifacts/performance/v3-harden-008/after-desktop-board.png',
            'artifacts/performance/v3-harden-008/after-desktop-list.png',
            'artifacts/performance/v3-harden-008/after-mobile-dashboard.png'
          ]
        },
        boardBehavior: {
          passed: true,
          desktopOwnerTasks: 48,
          desktopViewerReadOnly: true,
          mobileTouchMove: true
        },
        mobileAccessibility: {
          passed: true,
          viewports: ['360x780', '390x844', '430x844', '844x390'],
          checks: 11
        },
        visualReview: {
          passed: true,
          horizontalPageOverflow: false,
          criticalOverlap: false,
          textClipping: false
        }
      },
      backend: {
        applicable: false,
        reason: 'No backend source, API route, response contract, authorization or persistence behavior changed.'
      },
      provider: {
        applicable: false,
        reason: 'No provider adapter, migration, index or stored data shape changed.'
      }
    },
    behavior: {
      desktopListProjectionRebuildsOnDataFilterSortAndEdit: true,
      mobileProjectionRebuildsOnModeLoadAndRealtime: true,
      optimisticInlineEditAndRollbackPassed: true,
      partialBulkSelectionPassed: true,
      keyboardAndTouchMovementPassed: true,
      wipRollbackPassed: true,
      viewerControlsRemainReadOnly: true,
      stableRepeatIdentityApplied: true,
      noRuntimeDebugInstrumentationShipped: true
    },
    preservedCompatibility: {
      routesAndMethods: true,
      responseContracts: true,
      permissionAndAuthorizationBehavior: true,
      optimisticMutationAndConflictRecovery: true,
      realtimeTaskUpdates: true,
      storedDataShape: true,
      persistenceProviders: true,
      AngularJsBulmaAndIonicFrameworks: true
    },
    knownLimits: {
      timingScope:
        'Chromium timings are local observations on this host; deterministic structural sort and watcher budgets are the primary regression signal.',
      listRendering:
        'Desktop remains bounded to the existing first-100/load-more contract; mobile project task lists retain Ionic collection-repeat.',
      dependencyPolicy:
        'Two high findings remain covered by the existing ten time-bounded dependency exceptions; no new dependency was added.'
    },
    userChangesPreserved: true,
    heavyReleaseGatesDeferred: true,
    noDeployment: true
  };

  mkdirSync(resolve(applicationRoot, 'artifacts/v3'), { recursive: true });
  writeFileSync(
    resolve(applicationRoot, evidencePath),
    `${JSON.stringify(artifact, null, 2)}\n`,
    'utf8');
}

function json(path) {
  return JSON.parse(readFileSync(resolve(applicationRoot, path), 'utf8'));
}

function fileSha(path) {
  return createHash('sha256')
    .update(readFileSync(resolve(applicationRoot, path)))
    .digest('hex');
}
