(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('WorkAutomationController', function(
      $scope,
      $stateParams,
      $q,
      zumboApi,
      sessionStore,
      apiClient,
      mobileActionError) {
      var vm = this;
      var core = window.ZumboWorkAutomationCore;
      var tabs = ['schedules', 'templates', 'activity'];

      apiClient.transitionContext('work-automation:' + $stateParams.projectId);
      vm.tab = tabs.indexOf($stateParams.tab) >= 0 ? $stateParams.tab : 'schedules';
      vm.project = null;
      vm.boards = [];
      vm.schema = { issueTypes: [] };
      vm.model = emptyModel();
      vm.timeZone = core.timeZone();
      vm.limits = core.limits;
      vm.loading = true;
      resetDrafts();

      vm.setTab = function(tab) {
        if (tabs.indexOf(tab) < 0) return;
        vm.tab = tab;
        vm.error = null;
        vm.confirmation = null;
      };
      vm.templateName = function(id) { return core.templateName(vm.model.templates, id); };
      vm.frequencyLabel = core.frequencyLabel;
      vm.recurrenceState = core.recurrenceState;
      vm.occurrenceState = core.occurrenceState;
      vm.labelState = function() { return core.normalizeLabels(vm.templateDraft.labelsText); };
      vm.issueTypes = function() {
        var active = (vm.schema.issueTypes || []).filter(function(type) { return type.active; });
        return active.length ? active : [{ key: 'Task', name: 'Task' }];
      };
      vm.selectedRecurrence = function() {
        return vm.model.recurrences.find(function(item) { return item.id === vm.selectedRecurrenceId; }) || null;
      };
      vm.requestConfirmation = function(kind, id) { vm.confirmation = { kind: kind, id: id }; };
      vm.cancelConfirmation = function() { vm.confirmation = null; };
      vm.confirmationIs = function(kind, id) {
        return !!vm.confirmation && vm.confirmation.kind === kind && vm.confirmation.id === id;
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        var projectId = $stateParams.projectId;
        return $q.all([
          zumboApi.project(projectId),
          zumboApi.boards(projectId),
          zumboApi.workItemSchema(projectId),
          zumboApi.workTemplates(projectId, true),
          zumboApi.workRecurrences(projectId, true)
        ]).then(function(result) {
          vm.project = result[0];
          vm.boards = result[1];
          vm.schema = result[2] || { issueTypes: [] };
          sessionStore.state.project = vm.project;
          var role = core.roleOf(vm.project, sessionStore.state.currentUser && sessionStore.state.currentUser.id);
          vm.role = role;
          vm.canEdit = core.canEdit(role, sessionStore.state.currentUser);
          var templates = result[3].items || [];
          var recurrences = result[4].items || [];
          templates.forEach(function(template) {
            apiClient.remember('/api/work-items/templates/' + template.id, template);
          });
          recurrences.forEach(function(recurrence) {
            apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
          });
          vm.model = {
            templates: templates,
            activeTemplates: templates.filter(function(template) { return !template.archived; }),
            recurrences: recurrences,
            activeRecurrences: recurrences.filter(function(recurrence) {
              return !recurrence.archived && recurrence.active;
            }),
            occurrences: vm.model.occurrences || [],
            audit: vm.model.audit || [],
            auditTarget: vm.model.auditTarget || null
          };
          if (!vm.templateDraft.boardId && vm.boards.length) vm.templateDraft.boardId = vm.boards[0].id;
          if (!vm.recurrenceDraft.templateId && vm.model.activeTemplates.length) {
            vm.recurrenceDraft.templateId = vm.model.activeTemplates[0].id;
          }
          var selected = recurrences.find(function(recurrence) {
            return recurrence.id === vm.selectedRecurrenceId;
          }) || recurrences[0];
          return selected ? vm.selectRecurrence(selected) : null;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Otomasyon kayıtları yüklenemedi.');
          return null;
        }).finally(function() {
          vm.loading = false;
        });
      };

      vm.editTemplate = function(template) {
        vm.templateDraft = core.templateDraft(template, firstBoardId());
        vm.tab = 'templates';
        loadAudit('WorkItemTemplate', template.id, template.name);
      };
      vm.cancelTemplateEdit = resetTemplateDraft;
      vm.useTemplate = function(template) {
        vm.recurrenceDraft = core.recurrenceDraft(template.id);
        vm.preview = null;
        vm.tab = 'schedules';
      };
      vm.saveTemplate = function() {
        var draft = vm.templateDraft;
        var labels = core.normalizeLabels(draft.labelsText);
        if (!vm.canEdit || vm.busy || !draft.boardId || !draft.name || !draft.title
            || labels.tooMany || labels.tooLong) return;
        var request = {
          boardId: draft.boardId,
          name: draft.name,
          title: draft.title,
          description: draft.description || null,
          type: draft.type,
          priority: draft.priority,
          assigneeUserId: draft.assigneeUserId || null,
          teamId: draft.teamId || null,
          dueAfterDays: draft.dueAfterDays == null || draft.dueAfterDays === ''
            ? null
            : Number(draft.dueAfterDays),
          labels: labels.values,
          customFields: core.customFieldsForRequest(draft.customFields)
        };
        return mutate(
          draft.id
            ? zumboApi.updateWorkTemplate(draft.id, request)
            : zumboApi.createWorkTemplate(angular.extend({ projectId: vm.project.id }, request)),
          draft.id ? 'İş şablonu güncellendi.' : 'İş şablonu oluşturuldu.',
          resetTemplateDraft);
      };
      vm.archiveTemplate = function(template) {
        if (!vm.canEdit || !vm.confirmationIs('template', template.id)) return;
        apiClient.remember('/api/work-items/templates/' + template.id, template);
        return mutate(
          zumboApi.archiveWorkTemplate(template.id),
          'İş şablonu arşivlendi.',
          resetTemplateDraft);
      };

      vm.previewRecurrence = function() {
        var request = recurrenceRequest();
        if (!vm.canEdit || vm.busy || !validRecurrence(request)) return;
        vm.busy = true;
        vm.error = null;
        vm.preview = null;
        return zumboApi.previewWorkRecurrence(angular.extend({
          previewCount: core.limits.previewCount
        }, request)).then(function(preview) {
          vm.preview = preview;
          return preview;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Takvim önizlemesi oluşturulamadı.');
          return null;
        }).finally(function() { vm.busy = false; });
      };
      vm.createRecurrence = function() {
        var request = recurrenceRequest();
        if (!vm.canEdit || vm.busy || !validRecurrence(request)) return;
        return mutate(
          zumboApi.createWorkRecurrence(request),
          'Yineleme etkinleştirildi.',
          resetRecurrenceDraft);
      };
      vm.setRecurrenceState = function(recurrence, active) {
        if (!vm.canEdit || recurrence.archived || recurrence.active === active) return;
        apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
        return mutate(
          zumboApi.setWorkRecurrenceState(recurrence.id, active),
          active ? 'Yineleme devam ettirildi.' : 'Yineleme duraklatıldı.',
          angular.noop);
      };
      vm.archiveRecurrence = function(recurrence) {
        if (!vm.canEdit || !vm.confirmationIs('recurrence', recurrence.id)) return;
        apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
        return mutate(
          zumboApi.archiveWorkRecurrence(recurrence.id),
          'Yineleme arşivlendi.',
          angular.noop);
      };
      vm.selectRecurrence = function(recurrence) {
        vm.selectedRecurrenceId = recurrence.id;
        vm.occurrenceLoading = true;
        vm.occurrenceError = null;
        return $q.all([
          zumboApi.workRecurrenceOccurrences(recurrence.id),
          loadAudit('WorkItemRecurrence', recurrence.id, vm.templateName(recurrence.templateId))
        ]).then(function(result) {
          if (vm.selectedRecurrenceId === recurrence.id) vm.model.occurrences = result[0].items || [];
          return recurrence;
        }).catch(function(error) {
          vm.occurrenceError = mobileActionError(error, 'Çalıştırma geçmişi yüklenemedi.');
          return null;
        }).finally(function() {
          vm.occurrenceLoading = false;
        });
      };

      $scope.$on('zumbo:concurrency-conflict', function(_, conflict) {
        if (!conflict.resource || ['work-item-templates', 'work-item-recurrences'].indexOf(conflict.resource.kind) < 0) return;
        resetDrafts();
        vm.notice = null;
        vm.error = 'Kayıt başka bir kullanıcı tarafından değiştirildi. Güncel durum yeniden yüklendi.';
        vm.load();
      });

      function loadAudit(type, id, label) {
        return zumboApi.audit(type, id).then(function(entries) {
          vm.model.audit = core.auditEntries(entries);
          vm.model.auditTarget = { type: type, id: id, label: label };
          return vm.model.audit;
        }).catch(function() {
          vm.model.audit = [];
          vm.model.auditTarget = { type: type, id: id, label: label };
          return [];
        });
      }
      function mutate(request, message, reset) {
        if (vm.busy) return $q.when(null);
        vm.busy = true;
        vm.error = null;
        vm.notice = null;
        vm.confirmation = null;
        return request.then(function(result) {
          reset();
          vm.notice = message;
          return vm.load().then(function() { return result; });
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Otomasyon işlemi tamamlanamadı.');
          if (/CONFLICT$/.test(error.code || '')) {
            resetDrafts();
            return vm.load();
          }
          return null;
        }).finally(function() { vm.busy = false; });
      }
      function recurrenceRequest() {
        return core.recurrenceRequest(vm.project && vm.project.id, vm.recurrenceDraft);
      }
      function validRecurrence(request) {
        return !!request.projectId && !!request.templateId && !!request.startAtUtc
          && request.interval >= 1 && request.interval <= core.limits.recurrenceInterval
          && request.maxOccurrences >= 1
          && request.maxOccurrences <= core.limits.recurrenceOccurrences;
      }
      function firstBoardId() { return vm.boards.length ? vm.boards[0].id : ''; }
      function resetTemplateDraft() {
        vm.templateDraft = core.templateDraft(null, firstBoardId());
      }
      function resetRecurrenceDraft() {
        var template = vm.model.activeTemplates && vm.model.activeTemplates[0];
        vm.recurrenceDraft = core.recurrenceDraft(template && template.id);
        vm.preview = null;
      }
      function resetDrafts() {
        resetTemplateDraft();
        resetRecurrenceDraft();
        vm.confirmation = null;
      }
      function emptyModel() {
        return {
          templates: [],
          activeTemplates: [],
          recurrences: [],
          activeRecurrences: [],
          occurrences: [],
          audit: [],
          auditTarget: null
        };
      }

      vm.load();
    });
})();
