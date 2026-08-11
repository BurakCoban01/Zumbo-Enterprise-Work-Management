(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopCapacityFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboCapacityPlanningCore;
          var apiActionError = helpers.apiActionError;
          vm.capacityPlans = [];
          vm.capacityPlan = null;
          vm.capacitySnapshot = null;
          vm.capacityScenario = null;
          vm.capacityPortfolios = [];
          vm.capacityDraft = core.plan(currentUserId());
          vm.capacityScenarioAllocations = [];
          vm.capacityPanel = 'people';
          vm.capacityBusy = false;
          vm.capacityError = null;
          vm.capacityNotice = null;

          function currentUserId() {
            return vm.session.currentUser && vm.session.currentUser.id;
          }

          function isSystemAdministrator() {
            return vm.hasSystemPermission('*');
          }

          function isManageableProject(project) {
            if (isSystemAdministrator()) return true;
            return (project.members || []).some(function(member) {
              return member.userId === currentUserId()
                && vm.projectRoleHasPermission(member.role, 'BoardManage');
            });
          }

          vm.capacityManageableProjects = function() {
            return (vm.projects || []).filter(isManageableProject);
          };

          vm.canCreateCapacityPlan = function() {
            return vm.capacityManageableProjects().length > 0;
          };

          vm.capacityProjectOptions = function() {
            var selected = vm.capacityDraft && vm.capacityDraft.projectIds || [];
            return (vm.projects || []).filter(function(project) {
              return isManageableProject(project)
                || (vm.capacityPlan && selected.indexOf(project.id) >= 0);
            });
          };

          vm.capacityViewerUsers = function() {
            var ownerUserId = vm.capacityDraft && vm.capacityDraft.ownerUserId;
            return (vm.users || []).filter(function(user) {
              return user.id !== ownerUserId;
            });
          };

          vm.capacityTeamOptions = function(member) {
            return (vm.teams || []).filter(function(team) {
              return team.id === (member && member.teamId)
                || (team.members || []).some(function(teamMember) {
                  return teamMember.userId === (member && member.userId)
                    && teamMember.status === 'Active';
                });
            });
          };

          vm.loadCapacityPlans = function() {
            if (vm.capacityBusy) return $q.when(vm.capacityPlans);
            vm.capacityBusy = true;
            vm.capacityError = null;
            return $q.all([
              apiClient.get('/api/capacity-plans?page=1&pageSize=100', {
                scope: 'desktop-capacity-plans',
                replace: true
              }),
              apiClient.get('/api/portfolios?page=1&pageSize=100', {
                scope: 'desktop-capacity-portfolios',
                replace: true
              })
            ]).then(function(result) {
              vm.capacityPlans = result[0].items || [];
              vm.capacityPortfolios = result[1].items || [];
              var current = vm.capacityPlan && vm.capacityPlans.find(function(item) {
                return item.id === vm.capacityPlan.id;
              });
              return current
                ? vm.selectCapacityPlan(current)
                : (vm.capacityPlans.length
                  ? vm.selectCapacityPlan(vm.capacityPlans[0])
                  : vm.newCapacityPlan());
            }).catch(function(error) {
              vm.capacityError = apiActionError(error, 'Kapasite planları yüklenemedi.');
            }).finally(function() {
              vm.capacityBusy = false;
            });
          };

          vm.selectCapacityPlan = function(item) {
            if (!item) return $q.when();
            vm.capacityBusy = true;
            vm.capacityError = null;
            vm.capacityNotice = null;
            return $q.all([
              apiClient.get('/api/capacity-plans/' + item.id, {
                scope: 'desktop-capacity-detail',
                replace: true
              }),
              apiClient.get('/api/capacity-plans/' + item.id + '/snapshot', {
                scope: 'desktop-capacity-snapshot',
                replace: true
              })
            ]).then(function(result) {
              vm.capacityPlan = result[0];
              vm.capacitySnapshot = result[1];
              vm.capacityDraft = core.hydratePlan(result[0]);
              vm.resetCapacityScenario();
              vm.capacityPanel = 'people';
            }).catch(function(error) {
              vm.capacityError = apiActionError(error, 'Kapasite planı yüklenemedi.');
            }).finally(function() {
              vm.capacityBusy = false;
            });
          };

          vm.newCapacityPlan = function() {
            vm.capacityPlan = null;
            vm.capacitySnapshot = null;
            vm.capacityScenario = null;
            vm.capacityScenarioAllocations = [];
            vm.capacityError = null;
            vm.capacityNotice = null;
            if (!vm.canCreateCapacityPlan()) {
              vm.capacityDraft = null;
              vm.capacityPanel = 'people';
              return;
            }
            vm.capacityDraft = core.plan(currentUserId());
            vm.capacityPanel = 'definition';
          };

          vm.saveCapacityPlan = function() {
            if (!vm.capacityPlan && !vm.canCreateCapacityPlan()) return $q.when();
            var validation = core.validate(vm.capacityDraft);
            if (validation) {
              vm.capacityError = validation;
              return $q.when();
            }
            vm.capacityBusy = true;
            vm.capacityError = null;
            var body = core.payload(vm.capacityDraft);
            var request = vm.capacityDraft.id
              ? apiClient.put('/api/capacity-plans/' + vm.capacityDraft.id, body)
              : apiClient.post('/api/capacity-plans', body);
            return request.then(function(saved) {
              vm.capacityNotice = vm.capacityDraft.id
                ? 'Kapasite planı güncellendi.'
                : 'Kapasite planı oluşturuldu.';
              vm.capacityPlan = saved;
              var listIndex = vm.capacityPlans.findIndex(function(item) {
                return item.id === saved.id;
              });
              if (listIndex >= 0) vm.capacityPlans[listIndex] = saved;
              else vm.capacityPlans.unshift(saved);
              return vm.selectCapacityPlan(saved);
            }).catch(function(error) {
              vm.capacityError = apiActionError(error, 'Kapasite planı kaydedilemedi.');
            }).finally(function() {
              vm.capacityBusy = false;
            });
          };

          vm.shareCapacityPlan = function() {
            if (!vm.capacityPlan || !vm.capacityPlan.canEdit) return $q.when();
            vm.capacityBusy = true;
            vm.capacityError = null;
            return apiClient.put(
              '/api/capacity-plans/' + vm.capacityPlan.id + '/sharing',
              { viewerUserIds: vm.capacityDraft.viewerUserIds }
            ).then(function(saved) {
              vm.capacityPlan = saved;
              vm.capacityDraft = core.hydratePlan(saved);
              vm.capacityNotice = 'Kapasite planı paylaşımı güncellendi.';
            }).catch(function(error) {
              vm.capacityError = apiActionError(error, 'Kapasite planı paylaşımı güncellenemedi.');
            }).finally(function() {
              vm.capacityBusy = false;
            });
          };

          vm.archiveCapacityPlan = function() {
            if (!vm.capacityPlan || !vm.capacityPlan.canEdit
                || !$window.confirm('Bu kapasite planını arşivlemek istediğinize emin misiniz?')) {
              return $q.when();
            }
            var archivedId = vm.capacityPlan.id;
            vm.capacityBusy = true;
            return apiClient.delete('/api/capacity-plans/' + archivedId)
              .then(function() {
                vm.capacityNotice = 'Kapasite planı arşivlendi.';
                vm.capacityPlans = vm.capacityPlans.filter(function(item) {
                  return item.id !== archivedId;
                });
                return vm.capacityPlans.length
                  ? vm.selectCapacityPlan(vm.capacityPlans[0])
                  : vm.newCapacityPlan();
              }).catch(function(error) {
                vm.capacityError = apiActionError(error, 'Kapasite planı arşivlenemedi.');
              }).finally(function() {
                vm.capacityBusy = false;
              });
          };

          vm.addCapacityMember = function() {
            if (vm.capacityDraft.members.length >= core.limits.members) return;
            vm.capacityDraft.members.push(core.member());
          };

          vm.removeCapacityMember = function(index) {
            if (vm.capacityDraft.members.length <= 1) return;
            var removed = vm.capacityDraft.members.splice(index, 1)[0];
            vm.capacityDraft.allocations = vm.capacityDraft.allocations.filter(function(item) {
              return item.userId !== removed.userId;
            });
          };

          vm.addCapacityAllocation = function() {
            if (vm.capacityDraft.allocations.length >= core.limits.allocations) return;
            vm.capacityDraft.allocations.push(core.allocation(vm.capacityDraft));
          };

          vm.removeCapacityAllocation = function(index) {
            vm.capacityDraft.allocations.splice(index, 1);
          };

          vm.resetCapacityScenario = function() {
            vm.capacityScenario = null;
            vm.capacityScenarioAllocations = (vm.capacityPlan
              ? vm.capacityPlan.allocations
              : []).map(core.hydrateAllocation);
          };

          vm.addCapacityScenarioAllocation = function() {
            if (!vm.capacityPlan
                || vm.capacityScenarioAllocations.length >= core.limits.allocations) return;
            vm.capacityScenarioAllocations.push(core.allocation(
              core.hydratePlan(vm.capacityPlan)));
          };

          vm.removeCapacityScenarioAllocation = function(index) {
            vm.capacityScenarioAllocations.splice(index, 1);
          };

          vm.previewCapacityScenario = function() {
            if (!vm.capacityPlan || !vm.capacityPlan.canEdit) return $q.when();
            var baseline = core.hydratePlan(vm.capacityPlan);
            var validation = core.validate(baseline, vm.capacityScenarioAllocations);
            if (validation) {
              vm.capacityError = validation;
              return $q.when();
            }
            vm.capacityBusy = true;
            vm.capacityError = null;
            return apiClient.post('/api/capacity-plans/' + vm.capacityPlan.id + '/scenarios', {
              allocations: core.payload(baseline, vm.capacityScenarioAllocations).allocations
            }, {
              scope: 'desktop-capacity-scenario',
              replace: true
            }).then(function(result) {
              vm.capacityScenario = result;
              vm.capacityNotice = 'Senaryo hesaplandı; kayıtlı plan değiştirilmedi.';
            }).catch(function(error) {
              vm.capacityError = apiActionError(error, 'Kapasite senaryosu hesaplanamadı.');
            }).finally(function() {
              vm.capacityBusy = false;
            });
          };

          vm.capacityUserName = function(id) {
            var user = (vm.users || []).find(function(item) { return item.id === id; });
            return user ? (user.displayName || user.username) : 'Erişilemeyen kullanıcı';
          };
          vm.capacityProjectName = function(id) {
            var project = (vm.projects || []).find(function(item) { return item.id === id; });
            return project ? project.name : 'Erişilemeyen proje';
          };
          vm.capacityTeamName = function(id) {
            if (!id) return 'Ekip atanmamış';
            var team = (vm.teams || []).find(function(item) { return item.id === id; });
            return team ? team.name : 'Erişilemeyen ekip';
          };
          vm.capacityPortfolioName = function(id) {
            var portfolio = vm.capacityPortfolios.find(function(item) { return item.id === id; });
            return portfolio ? portfolio.name : 'Portföy yok';
          };
          vm.capacityStateLabel = core.stateLabel;
          vm.capacityStateTone = core.stateTone;
          vm.capacitySourceLabel = core.sourceLabel;
          vm.capacityBarWidth = core.barWidth;
          vm.capacityScenarioDelta = core.scenarioDelta;
        }
      };
    });
})();
