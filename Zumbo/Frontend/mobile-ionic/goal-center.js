(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('GoalController', function(
      $scope,
      $q,
      $window,
      apiClient,
      zumboApi,
      sessionStore,
      mobileActionError) {
      var vm = this;
      var core = $window.ZumboGoalCore;
      vm.goals = [];
      vm.projects = [];
      vm.users = [];
      vm.portfolios = [];
      vm.initiativeOptions = [];
      vm.goal = null;
      vm.rollup = null;
      vm.mode = 'key-results';
      vm.goalDraft = core.goal();
      vm.initiativeKeys = [];
      vm.keyResultDraft = core.keyResult(sessionStore.state.currentUser.id);
      vm.statusDraft = core.statusUpdate();
      vm.progressDraft = core.progressUpdate();
      vm.activeKeyResult = null;

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        return $q.all([
          apiClient.get('/api/goals?page=1&pageSize=100', {
            scope: 'mobile-goals',
            replace: true
          }),
          apiClient.get('/api/portfolios?page=1&pageSize=100', {
            scope: 'mobile-goal-portfolios',
            replace: true
          }),
          zumboApi.projects(),
          zumboApi.users()
        ]).then(function(result) {
          vm.goals = result[0].items || [];
          vm.portfolios = result[1].items || [];
          vm.initiativeOptions = core.initiativeOptions(vm.portfolios);
          vm.projects = result[2];
          vm.users = result[3];
          if (vm.goal) {
            var current = vm.goals.find(function(item) { return item.id === vm.goal.id; });
            if (current) return vm.select(current);
          }
          return vm.goals.length ? vm.select(vm.goals[0]) : vm.newGoal();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Hedefler yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
          $scope.$broadcast('scroll.refreshComplete');
        });
      };

      vm.select = function(item) {
        if (!item) return $q.when();
        vm.loading = true;
        vm.error = null;
        return $q.all([
          apiClient.get('/api/goals/' + item.id, {
            scope: 'mobile-goal-detail',
            replace: true
          }),
          apiClient.get('/api/goals/' + item.id + '/rollup', {
            scope: 'mobile-goal-rollup',
            replace: true
          })
        ]).then(function(result) {
          vm.goal = result[0];
          vm.rollup = result[1];
          vm.goalDraft = core.hydrateGoal(result[0]);
          vm.initiativeKeys = core.selectedInitiativeKeys(result[0].initiativeLinks);
          vm.newKeyResult();
          vm.statusDraft = core.statusUpdate(result[0]);
          vm.mode = 'key-results';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Hedef ayrıntısı yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
        });
      };

      vm.newGoal = function() {
        vm.goal = null;
        vm.rollup = null;
        vm.goalDraft = core.goal();
        vm.initiativeKeys = [];
        vm.mode = 'definition';
        vm.error = null;
      };

      vm.setMode = function(mode) {
        vm.mode = mode;
        vm.error = null;
      };

      vm.saveGoal = function() {
        var validation = core.validateGoal(vm.goalDraft);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        vm.goalDraft.initiativeLinks = core.linksFromKeys(
          vm.initiativeKeys,
          vm.initiativeOptions);
        var body = core.goalPayload(vm.goalDraft);
        var request = vm.goalDraft.id
          ? apiClient.put('/api/goals/' + vm.goalDraft.id, body)
          : apiClient.post('/api/goals', body);
        return request.then(function(saved) {
          vm.notice = vm.goalDraft.id ? 'Hedef güncellendi.' : 'Hedef oluşturuldu.';
          vm.goal = saved;
          return vm.load();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Hedef kaydedilemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.archiveGoal = function() {
        if (!vm.goal || !vm.goal.canEdit
            || !$window.confirm('Bu hedefi arşivlemek istediğinize emin misiniz?')) {
          return $q.when();
        }
        var archivedId = vm.goal.id;
        vm.saving = true;
        return apiClient.delete('/api/goals/' + archivedId)
          .then(function() {
            vm.notice = 'Hedef arşivlendi.';
            vm.goals = vm.goals.filter(function(item) { return item.id !== archivedId; });
            vm.goal = null;
            vm.rollup = null;
            return vm.goals.length ? vm.select(vm.goals[0]) : vm.newGoal();
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Hedef arşivlenemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.newKeyResult = function() {
        vm.keyResultDraft = core.keyResult(sessionStore.state.currentUser.id);
        vm.activeKeyResult = null;
      };

      vm.editKeyResult = function(item) {
        vm.keyResultDraft = core.hydrateKeyResult(item);
        vm.activeKeyResult = null;
        vm.mode = 'key-result-form';
      };

      vm.saveKeyResult = function() {
        var validation = core.validateKeyResult(vm.keyResultDraft);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        var body = core.keyResultPayload(vm.keyResultDraft);
        var request = vm.keyResultDraft.id
          ? apiClient.put('/api/goals/' + vm.goal.id + '/key-results/'
            + vm.keyResultDraft.id, body)
          : apiClient.post('/api/goals/' + vm.goal.id + '/key-results', body);
        return request.then(function(saved) {
          vm.notice = 'Key result kaydedildi.';
          return vm.select(saved);
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Key result kaydedilemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.prepareProgress = function(item) {
        vm.activeKeyResult = item;
        vm.progressDraft = core.progressUpdate(item);
        vm.mode = 'progress-form';
      };

      vm.saveProgress = function() {
        if (!vm.activeKeyResult || !vm.activeKeyResult.canUpdate) return;
        var validation = core.validateUpdate(vm.progressDraft, 'İlerleme');
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        return apiClient.post('/api/goals/' + vm.goal.id + '/key-results/'
          + vm.activeKeyResult.id + '/progress-updates', {
          currentValue: Number(vm.progressDraft.currentValue),
          confidence: vm.progressDraft.confidence === ''
            || vm.progressDraft.confidence == null
            ? null : Number(vm.progressDraft.confidence),
          note: String(vm.progressDraft.note || '').trim()
        }).then(function(saved) {
          vm.notice = 'İlerleme yayınlandı.';
          return vm.select(saved);
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'İlerleme güncellenemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.saveStatus = function() {
        var validation = core.validateUpdate(vm.statusDraft, 'Durum');
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        return apiClient.post('/api/goals/' + vm.goal.id + '/status-updates', {
          status: vm.statusDraft.status,
          health: vm.statusDraft.health,
          confidence: vm.statusDraft.confidence === ''
            || vm.statusDraft.confidence == null
            ? null : Number(vm.statusDraft.confidence),
          note: String(vm.statusDraft.note || '').trim()
        }).then(function(saved) {
          vm.notice = 'Hedef durumu yayınlandı.';
          return vm.select(saved);
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Hedef durumu güncellenemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.statusLabel = core.statusLabel;
      vm.healthLabel = core.healthLabel;
      vm.directionLabel = core.directionLabel;
      vm.userName = function(id) {
        var user = vm.users.find(function(item) { return item.id === id; });
        return user ? (user.displayName || user.username) : 'Erişilemeyen kullanıcı';
      };
      vm.projectName = function(id) {
        var project = vm.projects.find(function(item) { return item.id === id; });
        return project ? project.name : 'Erişilemeyen proje';
      };
      vm.initiativeName = function(link) {
        var option = vm.initiativeOptions.find(function(item) {
          return item.portfolioId === link.portfolioId
            && item.initiativeId === link.initiativeId;
        });
        return option ? option.label : 'Erişilemeyen initiative';
      };

      $scope.$on('zumbo:concurrency-conflict', vm.load);
      $scope.$on('$ionicView.beforeEnter', vm.load);
      vm.load();
    });
})();
