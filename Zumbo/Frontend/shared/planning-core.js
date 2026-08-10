(function(root) {
  'use strict';

  var DAY_MS = 86400000;

  function pad(value) {
    return String(value).padStart(2, '0');
  }

  function dateKey(value, timeZone, preserveDateOnly) {
    if (!value) return '';
    if (typeof value === 'string' && preserveDateOnly !== false && /^\d{4}-\d{2}-\d{2}/.test(value)) {
      return value.slice(0, 10);
    }
    var date = value instanceof Date ? value : new Date(value);
    if (!Number.isFinite(date.getTime())) return '';
    if (!timeZone) return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate());
    var parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit'
    }).formatToParts(date).reduce(function(result, part) {
      result[part.type] = part.value;
      return result;
    }, {});
    return parts.year + '-' + parts.month + '-' + parts.day;
  }

  function keyDate(key) {
    var parts = String(key || '').split('-').map(Number);
    return parts.length === 3 && parts.every(Number.isFinite)
      ? new Date(Date.UTC(parts[0], parts[1] - 1, parts[2]))
      : null;
  }

  function inputDate(key) {
    var parts = String(key || '').split('-').map(Number);
    return parts.length === 3 && parts.every(Number.isFinite)
      ? new Date(parts[0], parts[1] - 1, parts[2])
      : null;
  }

  function addDays(key, amount) {
    var date = keyDate(key);
    if (!date) return '';
    date.setUTCDate(date.getUTCDate() + amount);
    return date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-' + pad(date.getUTCDate());
  }

  function dayDiff(left, right) {
    var leftDate = keyDate(left);
    var rightDate = keyDate(right);
    return leftDate && rightDate ? Math.round((rightDate.getTime() - leftDate.getTime()) / DAY_MS) : 0;
  }

  function startOfWeek(key) {
    var date = keyDate(key);
    if (!date) return '';
    var offset = (date.getUTCDay() + 6) % 7;
    return addDays(key, -offset);
  }

  function startOfMonth(key) {
    var date = keyDate(key);
    if (!date) return '';
    return date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-01';
  }

  function formatDate(key, locale, options) {
    var date = keyDate(key);
    if (!date) return 'Tarih yok';
    var formatOptions = Object.assign({
      day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC'
    }, options || {});
    Object.keys(formatOptions).forEach(function(key) {
      if (formatOptions[key] === undefined) delete formatOptions[key];
    });
    return new Intl.DateTimeFormat(locale || 'tr-TR', formatOptions).format(date);
  }

  function calendarDays(anchorKey, mode) {
    var start = mode === 'week' ? startOfWeek(anchorKey) : startOfWeek(startOfMonth(anchorKey));
    var count = mode === 'week' ? 7 : 42;
    var month = startOfMonth(anchorKey).slice(0, 7);
    return Array.from({ length: count }, function(_, index) {
      var key = addDays(start, index);
      return {
        key: key,
        inputDate: inputDate(key),
        inRange: mode === 'week' || key.slice(0, 7) === month,
        label: formatDate(key, 'tr-TR', { day: 'numeric', month: mode === 'week' ? 'short' : undefined, year: undefined })
      };
    });
  }

  function normalized(value) {
    return String(value || '').trim().toLocaleLowerCase('tr-TR');
  }

  function taskMatches(task, filters) {
    filters = filters || {};
    if (filters.assignee && task.assigneeUserId !== filters.assignee) return false;
    if (filters.team && task.teamId !== filters.team) return false;
    if (filters.type && task.type !== filters.type) return false;
    if (filters.query) {
      var haystack = normalized([task.title, task.description, task.status, task.priority, (task.labels || []).join(' ')].join(' '));
      if (haystack.indexOf(normalized(filters.query)) < 0) return false;
    }
    return true;
  }

  function statusMetadata(workflow) {
    return (workflow && workflow.statuses || []).reduce(function(result, status, index) {
      result[status.name] = { category: status.category || 'Custom', order: index };
      return result;
    }, {});
  }

  function statusCategory(status, metadata) {
    return metadata[status] ? metadata[status].category : 'Custom';
  }

  function isDone(task, metadata) {
    return !!task.completedAt || normalized(statusCategory(task.status, metadata)) === 'done';
  }

  function segmentTone(category) {
    var value = normalized(category).replace(/[^a-z0-9]+/g, '-');
    return value || 'custom';
  }

  function statusSegments(distribution, workflow) {
    var metadata = statusMetadata(workflow);
    var total = (distribution || []).reduce(function(sum, item) { return sum + Math.max(0, Number(item.count) || 0); }, 0);
    var segments = (distribution || []).filter(function(item) { return Number(item.count) > 0; }).map(function(item, index) {
      var meta = metadata[item.status] || { category: 'Custom', order: 10000 + index };
      var exactUnits = total ? Number(item.count) / total * 10000 : 0;
      return {
        status: item.status,
        category: meta.category,
        tone: segmentTone(meta.category),
        count: Number(item.count),
        percentageUnits: Math.floor(exactUnits),
        percentageRemainder: exactUnits - Math.floor(exactUnits),
        order: meta.order
      };
    });
    var missingUnits = 10000 - segments.reduce(function(sum, segment) { return sum + segment.percentageUnits; }, 0);
    segments.slice().sort(function(left, right) {
      return right.percentageRemainder - left.percentageRemainder || left.order - right.order;
    }).forEach(function(segment, index) {
      if (index < missingUnits) segment.percentageUnits += 1;
    });
    return segments.map(function(segment) {
      segment.percentage = segment.percentageUnits / 100;
      delete segment.percentageUnits;
      delete segment.percentageRemainder;
      return segment;
    }).sort(function(left, right) {
      return left.order - right.order || left.status.localeCompare(right.status, 'tr-TR');
    });
  }

  function taskDistribution(tasks) {
    var counts = {};
    (tasks || []).forEach(function(task) { counts[task.status] = (counts[task.status] || 0) + 1; });
    return Object.keys(counts).map(function(status) { return { status: status, count: counts[status] }; });
  }

  function dependencies(tasks) {
    var ids = new Set(tasks.map(function(task) { return task.id; }));
    var seen = new Set();
    var result = [];
    tasks.forEach(function(task) {
      (task.relations || []).forEach(function(relation) {
        var type = normalized(relation.relationType);
        var from = type === 'blocks' ? task.id : type === 'blockedby' || type === 'isblockedby' ? relation.relatedWorkItemId : null;
        var to = type === 'blocks' ? relation.relatedWorkItemId : type === 'blockedby' || type === 'isblockedby' ? task.id : null;
        if (!from || !to || !ids.has(from) || !ids.has(to)) return;
        var key = from + '>' + to;
        if (seen.has(key)) return;
        seen.add(key);
        result.push({ id: key, from: from, to: to });
      });
    });
    return result;
  }

  function timelineRow(task, sprintById) {
    var sprint = task.sprintId ? sprintById[task.sprintId] : null;
    var dueKey = dateKey(task.dueDate, null, true);
    var sprintStart = sprint ? dateKey(sprint.startDate, null, true) : '';
    var sprintEnd = sprint ? dateKey(sprint.endDate, null, true) : '';
    var startKey = sprintStart || dueKey;
    var endKey = dueKey || sprintEnd || startKey;
    if (startKey && endKey && endKey < startKey) startKey = endKey;
    var source = sprint && dueKey
      ? 'Sprint başlangıcı → görev bitişi'
      : sprint
        ? 'Sprint aralığından türetildi'
        : 'Yalnız görev bitiş tarihi';
    return {
      id: task.id,
      task: task,
      title: task.title,
      startKey: startKey,
      endKey: endKey,
      inputDate: inputDate(dueKey),
      dueKey: dueKey,
      source: source,
      derivedStart: !!sprint,
      derivedEnd: !!sprint && !dueKey,
      milestone: !!startKey && startKey === endKey,
      blockedBy: [],
      blocks: [],
      dependencyRisk: false
    };
  }

  function windowSpec(anchorKey, zoom) {
    var bucketDays = zoom === 'week' ? 2 : zoom === 'quarter' ? 30 : 7;
    var start = zoom === 'week' ? startOfWeek(anchorKey) : zoom === 'quarter' ? startOfMonth(anchorKey) : startOfWeek(anchorKey);
    var buckets = Array.from({ length: 12 }, function(_, index) {
      var bucketStart = addDays(start, index * bucketDays);
      return {
        index: index + 1,
        startKey: bucketStart,
        endKey: addDays(bucketStart, bucketDays - 1),
        label: formatDate(bucketStart, 'tr-TR', zoom === 'quarter'
          ? { month: 'short', year: '2-digit', day: undefined }
          : { day: '2-digit', month: 'short', year: undefined })
      };
    });
    return { startKey: start, endKey: addDays(start, bucketDays * 12 - 1), bucketDays: bucketDays, buckets: buckets };
  }

  function placeRows(rows, window) {
    return rows.map(function(row) {
      var rawStart = dayDiff(window.startKey, row.startKey);
      var rawEnd = dayDiff(window.startKey, row.endKey);
      var first = Math.max(0, Math.min(11, Math.floor(rawStart / window.bucketDays)));
      var last = Math.max(first, Math.min(11, Math.floor(rawEnd / window.bucketDays)));
      return Object.assign(row, {
        inWindow: row.endKey >= window.startKey && row.startKey <= window.endKey,
        column: first + 1,
        span: last - first + 1
      });
    });
  }

  function roadmapEntries(project, sprints, tasks, sprintRows, timeZone, workflow) {
    var metadata = statusMetadata(workflow);
    var taskBySprint = {};
    tasks.forEach(function(task) {
      if (!task.sprintId) return;
      taskBySprint[task.sprintId] = taskBySprint[task.sprintId] || [];
      taskBySprint[task.sprintId].push(task);
    });
    var entries = [];
    (project.milestones || []).forEach(function(item) {
      var key = dateKey(item.dueAt, timeZone, false);
      entries.push({
        id: 'milestone-' + item.id,
        kind: 'Kilometre taşı',
        title: item.name,
        status: item.status,
        startKey: key,
        endKey: key,
        milestone: true,
        progress: item.status === 'Completed' ? 100 : 0,
        progressSource: 'Kilometre taşı durumu'
      });
    });
    (project.releases || []).filter(function(item) { return !!item.scheduledAt; }).forEach(function(item) {
      var key = dateKey(item.scheduledAt, timeZone, false);
      entries.push({
        id: 'release-' + item.id,
        kind: 'Sürüm',
        title: item.name,
        status: item.status,
        startKey: key,
        endKey: key,
        milestone: true,
        progress: item.status === 'Published' ? 100 : item.status === 'Approved' ? 70 : 20,
        progressSource: 'Sürüm yaşam döngüsü'
      });
    });
    sprints.forEach(function(sprint) {
      var scoped = taskBySprint[sprint.id] || [];
      var complete = scoped.filter(function(task) { return isDone(task, metadata); }).length;
      entries.push({
        id: 'sprint-' + sprint.id,
        kind: 'Sprint',
        title: sprint.name,
        status: sprint.status,
        startKey: dateKey(sprint.startDate, null, true),
        endKey: dateKey(sprint.endDate, null, true),
        milestone: false,
        segments: statusSegments(taskDistribution(scoped), workflow),
        progress: scoped.length ? Math.round(complete / scoped.length * 100) : 0,
        progressSource: scoped.length ? complete + '/' + scoped.length + ' görev tamamlandı' : 'Sprint kapsamı boş'
      });
    });
    return entries.concat(sprintRows || []).sort(function(left, right) {
      return String(left.startKey).localeCompare(String(right.startKey)) || left.title.localeCompare(right.title, 'tr-TR');
    });
  }

  function buildModel(input) {
    input = input || {};
    var tasks = (input.tasks || []).filter(function(task) { return taskMatches(task, input.filters); });
    var sprints = (input.sprints || []).slice();
    var project = input.project || { milestones: [], releases: [] };
    var metadata = statusMetadata(input.workflow);
    var sprintById = sprints.reduce(function(result, sprint) { result[sprint.id] = sprint; return result; }, {});
    var rows = tasks.map(function(task) { return timelineRow(task, sprintById); });
    var edges = dependencies(tasks);
    var rowById = rows.reduce(function(result, row) { result[row.id] = row; return result; }, {});
    edges.forEach(function(edge) {
      var from = rowById[edge.from];
      var to = rowById[edge.to];
      if (!from || !to) return;
      from.blocks.push(to.id);
      to.blockedBy.push(from.id);
      edge.risk = !!from.endKey && !!to.startKey && from.endKey >= to.startKey;
      if (edge.risk) to.dependencyRisk = true;
    });
    var events = [];
    rows.forEach(function(row) {
      if (!row.dueKey) return;
      events.push({
        id: 'task-due-' + row.id,
        key: row.dueKey,
        kind: 'Bitiş',
        title: row.title,
        task: row.task,
        inputDate: inputDate(row.dueKey),
        tone: row.dependencyRisk ? 'risk' : isDone(row.task, metadata) ? 'done' : 'task'
      });
    });
    sprints.forEach(function(sprint) {
      var start = dateKey(sprint.startDate, null, true);
      var end = dateKey(sprint.endDate, null, true);
      if (start) events.push({ id: 'sprint-start-' + sprint.id, key: start, kind: 'Sprint başı', title: sprint.name, tone: 'sprint' });
      if (end) events.push({ id: 'sprint-end-' + sprint.id, key: end, kind: 'Sprint sonu', title: sprint.name, tone: 'sprint' });
    });
    (project.milestones || []).forEach(function(item) {
      var key = dateKey(item.dueAt, input.timeZone, false);
      if (key) events.push({ id: 'milestone-' + item.id, key: key, kind: 'Kilometre taşı', title: item.name, tone: 'milestone' });
    });
    (project.releases || []).forEach(function(item) {
      var key = dateKey(item.scheduledAt, input.timeZone, false);
      if (key) events.push({ id: 'release-' + item.id, key: key, kind: 'Sürüm', title: item.name, tone: 'release' });
    });
    events.sort(function(left, right) { return left.key.localeCompare(right.key) || left.title.localeCompare(right.title, 'tr-TR'); });
    var byDate = events.reduce(function(result, event) {
      result[event.key] = result[event.key] || [];
      result[event.key].push(event);
      return result;
    }, {});
    var anchorKey = dateKey(input.anchorDate || new Date(), input.timeZone, true);
    var window = windowSpec(anchorKey, input.zoom || 'month');
    var scheduledRows = rows.filter(function(row) { return !!row.startKey; });
    placeRows(scheduledRows, window);
    var roadmap = roadmapEntries(project, sprints, tasks, [], input.timeZone, input.workflow);
    placeRows(roadmap.filter(function(row) { return !!row.startKey; }), window);
    var total = tasks.length;
    var done = tasks.filter(function(task) { return isDone(task, metadata); }).length;
    var overdue = tasks.filter(function(task) {
      var key = dateKey(task.dueDate, null, true);
      return key && key < dateKey(input.today || new Date(), input.timeZone, true) && !isDone(task, metadata);
    }).length;
    var visibleSegments = statusSegments(taskDistribution(tasks), input.workflow);
    var projectDistribution = input.statusDistribution && input.statusDistribution.length
      ? input.statusDistribution
      : taskDistribution(tasks);
    var projectSegments = statusSegments(projectDistribution, input.workflow);
    var projectTotal = projectSegments.reduce(function(sum, segment) { return sum + segment.count; }, 0);
    var projectDone = projectSegments.filter(function(segment) { return normalized(segment.category) === 'done'; })
      .reduce(function(sum, segment) { return sum + segment.count; }, 0);
    return {
      anchorKey: anchorKey,
      calendarDays: calendarDays(anchorKey, input.calendarMode || 'month').map(function(day) {
        day.events = byDate[day.key] || [];
        return day;
      }),
      calendarEvents: events,
      timelineRows: scheduledRows,
      unscheduledTasks: tasks.filter(function(task) { return !dateKey(task.dueDate, null, true); }),
      dependencies: edges,
      dependencyRisks: edges.filter(function(edge) { return edge.risk; }),
      roadmapRows: roadmap,
      window: window,
      totals: {
        tasks: total,
        done: done,
        overdue: overdue,
        progress: total ? Math.round(done / total * 100) : 0,
        segments: visibleSegments,
        projectTasks: projectTotal,
        projectDone: projectDone,
        projectProgress: projectTotal ? Math.round(projectDone / projectTotal * 100) : 0,
        projectSegments: projectSegments
      }
    };
  }

  root.ZumboPlanningCore = Object.freeze({
    dateKey: dateKey,
    inputDate: inputDate,
    addDays: addDays,
    dayDiff: dayDiff,
    startOfWeek: startOfWeek,
    formatDate: formatDate,
    calendarDays: calendarDays,
    dependencies: dependencies,
    windowSpec: windowSpec,
    statusSegments: statusSegments,
    buildModel: buildModel
  });
})(window);
