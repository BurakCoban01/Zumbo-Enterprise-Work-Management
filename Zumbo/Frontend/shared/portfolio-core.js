/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ZumboPortfolioCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  function portfolio() {
    return { id: null, name: '', description: '', viewerUserIds: [], canEdit: true, initiatives: [], dependencies: [] };
  }

  function initiative(ownerUserId) {
    return {
      id: null,
      name: '',
      summary: '',
      parentInitiativeId: null,
      ownerUserId: ownerUserId || '',
      status: 'Planned',
      health: 'NoUpdate',
      confidence: null,
      targetAt: null,
      projectIds: [],
      milestoneLinks: []
    };
  }

  function dependency() {
    return {
      id: null,
      sourceProjectId: '',
      targetProjectId: '',
      description: '',
      status: 'Active',
      requiredBy: null
    };
  }

  function portfolioPayload(draft) {
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      viewerUserIds: unique(draft.viewerUserIds || [])
    };
  }

  function initiativePayload(draft) {
    return {
      name: String(draft.name || '').trim(),
      summary: String(draft.summary || '').trim() || null,
      parentInitiativeId: draft.parentInitiativeId || null,
      ownerUserId: draft.ownerUserId,
      status: draft.status,
      health: draft.health,
      confidence: draft.confidence === '' || draft.confidence == null ? null : Number(draft.confidence),
      targetAt: iso(draft.targetAt),
      projectIds: unique(draft.projectIds || []),
      milestoneLinks: (draft.milestoneLinks || []).map(function(link) {
        return { projectId: link.projectId, milestoneId: link.milestoneId };
      })
    };
  }

  function dependencyPayload(draft) {
    return {
      sourceProjectId: draft.sourceProjectId,
      targetProjectId: draft.targetProjectId,
      description: String(draft.description || '').trim(),
      status: draft.status || 'Active',
      requiredBy: iso(draft.requiredBy)
    };
  }

  function tree(initiatives) {
    var byParent = (initiatives || []).reduce(function(result, item) {
      var parent = item.parentInitiativeId || '';
      result[parent] = result[parent] || [];
      result[parent].push(item);
      return result;
    }, {});
    var rows = [];
    function visit(parentId, depth) {
      (byParent[parentId] || []).slice().sort(byName).forEach(function(item) {
        rows.push({ item: item, depth: depth });
        visit(item.id, depth + 1);
      });
    }
    visit('', 0);
    return rows;
  }

  function validatePortfolio(draft) {
    if (!String(draft.name || '').trim()) return 'Portföy adı gereklidir.';
    return null;
  }

  function validateInitiative(draft) {
    if (!String(draft.name || '').trim()) return 'Initiative adı gereklidir.';
    if (!draft.ownerUserId) return 'Initiative sahibi seçin.';
    if (!draft.projectIds || !draft.projectIds.length) return 'En az bir proje bağlayın.';
    var confidence = draft.confidence === '' || draft.confidence == null ? null : Number(draft.confidence);
    if (confidence != null && (!Number.isFinite(confidence) || confidence < 0 || confidence > 100)) {
      return 'Güven 0 ile 100 arasında olmalıdır.';
    }
    return null;
  }

  function validateDependency(draft) {
    if (!draft.sourceProjectId || !draft.targetProjectId) return 'Bağımlılık için iki proje seçin.';
    if (draft.sourceProjectId === draft.targetProjectId) return 'Bir proje kendisine bağlanamaz.';
    if (!String(draft.description || '').trim()) return 'Bağımlılık açıklaması gereklidir.';
    return null;
  }

  function healthLabel(value) {
    return {
      NoUpdate: 'Güncelleme yok',
      OnTrack: 'Yolunda',
      AtRisk: 'Riskli',
      OffTrack: 'Raydan çıktı'
    }[value] || value;
  }

  function statusLabel(value) {
    return {
      Planned: 'Planlandı',
      Active: 'Aktif',
      Paused: 'Duraklatıldı',
      Completed: 'Tamamlandı',
      Cancelled: 'İptal'
    }[value] || value;
  }

  function projectName(projectId, projects) {
    var project = (projects || []).find(function(item) { return item.id === projectId; });
    return project ? project.name : 'Erişilemeyen proje';
  }

  function unique(values) {
    return values.filter(function(value, index) { return value && values.indexOf(value) === index; });
  }

  function iso(value) {
    if (!value) return null;
    var date = value instanceof Date ? value : new Date(value);
    return Number.isFinite(date.getTime()) ? date.toISOString() : null;
  }

  function byName(left, right) {
    return String(left.name || '').localeCompare(String(right.name || ''), 'tr-TR');
  }

  return {
    portfolio: portfolio,
    initiative: initiative,
    dependency: dependency,
    portfolioPayload: portfolioPayload,
    initiativePayload: initiativePayload,
    dependencyPayload: dependencyPayload,
    tree: tree,
    validatePortfolio: validatePortfolio,
    validateInitiative: validateInitiative,
    validateDependency: validateDependency,
    healthLabel: healthLabel,
    statusLabel: statusLabel,
    projectName: projectName
  };
});
