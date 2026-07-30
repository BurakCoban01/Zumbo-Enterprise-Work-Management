(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('PortfolioController', function($scope, $q, $window, apiClient, zumboApi, sessionStore, mobileActionError) {
      var vm = this;
      var core = $window.ZumboPortfolioCore;
      var loadPromise = null;
      vm.portfolios = [];
      vm.projects = [];
      vm.users = [];
      vm.portfolio = null;
      vm.portfolioTreeRows = [];
      vm.roadmap = null;
      vm.mode = 'roadmap';
      vm.portfolioDraft = core.portfolio();
      vm.initiativeDraft = core.initiative(sessionStore.state.currentUser.id);
      vm.statusDraft = { status: 'Active', health: 'OnTrack', confidence: 75, note: '' };
      vm.dependencyDraft = core.dependency();

      vm.load = function() {
        if (loadPromise) return loadPromise;
        vm.loading = true;
        vm.error = null;
        loadPromise = $q.all([
          apiClient.get('/api/portfolios?page=1&pageSize=100', {
            scope: 'mobile-portfolios',
            replace: true
          }),
          zumboApi.projects(),
          zumboApi.users()
        ]).then(function(result) {
          vm.portfolios = result[0].items || [];
          vm.projects = result[1];
          vm.users = result[2];
          if (vm.portfolio) {
            var current = vm.portfolios.find(function(item) { return item.id === vm.portfolio.id; });
            if (current) return vm.select(current);
          }
          return vm.portfolios.length ? vm.select(vm.portfolios[0]) : vm.newPortfolio();
        }).catch(function(error) {
          if (error && error.canceled) return;
          vm.error = mobileActionError(error, 'Portföyler yüklenemedi.');
        }).finally(function() {
          loadPromise = null;
          vm.loading = false;
          $scope.$broadcast('scroll.refreshComplete');
        });
        return loadPromise;
      };

      vm.select = function(item) {
        if (!item) return $q.when();
        vm.loading = true;
        return $q.all([
          apiClient.get('/api/portfolios/' + item.id, {
            scope: 'mobile-portfolio-detail',
            replace: true
          }),
          apiClient.get('/api/portfolios/' + item.id + '/roadmap', {
            scope: 'mobile-portfolio-roadmap',
            replace: true
          })
        ]).then(function(result) {
          vm.portfolio = result[0];
          vm.portfolioTreeRows = core.tree(result[0].initiatives);
          vm.portfolioDraft = angular.copy(result[0]);
          vm.roadmap = result[1];
          vm.newInitiative();
          vm.newDependency();
        }).catch(function(error) {
          if (error && error.canceled) return;
          vm.error = mobileActionError(error, 'Portföy ayrıntısı yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
        });
      };

      vm.newPortfolio = function() {
        vm.portfolio = null;
        vm.portfolioTreeRows = [];
        vm.roadmap = null;
        vm.portfolioDraft = core.portfolio();
        vm.mode = 'definition';
      };

      vm.savePortfolio = function() {
        var validation = core.validatePortfolio(vm.portfolioDraft);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        var request = vm.portfolioDraft.id
          ? apiClient.put('/api/portfolios/' + vm.portfolioDraft.id, core.portfolioPayload(vm.portfolioDraft))
          : apiClient.post('/api/portfolios', core.portfolioPayload(vm.portfolioDraft));
        return request.then(function(saved) {
          vm.notice = vm.portfolioDraft.id ? 'Portföy güncellendi.' : 'Portföy oluşturuldu.';
          return vm.load().then(function() { return vm.select(saved); });
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Portföy kaydedilemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.archivePortfolio = function() {
        if (!vm.portfolio || !vm.portfolio.canEdit
            || !$window.confirm('Bu portföyü arşivlemek istediğinize emin misiniz?')) {
          return $q.when();
        }
        var archivedId = vm.portfolio.id;
        vm.saving = true;
        return apiClient.delete('/api/portfolios/' + archivedId)
          .then(function() {
            vm.notice = 'Portföy arşivlendi.';
            vm.portfolios = vm.portfolios.filter(function(item) {
              return item.id !== archivedId;
            });
            vm.portfolio = null;
            vm.roadmap = null;
            vm.portfolioTreeRows = [];
            return vm.portfolios.length ? vm.select(vm.portfolios[0]) : vm.newPortfolio();
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Portföy arşivlenemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.setMode = function(mode) {
        vm.mode = mode;
        vm.error = null;
      };

      vm.newInitiative = function(parentId) {
        vm.initiativeDraft = core.initiative(sessionStore.state.currentUser.id);
        vm.initiativeDraft.parentInitiativeId = parentId || null;
      };

      vm.editInitiative = function(item) {
        vm.initiativeDraft = angular.copy(item);
        vm.mode = 'initiative-form';
      };

      vm.saveInitiative = function() {
        var validation = core.validateInitiative(vm.initiativeDraft);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        var body = core.initiativePayload(vm.initiativeDraft);
        var request = vm.initiativeDraft.id
          ? apiClient.put('/api/portfolios/' + vm.portfolio.id + '/initiatives/'
            + vm.initiativeDraft.id, body)
          : apiClient.post('/api/portfolios/' + vm.portfolio.id + '/initiatives', body);
        return request
          .then(function(saved) {
            vm.notice = 'Initiative kaydedildi.';
            vm.mode = 'hierarchy';
            return vm.select(saved);
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Initiative kaydedilemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.prepareStatus = function(item) {
        vm.initiativeDraft = angular.copy(item);
        vm.statusDraft = {
          status: item.status,
          health: item.health,
          confidence: item.confidence,
          note: ''
        };
        vm.mode = 'status-form';
      };

      vm.saveStatus = function() {
        if (!vm.portfolio || !vm.initiativeDraft.canUpdateStatus || !vm.initiativeDraft.id) return;
        if (!String(vm.statusDraft.note || '').trim()) {
          vm.error = 'Durum notu gereklidir.';
          return;
        }
        vm.saving = true;
        return apiClient.post('/api/portfolios/' + vm.portfolio.id + '/initiatives/'
          + vm.initiativeDraft.id + '/status-updates', vm.statusDraft)
          .then(function(saved) {
            vm.notice = 'Durum güncellemesi yayınlandı.';
            vm.mode = 'updates';
            return vm.select(saved);
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Durum güncellenemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.newDependency = function() {
        vm.dependencyDraft = core.dependency();
      };

      vm.editDependency = function(item) {
        vm.dependencyDraft = angular.copy(item);
        vm.mode = 'dependency-form';
      };

      vm.saveDependency = function() {
        var validation = core.validateDependency(vm.dependencyDraft);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        var body = core.dependencyPayload(vm.dependencyDraft);
        var request = vm.dependencyDraft.id
          ? apiClient.put('/api/portfolios/' + vm.portfolio.id + '/dependencies/'
            + vm.dependencyDraft.id, body)
          : apiClient.post('/api/portfolios/' + vm.portfolio.id + '/dependencies', body);
        return request
          .then(function(saved) {
            vm.notice = 'Bağımlılık kaydedildi.';
            vm.mode = 'dependencies';
            return vm.select(saved);
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Bağımlılık kaydedilemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.tree = function() { return vm.portfolioTreeRows; };
      vm.healthLabel = core.healthLabel;
      vm.statusLabel = core.statusLabel;
      vm.canUpdateAnyStatus = function() {
        return !!(vm.portfolio && vm.portfolio.initiatives.some(function(item) {
          return item.canUpdateStatus;
        }));
      };
      vm.projectName = function(id) { return core.projectName(id, vm.projects); };
      vm.roadmapItem = function(id) {
        return vm.roadmap && vm.roadmap.initiatives.find(function(item) { return item.id === id; });
      };
      vm.linkedProjects = function() {
        if (!vm.portfolio) return [];
        var ids = vm.portfolio.initiatives.reduce(function(result, item) {
          return result.concat(item.projectIds || []);
        }, []);
        return vm.projects.filter(function(item) { return ids.indexOf(item.id) >= 0; });
      };
      vm.userName = function(id) {
        var user = vm.users.find(function(item) { return item.id === id; });
        return user ? (user.displayName || user.username) : 'Erişilemeyen kullanıcı';
      };

      $scope.$on('zumbo:concurrency-conflict', vm.load);
      $scope.$on('$ionicView.beforeEnter', vm.load);
      vm.load();
    });
})();
