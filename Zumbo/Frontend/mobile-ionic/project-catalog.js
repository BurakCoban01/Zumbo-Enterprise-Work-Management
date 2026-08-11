(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('ProjectCatalogController', function($scope, $stateParams, $q, zumboApi, sessionStore, apiClient) {
      var vm = this;
      var core = window.ZumboProjectCatalogCore;
      vm.auditActionLabel = window.ZumboAuditPrivacyCore.auditActionLabel;
      var allowedTabs = ['releases', 'milestones', 'components', 'templates', 'activity'];

      apiClient.transitionContext('project-catalog:' + $stateParams.projectId);
      vm.tab = allowedTabs.indexOf($stateParams.tab) >= 0 ? $stateParams.tab : 'releases';
      vm.limits = core.limits;
      vm.project = null;
      vm.model = core.snapshot(null);
      vm.audit = [];
      vm.loading = true;
      resetDrafts();

      vm.setTab = function(tab) {
        if (allowedTabs.indexOf(tab) < 0) return;
        vm.tab = tab;
        vm.error = null;
        vm.confirmation = null;
      };
      vm.versionName = function(versionId) { return core.versionName(vm.project, versionId); };
      vm.templateComponentState = function() {
        return core.normalizeComponentNames(vm.templateDraft.defaultComponentNamesText);
      };
      vm.requestConfirmation = function(kind, id) { vm.confirmation = { kind: kind, id: id }; };
      vm.cancelConfirmation = function() { vm.confirmation = null; };
      vm.confirmationIs = function(kind, id) {
        return !!vm.confirmation && vm.confirmation.kind === kind && vm.confirmation.id === id;
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        return $q.all([
          zumboApi.project($stateParams.projectId),
          zumboApi.audit('Project', $stateParams.projectId)
        ]).then(function(result) {
          sync(result[0]);
          vm.audit = core.auditEntries(result[1]);
          sessionStore.state.project = result[0];
          return result[0];
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'Proje kataloğu yüklenemedi.');
          return null;
        }).finally(function() {
          vm.loading = false;
        });
      };

      $scope.$on('zumbo:concurrency-conflict', function(_, conflict) {
        if (!conflict.resource || conflict.resource.kind !== 'projects') return;
        resetDrafts();
        vm.notice = null;
        vm.error = 'Proje başka bir kullanıcı tarafından değiştirildi. Güncel kayıt yeniden yüklendi.';
        vm.load();
      });

      vm.editTemplate = function(template) {
        vm.templateDraft = {
          id: template.id,
          name: template.name,
          isDefault: template.isDefault,
          defaultComponentNamesText: (template.defaultComponentNames || []).join('\n')
        };
      };
      vm.cancelTemplateEdit = resetTemplateDraft;
      vm.saveTemplate = function() {
        var state = core.normalizeComponentNames(vm.templateDraft.defaultComponentNamesText);
        if (!vm.canManage || !vm.templateDraft.name || state.tooMany || state.tooLong) return;
        return mutate(zumboApi.upsertProjectTemplate(vm.project.id, vm.templateDraft.id, {
          name: vm.templateDraft.name,
          isDefault: !!vm.templateDraft.isDefault,
          defaultComponentNames: state.values
        }), vm.templateDraft.id ? 'Şablon güncellendi.' : 'Şablon oluşturuldu.', resetTemplateDraft);
      };
      vm.archiveTemplate = function(template) {
        if (!vm.canManage || !vm.confirmationIs('template', template.id)) return;
        return mutate(
          zumboApi.archiveProjectTemplate(vm.project.id, template.id),
          'Şablon arşivlendi.',
          resetTemplateDraft);
      };

      vm.editComponent = function(component) {
        vm.componentDraft = { id: component.id, name: component.name, description: component.description || '' };
      };
      vm.cancelComponentEdit = resetComponentDraft;
      vm.saveComponent = function() {
        var draft = vm.componentDraft;
        if (!vm.canManage || !draft.name) return;
        var request = { name: draft.name, description: draft.description || null };
        return mutate(
          draft.id
            ? zumboApi.updateProjectComponent(vm.project.id, draft.id, request)
            : zumboApi.createProjectComponent(vm.project.id, request),
          draft.id ? 'Bileşen güncellendi.' : 'Bileşen oluşturuldu.',
          resetComponentDraft);
      };
      vm.archiveComponent = function(component) {
        if (!vm.canManage || !vm.confirmationIs('component', component.id)) return;
        return mutate(
          zumboApi.archiveProjectComponent(vm.project.id, component.id),
          'Bileşen arşivlendi.',
          resetComponentDraft);
      };

      vm.createVersion = function() {
        if (!vm.canManage || !vm.versionDraft.name) return;
        return mutate(
          zumboApi.createProjectVersion(vm.project.id, { name: vm.versionDraft.name }),
          'Sürüm oluşturuldu.',
          resetVersionDraft);
      };
      vm.archiveVersion = function(version) {
        if (!vm.canManage || !vm.confirmationIs('version', version.id)) return;
        return mutate(
          zumboApi.archiveProjectVersion(vm.project.id, version.id),
          'Sürüm arşivlendi.',
          resetVersionDraft);
      };
      vm.createRelease = function() {
        var draft = vm.releaseDraft;
        if (!vm.canManage || !draft.versionId || !draft.name) return;
        return mutate(zumboApi.createProjectRelease(vm.project.id, {
          versionId: draft.versionId,
          name: draft.name,
          scheduledAt: draft.scheduledAt || null
        }), 'Yayın taslağı oluşturuldu.', resetReleaseDraft);
      };
      vm.approveRelease = function(release) {
        if (!vm.canRelease || release.status !== 'Draft') return;
        return mutate(
          zumboApi.approveProjectRelease(vm.project.id, release.id),
          'Yayın onaylandı.',
          angular.noop);
      };
      vm.publishRelease = function(release) {
        if (!vm.canRelease || release.status !== 'Approved') return;
        return mutate(
          zumboApi.publishProjectRelease(vm.project.id, release.id),
          'Yayın tamamlandı.',
          angular.noop);
      };

      vm.editMilestone = function(milestone) {
        vm.milestoneDraft = {
          id: milestone.id,
          name: milestone.name,
          dueAt: core.toDateInput(milestone.dueAt)
        };
      };
      vm.cancelMilestoneEdit = resetMilestoneDraft;
      vm.saveMilestone = function() {
        var draft = vm.milestoneDraft;
        if (!vm.canManage || !draft.name || !draft.dueAt) return;
        var request = { name: draft.name, dueAt: draft.dueAt };
        return mutate(
          draft.id
            ? zumboApi.updateProjectMilestone(vm.project.id, draft.id, request)
            : zumboApi.createProjectMilestone(vm.project.id, request),
          draft.id ? 'Kilometre taşı güncellendi.' : 'Kilometre taşı oluşturuldu.',
          resetMilestoneDraft);
      };
      vm.completeMilestone = function(milestone) {
        if (!vm.canManage || milestone.status !== 'Open') return;
        return mutate(
          zumboApi.completeProjectMilestone(vm.project.id, milestone.id),
          'Kilometre taşı tamamlandı.',
          resetMilestoneDraft);
      };

      function mutate(request, message, reset) {
        if (vm.busy) return $q.when(null);
        vm.busy = true;
        vm.error = null;
        vm.notice = null;
        vm.confirmation = null;
        return request.then(function(project) {
          sync(project);
          sessionStore.state.project = project;
          reset();
          vm.notice = message;
          return zumboApi.audit('Project', project.id).then(function(audit) {
            vm.audit = core.auditEntries(audit);
            return project;
          });
        }).catch(function(error) {
          vm.error = core.errorMessage(error, 'İşlem tamamlanamadı.');
          return null;
        }).finally(function() {
          vm.busy = false;
        });
      }

      function sync(project) {
        vm.project = project;
        vm.model = core.snapshot(project);
        vm.role = core.roleOf(project, sessionStore.state.currentUser && sessionStore.state.currentUser.id);
        vm.canManage = core.canManage(vm.role, sessionStore.state.projectRoles);
        vm.canRelease = core.canRelease(vm.role, sessionStore.state.projectRoles);
      }
      function resetTemplateDraft() {
        vm.templateDraft = { id: null, name: '', isDefault: false, defaultComponentNamesText: '' };
      }
      function resetComponentDraft() {
        vm.componentDraft = { id: null, name: '', description: '' };
      }
      function resetVersionDraft() {
        vm.versionDraft = { name: '' };
      }
      function resetReleaseDraft() {
        vm.releaseDraft = { versionId: '', name: '', scheduledAt: null };
      }
      function resetMilestoneDraft() {
        vm.milestoneDraft = { id: null, name: '', dueAt: null };
      }
      function resetDrafts() {
        resetTemplateDraft();
        resetComponentDraft();
        resetVersionDraft();
        resetReleaseDraft();
        resetMilestoneDraft();
        vm.confirmation = null;
      }

      vm.load();
    });
})();
