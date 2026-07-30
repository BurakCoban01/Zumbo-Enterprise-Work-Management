function freezeItems(items) {
  return Object.freeze(items.map(item => Object.freeze(item)));
}

export function profile(id, width, height, orientation, theme, reducedMotion) {
  return { id, width, height, orientation, theme, reducedMotion };
}

export function surface(id, stateName, path, selector, authentication, evidenceState) {
  return { id, stateName, path, selector, authentication, evidenceState };
}

export const profiles = freezeItems([
  profile('430-portrait-light-reduced', 430, 932, 'portrait', 'light', 'reduce'),
  profile('390-portrait-dark-reduced', 390, 844, 'portrait', 'dark', 'reduce'),
  profile('360-portrait-light-motion', 360, 800, 'portrait', 'light', 'no-preference'),
  profile('844-landscape-dark-reduced', 844, 390, 'landscape', 'dark', 'reduce')
]);

export const mobileSurfaces = freezeItems([
  surface('login', 'login', '/login', '.login-entry-surface', 'anonymous', 'normal'),
  surface('forgot-password', 'forgot-password', '/forgot-password', '.login-form', 'anonymous', 'normal'),
  surface('reset-password', 'reset-password', '/reset-password?token=invalid-qa-token', '.login-form', 'anonymous', 'negative'),
  surface('public-intake', 'public-intake', '/intake/:publicId', '.mobile-public-intake', 'anonymous', 'high-data'),
  surface('project-detail', 'project-detail', '/projects/:projectId', '[data-project-saving]', 'authenticated', 'high-data'),
  surface('project-catalog', 'project-catalog', '/projects/:projectId/catalog?tab=releases', '.mobile-catalog', 'authenticated', 'high-data'),
  surface('project-intake', 'project-intake', '/projects/:projectId/intake?tab=forms', '.mobile-intake', 'authenticated', 'high-data'),
  surface('project-automation', 'project-automation', '/projects/:projectId/automation?tab=rules', '.mobile-automation', 'authenticated', 'empty'),
  surface('project-jobs', 'project-jobs', '/projects/:projectId/jobs?mode=history', '.mobile-job-center', 'authenticated', 'empty'),
  surface('project-planning', 'project-planning', '/projects/:projectId/plan?mode=calendar', '.mobile-plan-surface', 'authenticated', 'high-data'),
  surface('project-reporting', 'project-reporting', '/projects/:projectId/insights?mode=reports&range=30', '.mobile-report-surface', 'authenticated', 'high-data'),
  surface('team-detail', 'team-detail', '/teams/:teamId', '[data-team-saving]', 'authenticated', 'high-data'),
  surface('task-detail', 'task-detail', '/tasks/:taskId', '.mobile-task-detail', 'authenticated', 'high-data'),
  surface('integration-center', 'integration-center', '/profile/integrations', '.mobile-integration-center', 'authenticated', 'empty'),
  surface('operations-center', 'operations-center', '/profile/operations', '.mobile-operations-center', 'authenticated', 'normal'),
  surface('portfolio-center', 'portfolio-center', '/portfolios', '.mobile-portfolio', 'authenticated', 'empty'),
  surface('goal-center', 'goal-center', '/goals', '.mobile-goal', 'authenticated', 'empty'),
  surface('capacity-center', 'capacity-center', '/capacity', '.mobile-capacity', 'authenticated', 'empty'),
  surface('knowledge-center', 'knowledge-center', '/knowledge', '.mobile-knowledge', 'authenticated', 'empty'),
  surface('dashboard', 'app.dashboard', '/app/dashboard', '.mobile-home-actions', 'authenticated', 'high-data'),
  surface('tasks', 'app.tasks', '/app/tasks', '.work-mode-segments', 'authenticated', 'high-data'),
  surface('create', 'app.create', '/app/create', '.mobile-create-entry', 'authenticated', 'normal'),
  surface('notifications', 'app.notifications', '/app/notifications', '.mobile-inbox-segments', 'authenticated', 'normal'),
  surface('more', 'app.more', '/app/more', '.mobile-more-nav', 'authenticated', 'normal'),
  surface('projects', 'app.projects', '/app/projects', '.workspace-segments', 'authenticated', 'high-data'),
  surface('search', 'app.search', '/app/search?q=Sentetik', '.mobile-global-search', 'authenticated', 'high-data'),
  surface('profile', 'app.profile', '/app/profile', '.mobile-profile-heading', 'authenticated', 'normal')
]);

const matrixStates = new Set(['normal', 'high-data', 'empty', 'negative']);

export const additionalStateIds = Object.freeze(['loading', 'empty-no-project', 'permission', 'offline']);
export const externalScenarioGates = freezeItems([
  {
    state: 'error',
    testFile: 'v3-bulk-job-center-real-browser.mjs',
    requiredChecks: ['real-dry-run-partial-error-artifact'],
    evidence: 'artifacts/ui/v3-surface-004-real/result.json'
  },
  {
    state: 'conflict',
    testFile: 'v3-board-excellence-real-browser.mjs',
    requiredChecks: ['real-concurrency-conflict'],
    evidence: 'artifacts/ui/v3-ux-004-real/result.json'
  },
  {
    state: 'essential-parity',
    testFile: 'v3-mobile-work-parity-real-browser.mjs',
    requiredChecks: [
      'real-touch-safe-board-move',
      'real-list-mode',
      'real-backlog-plan',
      'real-sprint-start',
      'real-create',
      'real-edit-move',
      'real-checklist-relation',
      'real-attachment-upload',
      'real-watch-vote',
      'real-comment-worklog',
      'real-self-approval-denied',
      'real-approve-transition',
      'real-search',
      'real-inbox-read',
      'real-offline-mutation-block',
      'real-viewer-read-only-comment'
    ],
    evidence: 'artifacts/ui/v3-mobile-002-real/result.json'
  },
  {
    state: 'pwa-lifecycle',
    testFile: 'v3-mobile-pwa-real-browser.mjs',
    requiredChecks: [
      'verified-first-install',
      'authenticated-api-response-not-cached',
      'offline-deep-link-navigation-shell',
      'user-controlled-update-prompt',
      'corrupt-first-install-visible-and-rejected'
    ],
    evidence: 'artifacts/ui/v3-mobile-003-real/result.json'
  },
  {
    state: 'accessibility-preferences',
    testFile: 'v3-mobile-accessibility-browser.mjs',
    requiredChecks: [
      'authenticated-route-touch-and-screen-reader-matrix',
      'keyboard-safe-login-submit-and-focus',
      'landscape-login-reflow',
      'landscape-home-reflow',
      'reduced-motion-and-forced-colors'
    ],
    evidence: 'artifacts/ui/v3-mobile-004/result.json'
  }
]);

export const expectedMatrixCaptureCount = profiles.length * mobileSurfaces.length;

export function matrixState(item) {
  return item.evidenceState;
}

export function isMatrixState(state) {
  return matrixStates.has(state);
}
