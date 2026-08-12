/* global module */
(function(root, factory) {
  'use strict';

  var core = factory();
  if (typeof module === 'object' && module.exports) module.exports = core;
  if (root) root.ZumboProjectCatalogCore = core;
})(typeof window !== 'undefined' ? window : globalThis, function() {
  'use strict';

  var limits = Object.freeze({
    templateName: 120,
    defaultComponentCount: 50,
    componentName: 80,
    componentDescription: 500,
    versionName: 80,
    releaseName: 100,
    milestoneName: 100
  });

  function roleOf(project, userId) {
    var member = (project && project.members || []).find(function(candidate) {
      return candidate.userId === userId;
    });
    return member ? member.role : null;
  }

  function definitionFor(role, definitions) {
    return (definitions || []).find(function(item) { return item.name === role && item.isActive !== false; }) || null;
  }

  function canManage(role, definitions) {
    var definition = definitionFor(role, definitions);
    return !!definition && (definition.permissions || []).some(function(permission) {
      return permission === '*' || permission === 'BoardManage';
    });
  }

  function canRelease(role, definitions) {
    return !!(definitionFor(role, definitions) || {}).isProtected;
  }

  function normalizeComponentNames(value) {
    var seen = Object.create(null);
    var values = String(value || '')
      .split(/[\n,]/)
      .map(function(item) { return item.trim(); })
      .filter(function(item) {
        if (!item) return false;
        var key = item.toLowerCase();
        if (seen[key]) return false;
        seen[key] = true;
        return true;
      });
    return {
      values: values,
      tooMany: values.length > limits.defaultComponentCount,
      tooLong: values.some(function(item) { return item.length > limits.componentName; })
    };
  }

  function toDateInput(value) {
    if (!value) return null;
    var parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  function versionName(project, versionId) {
    var version = (project && project.versions || []).find(function(candidate) {
      return candidate.id === versionId;
    });
    return version ? version.name : 'Bilinmeyen sürüm';
  }

  function snapshot(project) {
    project = project || {};
    return {
      templates: (project.templates || []).slice(),
      activeTemplates: (project.templates || []).filter(function(item) { return !item.archived; }),
      components: (project.components || []).slice(),
      activeComponents: (project.components || []).filter(function(item) { return !item.archived; }),
      versions: (project.versions || []).slice(),
      plannedVersions: (project.versions || []).filter(function(item) { return item.status === 'Planned'; }),
      releases: (project.releases || []).slice(),
      milestones: (project.milestones || []).slice().sort(function(left, right) {
        return new Date(left.dueAt).getTime() - new Date(right.dueAt).getTime();
      }),
      openMilestones: (project.milestones || []).filter(function(item) { return item.status === 'Open'; })
    };
  }

  function auditEntries(entries) {
    return (entries || []).filter(function(entry) {
      return /^Project(?:Template|Component|Version|Release|Milestone)/.test(entry.action || '');
    });
  }

  function errorMessage(error, fallback) {
    var code = error && (error.code || error.data && error.data.error && error.data.error.code);
    var messages = {
      PROJECT_TEMPLATE_EXISTS: 'Bu adla etkin bir proje şablonu zaten var.',
      PROJECT_DEFAULT_TEMPLATE_REQUIRED: 'Önce başka bir şablonu varsayılan yapın.',
      PROJECT_TEMPLATE_ARCHIVED: 'Arşivlenmiş şablon değiştirilemez.',
      PROJECT_COMPONENT_EXISTS: 'Bu adla etkin bir bileşen zaten var.',
      PROJECT_VERSION_EXISTS: 'Bu adla etkin bir sürüm zaten var.',
      PROJECT_VERSION_RELEASED: 'Yayınlanmış sürüm arşivlenemez.',
      PROJECT_VERSION_HAS_RELEASE: 'Etkin yayını olan sürüm arşivlenemez.',
      PROJECT_RELEASE_EXISTS: 'Bu sürüm için zaten bir yayın var.',
      PROJECT_RELEASE_NOT_DRAFT: 'Yalnızca taslak yayın onaylanabilir.',
      PROJECT_RELEASE_NOT_APPROVED: 'Yayınlamadan önce onay gerekir.',
      PROJECT_MILESTONE_EXISTS: 'Bu adla açık bir kilometre taşı zaten var.',
      PROJECT_MILESTONE_COMPLETED: 'Tamamlanmış kilometre taşı değiştirilemez.',
      CONCURRENCY_CONFLICT: 'Proje başka bir kullanıcı tarafından değiştirildi. Güncel kayıt yeniden yüklendi.',
      FORBIDDEN: 'Bu işlem için proje yetkiniz yok.',
      VALIDATION_ERROR: 'Alanları ve belirtilen sınırları kontrol edin.'
    };
    return messages[code] || error && error.message || fallback;
  }

  return Object.freeze({
    limits: limits,
    roleOf: roleOf,
    canManage: canManage,
    canRelease: canRelease,
    normalizeComponentNames: normalizeComponentNames,
    toDateInput: toDateInput,
    versionName: versionName,
    snapshot: snapshot,
    auditEntries: auditEntries,
    errorMessage: errorMessage
  });
});
