const directRoutes = new Set([
  '/login',
  '/forgot-password',
  '/reset-password',
  '/portfolios',
  '/goals',
  '/capacity',
  '/knowledge'
]);

const tabRoutes: Readonly<Record<string, string>> = {
  '/app/dashboard': '/workspace/home',
  '/app/tasks': '/workspace/work',
  '/app/create': '/workspace/create',
  '/app/notifications': '/workspace/inbox',
  '/app/more': '/workspace/more',
  '/app/projects': '/workspace/projects',
  '/app/search': '/workspace/search',
  '/app/profile': '/workspace/account'
};

export function legacyMobilePath(hash: string): string | null {
  if (!hash.startsWith('#/')) return null;
  const legacy = new URL(hash.slice(1), 'http://zumbo.local');
  const path = legacy.pathname.replace(/\/$/, '') || '/';
  const query = legacy.search;
  const tabRoute = tabRoutes[path];
  if (tabRoute) return `${tabRoute}${query}`;
  if (directRoutes.has(path)) {
    const workspaceRoute = ['/portfolios', '/goals', '/capacity', '/knowledge'].includes(path)
      ? `/workspace${path}`
      : path;
    return `${workspaceRoute}${query}`;
  }
  if (/^\/(?:tasks|teams|intake)\/[^/]+$/.test(path)) return `${path}${query}`;
  if (/^\/projects\/[^/]+(?:\/(?:catalog|intake|automation|jobs|insights))?$/.test(path)) return `${path}${query}`;
  if (/^\/profile\/(?:integrations|operations)$/.test(path)) return `${path}${query}`;
  return '/workspace/home';
}

export function applyLegacyMobileLocation(): void {
  const path = legacyMobilePath(location.hash);
  if (!path) return;
  history.replaceState(null, '', `${document.baseURI}${path.replace(/^\//, '')}`);
}
