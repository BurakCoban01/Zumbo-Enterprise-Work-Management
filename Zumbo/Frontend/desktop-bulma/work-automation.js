(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopWorkAutomationFeature', function($q, apiClient) {
      var core = window.ZumboWorkAutomationCore;

      return {
        install: function(vm) {
          var loadSequence = 0;

          vm.workAutomationTabs = [
            { id: 'schedules', label: 'Yinelemeler', icon: 'repeat-2' },
            { id: 'templates', label: 'İş şablonları', icon: 'copy-check' },
            { id: 'activity', label: 'Çalıştırma geçmişi', icon: 'history' }
          ];
          vm.workAutomationTab = 'schedules';
          vm.workAutomationLimits = core.limits;
          vm.workAutomationTimeZone = core.timeZone();
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
          };
          vm.automationTemplateName = function(id) {
            return core.templateName(vm.workAutomation.templates, id);
          };
          vm.automationFrequencyLabel = core.frequencyLabel;
          vm.automationRecurrenceState = core.recurrenceState;
          vm.automationOccurrenceState = core.occurrenceState;
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
                + '&page=1&pageSize=100&includeArchived=true')
            ]).then(function(result) {
              if (sequence !== loadSequence || !vm.project || vm.project.id !== projectId) return null;
              var templates = result[0].items || [];
              var recurrences = result[1].items || [];
              templates.forEach(function(template) {
                apiClient.remember('/api/work-items/templates/' + template.id, template);
              });
              recurrences.forEach(function(recurrence) {
                apiClient.remember('/api/work-items/recurrences/' + recurrence.id, recurrence);
              });
              vm.workAutomation = {
                projectId: projectId,
                templates: templates,
                activeTemplates: templates.filter(function(template) { return !template.archived; }),
                recurrences: recurrences,
                activeRecurrences: recurrences.filter(function(recurrence) {
                  return !recurrence.archived && recurrence.active;
                }),
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
              return selected ? vm.selectWorkRecurrence(selected) : null;
            }).catch(function(error) {
              if (sequence === loadSequence) {
                vm.workAutomationError = core.errorMessage(error, 'Otomasyon kayıtları yüklenemedi.');
              }
              return null;
            }).finally(function() {
              if (sequence === loadSequence) vm.workAutomationLoading = false;
            });
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
            return apiClient.get('/api/audit/entity/' + entityType + '/' + entityId)
              .then(function(entries) {
                vm.workAutomation.audit = core.auditEntries(entries);
                vm.workAutomation.auditTarget = { type: entityType, id: entityId, label: label };
                return vm.workAutomation.audit;
              }).catch(function() {
                vm.workAutomation.audit = [];
                vm.workAutomation.auditTarget = { type: entityType, id: entityId, label: label };
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
          occurrences: [],
          audit: [],
          auditTarget: null
        };
      }
    });
})();
