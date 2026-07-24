(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopProjectCatalogFeature', function($q, apiClient) {
      var core = window.ZumboProjectCatalogCore;

      return {
        install: function(vm, helpers) {
          var setProjectState = helpers.setProjectState;
          var conflictReload = null;

          vm.projectCatalogTabs = [
            { id: 'releases', label: 'Sürümler ve yayınlar', icon: 'rocket' },
            { id: 'milestones', label: 'Kilometre taşları', icon: 'milestone' },
            { id: 'components', label: 'Bileşenler', icon: 'boxes' },
            { id: 'templates', label: 'Şablonlar', icon: 'copy-check' },
            { id: 'activity', label: 'Etkinlik', icon: 'history' }
          ];
          vm.projectCatalogTab = 'releases';
          vm.projectCatalogLimits = core.limits;
          vm.projectCatalog = core.snapshot(vm.project);
          resetDrafts();

          vm.syncProjectCatalog = function(project) {
            vm.projectCatalog = core.snapshot(project);
            vm.catalogRole = core.roleOf(project, vm.session.currentUser && vm.session.currentUser.id);
            vm.canManageProjectCatalog = core.canManage(vm.catalogRole);
            vm.canReleaseProjectCatalog = core.canRelease(vm.catalogRole);
          };
          vm.syncProjectCatalog(vm.project);

          vm.setProjectCatalogTab = function(tab) {
            if (!vm.projectCatalogTabs.some(function(candidate) { return candidate.id === tab; })) return;
            vm.projectCatalogTab = tab;
            vm.catalogError = null;
            vm.catalogConfirmation = null;
          };

          vm.projectCatalogVersionName = function(versionId) {
            return core.versionName(vm.project, versionId);
          };
          vm.projectCatalogAudit = function() {
            return core.auditEntries(vm.entityAudit);
          };
          vm.projectTemplateComponentState = function() {
            return core.normalizeComponentNames(vm.projectTemplateDraft.defaultComponentNamesText);
          };
          vm.requestCatalogConfirmation = function(kind, id) {
            vm.catalogConfirmation = { kind: kind, id: id };
          };
          vm.cancelCatalogConfirmation = function() {
            vm.catalogConfirmation = null;
          };
          vm.catalogConfirmationIs = function(kind, id) {
            return !!vm.catalogConfirmation
              && vm.catalogConfirmation.kind === kind
              && vm.catalogConfirmation.id === id;
          };

          vm.editProjectTemplate = function(template) {
            vm.projectTemplateDraft = {
              id: template.id,
              name: template.name,
              isDefault: template.isDefault,
              defaultComponentNamesText: (template.defaultComponentNames || []).join('\n')
            };
            vm.catalogError = null;
          };
          vm.cancelProjectTemplateEdit = function() {
            resetTemplateDraft();
          };
          vm.saveProjectTemplate = function() {
            var draft = vm.projectTemplateDraft;
            var componentState = core.normalizeComponentNames(draft.defaultComponentNamesText);
            if (!vm.canManageProjectCatalog || !draft.name || componentState.tooMany || componentState.tooLong) return;
            var request = {
              name: draft.name,
              isDefault: !!draft.isDefault,
              defaultComponentNames: componentState.values
            };
            return mutate(
              draft.id
                ? apiClient.put('/api/projects/' + vm.project.id + '/templates/' + draft.id, request)
                : apiClient.post('/api/projects/' + vm.project.id + '/templates', request),
              draft.id ? 'Proje şablonu güncellendi.' : 'Proje şablonu oluşturuldu.',
              resetTemplateDraft,
              'Proje şablonu kaydedilemedi.');
          };
          vm.archiveProjectTemplate = function(template) {
            if (!vm.canManageProjectCatalog || !vm.catalogConfirmationIs('template', template.id)) return;
            return mutate(
              apiClient.delete('/api/projects/' + vm.project.id + '/templates/' + template.id),
              'Proje şablonu arşivlendi.',
              resetTemplateDraft,
              'Proje şablonu arşivlenemedi.');
          };

          vm.editProjectComponent = function(component) {
            vm.projectComponentDraft = {
              id: component.id,
              name: component.name,
              description: component.description || ''
            };
            vm.catalogError = null;
          };
          vm.cancelProjectComponentEdit = function() {
            resetComponentDraft();
          };
          vm.saveProjectComponent = function() {
            var draft = vm.projectComponentDraft;
            if (!vm.canManageProjectCatalog || !draft.name) return;
            var request = { name: draft.name, description: draft.description || null };
            return mutate(
              draft.id
                ? apiClient.put('/api/projects/' + vm.project.id + '/components/' + draft.id, request)
                : apiClient.post('/api/projects/' + vm.project.id + '/components', request),
              draft.id ? 'Bileşen güncellendi.' : 'Bileşen oluşturuldu.',
              resetComponentDraft,
              'Bileşen kaydedilemedi.');
          };
          vm.archiveProjectComponent = function(component) {
            if (!vm.canManageProjectCatalog || !vm.catalogConfirmationIs('component', component.id)) return;
            return mutate(
              apiClient.delete('/api/projects/' + vm.project.id + '/components/' + component.id),
              'Bileşen arşivlendi.',
              resetComponentDraft,
              'Bileşen arşivlenemedi.');
          };

          vm.createProjectVersion = function() {
            if (!vm.canManageProjectCatalog || !vm.projectVersionDraft.name) return;
            return mutate(
              apiClient.post('/api/projects/' + vm.project.id + '/versions', {
                name: vm.projectVersionDraft.name
              }),
              'Sürüm oluşturuldu.',
              resetVersionDraft,
              'Sürüm oluşturulamadı.');
          };
          vm.archiveProjectVersion = function(version) {
            if (!vm.canManageProjectCatalog || !vm.catalogConfirmationIs('version', version.id)) return;
            return mutate(
              apiClient.delete('/api/projects/' + vm.project.id + '/versions/' + version.id),
              'Sürüm arşivlendi.',
              resetVersionDraft,
              'Sürüm arşivlenemedi.');
          };
          vm.createProjectRelease = function() {
            var draft = vm.projectReleaseDraft;
            if (!vm.canManageProjectCatalog || !draft.versionId || !draft.name) return;
            return mutate(
              apiClient.post('/api/projects/' + vm.project.id + '/releases', {
                versionId: draft.versionId,
                name: draft.name,
                scheduledAt: draft.scheduledAt || null
              }),
              'Yayın taslağı oluşturuldu.',
              resetReleaseDraft,
              'Yayın taslağı oluşturulamadı.');
          };
          vm.approveProjectRelease = function(release) {
            if (!vm.canReleaseProjectCatalog || release.status !== 'Draft') return;
            return mutate(
              apiClient.post('/api/projects/' + vm.project.id + '/releases/' + release.id + '/approve', {}),
              'Yayın onaylandı.',
              angular.noop,
              'Yayın onaylanamadı.');
          };
          vm.publishProjectRelease = function(release) {
            if (!vm.canReleaseProjectCatalog || release.status !== 'Approved') return;
            return mutate(
              apiClient.post('/api/projects/' + vm.project.id + '/releases/' + release.id + '/publish', {}),
              'Yayın yayınlandı ve sürüm tamamlandı.',
              angular.noop,
              'Yayın yayınlanamadı.');
          };

          vm.editProjectMilestone = function(milestone) {
            vm.projectMilestoneDraft = {
              id: milestone.id,
              name: milestone.name,
              dueAt: core.toDateInput(milestone.dueAt)
            };
            vm.catalogError = null;
          };
          vm.cancelProjectMilestoneEdit = function() {
            resetMilestoneDraft();
          };
          vm.saveProjectMilestone = function() {
            var draft = vm.projectMilestoneDraft;
            if (!vm.canManageProjectCatalog || !draft.name || !draft.dueAt) return;
            var request = { name: draft.name, dueAt: draft.dueAt };
            return mutate(
              draft.id
                ? apiClient.put('/api/projects/' + vm.project.id + '/milestones/' + draft.id, request)
                : apiClient.post('/api/projects/' + vm.project.id + '/milestones', request),
              draft.id ? 'Kilometre taşı güncellendi.' : 'Kilometre taşı oluşturuldu.',
              resetMilestoneDraft,
              'Kilometre taşı kaydedilemedi.');
          };
          vm.completeProjectMilestone = function(milestone) {
            if (!vm.canManageProjectCatalog || milestone.status !== 'Open') return;
            return mutate(
              apiClient.post('/api/projects/' + vm.project.id + '/milestones/' + milestone.id + '/complete', {}),
              'Kilometre taşı tamamlandı.',
              resetMilestoneDraft,
              'Kilometre taşı tamamlanamadı.');
          };

          vm.reloadProjectAfterConflict = function() {
            if (!vm.project || conflictReload) return conflictReload || $q.when(null);
            var projectId = vm.project.id;
            conflictReload = apiClient.get('/api/projects/' + projectId)
              .then(function(project) {
                setProjectState(project);
                resetDrafts();
                return project;
              })
              .finally(function() { conflictReload = null; });
            return conflictReload;
          };

          function mutate(request, successMessage, reset, fallback) {
            if (vm.catalogBusy) return $q.when(null);
            vm.catalogBusy = true;
            vm.catalogError = null;
            vm.catalogConfirmation = null;
            return request.then(function(project) {
              setProjectState(project, true);
              reset();
              vm.notify('success', successMessage);
              return vm.loadProjectAudit().then(function() { return project; });
            }).catch(function(error) {
              vm.catalogError = core.errorMessage(error, fallback);
              if (error.code === 'CONCURRENCY_CONFLICT') resetDrafts();
              return null;
            }).finally(function() {
              vm.catalogBusy = false;
            });
          }

          function resetTemplateDraft() {
            vm.projectTemplateDraft = {
              id: null,
              name: '',
              isDefault: false,
              defaultComponentNamesText: ''
            };
          }
          function resetComponentDraft() {
            vm.projectComponentDraft = { id: null, name: '', description: '' };
          }
          function resetVersionDraft() {
            vm.projectVersionDraft = { name: '' };
          }
          function resetReleaseDraft() {
            vm.projectReleaseDraft = { versionId: '', name: '', scheduledAt: null };
          }
          function resetMilestoneDraft() {
            vm.projectMilestoneDraft = { id: null, name: '', dueAt: null };
          }
          function resetDrafts() {
            resetTemplateDraft();
            resetComponentDraft();
            resetVersionDraft();
            resetReleaseDraft();
            resetMilestoneDraft();
            vm.catalogConfirmation = null;
          }
        }
      };
    });
})();
