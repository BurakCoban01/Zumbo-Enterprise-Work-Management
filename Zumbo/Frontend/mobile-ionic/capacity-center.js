(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('CapacityController', function(
      $scope,
      $q,
      $window,
      apiClient,
      zumboApi,
      sessionStore,
      mobileActionError) {
      var vm = this;
      var core = $window.ZumboCapacityPlanningCore;
      vm.plans = [];
      vm.projects = [];
      vm.users = [];
      vm.teams = [];
      vm.portfolios = [];
      vm.plan = null;
      vm.snapshot = null;
      vm.scenario = null;
      vm.mode = 'people';
      vm.draft = core.plan(sessionStore.state.currentUser.id);
      vm.scenarioAllocations = [];

      function currentUserId() {
        return sessionStore.state.currentUser && sessionStore.state.currentUser.id;
      }

      function isSystemAdministrator() {
        var roles = sessionStore.state.currentUser && sessionStore.state.currentUser.roles || [];
        return roles.indexOf('SystemAdmin') >= 0;
      }

      function isManageableProject(project) {
        if (isSystemAdministrator()) return true;
        return (project.members || []).some(function(member) {
          return member.userId === currentUserId()
            && ['ProjectOwner', 'ProjectAdmin'].indexOf(member.role) >= 0;
        });
      }

      vm.manageableProjects = function() {
        return vm.projects.filter(isManageableProject);
      };

      vm.canCreatePlan = function() {
        return vm.manageableProjects().length > 0;
      };

      vm.projectOptions = function() {
        var selected = vm.draft && vm.draft.projectIds || [];
        return vm.projects.filter(function(project) {
          return isManageableProject(project)
            || (vm.plan && selected.indexOf(project.id) >= 0);
        });
      };

      vm.viewerUsers = function() {
        var ownerUserId = vm.draft && vm.draft.ownerUserId;
        return vm.users.filter(function(user) { return user.id !== ownerUserId; });
      };

      vm.teamOptions = function(member) {
        return vm.teams.filter(function(team) {
          return team.id === (member && member.teamId)
            || (team.members || []).some(function(teamMember) {
              return teamMember.userId === (member && member.userId)
                && teamMember.status === 'Active';
            });
        });
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        return $q.all([
          apiClient.get('/api/capacity-plans?page=1&pageSize=100', {
            scope: 'mobile-capacity-plans',
            replace: true
          }),
          apiClient.get('/api/portfolios?page=1&pageSize=100', {
            scope: 'mobile-capacity-portfolios',
            replace: true
          }),
          zumboApi.projects(),
          zumboApi.users(),
          zumboApi.teams()
        ]).then(function(result) {
          vm.plans = result[0].items || [];
          vm.portfolios = result[1].items || [];
          vm.projects = result[2] || [];
          vm.users = result[3] || [];
          vm.teams = result[4] || [];
          var current = vm.plan && vm.plans.find(function(item) {
            return item.id === vm.plan.id;
          });
          return current
            ? vm.select(current)
            : (vm.plans.length ? vm.select(vm.plans[0]) : vm.newPlan());
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kapasite planları yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
          $scope.$broadcast('scroll.refreshComplete');
        });
      };

      vm.select = function(item) {
        if (!item) return $q.when();
        vm.loading = true;
        vm.error = null;
        vm.notice = null;
        return $q.all([
          apiClient.get('/api/capacity-plans/' + item.id, {
            scope: 'mobile-capacity-detail',
            replace: true
          }),
          apiClient.get('/api/capacity-plans/' + item.id + '/snapshot', {
            scope: 'mobile-capacity-snapshot',
            replace: true
          })
        ]).then(function(result) {
          vm.plan = result[0];
          vm.snapshot = result[1];
          vm.draft = core.hydratePlan(result[0]);
          vm.resetScenario();
          vm.mode = 'people';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kapasite planı yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
        });
      };

      vm.newPlan = function() {
        vm.plan = null;
        vm.snapshot = null;
        vm.scenario = null;
        vm.scenarioAllocations = [];
        vm.error = null;
        vm.notice = null;
        if (!vm.canCreatePlan()) {
          vm.draft = null;
          vm.mode = 'people';
          return;
        }
        vm.draft = core.plan(currentUserId());
        vm.mode = 'definition';
      };

      vm.setMode = function(mode) {
        vm.mode = mode;
        vm.error = null;
      };

      vm.save = function() {
        if (!vm.plan && !vm.canCreatePlan()) return $q.when();
        var validation = core.validate(vm.draft);
        if (validation) {
          vm.error = validation;
          return $q.when();
        }
        vm.saving = true;
        vm.error = null;
        var body = core.payload(vm.draft);
        var request = vm.draft.id
          ? apiClient.put('/api/capacity-plans/' + vm.draft.id, body)
          : apiClient.post('/api/capacity-plans', body);
        return request.then(function(saved) {
          vm.notice = vm.draft.id
            ? 'Kapasite planı güncellendi.'
            : 'Kapasite planı oluşturuldu.';
          vm.plan = saved;
          return vm.load();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kapasite planı kaydedilemedi.');
        }).finally(function() {
          vm.saving = false;
        });
      };

      vm.share = function() {
        if (!vm.plan || !vm.plan.canEdit) return $q.when();
        vm.saving = true;
        vm.error = null;
        return apiClient.put('/api/capacity-plans/' + vm.plan.id + '/sharing', {
          viewerUserIds: vm.draft.viewerUserIds
        }).then(function(saved) {
          vm.plan = saved;
          vm.draft = core.hydratePlan(saved);
          vm.notice = 'Kapasite planı paylaşımı güncellendi.';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kapasite planı paylaşımı güncellenemedi.');
        }).finally(function() {
          vm.saving = false;
        });
      };

      vm.archive = function() {
        if (!vm.plan || !vm.plan.canEdit
            || !$window.confirm('Bu kapasite planını arşivlemek istediğinize emin misiniz?')) {
          return $q.when();
        }
        var archivedId = vm.plan.id;
        vm.saving = true;
        return apiClient.delete('/api/capacity-plans/' + archivedId)
          .then(function() {
            vm.notice = 'Kapasite planı arşivlendi.';
            vm.plans = vm.plans.filter(function(item) { return item.id !== archivedId; });
            vm.plan = null;
            vm.snapshot = null;
            return vm.plans.length ? vm.select(vm.plans[0]) : vm.newPlan();
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Kapasite planı arşivlenemedi.');
          }).finally(function() {
            vm.saving = false;
          });
      };

      vm.addMember = function() {
        if (vm.draft.members.length < core.limits.members) vm.draft.members.push(core.member());
      };
      vm.removeMember = function(index) {
        if (vm.draft.members.length <= 1) return;
        var removed = vm.draft.members.splice(index, 1)[0];
        vm.draft.allocations = vm.draft.allocations.filter(function(item) {
          return item.userId !== removed.userId;
        });
      };
      vm.addAllocation = function() {
        if (vm.draft.allocations.length < core.limits.allocations) {
          vm.draft.allocations.push(core.allocation(vm.draft));
        }
      };
      vm.removeAllocation = function(index) {
        vm.draft.allocations.splice(index, 1);
      };

      vm.resetScenario = function() {
        vm.scenario = null;
        vm.scenarioAllocations = (vm.plan ? vm.plan.allocations : [])
          .map(core.hydrateAllocation);
      };
      vm.addScenarioAllocation = function() {
        if (!vm.plan || vm.scenarioAllocations.length >= core.limits.allocations) return;
        vm.scenarioAllocations.push(core.allocation(core.hydratePlan(vm.plan)));
      };
      vm.removeScenarioAllocation = function(index) {
        vm.scenarioAllocations.splice(index, 1);
      };
      vm.previewScenario = function() {
        if (!vm.plan || !vm.plan.canEdit) return $q.when();
        var baseline = core.hydratePlan(vm.plan);
        var validation = core.validate(baseline, vm.scenarioAllocations);
        if (validation) {
          vm.error = validation;
          return $q.when();
        }
        vm.saving = true;
        vm.error = null;
        return apiClient.post('/api/capacity-plans/' + vm.plan.id + '/scenarios', {
          allocations: core.payload(baseline, vm.scenarioAllocations).allocations
        }, {
          scope: 'mobile-capacity-scenario',
          replace: true
        }).then(function(result) {
          vm.scenario = result;
          vm.notice = 'Senaryo hesaplandı; kayıtlı plan değiştirilmedi.';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kapasite senaryosu hesaplanamadı.');
        }).finally(function() {
          vm.saving = false;
        });
      };

      vm.userName = function(id) {
        var user = vm.users.find(function(item) { return item.id === id; });
        return user ? (user.displayName || user.username) : 'Erişilemeyen kullanıcı';
      };
      vm.projectName = function(id) {
        var project = vm.projects.find(function(item) { return item.id === id; });
        return project ? project.name : 'Erişilemeyen proje';
      };
      vm.teamName = function(id) {
        if (!id) return 'Ekip atanmamış';
        var team = vm.teams.find(function(item) { return item.id === id; });
        return team ? team.name : 'Erişilemeyen ekip';
      };
      vm.stateLabel = core.stateLabel;
      vm.stateTone = core.stateTone;
      vm.sourceLabel = core.sourceLabel;
      vm.barWidth = core.barWidth;
      vm.scenarioDelta = core.scenarioDelta;

      $scope.$on('zumbo:concurrency-conflict', vm.load);
      $scope.$on('$ionicView.beforeEnter', vm.load);
      vm.load();
    });
})();
