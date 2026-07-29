(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopWorkAutomationFeature', function($q, apiClient) {
      var core = window.ZumboWorkAutomationCore;

      return {
        install: function(vm) {
          var loadSequence = 0;
          var ruleSequence = 0;

          vm.workAutomationTabs = [
            { id: 'rules', label: 'Kurallar', icon: 'workflow' },
            { id: 'runs', label: 'Kural çalıştırmaları', icon: 'list-checks' },
            { id: 'schedules', label: 'Yinelemeler', icon: 'repeat-2' },
            { id: 'templates', label: 'İş şablonları', icon: 'copy-check' },
            { id: 'activity', label: 'Çalıştırma geçmişi', icon: 'history' }
          ];
          vm.workAutomationTab = 'rules';
          vm.workAutomationLimits = core.limits;
          vm.workAutomationTimeZone = core.timeZone();
          vm.automationEventTypes = [
            { value: 'WorkItemCreated', label: 'İş oluşturulduğunda' },
            { value: 'WorkItemUpdated', label: 'İş güncellendiğinde' },
            { value: 'WorkItemTransitioned', label: 'İş durum değiştirdiğinde' }
          ];
          vm.automationConditionFields = [
            'Status', 'PreviousStatus', 'Priority', 'Type', 'AssigneeUserId', 'Labels'
          ];
          vm.automationConditionOperators = [
            'Equals', 'NotEquals', 'Contains', 'NotContains', 'IsEmpty', 'IsNotEmpty'
          ];
          vm.automationActionTypes = [
            'AssignToActor', 'AssignUser', 'ClearAssignee', 'AddLabel',
            'RemoveLabel', 'SetPriority', 'AddComment'
          ];
          vm.automationRunStatusFilter = '';
          vm.workAutomation = emptyModel();
          resetDrafts();

          vm.syncWorkAutomationContext = function(project) {
            var projectId = project && project.id;
            if (vm.workAutomation.projectId !== projectId) {
              loadSequence += 1;
              vm.workAutomation = emptyModel(projectId);
              resetDrafts();
            }
            vm.workAutomationRole = core.roleOf(
              project,
              vm.session.currentUser && vm.session.currentUser.id);
            vm.canEditWorkAutomation = core.canEdit(
              vm.workAutomationRole,
              vm.session.currentUser);
          };
          vm.syncWorkAutomationContext(vm.project);

          vm.setWorkAutomationTab = function(tab) {
            if (!vm.workAutomationTabs.some(function(candidate) { return candidate.id === tab; })) return;
            vm.workAutomationTab = tab;
            vm.workAutomationError = null;
            vm.workAutomationConfirmation = null;
            if (tab === 'runs') vm.loadAutomationRuns();
          };
          vm.automationTemplateName = function(id) {
            return core.templateName(vm.workAutomation.templates, id);
          };
          vm.automationFrequencyLabel = core.frequencyLabel;
          vm.automationRecurrenceState = core.recurrenceState;
          vm.automationOccurrenceState = core.occurrenceState;
          vm.automationRuleState = core.ruleState;
          vm.automationRunState = core.runState;
          vm.automationRuleTriggerLabel = core.triggerLabel;
          vm.automationActionNeedsValue = core.actionNeedsValue;
          vm.automationConditionNeedsValue = core.conditionNeedsValue;
          vm.automationConditionFieldLabel = core.conditionFieldLabel;
          vm.automationConditionOperatorLabel = core.conditionOperatorLabel;
          vm.automationActionTypeLabel = core.actionTypeLabel;
          vm.validAutomationRule = function() { return core.validRule(vm.automationRuleDraft); };
          vm.selectedAutomationRule = function() {
            return vm.workAutomation.rules.find(function(rule) {
              return rule.id === vm.automationSelectedRuleId;
            }) || null;
          };
          vm.selectedAutomationRun = function() {
            return vm.workAutomation.runs.find(function(run) {
              return run.id === vm.automationSelectedRunId;
            }) || null;
          };
          vm.automationIssueTypes = function() {
            var types = vm.activeIssueTypes ? vm.activeIssueTypes() : [];
            return types.length ? types : [{ key: 'Task', name: 'Task', active: true }];
          };
          vm.selectedWorkRecurrence = function() {
            return vm.workAutomation.recurrences.find(function(recurrence) {
              return recurrence.id === vm.automationSelectedRecurrenceId;
            }) || null;
          };
          vm.automationLabelState = function() {
            return core.normalizeLabels(vm.workTemplateDraft.labelsText);
          };
          vm.automationMemberName = function(userId) {
            if (!userId) return 'Atanmamış';
            return vm.userName ? vm.userName(userId) : userId;
          };
          vm.requestWorkAutomationConfirmation = function(kind, id) {
            vm.workAutomationConfirmation = { kind: kind, id: id };
          };
          vm.cancelWorkAutomationConfirmation = function() {
            vm.workAutomationConfirmation = null;
          };
          vm.workAutomationConfirmationIs = function(kind, id) {
            return !!vm.workAutomationConfirmation
              && vm.workAutomationConfirmation.kind === kind
              && vm.workAutomationConfirmation.id === id;
          };

          vm.loadWorkAutomation = function() {
            if (!vm.project || !vm.projectMembership) return $q.when(null);
            var projectId = vm.project.id;
            var sequence = ++loadSequence;
            vm.workAutomationLoading = true;
            vm.workAutomationError = null;
            return $q.all([
              apiClient.get('/api/work-items/templates?projectId=' + encodeURIComponent(projectId)
                + '&page=1&pageSize=100&includeArchived=true'),
              apiClient.get('/api/work-items/recurrences?projectId=' + encodeURIComponent(projectId)
                + '&page=1&pageSize=100&includeArchived=true'),
              apiClient.get('/api/automations?projectId=' + encodeURIComponent(projectId)
                + '&page=1&pageSize=100&includeArchived=true',
              { scope: 'desktop-automation-rules', replace: true }),
              apiClient.get('/api/automations/runs?projectId=' + encodeURIComponent(projectId)
                + '&page=1&pageSize=50',
              { scope: 'desktop-automation-runs', replace: true })
            ]).then(function(result) {
              if (sequence !== loadSequence || !vm.project || vm.project.id !== projectId) return null;
              var templates = result[0].items || [];
              var recurrences = result[1].items || [];
              var rules = result[2] && result[2].items || [];
              var runs = result[3] && result[3].items || [];
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
              vm.workAutomation = {
                projectId: projectId,
                templates: templates,
                activeTemplates: templates.filter(function(template) { return !template.archived; }),
                recurrences: recurrences,
                activeRecurrences: recurrences.filter(function(recurrence) {
                  return !recurrence.archived && recurrence.active;
                }),
                rules: rules,
                activeRules: rules.filter(function(rule) { return rule.active && !rule.archived; }),
                runs: runs,
                runTotal: result[3] && result[3].total || runs.length,
                occurrences: vm.workAutomation.occurrences || [],
                audit: vm.workAutomation.audit || [],
                auditTarget: vm.workAutomation.auditTarget || null
              };
              if (!vm.workTemplateDraft.id && !vm.workTemplateDraft.boardId && vm.boards.length) {
                vm.workTemplateDraft.boardId = vm.boards[0].id;
              }
              if (!vm.recurrenceDraft.templateId && vm.workAutomation.activeTemplates.length) {
                vm.recurrenceDraft.templateId = vm.workAutomation.activeTemplates[0].id;
              }
              var selected = recurrences.find(function(recurrence) {
                return recurrence.id === vm.automationSelectedRecurrenceId;
              }) || recurrences[0];
              if (!vm.automationSelectedRuleId && rules.length) {
                vm.automationSelectedRuleId = (rules.find(function(rule) {
                  return !rule.archived;
                }) || rules[0]).id;
              }
              var selections = [];
              if (selected) selections.push(vm.selectWorkRecurrence(selected));
              var selectedRule = rules.find(function(rule) {
                return rule.id === vm.automationSelectedRuleId;
              });
              if (selectedRule) selections.push(vm.selectAutomationRule(selectedRule));
              return selections.length ? $q.all(selections) : null;
            }).catch(function(error) {
              if (sequence === loadSequence) {
                vm.workAutomationError = core.errorMessage(error, 'Otomasyon kayıtları yüklenemedi.');
              }
              return null;
            }).finally(function() {
              if (sequence === loadSequence) vm.workAutomationLoading = false;
            });
          };

          vm.newAutomationRule = function() {
            ruleSequence += 1;
            vm.automationSelectedRuleId = null;
            vm.automationRuleDraft = core.newRuleDraft();
            vm.automationDryRunResult = null;
            vm.workAutomationError = null;
          };
          vm.addAutomationCondition = function() {
            if (vm.automationRuleDraft.conditions.length >= core.limits.ruleConditions) return;
            vm.automationRuleDraft.conditions.push({
              field: 'Status',
              operator: 'Equals',
              value: ''
            });
          };
          vm.removeAutomationCondition = function(index) {
            vm.automationRuleDraft.conditions.splice(index, 1);
          };
          vm.addAutomationAction = function() {
            if (vm.automationRuleDraft.actions.length >= core.limits.ruleActions) return;
            vm.automationRuleDraft.actions.push({ type: 'AddLabel', value: '' });
          };
          vm.removeAutomationAction = function(index) {
            if (vm.automationRuleDraft.actions.length <= 1) return;
            vm.automationRuleDraft.actions.splice(index, 1);
          };
          vm.selectAutomationRule = function(rule) {
            if (!rule) return $q.when(null);
            var sequence = ++ruleSequence;
            vm.automationSelectedRuleId = rule.id;
            vm.automationRuleLoading = true;
            vm.workAutomationError = null;
            vm.automationDryRunResult = null;
            return apiClient.get(
              '/api/automations/' + rule.id + (rule.hasDraft ? '?draft=true' : ''),
              {
              scope: 'desktop-automation-rule-detail',
              replace: true
              }).catch(function(error) {
              if (!rule.hasDraft || error.code !== 'AUTOMATION_DRAFT_NOT_FOUND') {
                return $q.reject(error);
              }
              return apiClient.get('/api/automations/' + rule.id, {
                scope: 'desktop-automation-rule-detail',
                replace: true
              });
            }).then(function(detail) {
              if (sequence !== ruleSequence || vm.automationSelectedRuleId !== rule.id) return null;
              apiClient.remember('/api/automations/' + detail.id, detail);
              vm.automationRuleDraft = core.ruleDraft(detail);
              return detail;
            }).catch(function(error) {
              if (sequence === ruleSequence) {
                vm.workAutomationError = core.errorMessage(error, 'Kural ayrıntısı yüklenemedi.');
              }
              return null;
            }).finally(function() {
              if (sequence === ruleSequence) vm.automationRuleLoading = false;
            });
          };
          vm.saveAutomationRule = function() {
            if (!vm.canEditWorkAutomation || vm.workAutomationBusy
                || !core.validRule(vm.automationRuleDraft)) return;
            var draft = vm.automationRuleDraft;
            var request = core.ruleRequest(vm.project.id, draft);
            var promise = draft.id
              ? apiClient.put('/api/automations/' + draft.id + '/draft', request)
              : apiClient.post('/api/automations', request);
            return ruleMutate(promise, draft.id ? 'Kural taslağı güncellendi.' : 'Kural taslağı oluşturuldu.');
          };
          vm.publishAutomationRule = function() {
            var rule = vm.selectedAutomationRule();
            if (!vm.canEditWorkAutomation || !rule || !vm.automationRuleDraft.id) return;
            apiClient.remember('/api/automations/' + rule.id, vm.automationRuleDraft);
            return ruleMutate(
              apiClient.post('/api/automations/' + rule.id + '/publish', {}),
              'Kural yayınlandı ve etkinleştirildi.');
          };
          vm.setAutomationRuleState = function(rule, active) {
            if (!vm.canEditWorkAutomation || !rule || rule.active === active) return;
            apiClient.remember('/api/automations/' + rule.id, rule);
            return ruleMutate(
              apiClient.patch('/api/automations/' + rule.id + '/state', { active: active }),
              active ? 'Kural etkinleştirildi.' : 'Kural duraklatıldı.');
          };
          vm.archiveAutomationRule = function(rule) {
            if (!vm.canEditWorkAutomation
                || !vm.workAutomationConfirmationIs('rule', rule.id)) return;
            apiClient.remember('/api/automations/' + rule.id, rule);
            return ruleMutate(
              apiClient.delete('/api/automations/' + rule.id),
              'Kural arşivlendi.',
              true);
          };
          vm.runAutomationDryRun = function() {
            var rule = vm.selectedAutomationRule();
            if (!vm.canEditWorkAutomation || !rule || !vm.automationRuleDraft.id
                || vm.workAutomationBusy) return;
            vm.workAutomationBusy = true;
            vm.workAutomationError = null;
            vm.automationDryRunResult = null;
            var context = {
              triggerType: vm.automationRuleDraft.triggerType,
              eventType: vm.automationRuleDraft.triggerType === 'Event'
                ? vm.automationRuleDraft.eventType
                : null,
              sourceId: vm.automationDryRun.sourceId || null,
              fields: {
                Status: vm.automationDryRun.status || null,
                PreviousStatus: vm.automationDryRun.previousStatus || null,
                Priority: vm.automationDryRun.priority || null,
                Type: vm.automationDryRun.type || null,
                AssigneeUserId: vm.automationDryRun.assigneeUserId || null,
                Labels: vm.automationDryRun.labels || null
              }
            };
            return apiClient.post('/api/automations/' + rule.id + '/dry-run', context)
              .then(function(result) {
                vm.automationDryRunResult = result;
                return result;
              }).catch(function(error) {
                vm.workAutomationError = core.errorMessage(error, 'Kural önizlemesi çalıştırılamadı.');
                return null;
              }).finally(function() { vm.workAutomationBusy = false; });
          };
          vm.loadAutomationRuns = function() {
            if (!vm.project) return $q.when(null);
            vm.automationRunsLoading = true;
            vm.automationRunsError = null;
            return apiClient.get(
              '/api/automations/runs?projectId=' + encodeURIComponent(vm.project.id)
                + '&page=1&pageSize=50'
                + (vm.automationRunStatusFilter
                  ? '&status=' + encodeURIComponent(vm.automationRunStatusFilter)
                  : ''),
              {
              scope: 'desktop-automation-runs',
              replace: true
              }).then(function(page) {
              vm.workAutomation.runs = page.items || [];
              vm.workAutomation.runTotal = page.total || 0;
              vm.workAutomation.runs.forEach(function(run) {
                apiClient.remember('/api/automations/runs/' + run.id, run);
              });
              return page;
            }).catch(function(error) {
              vm.automationRunsError = core.errorMessage(error, 'Kural çalıştırmaları yüklenemedi.');
              return null;
            }).finally(function() { vm.automationRunsLoading = false; });
          };
          vm.selectAutomationRun = function(run) {
            vm.automationSelectedRunId = run && run.id;
          };
          vm.replayAutomationRun = function(run) {
            if (!vm.canEditWorkAutomation || !run || run.status !== 'DeadLetter') return;
            apiClient.remember('/api/automations/runs/' + run.id, run);
            vm.workAutomationBusy = true;
            return apiClient.post('/api/automations/runs/' + run.id + '/replay', {})
              .then(function() {
                vm.notify('success', 'Çalıştırma yeniden deneme sırasına alındı.');
                return vm.loadAutomationRuns();
              }).catch(function(error) {
                vm.automationRunsError = core.errorMessage(error, 'Çalıştırma yeniden sıraya alınamadı.');
              }).finally(function() { vm.workAutomationBusy = false; });
          };

          vm.editWorkTemplate = function(template) {
            vm.workTemplateDraft = core.templateDraft(template, firstBoardId());
            vm.workAutomationTab = 'templates';
            vm.workAutomationError = null;
            vm.workAutomationConfirmation = null;
            vm.loadWorkAutomationAudit('WorkItemTemplate', template.id, template.name);
          };
          vm.cancelWorkTemplateEdit = function() {
            resetTemplateDraft();
          };
          vm.useWorkTemplate = function(template) {
            if (!template || template.archived) return;
            vm.recurrenceDraft = core.recurrenceDraft(template.id);
            vm.recurrencePreview = null;
            vm.workAutomationTab = 'schedules';
          };
          vm.saveWorkTemplate = function() {
            var draft = vm.workTemplateDraft;
            var labels = core.normalizeLabels(draft.labelsText);
            if (!vm.canEditWorkAutomation || vm.workAutomationBusy || !draft.boardId
                || !draft.name || !draft.title || labels.tooMany || labels.tooLong) return;
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
            var requestPromise = draft.id
              ? apiClient.put('/api/work-items/templates/' + draft.id, request)
              : apiClient.post('/api/work-items/templates', angular.extend({ projectId: vm.project.id }, request));
            return mutate(
              requestPromise,
              draft.id ? 'İş şablonu güncellendi.' : 'İş şablonu oluşturuldu.',
              resetTemplateDraft);
          };
          vm.archiveWorkTemplate = function(template) {
            if (!vm.canEditWorkAutomation
                || !vm.workAutomationConfirmationIs('template', template.id)) return;
            apiClient.remember('/api/work-items/templates/' + template.id, template);
            return mutate(
              apiClient.delete('/api/work-items/templates/' + template.id),
              'İş şablonu arşivlendi.',
              resetTemplateDraft);
          };

          vm.previewWorkRecurrence = function() {
            var request = recurrenceRequest();
            if (!vm.canEditWorkAutomation || vm.workAutomationBusy || !validRecurrence(request)) return;
            vm.workAutomationBusy = true;
            vm.workAutomationError = null;
            vm.recurrencePreview = null;
            return apiClient.post('/api/work-items/recurrences/preview', angular.extend({
              previewCount: core.limits.previewCount
            }, request)).then(function(preview) {
              vm.recurrencePreview = preview;
              return preview;
            }).catch(function(error) {
              vm.workAutomationError = core.errorMessage(error, 'Takvim önizlemesi oluşturulamadı.');
              return null;
            }).finally(function() {
              vm.workAutomationBusy = false;
            });
          };
          vm.createWorkRecurrence = function() {
            var request = recurrenceRequest();
            if (!vm.canEditWorkAutomation || vm.workAutomationBusy || !validRecurrence(request)) return;
            return mutate(
              apiClient.post('/api/work-items/recurrences', request),
              'Yineleme etkinleştirildi.',
              resetRecurrenceDraft);
          };
          vm.setWorkRecurrenceState = function(recurrence, active) {
            if (!vm.canEditWorkAutomation || recurrence.archived || recurrence.active === active) return;
            apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
            return mutate(
              apiClient.patch('/api/work-items/recurrences/' + recurrence.id + '/state', { active: active }),
              active ? 'Yineleme devam ettirildi.' : 'Yineleme duraklatıldı.',
              angular.noop);
          };
          vm.archiveWorkRecurrence = function(recurrence) {
            if (!vm.canEditWorkAutomation
                || !vm.workAutomationConfirmationIs('recurrence', recurrence.id)) return;
            apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
            return mutate(
              apiClient.delete('/api/work-items/recurrences/' + recurrence.id),
              'Yineleme arşivlendi.',
              angular.noop);
          };

          vm.selectWorkRecurrence = function(recurrence) {
            if (!recurrence) return $q.when(null);
            vm.automationSelectedRecurrenceId = recurrence.id;
            vm.workAutomationOccurrenceLoading = true;
            vm.workAutomationOccurrenceError = null;
            return $q.all([
              apiClient.get('/api/work-items/recurrences/' + recurrence.id
                + '/occurrences?page=1&pageSize=50'),
              vm.loadWorkAutomationAudit('WorkItemRecurrence', recurrence.id, core.templateName(
                vm.workAutomation.templates,
                recurrence.templateId))
            ]).then(function(result) {
              if (vm.automationSelectedRecurrenceId !== recurrence.id) return null;
              vm.workAutomation.occurrences = result[0].items || [];
              return recurrence;
            }).catch(function(error) {
              vm.workAutomationOccurrenceError = core.errorMessage(
                error,
                'Çalıştırma geçmişi yüklenemedi.');
              return null;
            }).finally(function() {
              vm.workAutomationOccurrenceLoading = false;
            });
          };

          vm.loadWorkAutomationAudit = function(entityType, entityId, label) {
            if (!entityId) return $q.when([]);
            vm.workAutomationAuditError = null;
            return apiClient.get('/api/audit/entity/' + entityType + '/' + entityId)
              .then(function(entries) {
                vm.workAutomation.audit = core.auditEntries(entries);
                vm.workAutomation.auditTarget = { type: entityType, id: entityId, label: label };
                return vm.workAutomation.audit;
              }).catch(function(error) {
                vm.workAutomation.audit = [];
                vm.workAutomation.auditTarget = { type: entityType, id: entityId, label: label };
                vm.workAutomationAuditError = core.errorMessage(
                  error,
                  'Etkinlik kaydı yüklenemedi.');
                return [];
              });
          };

          vm.reloadWorkAutomationAfterConflict = function() {
            resetDrafts();
            vm.workAutomationError = 'Kayıt başka bir kullanıcı tarafından değiştirildi. Güncel durum yeniden yüklendi.';
            return vm.loadWorkAutomation();
          };

          function mutate(request, message, reset) {
            if (vm.workAutomationBusy) return $q.when(null);
            vm.workAutomationBusy = true;
            vm.workAutomationError = null;
            vm.workAutomationConfirmation = null;
            return request.then(function(result) {
              reset();
              vm.notify('success', message);
              return vm.loadWorkAutomation().then(function() { return result; });
            }).catch(function(error) {
              vm.workAutomationError = core.errorMessage(error, 'Otomasyon işlemi tamamlanamadı.');
              if (/CONFLICT$/.test(error.code || '')) {
                resetDrafts();
                return vm.loadWorkAutomation();
              }
              return null;
            }).finally(function() {
              vm.workAutomationBusy = false;
            });
          }

          function ruleMutate(request, message, archived) {
            if (vm.workAutomationBusy) return $q.when(null);
            vm.workAutomationBusy = true;
            vm.workAutomationError = null;
            vm.workAutomationConfirmation = null;
            return request.then(function(result) {
              if (result && result.id) {
                apiClient.remember('/api/automations/' + result.id, result);
                vm.automationSelectedRuleId = result.id;
                vm.automationRuleDraft = core.ruleDraft(result);
              }
              if (archived) {
                ruleSequence += 1;
                vm.automationSelectedRuleId = null;
              }
              vm.notify('success', message);
              return vm.loadWorkAutomation().then(function() {
                if (archived) vm.newAutomationRule();
                return result;
              });
            }).catch(function(error) {
              vm.workAutomationError = core.errorMessage(error, 'Kural işlemi tamamlanamadı.');
              if (/CONFLICT$/.test(error.code || '')) vm.loadWorkAutomation();
              return null;
            }).finally(function() { vm.workAutomationBusy = false; });
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
          function firstBoardId() {
            return vm.boards && vm.boards.length ? vm.boards[0].id : '';
          }
          function resetTemplateDraft() {
            vm.workTemplateDraft = core.templateDraft(null, firstBoardId());
          }
          function resetRecurrenceDraft() {
            var template = vm.workAutomation.activeTemplates && vm.workAutomation.activeTemplates[0];
            vm.recurrenceDraft = core.recurrenceDraft(template && template.id);
            vm.recurrencePreview = null;
          }
          function resetDrafts() {
            resetTemplateDraft();
            resetRecurrenceDraft();
            vm.automationRuleDraft = core.newRuleDraft();
            vm.automationDryRun = {
              sourceId: '',
              status: 'To Do',
              previousStatus: '',
              priority: 'Medium',
              type: 'Task',
              assigneeUserId: '',
              labels: ''
            };
            vm.automationDryRunResult = null;
            vm.workAutomationConfirmation = null;
          }
        }
      };

      function emptyModel(projectId) {
        return {
          projectId: projectId || null,
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
    });
})();
