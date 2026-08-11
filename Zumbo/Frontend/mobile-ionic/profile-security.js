(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('ProfileSecurityController', function(
    $scope,
    $state,
    $q,
    $window,
    $ionicPopup,
    apiClient,
    sessionStore,
    zumboApi,
    mobileActionError,
    mobilePrivacyFeature
  ) {
    var vm = this;
    var securityCore = $window.ZumboAccountSecurityCore;
    var webhookCore = $window.ZumboWebhookCore;
    var operationsCore = $window.ZumboOperationsCore;
    vm.loading = true;
    vm.error = '';
    vm.busy = '';
    vm.sessions = [];
    vm.preferences = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    vm.mutedTypesText = '';
    vm.mfaStatus = { enabled: false, remainingRecoveryCodes: 0 };
    vm.mfaDraft = { password: '', code: '' };
    vm.recoveryDraft = { password: '', code: '' };
    vm.mfaSetup = null;
    vm.recoveryCodes = [];
    vm.integrationRoles = [];
    vm.canManageIntegrations = function() {
      return webhookCore.hasPermission(sessionStore.state.currentUser, vm.integrationRoles);
    };
    vm.canManageOperations = function() {
      return operationsCore.hasPermission(sessionStore.state.currentUser, vm.integrationRoles);
    };
    mobilePrivacyFeature.install(vm);

    vm.load = function() {
      vm.loading = true;
      vm.error = '';
      securityCore.clearOneTimeSecrets(vm);
      return $q.all([
        zumboApi.notificationPreferences().then(function(preferences) {
          vm.preferences = preferences;
          vm.mutedTypesText = (preferences.mutedTypes || []).join(', ');
        }),
        zumboApi.mfaStatus().then(function(status) { vm.mfaStatus = status; }),
        zumboApi.sessions().then(function(sessions) { vm.sessions = sessions; }),
        zumboApi.roles().then(function(roles) { vm.integrationRoles = roles || []; })
          .catch(function() { vm.integrationRoles = []; }),
        vm.loadPrivacyWorkflow()
      ]).catch(function(error) {
        vm.error = mobileActionError(error, 'Güvenlik bilgileri yüklenemedi.');
      }).finally(function() {
        vm.loading = false;
        $scope.$broadcast('scroll.refreshComplete');
      });
    };

    vm.savePreferences = function() {
      if (vm.busy) return;
      vm.busy = 'preferences';
      return zumboApi.saveNotificationPreferences({
        inAppEnabled: vm.preferences.inAppEnabled,
        emailEnabled: vm.preferences.emailEnabled,
        mutedTypes: securityCore.normalizeMutedTypes(vm.mutedTypesText)
      }).then(function(preferences) {
        vm.preferences = preferences;
        vm.mutedTypesText = (preferences.mutedTypes || []).join(', ');
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Bildirim tercihleri kaydedilemedi.');
      }).finally(function() { vm.busy = ''; });
    };

    vm.beginMfaSetup = function() {
      if (!vm.mfaDraft.password || vm.busy) return;
      vm.busy = 'mfa';
      return zumboApi.beginMfaSetup(vm.mfaDraft.password).then(function(setup) {
        vm.mfaSetup = setup;
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'İki adımlı doğrulama başlatılamadı.');
      }).finally(function() { vm.busy = ''; });
    };

    vm.confirmMfaSetup = function() {
      if (!vm.mfaDraft.code || vm.busy) return;
      vm.busy = 'mfa';
      return zumboApi.confirmMfaSetup(vm.mfaDraft.code).then(function(result) {
        vm.mfaStatus = { enabled: result.enabled, remainingRecoveryCodes: result.recoveryCodes.length };
        vm.recoveryCodes = result.recoveryCodes;
        vm.mfaSetup = null;
        vm.mfaDraft = { password: '', code: '' };
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Doğrulama kodu kabul edilmedi.');
      }).finally(function() { vm.busy = ''; });
    };

    vm.regenerateRecoveryCodes = function() {
      if (!vm.recoveryDraft.password || !vm.recoveryDraft.code || vm.busy) return;
      return $ionicPopup.confirm({
        title: 'Kurtarma kodlarını yenile',
        template: 'Mevcut kurtarma kodları hemen geçersiz olacak.',
        cancelText: 'Vazgeç',
        okText: 'Yenile'
      }).then(function(confirmed) {
        if (!confirmed) return;
        vm.busy = 'recovery';
        vm.recoveryCodes = [];
        return zumboApi.regenerateMfaRecoveryCodes(vm.recoveryDraft).then(function(result) {
          vm.recoveryCodes = result.recoveryCodes || [];
          vm.mfaStatus.remainingRecoveryCodes = vm.recoveryCodes.length;
          vm.recoveryDraft = { password: '', code: '' };
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Kurtarma kodları yenilenemedi.');
        }).finally(function() { vm.busy = ''; });
      });
    };

    vm.disableMfa = function() {
      if (!vm.mfaDraft.password || !vm.mfaDraft.code || vm.busy) return;
      return $ionicPopup.confirm({
        title: 'İki adımlı doğrulamayı kapat',
        template: 'Hesabın yalnız parolayla korunacak.',
        cancelText: 'Vazgeç',
        okText: 'Kapat'
      }).then(function(confirmed) {
        if (!confirmed) return;
        vm.busy = 'mfa';
        return zumboApi.disableMfa(vm.mfaDraft).then(function(status) {
          vm.mfaStatus = status;
          vm.mfaDraft = { password: '', code: '' };
          vm.recoveryCodes = [];
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'İki adımlı doğrulama kapatılamadı.');
        }).finally(function() { vm.busy = ''; });
      });
    };

    vm.sessionIsActive = function(session) {
      return securityCore.isSessionActive(session);
    };

    vm.visibleSessions = function() {
      return securityCore.selectVisibleSessions(vm.sessions);
    };

    vm.revokeSession = function(session) {
      if (!session || !vm.sessionIsActive(session) || vm.busy) return;
      return $ionicPopup.confirm({
        title: session.isCurrent ? 'Bu cihazdan çık' : 'Oturumu kapat',
        template: (session.deviceName || 'Seçilen cihaz') + ' için oturum hemen kapatılacak.',
        cancelText: 'Vazgeç',
        okText: 'Oturumu kapat'
      }).then(function(confirmed) {
        if (!confirmed) return;
        vm.busy = session.id;
        return zumboApi.revokeSession(session.id).then(function() {
          session.revokedAt = new Date().toISOString();
          if (!session.isCurrent) return;
          apiClient.clearSession('current-session-revoked');
          sessionStore.clear();
          $state.go('login');
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Oturum kapatılamadı.');
        }).finally(function() { vm.busy = ''; });
      });
    };

    vm.copyRecoveryCodes = function() {
      if (!vm.recoveryCodes.length || !$window.navigator.clipboard) return;
      return $window.navigator.clipboard.writeText(vm.recoveryCodes.join('\n'));
    };

    vm.dismissRecoveryCodes = function() {
      vm.recoveryCodes = [];
    };

    $scope.$on('$ionicView.afterLeave', vm.stopPrivacyPolling);
    $scope.$on('$destroy', vm.stopPrivacyPolling);
    $scope.$on('$ionicView.beforeEnter', vm.load);
  });
})();
