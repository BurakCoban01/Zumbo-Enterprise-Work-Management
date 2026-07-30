function freezeItems(items) {
  return Object.freeze(items.map(item => Object.freeze(item)));
}

export function profile(id, width, height, theme, density, reducedMotion) {
  return { id, width, height, nominalWidth: width, zoomPercent: 100, theme, density, reducedMotion };
}

export function section(id, selector) {
  return { id, kind: 'section', section: id, selector };
}

export function projectView(id, sectionId, selector) {
  return { id, kind: 'project-view', section: sectionId, selector };
}

export const profiles = freezeItems([
  profile('1920-light-comfortable-reduced', 1920, 1080, 'light', 'comfortable', 'reduce'),
  profile('1440-dark-compact-reduced', 1440, 1000, 'dark', 'compact', 'reduce'),
  profile('1366-light-compact-motion', 1366, 900, 'light', 'compact', 'no-preference'),
  profile('1024-dark-comfortable-reduced', 1024, 768, 'dark', 'comfortable', 'reduce'),
  {
    ...profile('zoom-200-light-comfortable-reduced', 720, 1000, 'light', 'comfortable', 'reduce'),
    nominalWidth: 1440,
    zoomPercent: 200
  }
]);

export const sectionSurfaces = freezeItems([
  section('home', '.home-surface'),
  section('mywork', '[aria-label="İşlerim görünümü"]'),
  section('inbox', '.inbox-layout'),
  section('projects', '.management-layout'),
  section('portfolios', '.portfolio-center'),
  section('goals', '.goal-center'),
  section('capacity', '.capacity-center'),
  section('knowledge', '.knowledge-center'),
  section('teams', '[aria-label="Ekip listesi"]'),
  section('audit', '.audit-workspace'),
  section('archive', '.archive-group, .section-view .section-empty'),
  section('settings', '.settings-view')
]);

export const projectSurfaces = freezeItems([
  projectView('overview', 'board', '.project-overview'),
  projectView('board', 'board', '.board-shell'),
  projectView('list', 'board', '.list-work-view'),
  projectView('backlog', 'board', '.planning-view'),
  projectView('sprint', 'board', '.sprint-view'),
  projectView('calendar', 'board', '.planning-surface-v3'),
  projectView('timeline', 'board', '.planning-surface-v3'),
  projectView('roadmap', 'board', '.planning-surface-v3'),
  projectView('catalog', 'board', '.project-catalog-surface'),
  projectView('intake', 'board', '.intake-surface'),
  projectView('automation', 'board', '.automation-surface'),
  projectView('jobs', 'board', '.job-center'),
  projectView('workload', 'reports', '.reporting-surface'),
  projectView('reports', 'reports', '.reporting-surface'),
  projectView('dashboards', 'reports', '.reporting-surface')
]);

const highDataProjectViews = new Set([
  'overview', 'board', 'list', 'backlog', 'sprint',
  'calendar', 'timeline', 'roadmap', 'workload', 'reports'
]);
const emptySections = new Set(['portfolios', 'goals', 'capacity', 'knowledge', 'archive']);
const emptyProjectViews = new Set(['intake', 'automation', 'jobs', 'dashboards']);
const matrixStates = new Set(['normal', 'high-data', 'empty']);

export const additionalStateIds = Object.freeze(['loading', 'empty-no-board', 'permission', 'offline']);
export const externalScenarioGates = freezeItems([
  {
    state: 'error',
    testFile: 'v3-bulk-job-center-real-browser.mjs',
    requiredCheck: 'real-dry-run-partial-error-artifact',
    evidence: 'artifacts/ui/v3-surface-004-real/result.json'
  },
  {
    state: 'conflict',
    testFile: 'v3-board-excellence-real-browser.mjs',
    requiredCheck: 'real-concurrency-conflict',
    evidence: 'artifacts/ui/v3-ux-004-real/result.json'
  }
]);
export const expectedMatrixCaptureCount = profiles.length * (sectionSurfaces.length + projectSurfaces.length);

export function matrixState(surface) {
  if (surface.kind === 'project-view' && highDataProjectViews.has(surface.id)) return 'high-data';
  if (surface.kind === 'section' && emptySections.has(surface.id)) return 'empty';
  if (surface.kind === 'project-view' && emptyProjectViews.has(surface.id)) return 'empty';
  return 'normal';
}

export function isMatrixState(state) {
  return matrixStates.has(state);
}
