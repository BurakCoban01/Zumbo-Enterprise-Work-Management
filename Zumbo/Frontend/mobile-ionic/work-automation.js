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
      var tabs = ['rules', 'runs', 'schedules', 'templates', 'activity'];
      var loadSequence = 0;
      var ruleSequence = 0;

      apiClient.transitionContext('work-automation:' + $stateParams.projectId);
      vm.tab = tabs.indexOf($stateParams.tab) >= 0 ? $stateParams.tab : 'rules';
      vm.project = null;
      vm.boards = [];
      vm.schema = { issueTypes: [] };
      vm.model = emptyModel();
      vm.timeZone = core.timeZone();
      vm.limits = core.limits;
      vm.eventTypes = [
        { value: 'WorkItemCreated', label: 'İş oluşturulduğunda' },
        { value: 'WorkItemUpdated', label: 'İş güncellendiğinde' },
        { value: 'WorkItemTransitioned', label: 'İş durum değiştirdiğinde' }
      ];
      vm.conditionFields = ['Status', 'PreviousStatus', 'Priority', 'Type', 'AssigneeUserId', 'Labels'];
      vm.conditionOperators = ['Equals', 'NotEquals', 'Contains', 'NotContains', 'IsEmpty', 'IsNotEmpty'];
      vm.actionTypes = ['AssignToActor', 'AssignUser', 'ClearAssignee', 'AddLabel', 'RemoveLabel', 'SetPriority', 'AddComment'];
      vm.runStatusFilter = '';
      vm.loading = true;
      resetDrafts();

      vm.setTab = function(tab) {
        if (tabs.indexOf(tab) < 0) return;
        vm.tab = tab;
        vm.error = null;
        vm.confirmation = null;
        if (tab === 'runs') vm.loadRuns();
      };
      vm.templateName = function(id) { return core.templateName(vm.model.templates, id); };
      vm.frequencyLabel = core.frequencyLabel;
      vm.recurrenceState = core.recurrenceState;
      vm.occurrenceState = core.occurrenceState;
      vm.ruleState = core.ruleState;
      vm.runState = core.runState;
      vm.ruleTriggerLabel = core.triggerLabel;
      vm.actionNeedsValue = core.actionNeedsValue;
      vm.conditionNeedsValue = core.conditionNeedsValue;
      vm.conditionFieldLabel = core.conditionFieldLabel;
      vm.conditionOperatorLabel = core.conditionOperatorLabel;
      vm.actionTypeLabel = core.actionTypeLabel;
      vm.validRule = function() { return core.validRule(vm.ruleDraft); };
      vm.selectedRule = function() {
        return vm.model.rules.find(function(rule) { return rule.id === vm.selectedRuleId; }) || null;
      };
      vm.selectedRun = function() {
        return vm.model.runs.find(function(run) { return run.id === vm.selectedRunId; }) || null;
      };
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
        var sequence = ++loadSequence;
        vm.loading = true;
        vm.error = null;
        var projectId = $stateParams.projectId;
        return $q.all([
          zumboApi.project(projectId),
          zumboApi.boards(projectId),
          zumboApi.workItemSchema(projectId),
          zumboApi.workTemplates(projectId, true),
          zumboApi.workRecurrences(projectId, true),
          zumboApi.automationRules(projectId, true),
          zumboApi.automationRuns(projectId, '')
        ]).then(function(result) {
          if (sequence !== loadSequence) return null;
          vm.project = result[0];
          vm.boards = result[1];
          vm.schema = result[2] || { issueTypes: [] };
          sessionStore.state.project = vm.project;
          var role = core.roleOf(vm.project, sessionStore.state.currentUser && sessionStore.state.currentUser.id);
          vm.role = role;
          vm.canEdit = core.canEdit(role, sessionStore.state.currentUser);
          var templates = result[3].items || [];
          var recurrences = result[4].items || [];
          var rules = result[5] && result[5].items || [];
          var runs = result[6] && result[6].items || [];
          templates.forEach(function(template) {
            apiClient.remember('/api/work-items/templates/' + template.id, template);
          });
          recurrences.forEach(function(recurrence) {
            apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
          });
          rules.forEach(function(rule) {
            apiClient.remember('/api/automations/' + rule.id, rule);
          });
          runs.forEach(function(run) {
            apiClient.remember('/api/automations/runs/' + run.id, run);
          });
          vm.model = {
            templates: templates,
            activeTemplates: templates.filter(function(template) { return !template.archived; }),
            recurrences: recurrences,
            activeRecurrences: recurrences.filter(function(recurrence) {
              return !recurrence.archived && recurrence.active;
            }),
            rules: rules,
            activeRules: rules.filter(function(rule) { return rule.active && !rule.archived; }),
            runs: runs,
            runTotal: result[6] && result[6].total || runs.length,
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
          if (!vm.selectedRuleId && rules.length) {
            vm.selectedRuleId = (rules.find(function(rule) {
              return !rule.archived;
            }) || rules[0]).id;
          }
          var selections = [];
          if (selected) selections.push(vm.selectRecurrence(selected));
          var selectedRule = rules.find(function(rule) {
            return rule.id === vm.selectedRuleId;
          });
          if (selectedRule) selections.push(vm.selectRule(selectedRule));
          return selections.length ? $q.all(selections) : null;
        }).catch(function(error) {
          if (sequence === loadSequence) {
            vm.error = mobileActionError(error, 'Otomasyon kayıtları yüklenemedi.');
          }
          return null;
        }).finally(function() {
          if (sequence === loadSequence) vm.loading = false;
        });
      };

      vm.newRule = function() {
        ruleSequence += 1;
        vm.selectedRuleId = null;
        vm.ruleDraft = core.newRuleDraft();
        vm.dryRunResult = null;
        vm.error = null;
      };
      vm.addCondition = function() {
        if (vm.ruleDraft.conditions.length >= core.limits.ruleConditions) return;
        vm.ruleDraft.conditions.push({ field: 'Status', operator: 'Equals', value: '' });
      };
      vm.removeCondition = function(index) { vm.ruleDraft.conditions.splice(index, 1); };
      vm.addAction = function() {
        if (vm.ruleDraft.actions.length >= core.limits.ruleActions) return;
        vm.ruleDraft.actions.push({ type: 'AddLabel', value: '' });
      };
      vm.removeAction = function(index) {
        if (vm.ruleDraft.actions.length > 1) vm.ruleDraft.actions.splice(index, 1);
      };
      vm.selectRule = function(rule) {
        var sequence = ++ruleSequence;
        vm.selectedRuleId = rule.id;
        vm.ruleLoading = true;
        vm.error = null;
        vm.dryRunResult = null;
        return zumboApi.automationRule(rule.id, !!rule.hasDraft).catch(function(error) {
          if (!rule.hasDraft || error.code !== 'AUTOMATION_DRAFT_NOT_FOUND') {
            return $q.reject(error);
          }
          return zumboApi.automationRule(rule.id, false);
        }).then(function(detail) {
          if (sequence !== ruleSequence || vm.selectedRuleId !== rule.id) return null;
          apiClient.remember('/api/automations/' + detail.id, detail);
          vm.ruleDraft = core.ruleDraft(detail);
          return detail;
        }).catch(function(error) {
          if (sequence === ruleSequence) {
            vm.error = mobileActionError(error, 'Kural ayrıntısı yüklenemedi.');
          }
          return null;
        }).finally(function() {
          if (sequence === ruleSequence) vm.ruleLoading = false;
        });
      };
      vm.saveRule = function() {
        if (!vm.canEdit || vm.busy || !core.validRule(vm.ruleDraft)) return;
        var request = core.ruleRequest(vm.project.id, vm.ruleDraft);
        return ruleMutate(
          vm.ruleDraft.id
            ? zumboApi.updateAutomationRuleDraft(vm.ruleDraft.id, request)
            : zumboApi.createAutomationRule(request),
          vm.ruleDraft.id ? 'Kural taslağı güncellendi.' : 'Kural taslağı oluşturuldu.');
      };
      vm.publishRule = function() {
        var rule = vm.selectedRule();
        if (!vm.canEdit || !rule || !vm.ruleDraft.id) return;
        apiClient.remember('/api/automations/' + rule.id, vm.ruleDraft);
        return ruleMutate(
          zumboApi.publishAutomationRule(rule.id),
          'Kural yayınlandı ve etkinleştirildi.');
      };
      vm.setRuleState = function(rule, active) {
        if (!vm.canEdit || !rule || rule.active === active) return;
        apiClient.remember('/api/automations/' + rule.id, rule);
        return ruleMutate(
          zumboApi.setAutomationRuleState(rule.id, active),
          active ? 'Kural etkinleştirildi.' : 'Kural duraklatıldı.');
      };
      vm.archiveRule = function(rule) {
        if (!vm.canEdit || !vm.confirmationIs('rule', rule.id)) return;
        apiClient.remember('/api/automations/' + rule.id, rule);
        return ruleMutate(
          zumboApi.archiveAutomationRule(rule.id),
          'Kural arşivlendi.',
          true);
      };
      vm.runDryRun = function() {
        var rule = vm.selectedRule();
        if (!vm.canEdit || !rule || !vm.ruleDraft.id || vm.busy) return;
        vm.busy = true;
        vm.error = null;
        vm.dryRunResult = null;
        return zumboApi.dryRunAutomationRule(rule.id, {
          triggerType: vm.ruleDraft.triggerType,
          eventType: vm.ruleDraft.triggerType === 'Event' ? vm.ruleDraft.eventType : null,
          sourceId: vm.dryRun.sourceId || null,
          fields: {
            Status: vm.dryRun.status || null,
            PreviousStatus: vm.dryRun.previousStatus || null,
            Priority: vm.dryRun.priority || null,
            Type: vm.dryRun.type || null,
            AssigneeUserId: vm.dryRun.assigneeUserId || null,
            Labels: vm.dryRun.labels || null
          }
        }).then(function(result) {
          vm.dryRunResult = result;
          return result;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kural önizlemesi çalıştırılamadı.');
          return null;
        }).finally(function() { vm.busy = false; });
      };
      vm.loadRuns = function() {
        if (!vm.project) return $q.when(null);
        vm.runsLoading = true;
        vm.runsError = null;
        return zumboApi.automationRuns(vm.project.id, vm.runStatusFilter)
          .then(function(page) {
            vm.model.runs = page.items || [];
            vm.model.runTotal = page.total || 0;
            vm.model.runs.forEach(function(run) {
              apiClient.remember('/api/automations/runs/' + run.id, run);
            });
            return page;
          }).catch(function(error) {
            vm.runsError = mobileActionError(error, 'Kural çalıştırmaları yüklenemedi.');
            return null;
          }).finally(function() { vm.runsLoading = false; });
      };
      vm.selectRun = function(run) { vm.selectedRunId = run && run.id; };
      vm.replayRun = function(run) {
        if (!vm.canEdit || !run || run.status !== 'DeadLetter') return;
        apiClient.remember('/api/automations/runs/' + run.id, run);
        vm.busy = true;
        return zumboApi.replayAutomationRun(run.id).then(function() {
          vm.notice = 'Çalıştırma yeniden deneme sırasına alındı.';
          return vm.loadRuns();
        }).catch(function(error) {
          vm.runsError = mobileActionError(error, 'Çalıştırma yeniden sıraya alınamadı.');
        }).finally(function() { vm.busy = false; });
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
        vm.auditError = null;
        return zumboApi.audit(type, id).then(function(entries) {
          vm.model.audit = core.auditEntries(entries);
          vm.model.auditTarget = { type: type, id: id, label: label };
          return vm.model.audit;
        }).catch(function(error) {
          vm.model.audit = [];
          vm.model.auditTarget = { type: type, id: id, label: label };
          vm.auditError = mobileActionError(error, 'Etkinlik kaydı yüklenemedi.');
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
      function ruleMutate(request, message, archived) {
        if (vm.busy) return $q.when(null);
        vm.busy = true;
        vm.error = null;
        vm.notice = null;
        vm.confirmation = null;
        return request.then(function(result) {
          if (result && result.id) {
            apiClient.remember('/api/automations/' + result.id, result);
            vm.selectedRuleId = result.id;
            vm.ruleDraft = core.ruleDraft(result);
          }
          if (archived) {
            ruleSequence += 1;
            vm.selectedRuleId = null;
          }
          vm.notice = message;
          return vm.load().then(function() {
            if (archived) vm.newRule();
            return result;
          });
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kural işlemi tamamlanamadı.');
          if (/CONFLICT$/.test(error.code || '')) vm.load();
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
        vm.ruleDraft = core.newRuleDraft();
        vm.dryRun = {
          sourceId: '',
          status: 'To Do',
          previousStatus: '',
          priority: 'Medium',
          type: 'Task',
          assigneeUserId: '',
          labels: ''
        };
        vm.dryRunResult = null;
      }
      function emptyModel() {
        return {
          templates: [],
          activeTemplates: [],
          recurrences: [],
          activeRecurrences: [],
          rules: [],
          activeRules: [],
          runs: [],
          runTotal: 0,
          occurrences: [],
          audit: [],
          auditTarget: null
        };
      }

      vm.load();
    });
})();
