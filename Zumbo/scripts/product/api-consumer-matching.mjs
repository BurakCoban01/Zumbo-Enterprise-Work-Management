export function selectMostSpecificOperations(clientPattern, operations) {
  const scored = operations
    .map(operation => ({
      operation,
      score: matchSpecificity(clientPattern, operation.path)
    }))
    .filter(candidate => candidate.score !== null);
  if (scored.length === 0) return [];
  const maximum = Math.max(...scored.map(candidate => candidate.score));
  return scored
    .filter(candidate => candidate.score === maximum)
    .map(candidate => candidate.operation);
}

export function matchSpecificity(clientPattern, routePath) {
  const clientSegments = normalizePath(clientPattern).split('/');
  const routeSegments = normalizePath(routePath).split('/');
  if (clientSegments.length !== routeSegments.length) return null;

  let score = 0;
  for (let index = 0; index < clientSegments.length; index += 1) {
    const client = clientSegments[index];
    const route = routeSegments[index];
    const clientDynamic = client.includes('{*}');
    const routeDynamic = /^\{[^}]+\}$/.test(route);
    if (!clientDynamic && !routeDynamic) {
      if (client !== route) return null;
      score += 8;
    } else if (clientDynamic && routeDynamic) {
      score += 4;
    } else if (clientDynamic) {
      const matcher = new RegExp(
        `^${escapeRegExp(client).replaceAll('\\{\\*\\}', '.*')}$`);
      if (!matcher.test(route)) return null;
      score += 2;
    } else {
      score += 1;
    }
  }
  return score;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function normalizePath(path) {
  const withoutQuery = path.split('?')[0].replace(
    /\{([^}:]+):[^}]+\}/g,
    '{$1}'
  );
  return withoutQuery.length > 1 ? withoutQuery.replace(/\/$/, '') : withoutQuery;
}
