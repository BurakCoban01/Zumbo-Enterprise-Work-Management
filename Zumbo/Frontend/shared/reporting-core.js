/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ZumboReportingCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  function number(value) {
    var parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function header(response, name) {
    if (!response || typeof response.headers !== 'function') return null;
    return response.headers(name) || response.headers(name.toLowerCase()) || null;
  }

  function snapshot(response) {
    var generatedAt = header(response, 'X-Zumbo-Report-Generated-At');
    return {
      data: response && response.data ? response.data.data : null,
      generatedAt: generatedAt,
      sourceVersion: number(header(response, 'X-Zumbo-Report-Source-Version')),
      stale: String(header(response, 'X-Zumbo-Report-Stale')).toLowerCase() === 'true',
      ageSeconds: number(header(response, 'X-Zumbo-Report-Age-Seconds'))
    };
  }

  function open(task) {
    return task && !task.archived && task.status !== 'Done' && !task.completedAt;
  }

  function workloadModel(input) {
    input = input || {};
    var tasks = input.tasks || [];
    var complete = input.scopeComplete === true;
    var userName = input.userName || function(id) { return id || 'Atanmamış'; };
    var rows = (input.workload || []).map(function(item) {
      var matching = complete ? tasks.filter(function(task) {
        return open(task) && task.assigneeUserId === item.userId;
      }) : [];
      return {
        id: item.userId,
        label: userName(item.userId),
        openItems: number(item.openItems),
        overdueItems: number(item.overdueItems),
        loggedHours: number(item.loggedHours),
        estimatedPoints: complete ? matching.reduce(function(total, task) { return total + number(task.estimatePoints); }, 0) : null,
        unestimatedItems: complete ? matching.filter(function(task) { return number(task.estimatePoints) <= 0; }).length : null,
        tasks: matching,
        risk: number(item.overdueItems) > 0 ? 'attention' : 'normal'
      };
    });
    if (complete) {
      var unassigned = tasks.filter(function(task) { return open(task) && !task.assigneeUserId; });
      if (unassigned.length) rows.push({
        id: '', label: 'Atanmamış', openItems: unassigned.length,
        overdueItems: unassigned.filter(function(task) { return task.dueDate && new Date(task.dueDate) < new Date(); }).length,
        loggedHours: 0,
        estimatedPoints: unassigned.reduce(function(total, task) { return total + number(task.estimatePoints); }, 0),
        unestimatedItems: unassigned.filter(function(task) { return number(task.estimatePoints) <= 0; }).length,
        tasks: unassigned,
        risk: 'attention'
      });
    }
    var maxOpen = Math.max.apply(null, rows.map(function(row) { return row.openItems; }).concat([1]));
    rows.forEach(function(row) { row.relativeWidth = Math.round(row.openItems / maxOpen * 100); });
    return {
      rows: rows,
      scopeComplete: complete,
      capacityConfigured: false,
      totals: {
        people: rows.filter(function(row) { return !!row.id; }).length,
        openItems: rows.reduce(function(total, row) { return total + row.openItems; }, 0),
        overdueItems: rows.reduce(function(total, row) { return total + row.overdueItems; }, 0),
        loggedHours: rows.reduce(function(total, row) { return total + row.loggedHours; }, 0),
        unestimatedItems: complete ? rows.reduce(function(total, row) { return total + row.unestimatedItems; }, 0) : null
      }
    };
  }

  function reportingModel(input) {
    input = input || {};
    var status = input.status || [];
    var statusTotal = status.reduce(function(total, row) { return total + number(row.count); }, 0);
    var maxStatus = Math.max.apply(null, status.map(function(row) { return number(row.count); }).concat([1]));
    return {
      summary: input.summary || { total: 0, done: 0, inProgress: 0, overdue: 0 },
      status: status.map(function(row) {
        return {
          status: row.status,
          count: number(row.count),
          percent: statusTotal ? Math.round(number(row.count) / statusTotal * 100) : 0,
          relativeWidth: Math.round(number(row.count) / maxStatus * 100)
        };
      }),
      flow: input.flow || { completedItems: 0, cycleTimeSampleSize: 0, averageLeadTimeHours: 0, medianLeadTimeHours: 0, averageCycleTimeHours: null, medianCycleTimeHours: null },
      completion: input.completion || { createdItems: 0, completedItems: 0, completionRatePercent: 0 },
      teams: (input.teams || []).slice().sort(function(left, right) {
        return String(left.teamName).localeCompare(String(right.teamName), 'tr-TR');
      }),
      risks: input.risks || [],
      rangeDays: number(input.rangeDays) || 30
    };
  }

  function freshness(snapshots) {
    var values = Object.values(snapshots || {}).filter(Boolean);
    var times = values.map(function(value) { return Date.parse(value.generatedAt); }).filter(Number.isFinite);
    return {
      generatedAt: times.length ? new Date(Math.min.apply(null, times)).toISOString() : null,
      stale: values.some(function(value) { return value.stale; }),
      maxAgeSeconds: Math.max.apply(null, values.map(function(value) { return number(value.ageSeconds); }).concat([0])),
      sourceVersions: Array.from(new Set(values.map(function(value) { return value.sourceVersion; }).filter(Boolean)))
    };
  }

  return {
    snapshot: snapshot,
    workloadModel: workloadModel,
    reportingModel: reportingModel,
    freshness: freshness
  };
});
