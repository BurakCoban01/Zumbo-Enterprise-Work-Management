/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboCapacityPlanningCore = api;
})(typeof window !== 'undefined' ? window : globalThis, function() {
  'use strict';

  function dateValue(value) {
    if (value instanceof Date) {
      return new Date(value.getFullYear(), value.getMonth(), value.getDate());
    }
    if (!value) return null;
    var parts = String(value).slice(0, 10).split('-').map(Number);
    return parts.length === 3 && parts.every(Number.isFinite)
      ? new Date(parts[0], parts[1] - 1, parts[2])
      : null;
  }

  function dateKey(value) {
    var date = dateValue(value);
    if (!date) return '';
    return [
      date.getFullYear(),
      String(date.getMonth() + 1).padStart(2, '0'),
      String(date.getDate()).padStart(2, '0')
    ].join('-');
  }

  function addDays(value, days) {
    var date = dateValue(value) || new Date();
    return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
  }

  function monday(value) {
    var date = dateValue(value) || new Date();
    var offset = date.getDay() === 0 ? -6 : 1 - date.getDay();
    return addDays(date, offset);
  }

  function plan(currentUserId) {
    var start = monday(new Date());
    var end = addDays(start, 27);
    return {
      id: null,
      name: '',
      description: '',
      periodStart: start,
      periodEnd: end,
      portfolioId: '',
      projectIds: [],
      members: currentUserId
        ? [{ userId: currentUserId, teamId: '', weeklyCapacityHours: 40 }]
        : [],
      allocations: [],
      viewerUserIds: [],
      version: 0
    };
  }

  function hydratePlan(source) {
    source = source || {};
    return {
      id: source.id || null,
      name: source.name || '',
      description: source.description || '',
      periodStart: dateValue(source.periodStart),
      periodEnd: dateValue(source.periodEnd),
      portfolioId: source.portfolioId || '',
      projectIds: (source.projectIds || []).slice(),
      members: (source.members || []).map(function(item) {
        return {
          userId: item.userId,
          teamId: item.teamId || '',
          weeklyCapacityHours: Number(item.weeklyCapacityHours)
        };
      }),
      allocations: (source.allocations || []).map(hydrateAllocation),
      viewerUserIds: (source.viewerUserIds || []).slice(),
      version: Number(source.version || 0)
    };
  }

  function hydrateAllocation(item) {
    item = item || {};
    return {
      id: item.id || null,
      userId: item.userId || '',
      projectId: item.projectId || '',
      startDate: dateValue(item.startDate),
      endDate: dateValue(item.endDate),
      percent: Number(item.percent || 0)
    };
  }

  function member() {
    return { userId: '', teamId: '', weeklyCapacityHours: 40 };
  }

  function allocation(draft) {
    return {
      id: null,
      userId: draft && draft.members[0] ? draft.members[0].userId : '',
      projectId: draft && draft.projectIds[0] ? draft.projectIds[0] : '',
      startDate: draft ? dateValue(draft.periodStart) : monday(new Date()),
      endDate: draft ? dateValue(draft.periodEnd) : addDays(monday(new Date()), 27),
      percent: 100
    };
  }

  function payload(draft, allocations) {
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      periodStart: dateKey(draft.periodStart),
      periodEnd: dateKey(draft.periodEnd),
      portfolioId: draft.portfolioId || null,
      projectIds: (draft.projectIds || []).slice(),
      members: (draft.members || []).map(function(item) {
        return {
          userId: item.userId,
          teamId: item.teamId || null,
          weeklyCapacityHours: Number(item.weeklyCapacityHours)
        };
      }),
      allocations: (allocations || draft.allocations || []).map(function(item) {
        return {
          id: item.id || null,
          userId: item.userId,
          projectId: item.projectId,
          startDate: dateKey(item.startDate),
          endDate: dateKey(item.endDate),
          percent: Number(item.percent)
        };
      }),
      viewerUserIds: (draft.viewerUserIds || []).slice()
    };
  }

  function validate(draft, allocations) {
    if (!String(draft.name || '').trim()) return 'Plan adı zorunludur.';
    if (String(draft.name || '').trim().length > 120) return 'Plan adı 120 karakteri aşamaz.';
    if (String(draft.description || '').length > 500) return 'Açıklama 500 karakteri aşamaz.';
    var start = dateValue(draft.periodStart);
    var end = dateValue(draft.periodEnd);
    if (!start || !end || end < start) return 'Geçerli bir plan dönemi seçin.';
    if (Math.round((end - start) / 86400000) + 1 > 366) {
      return 'Plan dönemi 366 günü aşamaz.';
    }
    if (!(draft.projectIds || []).length) return 'En az bir proje seçin.';
    if (draft.projectIds.length > 20) return 'En fazla 20 proje seçilebilir.';
    if (!(draft.members || []).length) return 'En az bir kişi ekleyin.';
    if (draft.members.length > 100) return 'En fazla 100 kişi eklenebilir.';
    var memberIds = {};
    for (var index = 0; index < draft.members.length; index += 1) {
      var person = draft.members[index];
      if (!person.userId) return 'Her kapasite satırı için bir kişi seçin.';
      if (memberIds[person.userId]) return 'Bir kişi plana yalnızca bir kez eklenebilir.';
      memberIds[person.userId] = true;
      var hours = Number(person.weeklyCapacityHours);
      if (!Number.isFinite(hours) || hours < 0 || hours > 168) {
        return 'Haftalık kapasite 0 ile 168 saat arasında olmalıdır.';
      }
    }
    var rows = allocations || draft.allocations || [];
    if (rows.length > 500) return 'En fazla 500 tahsis eklenebilir.';
    for (var rowIndex = 0; rowIndex < rows.length; rowIndex += 1) {
      var row = rows[rowIndex];
      if (!memberIds[row.userId]) return 'Her tahsis plandaki bir kişiye bağlı olmalıdır.';
      if (draft.projectIds.indexOf(row.projectId) < 0) {
        return 'Her tahsis plan kapsamındaki bir projeye bağlı olmalıdır.';
      }
      var rowStart = dateValue(row.startDate);
      var rowEnd = dateValue(row.endDate);
      if (!rowStart || !rowEnd || rowStart < start || rowEnd > end || rowEnd < rowStart) {
        return 'Tahsis tarihleri plan dönemi içinde olmalıdır.';
      }
      var percent = Number(row.percent);
      if (!Number.isFinite(percent) || percent <= 0 || percent > 100) {
        return 'Tahsis oranı %1 ile %100 arasında olmalıdır.';
      }
    }
    return null;
  }

  function stateLabel(state) {
    return {
      Available: 'Uygun',
      NearCapacity: 'Sınıra yakın',
      OverCapacity: 'Kapasite üstü'
    }[state] || 'Bilinmiyor';
  }

  function stateTone(state) {
    return {
      Available: 'available',
      NearCapacity: 'near',
      OverCapacity: 'over'
    }[state] || 'unknown';
  }

  function sourceLabel(status) {
    return status === 'Partial' ? 'Kısmi kaynak' : 'Güncel';
  }

  function barWidth(percent) {
    var value = Number(percent || 0);
    return Math.max(0, Math.min(100, value));
  }

  function scenarioDelta(scenario, key) {
    if (!scenario || !scenario.baseline || !scenario.candidate) return 0;
    return Number(scenario.candidate.summary[key] || 0)
      - Number(scenario.baseline.summary[key] || 0);
  }

  return {
    limits: {
      projects: 20,
      members: 100,
      allocations: 500,
      viewers: 50,
      days: 366
    },
    dateValue: dateValue,
    dateKey: dateKey,
    addDays: addDays,
    plan: plan,
    hydratePlan: hydratePlan,
    hydrateAllocation: hydrateAllocation,
    member: member,
    allocation: allocation,
    payload: payload,
    validate: validate,
    stateLabel: stateLabel,
    stateTone: stateTone,
    sourceLabel: sourceLabel,
    barWidth: barWidth,
    scenarioDelta: scenarioDelta
  };
});
