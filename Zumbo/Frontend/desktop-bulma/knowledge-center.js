(function() {
  'use strict';

  angular.module('zumboDesktop')
    .directive('knowledgeSegments', function() {
      return {
        restrict: 'E',
        scope: { segments: '<' },
        template: '<span ng-repeat="segment in segments track by $index" ng-switch="segment.type">'
          + '<a ng-switch-when="link" ng-href="{{segment.href}}" ng-attr-target="{{segment.href.indexOf(\'http\')===0 ? \'_blank\' : undefined}}" rel="noopener noreferrer">{{segment.text}}</a>'
          + '<code ng-switch-when="code">{{segment.text}}</code>'
          + '<strong ng-switch-when="strong">{{segment.text}}</strong>'
          + '<span ng-switch-default>{{segment.text}}</span>'
          + '</span>'
      };
    })
    .factory('desktopKnowledgeFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboKnowledgeCore;
          var apiActionError = helpers.apiActionError;
          vm.knowledgeDocuments = [];
          vm.knowledgeDocument = null;
          vm.knowledgePortfolios = [];
          vm.knowledgeScopes = [];
          vm.knowledgeLinkOptions = { workItems: [], users: [], sourceStatus: 'Ready' };
          vm.knowledgeDraft = core.draft();
          vm.knowledgeBlocks = [];
          vm.knowledgePreviewVersion = null;
          vm.knowledgePanel = 'content';
          vm.knowledgeQuery = '';
          vm.knowledgeComment = '';
          vm.knowledgeBusy = false;
          vm.knowledgeError = null;
          vm.knowledgeNotice = null;

          vm.loadKnowledge = function() {
            if (vm.knowledgeBusy) return $q.when(vm.knowledgeDocuments);
            vm.knowledgeBusy = true;
            vm.knowledgeError = null;
            var query = vm.knowledgeQuery
              ? '&query=' + encodeURIComponent(vm.knowledgeQuery) : '';
            return $q.all([
              apiClient.get('/api/knowledge-documents?page=1&pageSize=100' + query, {
                scope: 'desktop-knowledge',
                replace: true
              }),
              apiClient.get('/api/portfolios?page=1&pageSize=100', {
                scope: 'desktop-knowledge-portfolios',
                replace: true
              })
            ]).then(function(result) {
              vm.knowledgeDocuments = result[0].items || [];
              vm.knowledgeSourceStatus = result[0].sourceStatus;
              vm.knowledgePortfolios = result[1].items || [];
              vm.knowledgeScopes = core.scopeOptions(
                vm.projects,
                vm.knowledgePortfolios,
                vm.session.currentUser && vm.session.currentUser.id,
                vm.projectRoles);
              if (vm.knowledgeDocument) {
                var current = vm.knowledgeDocuments.find(function(item) {
                  return item.id === vm.knowledgeDocument.id;
                });
                if (current) return vm.selectKnowledge(current);
              }
              return vm.knowledgeDocuments.length
                ? vm.selectKnowledge(vm.knowledgeDocuments[0])
                : vm.newKnowledge();
            }).catch(function(error) {
              if (error && error.canceled) return;
              vm.knowledgeError = apiActionError(error, 'Dokümanlar yüklenemedi.');
            }).finally(function() {
              vm.knowledgeBusy = false;
            });
          };

          vm.selectKnowledge = function(item) {
            if (!item) return $q.when();
            vm.knowledgeBusy = true;
            vm.knowledgeError = null;
            return apiClient.get('/api/knowledge-documents/' + item.id, {
              scope: 'desktop-knowledge-detail',
              replace: true
            }).then(function(document) {
              vm.knowledgeDocument = document;
              vm.knowledgeDraft = core.hydrate(document);
              vm.knowledgeBlocks = core.parseMarkdown(document.contentMarkdown);
              vm.knowledgePreviewVersion = null;
              vm.knowledgePanel = 'content';
              vm.knowledgeComment = '';
              return vm.loadKnowledgeLinkOptions();
            }).catch(function(error) {
              if (error && error.canceled) return;
              vm.knowledgeError = apiActionError(error, 'Doküman ayrıntısı yüklenemedi.');
            }).finally(function() {
              vm.knowledgeBusy = false;
            });
          };

          vm.canCreateKnowledge = function() {
            return vm.knowledgeScopes.length > 0;
          };

          vm.newKnowledge = function() {
            vm.knowledgeDocument = null;
            vm.knowledgeBlocks = [];
            vm.knowledgePreviewVersion = null;
            vm.knowledgeDraft = core.draft(vm.knowledgeScopes[0]);
            vm.knowledgePanel = 'edit';
            vm.knowledgeError = null;
            vm.knowledgeLinkOptions = { workItems: [], users: [], sourceStatus: 'Ready' };
            return vm.loadKnowledgeLinkOptions();
          };

          vm.knowledgeScopeChanged = function() {
            var scope = core.applyScope(vm.knowledgeDraft.scopeKey, vm.knowledgeScopes);
            if (scope) {
              vm.knowledgeDraft.scopeType = scope.type;
              vm.knowledgeDraft.scopeId = scope.id;
            }
            vm.knowledgeDraft.workItemIds = [];
            return vm.loadKnowledgeLinkOptions();
          };

          vm.loadKnowledgeLinkOptions = function() {
            var scope = vm.knowledgeDocument
              ? { type: vm.knowledgeDocument.scopeType, id: vm.knowledgeDocument.scopeId }
              : core.applyScope(vm.knowledgeDraft.scopeKey, vm.knowledgeScopes);
            if (!scope) return $q.when();
            return apiClient.get('/api/knowledge-documents/scope-link-options?scopeType='
              + encodeURIComponent(scope.type) + '&scopeId=' + encodeURIComponent(scope.id), {
              scope: 'desktop-knowledge-link-options',
              replace: true
            }).then(function(options) {
              vm.knowledgeLinkOptions = options;
            }).catch(function(error) {
              if (error && error.canceled) return;
              vm.knowledgeError = apiActionError(error, 'Bağlantı seçenekleri yüklenemedi.');
            });
          };

          vm.saveKnowledge = function() {
            var scope = vm.knowledgeDocument
              ? { type: vm.knowledgeDocument.scopeType, id: vm.knowledgeDocument.scopeId }
              : core.applyScope(vm.knowledgeDraft.scopeKey, vm.knowledgeScopes);
            var validation = core.validate(vm.knowledgeDraft, scope);
            if (validation) {
              vm.knowledgeError = validation;
              return $q.when();
            }
            vm.knowledgeBusy = true;
            var request = vm.knowledgeDocument
              ? apiClient.put(
                '/api/knowledge-documents/' + vm.knowledgeDocument.id,
                core.versionPayload(vm.knowledgeDraft))
              : apiClient.post(
                '/api/knowledge-documents',
                core.createPayload(vm.knowledgeDraft, scope));
            return request.then(function(saved) {
              vm.knowledgeNotice = vm.knowledgeDocument
                ? 'Yeni doküman sürümü kaydedildi.'
                : 'Doküman oluşturuldu.';
              vm.knowledgeDocument = saved;
              return vm.loadKnowledge();
            }).catch(function(error) {
              vm.knowledgeError = apiActionError(error, 'Doküman kaydedilemedi.');
            }).finally(function() {
              vm.knowledgeBusy = false;
            });
          };

          vm.previewKnowledgeVersion = function(version) {
            if (!vm.knowledgeDocument) return $q.when();
            vm.knowledgeBusy = true;
            return apiClient.get('/api/knowledge-documents/' + vm.knowledgeDocument.id
              + '/versions/' + version.number, {
              scope: 'desktop-knowledge-version',
              replace: true
            }).then(function(result) {
              vm.knowledgePreviewVersion = result;
              vm.knowledgeBlocks = core.parseMarkdown(result.contentMarkdown);
              vm.knowledgePanel = 'content';
            }).catch(function(error) {
              vm.knowledgeError = apiActionError(error, 'Sürüm yüklenemedi.');
            }).finally(function() {
              vm.knowledgeBusy = false;
            });
          };

          vm.showCurrentKnowledge = function() {
            if (!vm.knowledgeDocument) return;
            vm.knowledgePreviewVersion = null;
            vm.knowledgeBlocks = core.parseMarkdown(
              vm.knowledgeDocument.contentMarkdown);
            vm.knowledgePanel = 'content';
          };

          vm.addKnowledgeComment = function() {
            if (!vm.knowledgeDocument || !vm.knowledgeDocument.canComment
                || !String(vm.knowledgeComment || '').trim()) return $q.when();
            vm.knowledgeBusy = true;
            return apiClient.post('/api/knowledge-documents/'
              + vm.knowledgeDocument.id + '/comments', {
              body: String(vm.knowledgeComment).trim()
            }).then(function(saved) {
              vm.knowledgeDocument = saved;
              vm.knowledgeComment = '';
              vm.knowledgeNotice = 'Yorum eklendi.';
            }).catch(function(error) {
              vm.knowledgeError = apiActionError(error, 'Yorum eklenemedi.');
            }).finally(function() {
              vm.knowledgeBusy = false;
            });
          };

          vm.resolveKnowledgeComment = function(comment) {
            vm.knowledgeBusy = true;
            return apiClient.patch('/api/knowledge-documents/'
              + vm.knowledgeDocument.id + '/comments/' + comment.id + '/resolve', {})
              .then(function(saved) {
                vm.knowledgeDocument = saved;
                vm.knowledgeNotice = 'Yorum çözüldü.';
              }).catch(function(error) {
                vm.knowledgeError = apiActionError(error, 'Yorum çözülemedi.');
              }).finally(function() {
                vm.knowledgeBusy = false;
              });
          };

          vm.archiveKnowledge = function() {
            if (!vm.knowledgeDocument || !vm.knowledgeDocument.canEdit
                || !$window.confirm('Bu dokümanı arşivlemek istediğinize emin misiniz?')) {
              return $q.when();
            }
            var id = vm.knowledgeDocument.id;
            vm.knowledgeBusy = true;
            return apiClient.delete('/api/knowledge-documents/' + id)
              .then(function() {
                vm.knowledgeNotice = 'Doküman arşivlendi.';
                vm.knowledgeDocuments = vm.knowledgeDocuments.filter(function(item) {
                  return item.id !== id;
                });
                vm.knowledgeDocument = null;
                return vm.knowledgeDocuments.length
                  ? vm.selectKnowledge(vm.knowledgeDocuments[0])
                  : vm.newKnowledge();
              }).catch(function(error) {
                vm.knowledgeError = apiActionError(error, 'Doküman arşivlenemedi.');
              }).finally(function() {
                vm.knowledgeBusy = false;
              });
          };

          vm.knowledgeUserName = function(id) {
            var option = (vm.knowledgeLinkOptions.users || []).find(function(item) {
              return item.id === id;
            });
            return option ? option.label : vm.userName(id);
          };
          vm.knowledgeWorkItemName = function(id) {
            var option = (vm.knowledgeLinkOptions.workItems || []).find(function(item) {
              return item.id === id;
            });
            return option ? option.label : 'Erişilemeyen iş';
          };
        }
      };
    });
})();
