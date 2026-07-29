/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ZumboDashboardCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  var catalog = [
    item('ProjectSummary', 'Proje özeti'),
    item('StatusDistribution', 'Durum dağılımı'),
    item('UserWorkload', 'İş yükü'),
    item('DueDateRisks', 'Teslimat riskleri'),
    item('FlowTime', 'Akış süresi'),
    item('CompletionRate', 'Tamamlama oranı'),
    item('TeamPerformance', 'Ekip teslimatı')
  ];
  var widgetSequence = 0;

  function item(type, label) { return { type: type, label: label }; }

  function create(projectId) {
    return {
      id: null,
      name: 'Teslimat görünümü',
      description: '',
      scope: 'Personal',
      projectIds: projectId ? [projectId] : [],
      widgets: [widget('ProjectSummary', 0)],
      filter: { rangeDays: 30, dueRiskDays: 30, statuses: [] },
      viewerUserIds: [],
      version: 0,
      canEdit: true
    };
  }

  function widget(type, index) {
    var entry = catalog.find(function(candidate) { return candidate.type === type; }) || catalog[0];
    return {
      id: 'widget-' + Date.now().toString(36) + '-' + index + '-' + (++widgetSequence),
      type: entry.type,
      title: entry.label,
      column: 1,
      row: index * 2 + 1,
      width: 12,
      height: 2,
      projectId: null,
      filter: null
    };
  }

  function fromResponse(value) {
    if (!value) return null;
    return {
      id: value.id,
      name: value.name,
      description: value.description || '',
      scope: value.scope,
      projectIds: (value.projectIds || []).slice(),
      widgets: (value.widgets || []).map(function(entry) {
        return Object.assign({}, entry, { filter: entry.filter || null });
      }),
      filter: Object.assign({ rangeDays: 30, dueRiskDays: 30, statuses: [] }, value.filter || {}),
      viewerUserIds: (value.viewerUserIds || []).slice(),
      version: Number(value.version || 0),
      canEdit: value.canEdit === true,
      archived: value.archived === true
    };
  }

  function addWidget(draft, type) {
    if (!draft || draft.widgets.length >= 12) return false;
    draft.widgets.push(widget(type, draft.widgets.length));
    normalizeRows(draft.widgets);
    return true;
  }

  function removeWidget(draft, id) {
    if (!draft || draft.widgets.length <= 1) return false;
    draft.widgets = draft.widgets.filter(function(entry) { return entry.id !== id; });
    normalizeRows(draft.widgets);
    return true;
  }

  function moveWidget(draft, index, direction) {
    var target = index + direction;
    if (!draft || target < 0 || target >= draft.widgets.length) return false;
    var entry = draft.widgets.splice(index, 1)[0];
    draft.widgets.splice(target, 0, entry);
    normalizeRows(draft.widgets);
    return true;
  }

  function normalizeRows(widgets) {
    widgets.forEach(function(entry, index) {
      entry.column = 1;
      entry.row = index * 2 + 1;
      entry.width = 12;
      entry.height = 2;
    });
  }

  function payload(draft) {
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      scope: draft.scope,
      projectIds: (draft.projectIds || []).slice(),
      widgets: (draft.widgets || []).map(function(entry) {
        return {
          id: entry.id,
          type: entry.type,
          title: String(entry.title || '').trim(),
          column: entry.column,
          row: entry.row,
          width: entry.width,
          height: entry.height,
          projectId: entry.projectId || null,
          filter: entry.filter || null
        };
      }),
      filter: Object.assign({}, draft.filter)
    };
  }

  function validate(draft) {
    if (!draft || !String(draft.name || '').trim()) return 'Dashboard adı zorunludur.';
    var projects = (draft.projectIds || []).filter(Boolean);
    if (!projects.length) return 'En az bir proje seçin.';
    if (draft.scope === 'Project' && projects.length !== 1) {
      return 'Proje dashboardu için tam bir proje seçin.';
    }
    if (draft.scope === 'Portfolio' && projects.length < 2) {
      return 'Portföy dashboardu için en az iki proje seçin.';
    }
    if (!draft.widgets || !draft.widgets.length) return 'En az bir widget ekleyin.';
    if (draft.widgets.length > 12) return 'Bir dashboard en fazla 12 widget içerebilir.';
    if (draft.widgets.some(function(entry) { return !String(entry.title || '').trim(); })) {
      return 'Her widget için bir başlık girin.';
    }
    var rangeDays = Number(draft.filter && draft.filter.rangeDays);
    var dueRiskDays = Number(draft.filter && draft.filter.dueRiskDays);
    if (rangeDays < 1 || rangeDays > 366) return 'Dönem 1 ile 366 gün arasında olmalıdır.';
    if (dueRiskDays < 1 || dueRiskDays > 90) return 'Risk günü 1 ile 90 arasında olmalıdır.';
    return null;
  }

  return {
    catalog: catalog,
    create: create,
    fromResponse: fromResponse,
    addWidget: addWidget,
    removeWidget: removeWidget,
    moveWidget: moveWidget,
    payload: payload,
    validate: validate
  };
});
