/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboAccountSecurityCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  function normalizeMutedTypes(value) {
    var values = Array.isArray(value) ? value : String(value || '').split(',');
    var seen = Object.create(null);
    return values.map(function(item) { return String(item || '').trim(); }).filter(function(item) {
      var key = item.toLocaleLowerCase('en-US');
      if (!item || seen[key]) return false;
      seen[key] = true;
      return true;
    });
  }

  function isSessionActive(session, now) {
    if (!session || session.revokedAt || !session.expiresAt) return false;
    var expiresAt = new Date(session.expiresAt).getTime();
    return Number.isFinite(expiresAt) && expiresAt > (now == null ? Date.now() : Number(now));
  }

  function clearOneTimeSecrets(state) {
    state.mfaSetup = null;
    state.recoveryCodes = [];
    return state;
  }

  function selectVisibleSessions(sessions, now, inactiveLimit) {
    var limit = inactiveLimit == null ? 2 : Math.max(0, Number(inactiveLimit));
    var inactiveCount = 0;
    return (sessions || []).slice().sort(function(left, right) {
      var activeDifference = Number(isSessionActive(right, now)) - Number(isSessionActive(left, now));
      if (activeDifference) return activeDifference;
      return new Date(right.lastSeenAt || 0).getTime() - new Date(left.lastSeenAt || 0).getTime();
    }).filter(function(session) {
      if (isSessionActive(session, now)) return true;
      inactiveCount += 1;
      return inactiveCount <= limit;
    });
  }

  return {
    normalizeMutedTypes: normalizeMutedTypes,
    isSessionActive: isSessionActive,
    clearOneTimeSecrets: clearOneTimeSecrets,
    selectVisibleSessions: selectVisibleSessions
  };
});
