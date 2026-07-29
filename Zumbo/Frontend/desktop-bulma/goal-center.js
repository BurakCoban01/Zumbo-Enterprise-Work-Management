(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopGoalFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboGoalCore;
          var apiActionError = helpers.apiActionError;
          vm.goals = [];
          vm.goal = null;
          vm.goalRollup = null;
          vm.goalPortfolios = [];
          vm.goalInitiativeOptions = [];
          vm.goalDraft = core.goal();
          vm.goalInitiativeKeys = [];
          vm.keyResultDraft = core.keyResult();
          vm.goalStatusDraft = core.statusUpdate();
          vm.keyResultProgressDraft = core.progressUpdate();
          vm.activeKeyResult = null;
          vm.goalPanel = 'key-results';
          vm.goalBusy = false;
          vm.goalError = null;
          vm.goalNotice = null;

          vm.loadGoals = function() {
            if (vm.goalBusy) return $q.when(vm.goals);
            vm.goalBusy = true;
            vm.goalError = null;
            return $q.all([
              apiClient.get('/api/goals?page=1&pageSize=100', {
                scope: 'desktop-goals',
                replace: true
              }),
              apiClient.get('/api/portfolios?page=1&pageSize=100', {
                scope: 'desktop-goal-portfolios',
                replace: true
              })
            ]).then(function(result) {
              vm.goals = result[0].items || [];
              vm.goalPortfolios = result[1].items || [];
              vm.goalInitiativeOptions = core.initiativeOptions(vm.goalPortfolios);
              if (vm.goal) {
                var current = vm.goals.find(function(item) { return item.id === vm.goal.id; });
                if (current) return vm.selectGoal(current);
              }
              return vm.goals.length ? vm.selectGoal(vm.goals[0]) : vm.newGoal();
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'Hedefler yüklenemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.selectGoal = function(item) {
            if (!item) return $q.when();
            vm.goalBusy = true;
            vm.goalError = null;
            return $q.all([
              apiClient.get('/api/goals/' + item.id, {
                scope: 'desktop-goal-detail',
                replace: true
              }),
              apiClient.get('/api/goals/' + item.id + '/rollup', {
                scope: 'desktop-goal-rollup',
                replace: true
              })
            ]).then(function(result) {
              vm.goal = result[0];
              vm.goalRollup = result[1];
              vm.goalDraft = core.hydrateGoal(result[0]);
              vm.goalInitiativeKeys = core.selectedInitiativeKeys(result[0].initiativeLinks);
              vm.newKeyResult();
              vm.goalStatusDraft = core.statusUpdate(result[0]);
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'Hedef ayrıntısı yüklenemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.newGoal = function() {
            vm.goal = null;
            vm.goalRollup = null;
            vm.goalDraft = core.goal();
            vm.goalInitiativeKeys = [];
            vm.goalPanel = 'definition';
            vm.goalError = null;
            vm.goalNotice = null;
          };

          vm.saveGoal = function() {
            var validation = core.validateGoal(vm.goalDraft);
            if (validation) {
              vm.goalError = validation;
              return $q.when();
            }
            vm.goalBusy = true;
            vm.goalDraft.initiativeLinks = core.linksFromKeys(
              vm.goalInitiativeKeys,
              vm.goalInitiativeOptions);
            var body = core.goalPayload(vm.goalDraft);
            var request = vm.goalDraft.id
              ? apiClient.put('/api/goals/' + vm.goalDraft.id, body)
              : apiClient.post('/api/goals', body);
            return request.then(function(saved) {
              vm.goalNotice = vm.goalDraft.id ? 'Hedef güncellendi.' : 'Hedef oluşturuldu.';
              vm.goal = saved;
              return vm.loadGoals();
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'Hedef kaydedilemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.archiveGoal = function() {
            if (!vm.goal || !vm.goal.canEdit
                || !$window.confirm('Bu hedefi arşivlemek istediğinize emin misiniz?')) {
              return $q.when();
            }
            var archivedId = vm.goal.id;
            vm.goalBusy = true;
            return apiClient.delete('/api/goals/' + archivedId)
              .then(function() {
                vm.goalNotice = 'Hedef arşivlendi.';
                vm.goals = vm.goals.filter(function(item) { return item.id !== archivedId; });
                vm.goal = null;
                vm.goalRollup = null;
                return vm.goals.length ? vm.selectGoal(vm.goals[0]) : vm.newGoal();
              }).catch(function(error) {
                vm.goalError = apiActionError(error, 'Hedef arşivlenemedi.');
              }).finally(function() {
                vm.goalBusy = false;
              });
          };

          vm.newKeyResult = function() {
            vm.keyResultDraft = core.keyResult(
              vm.session.currentUser && vm.session.currentUser.id);
            vm.activeKeyResult = null;
          };

          vm.editKeyResult = function(item) {
            vm.keyResultDraft = core.hydrateKeyResult(item);
            vm.activeKeyResult = null;
            vm.goalPanel = 'key-results';
          };

          vm.saveKeyResult = function() {
            if (!vm.goal || !vm.goal.canEdit) return $q.when();
            var validation = core.validateKeyResult(vm.keyResultDraft);
            if (validation) {
              vm.goalError = validation;
              return $q.when();
            }
            vm.goalBusy = true;
            var body = core.keyResultPayload(vm.keyResultDraft);
            var request = vm.keyResultDraft.id
              ? apiClient.put('/api/goals/' + vm.goal.id + '/key-results/'
                + vm.keyResultDraft.id, body)
              : apiClient.post('/api/goals/' + vm.goal.id + '/key-results', body);
            return request.then(function(saved) {
              vm.goalNotice = 'Key result kaydedildi.';
              return vm.selectGoal(saved);
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'Key result kaydedilemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.prepareKeyResultProgress = function(item) {
            vm.activeKeyResult = item;
            vm.keyResultProgressDraft = core.progressUpdate(item);
            vm.goalPanel = 'key-results';
          };

          vm.addKeyResultProgress = function() {
            if (!vm.goal || !vm.activeKeyResult || !vm.activeKeyResult.canUpdate) return $q.when();
            var validation = core.validateUpdate(vm.keyResultProgressDraft, 'İlerleme');
            if (validation) {
              vm.goalError = validation;
              return $q.when();
            }
            vm.goalBusy = true;
            return apiClient.post('/api/goals/' + vm.goal.id + '/key-results/'
              + vm.activeKeyResult.id + '/progress-updates', {
              currentValue: Number(vm.keyResultProgressDraft.currentValue),
              confidence: vm.keyResultProgressDraft.confidence === ''
                || vm.keyResultProgressDraft.confidence == null
                ? null : Number(vm.keyResultProgressDraft.confidence),
              note: String(vm.keyResultProgressDraft.note || '').trim()
            }).then(function(saved) {
              vm.goalNotice = 'Key result ilerlemesi yayınlandı.';
              return vm.selectGoal(saved);
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'İlerleme güncellenemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.addGoalStatus = function() {
            if (!vm.goal || !vm.goal.canUpdateStatus) return $q.when();
            var validation = core.validateUpdate(vm.goalStatusDraft, 'Durum');
            if (validation) {
              vm.goalError = validation;
              return $q.when();
            }
            vm.goalBusy = true;
            return apiClient.post('/api/goals/' + vm.goal.id + '/status-updates', {
              status: vm.goalStatusDraft.status,
              health: vm.goalStatusDraft.health,
              confidence: vm.goalStatusDraft.confidence === ''
                || vm.goalStatusDraft.confidence == null
                ? null : Number(vm.goalStatusDraft.confidence),
              note: String(vm.goalStatusDraft.note || '').trim()
            }).then(function(saved) {
              vm.goalNotice = 'Hedef durumu yayınlandı.';
              return vm.selectGoal(saved);
            }).catch(function(error) {
              vm.goalError = apiActionError(error, 'Hedef durumu güncellenemedi.');
            }).finally(function() {
              vm.goalBusy = false;
            });
          };

          vm.goalStatusLabel = core.statusLabel;
          vm.goalHealthLabel = core.healthLabel;
          vm.keyResultDirectionLabel = core.directionLabel;
          vm.goalProjectName = function(projectId) {
            var project = vm.projects.find(function(item) { return item.id === projectId; });
            return project ? project.name : 'Erişilemeyen proje';
          };
          vm.goalInitiativeName = function(link) {
            var option = vm.goalInitiativeOptions.find(function(item) {
              return item.portfolioId === link.portfolioId
                && item.initiativeId === link.initiativeId;
            });
            return option ? option.label : 'Erişilemeyen initiative';
          };
        }
      };
    });
})();
