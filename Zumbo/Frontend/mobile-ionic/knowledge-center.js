(function() {
  'use strict';

  angular.module('zumboMobile')
    .directive('mobileKnowledgeSegments', function() {
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
    .controller('KnowledgeController', function(
      $scope,
      $q,
      $window,
      apiClient,
      zumboApi,
      sessionStore,
      mobileActionError) {
      var vm = this;
      var core = $window.ZumboKnowledgeCore;
      vm.documents = [];
      vm.document = null;
      vm.projects = [];
      vm.portfolios = [];
      vm.scopes = [];
      vm.linkOptions = { workItems: [], users: [], sourceStatus: 'Ready' };
      vm.draft = core.draft();
      vm.blocks = [];
      vm.previewVersion = null;
      vm.mode = 'content';
      vm.query = '';
      vm.comment = '';

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        var query = vm.query ? '&query=' + encodeURIComponent(vm.query) : '';
        return $q.all([
          apiClient.get('/api/knowledge-documents?page=1&pageSize=100' + query, {
            scope: 'mobile-knowledge',
            replace: true
          }),
          apiClient.get('/api/portfolios?page=1&pageSize=100', {
            scope: 'mobile-knowledge-portfolios',
            replace: true
          }),
          zumboApi.projects()
        ]).then(function(result) {
          vm.documents = result[0].items || [];
          vm.sourceStatus = result[0].sourceStatus;
          vm.portfolios = result[1].items || [];
          vm.projects = result[2] || [];
          vm.scopes = core.scopeOptions(
            vm.projects,
            vm.portfolios,
            sessionStore.state.currentUser.id);
          if (vm.document) {
            var current = vm.documents.find(function(item) {
              return item.id === vm.document.id;
            });
            if (current) return vm.select(current);
          }
          return vm.documents.length ? vm.select(vm.documents[0]) : vm.newDocument();
        }).catch(function(error) {
          if (error && error.canceled) return;
          vm.error = mobileActionError(error, 'Dokümanlar yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
          $scope.$broadcast('scroll.refreshComplete');
        });
      };

      vm.select = function(item) {
        if (!item) return $q.when();
        vm.loading = true;
        return apiClient.get('/api/knowledge-documents/' + item.id, {
          scope: 'mobile-knowledge-detail',
          replace: true
        }).then(function(document) {
          vm.document = document;
          vm.draft = core.hydrate(document);
          vm.blocks = core.parseMarkdown(document.contentMarkdown);
          vm.previewVersion = null;
          vm.mode = 'content';
          vm.comment = '';
          return vm.loadLinkOptions();
        }).catch(function(error) {
          if (error && error.canceled) return;
          vm.error = mobileActionError(error, 'Doküman ayrıntısı yüklenemedi.');
        }).finally(function() {
          vm.loading = false;
        });
      };

      vm.canCreate = function() { return vm.scopes.length > 0; };

      vm.newDocument = function() {
        vm.document = null;
        vm.draft = core.draft(vm.scopes[0]);
        vm.blocks = [];
        vm.previewVersion = null;
        vm.mode = 'edit';
        vm.linkOptions = { workItems: [], users: [], sourceStatus: 'Ready' };
        return vm.loadLinkOptions();
      };

      vm.scopeChanged = function() {
        var scope = core.applyScope(vm.draft.scopeKey, vm.scopes);
        if (scope) {
          vm.draft.scopeType = scope.type;
          vm.draft.scopeId = scope.id;
        }
        vm.draft.workItemIds = [];
        return vm.loadLinkOptions();
      };

      vm.loadLinkOptions = function() {
        var scope = vm.document
          ? { type: vm.document.scopeType, id: vm.document.scopeId }
          : core.applyScope(vm.draft.scopeKey, vm.scopes);
        if (!scope) return $q.when();
        return apiClient.get('/api/knowledge-documents/scope-link-options?scopeType='
          + encodeURIComponent(scope.type) + '&scopeId=' + encodeURIComponent(scope.id), {
          scope: 'mobile-knowledge-links',
          replace: true
        }).then(function(options) {
          vm.linkOptions = options;
        }).catch(function(error) {
          if (error && error.canceled) return;
          vm.error = mobileActionError(error, 'Bağlantı seçenekleri yüklenemedi.');
        });
      };

      vm.save = function() {
        var scope = vm.document
          ? { type: vm.document.scopeType, id: vm.document.scopeId }
          : core.applyScope(vm.draft.scopeKey, vm.scopes);
        var validation = core.validate(vm.draft, scope);
        if (validation) { vm.error = validation; return; }
        vm.saving = true;
        var request = vm.document
          ? apiClient.put('/api/knowledge-documents/' + vm.document.id,
            core.versionPayload(vm.draft))
          : apiClient.post('/api/knowledge-documents',
            core.createPayload(vm.draft, scope));
        return request.then(function(saved) {
          vm.document = saved;
          vm.notice = vm.draft.id
            ? 'Yeni doküman sürümü kaydedildi.'
            : 'Doküman oluşturuldu.';
          return vm.load();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Doküman kaydedilemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.preview = function(version) {
        vm.loading = true;
        return apiClient.get('/api/knowledge-documents/' + vm.document.id
          + '/versions/' + version.number, {
          scope: 'mobile-knowledge-version',
          replace: true
        }).then(function(result) {
          vm.previewVersion = result;
          vm.blocks = core.parseMarkdown(result.contentMarkdown);
          vm.mode = 'content';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Sürüm yüklenemedi.');
        }).finally(function() { vm.loading = false; });
      };

      vm.showCurrent = function() {
        vm.previewVersion = null;
        vm.blocks = core.parseMarkdown(vm.document.contentMarkdown);
        vm.mode = 'content';
      };

      vm.addComment = function() {
        if (!String(vm.comment || '').trim()) return;
        vm.saving = true;
        return apiClient.post('/api/knowledge-documents/' + vm.document.id + '/comments', {
          body: String(vm.comment).trim()
        }).then(function(saved) {
          vm.document = saved;
          vm.comment = '';
          vm.notice = 'Yorum eklendi.';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Yorum eklenemedi.');
        }).finally(function() { vm.saving = false; });
      };

      vm.resolveComment = function(comment) {
        vm.saving = true;
        return apiClient.patch('/api/knowledge-documents/' + vm.document.id
          + '/comments/' + comment.id + '/resolve', {})
          .then(function(saved) {
            vm.document = saved;
            vm.notice = 'Yorum çözüldü.';
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Yorum çözülemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.archive = function() {
        if (!vm.document || !vm.document.canEdit
            || !$window.confirm('Bu dokümanı arşivlemek istediğinize emin misiniz?')) {
          return;
        }
        vm.saving = true;
        return apiClient.delete('/api/knowledge-documents/' + vm.document.id)
          .then(function() {
            vm.notice = 'Doküman arşivlendi.';
            vm.document = null;
            return vm.load();
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Doküman arşivlenemedi.');
          }).finally(function() { vm.saving = false; });
      };

      vm.userName = function(id) {
        var option = (vm.linkOptions.users || []).find(function(item) {
          return item.id === id;
        });
        return option ? option.label : 'Erişilemeyen kullanıcı';
      };
      vm.workItemName = function(id) {
        var option = (vm.linkOptions.workItems || []).find(function(item) {
          return item.id === id;
        });
        return option ? option.label : 'Erişilemeyen iş';
      };

      $scope.$on('zumbo:concurrency-conflict', vm.load);
      $scope.$on('$ionicView.beforeEnter', vm.load);
      vm.load();
    });
})();
