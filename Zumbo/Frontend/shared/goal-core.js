/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ZumboGoalCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  function goal(now) {
    var start = startOfQuarter(now || new Date());
    var end = new Date(start.getFullYear(), start.getMonth() + 3, 0);
    return {
      id: null,
      name: '',
      description: '',
      periodStart: start,
      periodEnd: end,
      viewerUserIds: [],
      initiativeLinks: [],
      projectIds: [],
      canEdit: true
    };
  }

  function keyResult(ownerUserId) {
    return {
      id: null,
      name: '',
      description: '',
      ownerUserId: ownerUserId || '',
      baselineValue: 0,
      targetValue: 100,
      initialValue: 0,
      unit: '%',
      direction: 'Increase'
    };
  }

  function statusUpdate(goalItem) {
    return {
      status: goalItem && goalItem.status || 'Active',
      health: goalItem && goalItem.health || 'OnTrack',
      confidence: goalItem && goalItem.confidence,
      note: ''
    };
  }

  function progressUpdate(keyResultItem) {
    return {
      currentValue: keyResultItem ? keyResultItem.currentValue : null,
      confidence: keyResultItem && keyResultItem.confidence,
      note: ''
    };
  }

  function hydrateGoal(item) {
    var result = angularCopy(item);
    result.periodStart = parseDate(item.periodStart);
    result.periodEnd = parseDate(item.periodEnd);
    return result;
  }

  function hydrateKeyResult(item) {
    var result = angularCopy(item);
    result.initialValue = item.currentValue;
    return result;
  }

  function goalPayload(draft) {
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      periodStart: dateKey(draft.periodStart),
      periodEnd: dateKey(draft.periodEnd),
      viewerUserIds: unique(draft.viewerUserIds || []),
      initiativeLinks: uniqueLinks(draft.initiativeLinks || []),
      projectIds: unique(draft.projectIds || [])
    };
  }

  function keyResultPayload(draft) {
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      ownerUserId: draft.ownerUserId,
      baselineValue: number(draft.baselineValue),
      targetValue: number(draft.targetValue),
      initialValue: number(draft.initialValue),
      unit: String(draft.unit || '').trim(),
      direction: draft.direction || 'Increase'
    };
  }

  function validateGoal(draft) {
    if (!String(draft.name || '').trim()) return 'Hedef adı gereklidir.';
    var start = parseDate(draft.periodStart);
    var end = parseDate(draft.periodEnd);
    if (!start || !end) return 'Hedef dönemi başlangıç ve bitiş tarihi gerektirir.';
    if (end < start) return 'Hedef dönemi başlangıçtan önce bitemez.';
    return null;
  }

  function validateKeyResult(draft) {
    if (!String(draft.name || '').trim()) return 'Key result adı gereklidir.';
    if (!draft.ownerUserId) return 'Key result sahibi seçin.';
    if (!String(draft.unit || '').trim()) return 'Ölçüm birimi gereklidir.';
    var baseline = number(draft.baselineValue);
    var target = number(draft.targetValue);
    var initial = number(draft.initialValue);
    if (![baseline, target, initial].every(Number.isFinite)) return 'Ölçüm değerleri sayısal olmalıdır.';
    if (baseline === target) return 'Baseline ve target farklı olmalıdır.';
    if (draft.direction === 'Increase' && target < baseline) {
      return 'Artış hedefinde target baseline değerinden büyük olmalıdır.';
    }
    if (draft.direction === 'Decrease' && target > baseline) {
      return 'Azalış hedefinde target baseline değerinden küçük olmalıdır.';
    }
    return null;
  }

  function validateUpdate(draft, label) {
    if (!String(draft.note || '').trim()) return label + ' notu gereklidir.';
    var confidence = draft.confidence;
    if (confidence !== '' && confidence != null
        && (!Number.isFinite(Number(confidence)) || Number(confidence) < 0 || Number(confidence) > 100)) {
      return 'Güven 0 ile 100 arasında olmalıdır.';
    }
    return null;
  }

  function initiativeOptions(portfolios) {
    return (portfolios || []).reduce(function(result, portfolioItem) {
      return result.concat((portfolioItem.initiatives || []).map(function(initiative) {
        return {
          key: portfolioItem.id + ':' + initiative.id,
          portfolioId: portfolioItem.id,
          initiativeId: initiative.id,
          label: portfolioItem.name + ' · ' + initiative.name
        };
      }));
    }, []);
  }

  function selectedInitiativeKeys(links) {
    return (links || []).map(function(link) {
      return link.portfolioId + ':' + link.initiativeId;
    });
  }

  function linksFromKeys(keys, options) {
    return unique(keys || []).map(function(key) {
      return (options || []).find(function(option) { return option.key === key; });
    }).filter(Boolean).map(function(option) {
      return { portfolioId: option.portfolioId, initiativeId: option.initiativeId };
    });
  }

  function statusLabel(value) {
    return {
      Draft: 'Taslak',
      Active: 'Aktif',
      Paused: 'Duraklatıldı',
      Completed: 'Tamamlandı',
      Cancelled: 'İptal'
    }[value] || value;
  }

  function healthLabel(value) {
    return {
      NoUpdate: 'Güncelleme yok',
      OnTrack: 'Yolunda',
      AtRisk: 'Riskli',
      OffTrack: 'Raydan çıktı'
    }[value] || value;
  }

  function directionLabel(value) {
    return value === 'Decrease' ? 'Azalış' : 'Artış';
  }

  function startOfQuarter(value) {
    var date = parseDate(value) || new Date();
    return new Date(date.getFullYear(), Math.floor(date.getMonth() / 3) * 3, 1);
  }

  function parseDate(value) {
    if (!value) return null;
    if (value instanceof Date) return Number.isFinite(value.getTime()) ? value : null;
    var match = String(value).slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!match) return null;
    return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  }

  function dateKey(value) {
    var date = parseDate(value);
    if (!date) return null;
    return [
      date.getFullYear(),
      String(date.getMonth() + 1).padStart(2, '0'),
      String(date.getDate()).padStart(2, '0')
    ].join('-');
  }

  function number(value) {
    if (value === '' || value == null) return NaN;
    return Number(value);
  }

  function unique(values) {
    return values.filter(function(value, index) {
      return value && values.indexOf(value) === index;
    });
  }

  function uniqueLinks(values) {
    var seen = {};
    return values.filter(function(link) {
      var key = link && link.portfolioId + ':' + link.initiativeId;
      if (!link || !link.portfolioId || !link.initiativeId || seen[key]) return false;
      seen[key] = true;
      return true;
    }).map(function(link) {
      return { portfolioId: link.portfolioId, initiativeId: link.initiativeId };
    });
  }

  function angularCopy(value) {
    return JSON.parse(JSON.stringify(value || {}));
  }

  return {
    goal: goal,
    keyResult: keyResult,
    statusUpdate: statusUpdate,
    progressUpdate: progressUpdate,
    hydrateGoal: hydrateGoal,
    hydrateKeyResult: hydrateKeyResult,
    goalPayload: goalPayload,
    keyResultPayload: keyResultPayload,
    validateGoal: validateGoal,
    validateKeyResult: validateKeyResult,
    validateUpdate: validateUpdate,
    initiativeOptions: initiativeOptions,
    selectedInitiativeKeys: selectedInitiativeKeys,
    linksFromKeys: linksFromKeys,
    statusLabel: statusLabel,
    healthLabel: healthLabel,
    directionLabel: directionLabel,
    dateKey: dateKey
  };
});
