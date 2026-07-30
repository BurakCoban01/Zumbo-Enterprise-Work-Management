(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopPortfolioFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboPortfolioCore;
          var apiActionError = helpers.apiActionError;
          vm.portfolios = [];
          vm.portfolio = null;
          vm.portfolioTreeRows = [];
          vm.portfolioDraft = core.portfolio();
          vm.portfolioRoadmap = null;
          vm.portfolioBusy = false;
          vm.portfolioError = null;
          vm.portfolioNotice = null;
          vm.initiativeDraft = core.initiative();
          vm.statusUpdateDraft = { status: 'Active', health: 'OnTrack', confidence: 75, note: '' };
          vm.dependencyDraft = core.dependency();
          vm.portfolioPanel = 'roadmap';

          vm.loadPortfolios = function() {
            if (vm.portfolioBusy) return $q.when(vm.portfolios);
            vm.portfolioBusy = true;
            vm.portfolioError = null;
            return apiClient.get('/api/portfolios?page=1&pageSize=100', {
              scope: 'desktop-portfolios',
              replace: true
            }).then(function(page) {
              vm.portfolios = page.items || [];
              if (vm.portfolio) {
                var current = vm.portfolios.find(function(item) { return item.id === vm.portfolio.id; });
                if (current) return vm.selectPortfolio(current);
              }
              return vm.portfolios.length ? vm.selectPortfolio(vm.portfolios[0]) : vm.newPortfolio();
            }).catch(function(error) {
              vm.portfolioError = apiActionError(error, 'Portföyler yüklenemedi.');
            }).finally(function() {
              vm.portfolioBusy = false;
            });
          };

          vm.selectPortfolio = function(item) {
            if (!item) return $q.when();
            vm.portfolioBusy = true;
            vm.portfolioError = null;
            return $q.all([
              apiClient.get('/api/portfolios/' + item.id, {
                scope: 'desktop-portfolio-detail',
                replace: true
              }),
              apiClient.get('/api/portfolios/' + item.id + '/roadmap', {
                scope: 'desktop-portfolio-roadmap',
                replace: true
              })
            ]).then(function(result) {
              vm.portfolio = result[0];
              vm.portfolioTreeRows = core.tree(result[0].initiatives);
              vm.portfolioDraft = angular.copy(result[0]);
              vm.portfolioRoadmap = result[1];
              vm.newInitiative();
              vm.newDependency();
            }).catch(function(error) {
              vm.portfolioError = apiActionError(error, 'Portföy ayrıntısı yüklenemedi.');
            }).finally(function() {
              vm.portfolioBusy = false;
            });
          };

          vm.newPortfolio = function() {
            vm.portfolio = null;
            vm.portfolioTreeRows = [];
            vm.portfolioRoadmap = null;
            vm.portfolioDraft = core.portfolio();
            vm.portfolioError = null;
            vm.portfolioNotice = null;
          };

          vm.savePortfolio = function() {
            var validation = core.validatePortfolio(vm.portfolioDraft);
            if (validation) {
              vm.portfolioError = validation;
              return $q.when();
            }
            vm.portfolioBusy = true;
            var body = core.portfolioPayload(vm.portfolioDraft);
            var request = vm.portfolioDraft.id
              ? apiClient.put('/api/portfolios/' + vm.portfolioDraft.id, body)
              : apiClient.post('/api/portfolios', body);
            return request.then(function(saved) {
              vm.portfolioNotice = vm.portfolioDraft.id ? 'Portföy güncellendi.' : 'Portföy oluşturuldu.';
              return vm.loadPortfolios().then(function() { return vm.selectPortfolio(saved); });
            }).catch(function(error) {
              vm.portfolioError = apiActionError(error, 'Portföy kaydedilemedi.');
            }).finally(function() {
              vm.portfolioBusy = false;
            });
          };

          vm.archivePortfolio = function() {
            if (!vm.portfolio || !vm.portfolio.canEdit
                || !$window.confirm('Bu portföyü arşivlemek istediğinize emin misiniz?')) {
              return $q.when();
            }
            var archivedId = vm.portfolio.id;
            vm.portfolioBusy = true;
            return apiClient.delete('/api/portfolios/' + archivedId)
              .then(function() {
                vm.portfolioNotice = 'Portföy arşivlendi.';
                vm.portfolios = vm.portfolios.filter(function(item) {
                  return item.id !== archivedId;
                });
                vm.portfolio = null;
                vm.portfolioRoadmap = null;
                vm.portfolioTreeRows = [];
                return vm.portfolios.length
                  ? vm.selectPortfolio(vm.portfolios[0])
                  : vm.newPortfolio();
              }).catch(function(error) {
                vm.portfolioError = apiActionError(error, 'Portföy arşivlenemedi.');
              }).finally(function() {
                vm.portfolioBusy = false;
              });
          };

          vm.newInitiative = function(parentId) {
            vm.initiativeDraft = core.initiative(
              vm.session.currentUser && vm.session.currentUser.id);
            vm.initiativeDraft.parentInitiativeId = parentId || null;
          };

          vm.editInitiative = function(item) {
            vm.initiativeDraft = angular.copy(item);
            vm.portfolioPanel = 'initiatives';
          };

          vm.saveInitiative = function() {
            if (!vm.portfolio || !vm.portfolio.canEdit) return $q.when();
            var validation = core.validateInitiative(vm.initiativeDraft);
            if (validation) {
              vm.portfolioError = validation;
              return $q.when();
            }
            vm.portfolioBusy = true;
            var body = core.initiativePayload(vm.initiativeDraft);
            var request = vm.initiativeDraft.id
              ? apiClient.put('/api/portfolios/' + vm.portfolio.id + '/initiatives/'
                + vm.initiativeDraft.id, body)
              : apiClient.post('/api/portfolios/' + vm.portfolio.id + '/initiatives', body);
            return request
              .then(function(saved) {
                vm.portfolioNotice = 'Initiative kaydedildi.';
                return vm.selectPortfolio(saved);
              }).catch(function(error) {
                vm.portfolioError = apiActionError(error, 'Initiative kaydedilemedi.');
              }).finally(function() {
                vm.portfolioBusy = false;
              });
          };

          vm.prepareStatusUpdate = function(item) {
            vm.initiativeDraft = angular.copy(item);
            vm.statusUpdateDraft = {
              status: item.status,
              health: item.health,
              confidence: item.confidence,
              note: ''
            };
            vm.portfolioPanel = 'updates';
          };

          vm.addInitiativeStatus = function() {
            if (!vm.portfolio || !vm.initiativeDraft.canUpdateStatus || !vm.initiativeDraft.id
                || !String(vm.statusUpdateDraft.note || '').trim()) return $q.when();
            vm.portfolioBusy = true;
            return apiClient.post(
              '/api/portfolios/' + vm.portfolio.id + '/initiatives/'
                + vm.initiativeDraft.id + '/status-updates',
              vm.statusUpdateDraft)
              .then(function(saved) {
                vm.portfolioNotice = 'Durum güncellemesi yayınlandı.';
                return vm.selectPortfolio(saved);
              }).catch(function(error) {
                vm.portfolioError = apiActionError(error, 'Durum güncellenemedi.');
              }).finally(function() {
                vm.portfolioBusy = false;
              });
          };

          vm.newDependency = function() {
            vm.dependencyDraft = core.dependency();
          };

          vm.editDependency = function(item) {
            vm.dependencyDraft = angular.copy(item);
            vm.portfolioPanel = 'dependencies';
          };

          vm.saveDependency = function() {
            if (!vm.portfolio || !vm.portfolio.canEdit) return $q.when();
            var validation = core.validateDependency(vm.dependencyDraft);
            if (validation) {
              vm.portfolioError = validation;
              return $q.when();
            }
            vm.portfolioBusy = true;
            var body = core.dependencyPayload(vm.dependencyDraft);
            var request = vm.dependencyDraft.id
              ? apiClient.put('/api/portfolios/' + vm.portfolio.id + '/dependencies/'
                + vm.dependencyDraft.id, body)
              : apiClient.post('/api/portfolios/' + vm.portfolio.id + '/dependencies', body);
            return request
              .then(function(saved) {
                vm.portfolioNotice = 'Bağımlılık kaydedildi.';
                return vm.selectPortfolio(saved);
              }).catch(function(error) {
                vm.portfolioError = apiActionError(error, 'Bağımlılık kaydedilemedi.');
              }).finally(function() {
                vm.portfolioBusy = false;
              });
          };

          vm.portfolioTree = function() {
            return vm.portfolioTreeRows;
          };
          vm.portfolioHealthLabel = core.healthLabel;
          vm.portfolioStatusLabel = core.statusLabel;
          vm.portfolioCanUpdateAnyStatus = function() {
            return !!(vm.portfolio && vm.portfolio.initiatives.some(function(item) {
              return item.canUpdateStatus;
            }));
          };
          vm.portfolioProjectName = function(projectId) {
            return core.projectName(projectId, vm.projects);
          };
          vm.roadmapInitiative = function(initiativeId) {
            return vm.portfolioRoadmap && vm.portfolioRoadmap.initiatives.find(function(item) {
              return item.id === initiativeId;
            });
          };
          vm.portfolioLinkedProjects = function() {
            if (!vm.portfolio) return [];
            var ids = vm.portfolio.initiatives.reduce(function(result, item) {
              return result.concat(item.projectIds || []);
            }, []);
            return vm.projects.filter(function(project) { return ids.indexOf(project.id) >= 0; });
          };
        }
      };
    });
})();
