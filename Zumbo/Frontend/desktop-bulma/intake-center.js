(function() {
  'use strict';

  angular.module('zumboDesktop')
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
    .factory('desktopIntakeFeature', function($q, $window, apiClient) {
      var core = window.ZumboIntakeCore;

      return {
        install: function(vm) {
          vm.intakeTabs = [
            { id: 'forms', label: 'Formlar', icon: 'files' },
            { id: 'submit', label: 'Talep oluştur', icon: 'send' },
            { id: 'triage', label: 'Triage', icon: 'inbox' }
          ];
          vm.intakeTab = 'forms';
          vm.intakeFieldTypes = core.fieldTypes;
          vm.intakeTriageStates = core.triageStates;
          vm.intakeLimits = core.limits;
          vm.intakeForms = [];
          vm.intakeSubmissions = [];
          vm.intakeQueueState = '';
          vm.intakeTriageDrafts = {};
          vm.intakePublic = { loading: false, form: null, model: null, confirmation: null, error: null };
          resetEditor();

          vm.intakeStateLabel = core.stateLabel;
          vm.intakeAccessLabel = core.accessLabel;
          vm.intakeTypeLabel = core.typeLabel;
          vm.intakeSecurityLabel = core.securityLabel;
          vm.intakeSubmissionValue = core.submissionValue;
          vm.intakeRole = function() {
            return core.roleOf(vm.project, vm.session.currentUser && vm.session.currentUser.id);
          };
          vm.canManageIntake = function() { return core.canManage(vm.intakeRole()); };
          vm.intakePublishedInternalForms = function() {
            return vm.intakeForms.filter(function(form) {
              return form.state === 'Published' && form.draft.accessPolicy === 'Internal';
            });
          };
          vm.intakeCompatibleFields = function(target) {
            return core.compatibleFields(vm.intakeDraft && vm.intakeDraft.definition.fields, target);
          };
          vm.intakeCustomFields = function() {
            return (vm.workItemSchema.customFields || []).filter(function(field) { return field.active !== false; });
          };
          vm.intakeIssueTypes = function() {
            return (vm.workItemSchema.issueTypes || []).filter(function(item) { return item.active !== false; });
          };
          vm.intakeDraftError = function() { return core.validateDraft(vm.intakeDraft); };
          vm.intakeSubmissionError = function() {
            return core.validateSubmission(vm.intakePublishedForm, vm.intakeSubmissionModel);
          };

          vm.setIntakeTab = function(tab) {
            if (!vm.intakeTabs.some(function(candidate) { return candidate.id === tab; })) return;
            vm.intakeTab = tab;
            vm.intakeError = null;
            vm.intakeNotice = null;
            if (tab === 'submit') {
              var form = vm.intakeSubmissionForm;
              if (!form || form.state !== 'Published') form = vm.intakePublishedInternalForms()[0];
              if (form) vm.selectIntakeSubmissionForm(form);
            }
            if (tab === 'triage' && vm.intakeSelectedForm) vm.loadIntakeQueue();
          };

          vm.loadIntake = function() {
            if (!vm.project || !vm.projectMembership) return $q.when([]);
            vm.intakeLoading = true;
            vm.intakeError = null;
            return apiClient.get('/api/intake/forms?projectId=' + encodeURIComponent(vm.project.id), {
              scope: 'desktop-intake-forms',
              replace: true
            }).then(function(forms) {
              vm.intakeForms = forms;
              var currentId = vm.intakeSelectedForm && vm.intakeSelectedForm.id;
              var selected = forms.find(function(form) { return form.id === currentId; })
                || forms.find(function(form) { return form.state !== 'Archived'; })
                || forms[0]
                || null;
              if (selected) vm.selectIntakeForm(selected, true);
              else resetEditor();
              if (vm.intakeTab === 'submit') {
                var internal = vm.intakePublishedInternalForms()[0];
                if (internal) return vm.selectIntakeSubmissionForm(internal);
              }
              if (vm.intakeTab === 'triage' && selected) return vm.loadIntakeQueue();
              return forms;
            }).catch(function(error) {
              vm.intakeError = core.errorMessage(error, 'Intake merkezi yüklenemedi.');
              return [];
            }).finally(function() {
              vm.intakeLoading = false;
            });
          };

          vm.newIntakeForm = function() {
            vm.intakeSelectedForm = null;
            vm.intakeDraft = core.newDraft(vm.project, vm.boards);
            vm.intakeEditorOpen = true;
            vm.intakeError = null;
            vm.intakeNotice = null;
          };

          vm.selectIntakeForm = function(form, keepTab) {
            vm.intakeSelectedForm = form;
            vm.intakeDraft = core.editDraft(form);
            vm.intakeEditorOpen = vm.canManageIntake() && form.state !== 'Archived';
            vm.intakeError = null;
            if (!keepTab) vm.intakeTab = 'forms';
            return form;
          };

          vm.cancelIntakeEdit = function() {
            if (vm.intakeSelectedForm) vm.intakeDraft = core.editDraft(vm.intakeSelectedForm);
            else resetEditor();
            vm.intakeEditorOpen = false;
            vm.intakeError = null;
          };

          vm.addIntakeField = function() {
            if (!vm.canManageIntake() || vm.intakeDraft.definition.fields.length >= core.limits.fields) return;
            vm.intakeDraft.definition.fields.push(core.newField(vm.intakeDraft.definition.fields.length));
          };

          vm.removeIntakeField = function(field) {
            var draft = vm.intakeDraft;
            if (!vm.canManageIntake() || draft.definition.fields.length <= 1) return;
            draft.definition.fields = draft.definition.fields.filter(function(candidate) { return candidate !== field; });
            var mapping = draft.definition.mapping;
            ['titleFieldKey', 'descriptionFieldKey', 'priorityFieldKey', 'dueDateFieldKey'].forEach(function(key) {
              if (mapping[key] === field.key) mapping[key] = '';
            });
            mapping.customFields = mapping.customFields.filter(function(item) {
              return item.intakeFieldKey !== field.key;
            });
          };

          vm.addIntakeCustomMapping = function() {
            vm.intakeDraft.definition.mapping.customFields.push({ intakeFieldKey: '', workItemFieldKey: '' });
          };

          vm.removeIntakeCustomMapping = function(mapping) {
            vm.intakeDraft.definition.mapping.customFields =
              vm.intakeDraft.definition.mapping.customFields.filter(function(candidate) { return candidate !== mapping; });
          };

          vm.saveIntakeForm = function() {
            var validation = core.validateDraft(vm.intakeDraft);
            if (!vm.canManageIntake() || validation || vm.intakeBusy) {
              vm.intakeError = validation;
              return $q.when(null);
            }
            vm.intakeBusy = true;
            vm.intakeError = null;
            var editing = !!vm.intakeDraft.id;
            var request = core.requestFor(vm.intakeDraft);
            var operation = editing
              ? apiClient.put('/api/intake/forms/' + vm.intakeDraft.id, request)
              : apiClient.post('/api/intake/forms', request);
            return operation.then(function(form) {
              upsertForm(form);
              vm.selectIntakeForm(form, true);
              vm.intakeNotice = editing ? 'Form taslağı güncellendi.' : 'Form taslağı oluşturuldu.';
              vm.notify('success', vm.intakeNotice);
              return form;
            }).catch(function(error) {
              vm.intakeError = core.errorMessage(error, 'Form kaydedilemedi.');
              if (error.code === 'CONCURRENCY_CONFLICT') return vm.loadIntake();
              return null;
            }).finally(function() { vm.intakeBusy = false; });
          };

          vm.publishIntakeForm = function(form) {
            if (!vm.canManageIntake() || vm.intakeBusy || form.state === 'Archived') return;
            return mutateForm(
              apiClient.post('/api/intake/forms/' + form.id + '/publish', {}),
              'Formun yeni sürümü yayınlandı.');
          };

          vm.archiveIntakeForm = function(form) {
            if (!vm.canManageIntake() || vm.intakeBusy || form.state === 'Archived') return;
            return mutateForm(
              apiClient.post('/api/intake/forms/' + form.id + '/archive', {}),
              'Form arşivlendi.');
          };

          vm.copyIntakePublicLink = function(form) {
            if (!form || !form.publicId) return;
            var url = $window.location.href.split('#')[0] + '#public=' + encodeURIComponent(form.publicId);
            if ($window.navigator.clipboard) {
              $window.navigator.clipboard.writeText(url).then(function() {
                vm.notify('success', 'Paylaşım bağlantısı kopyalandı.');
              });
            } else {
              vm.intakePublicLink = url;
            }
          };

          vm.selectIntakeSubmissionForm = function(form) {
            if (!form) return $q.when(null);
            vm.intakeSubmissionForm = form;
            vm.intakePublishedLoading = true;
            vm.intakeError = null;
            return apiClient.get('/api/intake/forms/' + form.id + '/published', {
              scope: 'desktop-intake-published',
              replace: true
            }).then(function(published) {
              vm.intakePublishedForm = published;
              vm.intakeSubmissionModel = core.submissionModel(published);
              vm.intakeConfirmation = null;
              return published;
            }).catch(function(error) {
              vm.intakeError = core.errorMessage(error, 'Yayındaki form yüklenemedi.');
              return null;
            }).finally(function() { vm.intakePublishedLoading = false; });
          };

          vm.captureIntakeFiles = function(field, files) {
            vm.intakeSubmissionModel.files[field.key] = files || [];
          };

          vm.submitIntake = function() {
            if (!vm.intakePublishedForm || vm.intakeSubmitBusy) return;
            var validation = core.validateSubmission(vm.intakePublishedForm, vm.intakeSubmissionModel);
            if (validation) {
              vm.intakeError = validation;
              return $q.when(null);
            }
            vm.intakeSubmitBusy = true;
            vm.intakeError = null;
            var formData = submissionFormData(vm.intakePublishedForm, vm.intakeSubmissionModel);
            return apiClient.post(
              '/api/intake/forms/' + vm.intakeSubmissionForm.id + '/submissions',
              formData,
              {
                idempotencyKey: apiClient.newIdempotencyKey(),
                contentTypeUndefined: true,
                transformRequest: angular.identity
              }).then(function(confirmation) {
                vm.intakeConfirmation = confirmation;
                vm.intakeSubmissionModel = core.submissionModel(vm.intakePublishedForm);
                return confirmation;
              }).catch(function(error) {
                vm.intakeError = core.errorMessage(error, 'Talep gönderilemedi.');
                return null;
              }).finally(function() { vm.intakeSubmitBusy = false; });
          };

          vm.loadIntakeQueue = function() {
            if (!vm.intakeSelectedForm) return $q.when([]);
            vm.intakeQueueLoading = true;
            vm.intakeError = null;
            var state = vm.intakeQueueState ? '&state=' + encodeURIComponent(vm.intakeQueueState) : '';
            return apiClient.get('/api/intake/forms/' + vm.intakeSelectedForm.id
              + '/submissions?page=1&pageSize=100' + state, {
              scope: 'desktop-intake-queue',
              replace: true
            }).then(function(page) {
              vm.intakeSubmissions = page.items || [];
              vm.intakeQueuePage = page;
              return vm.intakeSubmissions;
            }).catch(function(error) {
              vm.intakeError = core.errorMessage(error, 'Triage kuyruğu yüklenemedi.');
              return [];
            }).finally(function() { vm.intakeQueueLoading = false; });
          };

          vm.selectIntakeQueueForm = function(form) {
            vm.selectIntakeForm(form, true);
            return vm.loadIntakeQueue();
          };

          vm.triageIntakeSubmission = function(submission, state) {
            if (vm.intakeBusy || submission.state === 'Processing') return;
            vm.intakeBusy = true;
            vm.intakeError = null;
            return apiClient.post(
              '/api/intake/forms/' + vm.intakeSelectedForm.id + '/submissions/'
                + submission.id + '/triage',
              { state: state, note: vm.intakeTriageDrafts[submission.id] || null })
              .then(function(updated) {
                replaceSubmission(updated);
                vm.intakeNotice = 'Talep durumu güncellendi.';
                return updated;
              }).catch(function(error) {
                vm.intakeError = core.errorMessage(error, 'Talep sınıflandırılamadı.');
                return null;
              }).finally(function() { vm.intakeBusy = false; });
          };

          vm.openIntakeWorkItem = function(submission) {
            if (!submission || !submission.workItemId) return;
            vm.selectTask({ id: submission.workItemId });
          };

          vm.loadPublicIntake = function(publicId) {
            if (!publicId) return $q.when(null);
            vm.publicIntakeId = publicId;
            vm.intakePublic.loading = true;
            vm.intakePublic.error = null;
            return apiClient.get('/api/intake/public/forms/' + encodeURIComponent(publicId), {
              scope: 'desktop-public-intake',
              replace: true,
              refresh: false
            }).then(function(form) {
              vm.intakePublic.form = form;
              vm.intakePublic.model = core.submissionModel(form);
              return form;
            }).catch(function(error) {
              vm.intakePublic.error = core.errorMessage(error, 'Paylaşılan form yüklenemedi.');
              return null;
            }).finally(function() { vm.intakePublic.loading = false; });
          };

          vm.applyPublicIntakeLocation = function(params) {
            var publicId = params.get('public');
            if (!publicId) {
              vm.publicIntakeId = null;
              return false;
            }
            if (vm.publicIntakeId !== publicId) vm.loadPublicIntake(publicId);
            return true;
          };

          vm.capturePublicIntakeFiles = function(field, files) {
            vm.intakePublic.model.files[field.key] = files || [];
          };

          vm.submitPublicIntake = function() {
            var state = vm.intakePublic;
            if (!state.form || state.busy) return;
            var validation = core.validateSubmission(state.form, state.model);
            if (validation) {
              state.error = validation;
              return $q.when(null);
            }
            state.busy = true;
            state.error = null;
            var formData = submissionFormData(state.form, state.model);
            return apiClient.post(
              '/api/intake/public/forms/' + encodeURIComponent(vm.publicIntakeId) + '/submissions',
              formData,
              {
                idempotencyKey: apiClient.newIdempotencyKey(),
                contentTypeUndefined: true,
                transformRequest: angular.identity,
                refresh: false
              }).then(function(confirmation) {
                state.confirmation = confirmation;
                state.model = core.submissionModel(state.form);
                return confirmation;
              }).catch(function(error) {
                state.error = core.errorMessage(error, 'Talep gönderilemedi.');
                return null;
              }).finally(function() { state.busy = false; });
          };

          function resetEditor() {
            vm.intakeSelectedForm = null;
            vm.intakeDraft = core.newDraft(vm.project, vm.boards);
            vm.intakeEditorOpen = false;
          }

          vm.syncIntakeContext = function(project) {
            var changed = !project || vm.intakeProjectId !== project.id;
            vm.intakeProjectId = project && project.id || null;
            if (!changed) return;
            vm.intakeForms = [];
            vm.intakeSubmissions = [];
            resetEditor();
            if (project && vm.workMode === 'intake') vm.loadIntake();
          };

          function mutateForm(operation, message) {
            vm.intakeBusy = true;
            vm.intakeError = null;
            return operation.then(function(form) {
              upsertForm(form);
              vm.selectIntakeForm(form, true);
              vm.intakeNotice = message;
              vm.notify('success', message);
              return form;
            }).catch(function(error) {
              vm.intakeError = core.errorMessage(error, 'Form işlemi tamamlanamadı.');
              return null;
            }).finally(function() { vm.intakeBusy = false; });
          }

          function upsertForm(form) {
            var index = vm.intakeForms.findIndex(function(candidate) { return candidate.id === form.id; });
            if (index >= 0) vm.intakeForms[index] = form;
            else vm.intakeForms.unshift(form);
          }

          function replaceSubmission(submission) {
            var index = vm.intakeSubmissions.findIndex(function(candidate) {
              return candidate.id === submission.id;
            });
            if (index >= 0) vm.intakeSubmissions[index] = submission;
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
        }
      };
    });
})();
