(function() {
  'use strict';

  angular.module('zumboMobile')
    .directive('intakeFiles', function() {
      return {
        restrict: 'A',
        scope: { intakeFiles: '&' },
        link: function(scope, element) {
          element.on('change', function(event) {
            scope.$apply(function() {
              scope.intakeFiles({ files: Array.prototype.slice.call(event.target.files || []) });
            });
          });
          scope.$on('$destroy', function() { element.off('change'); });
        }
      };
    })
    .controller('MobileIntakeController', function(
      $scope,
      $state,
      $stateParams,
      $q,
      apiClient,
      zumboApi,
      sessionStore,
      mobileActionError
    ) {
      var vm = this;
      var core = window.ZumboIntakeCore;
      apiClient.transitionContext('project-intake:' + $stateParams.projectId);
      vm.tab = ['forms', 'submit', 'triage'].indexOf($stateParams.tab) >= 0 ? $stateParams.tab : 'forms';
      vm.fieldTypes = core.fieldTypes;
      vm.triageStates = core.triageStates;
      vm.limits = core.limits;
      vm.forms = [];
      vm.boards = [];
      vm.schema = { issueTypes: [], customFields: [] };
      vm.submissions = [];
      vm.queueState = '';
      vm.triageDrafts = {};
      vm.stateLabel = core.stateLabel;
      vm.accessLabel = core.accessLabel;
      vm.typeLabel = core.typeLabel;
      vm.securityLabel = core.securityLabel;
      vm.submissionValue = core.submissionValue;
      resetEditor();

      vm.setTab = function(tab) {
        if (['forms', 'submit', 'triage'].indexOf(tab) < 0) return;
        vm.tab = tab;
        vm.error = null;
        vm.notice = null;
        if (tab === 'submit') {
          var form = vm.submissionForm;
          if (!form || form.state !== 'Published') form = publishedInternalForms()[0];
          if (form) vm.selectSubmissionForm(form);
        }
        if (tab === 'triage' && vm.selectedForm) vm.loadQueue();
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        return $q.all([
          zumboApi.project($stateParams.projectId),
          zumboApi.boards($stateParams.projectId),
          zumboApi.workItemSchema($stateParams.projectId),
          apiClient.get('/api/intake/forms?projectId=' + encodeURIComponent($stateParams.projectId), {
            scope: 'mobile-intake-forms',
            replace: true
          })
        ]).then(function(result) {
          vm.project = result[0];
          vm.boards = result[1];
          vm.schema = result[2] || vm.schema;
          vm.forms = result[3];
          sessionStore.state.project = vm.project;
          vm.role = core.roleOf(vm.project, sessionStore.state.currentUser && sessionStore.state.currentUser.id);
          vm.canManage = core.canManage(vm.role, sessionStore.state.projectRoles);
          var currentId = vm.selectedForm && vm.selectedForm.id;
          var selected = vm.forms.find(function(form) { return form.id === currentId; })
            || vm.forms.find(function(form) { return form.state !== 'Archived'; })
            || vm.forms[0]
            || null;
          if (selected) vm.selectForm(selected, true);
          else resetEditor();
          if (vm.tab === 'submit' && publishedInternalForms()[0]) {
            return vm.selectSubmissionForm(publishedInternalForms()[0]);
          }
          if (vm.tab === 'triage' && selected) return vm.loadQueue();
          return result;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, mobileActionError(error, 'Intake merkezi yüklenemedi.'));
          return null;
        }).finally(function() { vm.loading = false; });
      };

      vm.publishedInternalForms = publishedInternalForms;
      vm.compatibleFields = function(target) {
        return core.compatibleFields(vm.draft && vm.draft.definition.fields, target);
      };
      vm.customFields = function() {
        return (vm.schema.customFields || []).filter(function(field) { return field.active !== false; });
      };
      vm.issueTypes = function() {
        return (vm.schema.issueTypes || []).filter(function(item) { return item.active !== false; });
      };
      vm.draftError = function() { return core.validateDraft(vm.draft); };

      vm.newForm = function() {
        vm.selectedForm = null;
        vm.draft = core.newDraft(vm.project, vm.boards);
        vm.editorOpen = true;
        vm.error = null;
      };

      vm.selectForm = function(form, keepTab) {
        vm.selectedForm = form;
        vm.draft = core.editDraft(form);
        vm.editorOpen = vm.canManage && form.state !== 'Archived';
        vm.error = null;
        if (!keepTab) vm.tab = 'forms';
        return form;
      };

      vm.cancelEdit = function() {
        if (vm.selectedForm) vm.draft = core.editDraft(vm.selectedForm);
        else resetEditor();
        vm.editorOpen = false;
      };

      vm.addField = function() {
        if (!vm.canManage || vm.draft.definition.fields.length >= core.limits.fields) return;
        vm.draft.definition.fields.push(core.newField(vm.draft.definition.fields.length));
      };

      vm.removeField = function(field) {
        if (!vm.canManage || vm.draft.definition.fields.length <= 1) return;
        vm.draft.definition.fields = vm.draft.definition.fields.filter(function(candidate) {
          return candidate !== field;
        });
        var mapping = vm.draft.definition.mapping;
        ['titleFieldKey', 'descriptionFieldKey', 'priorityFieldKey', 'dueDateFieldKey'].forEach(function(key) {
          if (mapping[key] === field.key) mapping[key] = '';
        });
        mapping.customFields = mapping.customFields.filter(function(item) {
          return item.intakeFieldKey !== field.key;
        });
      };

      vm.addCustomMapping = function() {
        vm.draft.definition.mapping.customFields.push({ intakeFieldKey: '', workItemFieldKey: '' });
      };

      vm.removeCustomMapping = function(mapping) {
        vm.draft.definition.mapping.customFields =
          vm.draft.definition.mapping.customFields.filter(function(candidate) { return candidate !== mapping; });
      };

      vm.saveForm = function() {
        var validation = core.validateDraft(vm.draft);
        if (!vm.canManage || vm.busy || validation) {
          vm.error = validation;
          return $q.when(null);
        }
        var editing = !!vm.draft.id;
        vm.busy = true;
        vm.error = null;
        var request = core.requestFor(vm.draft);
        var operation = editing
          ? apiClient.put('/api/intake/forms/' + vm.draft.id, request)
          : apiClient.post('/api/intake/forms', request);
        return operation.then(function(form) {
          upsertForm(form);
          vm.selectForm(form, true);
          vm.notice = editing ? 'Form taslağı güncellendi.' : 'Form taslağı oluşturuldu.';
          return form;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Form kaydedilemedi.');
          if (error.code === 'CONCURRENCY_CONFLICT') vm.load();
          return null;
        }).finally(function() { vm.busy = false; });
      };

      vm.publishForm = function(form) {
        if (!vm.canManage || vm.busy || form.state === 'Archived') return;
        return mutateForm(
          apiClient.post('/api/intake/forms/' + form.id + '/publish', {}),
          'Formun yeni sürümü yayınlandı.');
      };

      vm.archiveForm = function(form) {
        if (!vm.canManage || vm.busy || form.state === 'Archived') return;
        return mutateForm(
          apiClient.post('/api/intake/forms/' + form.id + '/archive', {}),
          'Form arşivlendi.');
      };

      vm.openPublicForm = function(form) {
        if (!form || !form.publicId) return;
        $state.go('public-intake', { publicId: form.publicId });
      };

      vm.selectSubmissionForm = function(form) {
        if (!form) return $q.when(null);
        vm.submissionForm = form;
        vm.publishedLoading = true;
        vm.error = null;
        return apiClient.get('/api/intake/forms/' + form.id + '/published', {
          scope: 'mobile-intake-published',
          replace: true
        }).then(function(published) {
          vm.publishedForm = published;
          vm.submissionModel = core.submissionModel(published);
          vm.confirmation = null;
          return published;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Yayındaki form yüklenemedi.');
          return null;
        }).finally(function() { vm.publishedLoading = false; });
      };

      vm.captureFiles = function(field, files) {
        vm.submissionModel.files[field.key] = files || [];
      };

      vm.submit = function() {
        if (!vm.publishedForm || vm.submitBusy) return;
        var validation = core.validateSubmission(vm.publishedForm, vm.submissionModel);
        if (validation) {
          vm.error = validation;
          return $q.when(null);
        }
        vm.submitBusy = true;
        vm.error = null;
        return apiClient.post(
          '/api/intake/forms/' + vm.submissionForm.id + '/submissions',
          submissionFormData(vm.publishedForm, vm.submissionModel),
          {
            idempotencyKey: apiClient.newIdempotencyKey(),
            contentTypeUndefined: true,
            transformRequest: angular.identity
          }).then(function(confirmation) {
            vm.confirmation = confirmation;
            vm.submissionModel = core.submissionModel(vm.publishedForm);
            return confirmation;
          }).catch(function(error) {
            vm.error = core.errorMessage(error, 'Talep gönderilemedi.');
            return null;
          }).finally(function() { vm.submitBusy = false; });
      };

      vm.loadQueue = function() {
        if (!vm.selectedForm) return $q.when([]);
        vm.queueLoading = true;
        vm.error = null;
        var state = vm.queueState ? '&state=' + encodeURIComponent(vm.queueState) : '';
        return apiClient.get('/api/intake/forms/' + vm.selectedForm.id
          + '/submissions?page=1&pageSize=100' + state, {
          scope: 'mobile-intake-queue',
          replace: true
        }).then(function(page) {
          vm.submissions = page.items || [];
          vm.queuePage = page;
          return vm.submissions;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Triage kuyruğu yüklenemedi.');
          return [];
        }).finally(function() { vm.queueLoading = false; });
      };

      vm.selectQueueForm = function(form) {
        vm.selectForm(form, true);
        return vm.loadQueue();
      };

      vm.triage = function(submission, state) {
        if (vm.busy || submission.state === 'Processing') return;
        vm.busy = true;
        vm.error = null;
        return apiClient.post(
          '/api/intake/forms/' + vm.selectedForm.id + '/submissions/' + submission.id + '/triage',
          { state: state, note: vm.triageDrafts[submission.id] || null })
          .then(function(updated) {
            var index = vm.submissions.findIndex(function(item) { return item.id === updated.id; });
            if (index >= 0) vm.submissions[index] = updated;
            vm.notice = 'Talep durumu güncellendi.';
            return updated;
          }).catch(function(error) {
            vm.error = core.errorMessage(error, 'Talep sınıflandırılamadı.');
            return null;
          }).finally(function() { vm.busy = false; });
      };

      vm.openWorkItem = function(submission) {
        if (submission && submission.workItemId) {
          $state.go('task-detail', { taskId: submission.workItemId });
        }
      };

      function publishedInternalForms() {
        return vm.forms.filter(function(form) {
          return form.state === 'Published' && form.draft.accessPolicy === 'Internal';
        });
      }

      function resetEditor() {
        vm.selectedForm = null;
        vm.draft = core.newDraft(vm.project, vm.boards);
        vm.editorOpen = false;
      }

      function mutateForm(operation, message) {
        vm.busy = true;
        vm.error = null;
        return operation.then(function(form) {
          upsertForm(form);
          vm.selectForm(form, true);
          vm.notice = message;
          return form;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Form işlemi tamamlanamadı.');
          return null;
        }).finally(function() { vm.busy = false; });
      }

      function upsertForm(form) {
        var index = vm.forms.findIndex(function(candidate) { return candidate.id === form.id; });
        if (index >= 0) vm.forms[index] = form;
        else vm.forms.unshift(form);
      }

      function submissionFormData(form, model) {
        var data = new FormData();
        data.append('submission', JSON.stringify(core.submissionPayload(form, model)));
        (form.fields || []).filter(function(field) {
          return field.type === 'Attachment';
        }).forEach(function(field) {
          (model.files[field.key] || []).forEach(function(file) {
            data.append('attachments.' + field.key, file, file.name);
          });
        });
        return data;
      }

      vm.load();
    })
    .controller('PublicIntakeController', function($stateParams, $q, apiClient) {
      var vm = this;
      var core = window.ZumboIntakeCore;
      vm.loading = true;
      vm.securityLabel = core.securityLabel;

      vm.captureFiles = function(field, files) {
        vm.model.files[field.key] = files || [];
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        return apiClient.get('/api/intake/public/forms/' + encodeURIComponent($stateParams.publicId), {
          scope: 'mobile-public-intake',
          replace: true,
          refresh: false
        }).then(function(form) {
          vm.form = form;
          vm.model = core.submissionModel(form);
          return form;
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Paylaşılan form yüklenemedi.');
          return null;
        }).finally(function() { vm.loading = false; });
      };

      vm.submit = function() {
        if (!vm.form || vm.busy) return $q.when(null);
        var validation = core.validateSubmission(vm.form, vm.model);
        if (validation) {
          vm.error = validation;
          return $q.when(null);
        }
        vm.busy = true;
        vm.error = null;
        var data = new FormData();
        data.append('submission', JSON.stringify(core.submissionPayload(vm.form, vm.model)));
        (vm.form.fields || []).filter(function(field) {
          return field.type === 'Attachment';
        }).forEach(function(field) {
          (vm.model.files[field.key] || []).forEach(function(file) {
            data.append('attachments.' + field.key, file, file.name);
          });
        });
        return apiClient.post(
          '/api/intake/public/forms/' + encodeURIComponent($stateParams.publicId) + '/submissions',
          data,
          {
            idempotencyKey: apiClient.newIdempotencyKey(),
            contentTypeUndefined: true,
            transformRequest: angular.identity,
            refresh: false
          }).then(function(confirmation) {
            vm.confirmation = confirmation;
            vm.model = core.submissionModel(vm.form);
            return confirmation;
          }).catch(function(error) {
            vm.error = core.errorMessage(error, 'Talep gönderilemedi.');
            return null;
          }).finally(function() { vm.busy = false; });
      };

      vm.load();
    });
})();
