(function() {
  'use strict';


function createDemoPassword() {
  var bytes = new Uint8Array(18);
  window.crypto.getRandomValues(bytes);
  return 'Z1!' + Array.prototype.map.call(bytes, function(value) {
    return ('0' + value.toString(16)).slice(-2);
  }).join('');
}

  angular.module('zumboMobile')
  .factory('authService', function($q, apiClient, tokenStorage, sessionStore) {
    var restorePromise = null;
    function accept(auth) {
      tokenStorage.setCsrf(auth.csrfToken);
      sessionStore.setUser(auth.user);
      return $q.all([
        apiClient.get('/api/auth/roles'),
        apiClient.get('/api/auth/roles?scope=Project')
      ]).then(function(result) {
        sessionStore.state.systemRoles = result[0] || [];
        sessionStore.state.projectRoles = result[1] || [];
        return auth;
      });
    }
    function restore() {
      if (!restorePromise) {
        restorePromise = apiClient.get('/api/browser-auth/session').then(accept)
          .catch(function(error) {
            restorePromise = null;
            return $q.reject(error);
          });
      }
      return restorePromise;
    }
    return {
      login: function(form) { return apiClient.post('/api/browser-auth/login', form).then(accept); },
      restore: restore,
      forgotPassword: function(email) {
        return apiClient.post('/api/auth/forgot-password', { email: email });
      },
      resetPassword: function(token, newPassword) {
        return apiClient.post('/api/auth/reset-password', { token: token, newPassword: newPassword });
      },
      logout: function() {
        return apiClient.post('/api/browser-auth/logout', { allSessions: false })
          .finally(function() {
            restorePromise = null;
            apiClient.clearSession('logout');
          });
      },
      registerDemo: function() {
        var suffix = Date.now();
        return apiClient.post('/api/browser-auth/register', {
          username: 'demo-user' + suffix,
          email: 'demo-user' + suffix + '@zumbo.local',
          password: createDemoPassword(),
          organizationId: 'mobile-demo-' + suffix
        }).then(accept);
      }
    };
  })
  .controller('ShellController', function($state, authService, sessionStore, realtimeService, mobilePwaService, displayNameResolver) {
    var vm = this;
    vm.session = sessionStore.state;
    vm.pwa = mobilePwaService.state;
    mobilePwaService.start();
    vm.theme = window.localStorage.getItem('zumbo.mobileTheme') || 'light';
    vm.sessionRestoring = true;
    vm.organizationName = function() {
      var id = vm.session.currentUser && vm.session.currentUser.organizationId;
      return displayNameResolver.organization(id, [], null);
    };
    if ($state.current.name === 'public-intake') vm.sessionRestoring = false;
    else authService.restore().then(function() {
        if ($state.current.name === 'login') $state.go('app.dashboard');
      }).catch(angular.noop).finally(function() {
        vm.sessionRestoring = false;
      });
    vm.toggleTheme = function() {
      vm.theme = vm.theme === 'dark' ? 'light' : 'dark';
      window.localStorage.setItem('zumbo.mobileTheme', vm.theme);
    };
    vm.applyUpdate = mobilePwaService.applyUpdate;
    vm.logout = function() {
      realtimeService.stop();
      authService.logout().finally(function() {
        sessionStore.clear();
        $state.go('login');
      });
    };
  })
  .controller('LoginController', function($state, authService, zumboApi, sessionStore, mobilePwaService) {
    var vm = this;
    vm.form = { usernameOrEmail: '', password: '', mfaCode: '' };
    vm.login = function() {
      vm.error = null;
      if (mobilePwaService.state.offline) {
        vm.error = 'Çevrimdışıyken giriş yapılamaz.';
        return;
      }
      authService.login(vm.form).then(function() { $state.go('app.dashboard'); }).catch(function(error) {
        var code = error.data && error.data.error && error.data.error.code;
        vm.mfaRequired = code === 'MFA_REQUIRED' || code === 'MFA_INVALID';
        vm.error = vm.mfaRequired ? 'Doğrulama kodunu kontrol edin.' : 'Giriş başarısız.';
      });
    };
    vm.demo = function() {
      vm.error = null;
      if (mobilePwaService.state.offline) {
        vm.error = 'Çevrimdışıyken demo çalışma alanı oluşturulamaz.';
        return;
      }
      vm.demoPending = true;
      authService.registerDemo()
        .then(function() { return zumboApi.createOrganization(); })
        .then(function() { return zumboApi.createProject(); })
        .then(function(project) {
          sessionStore.state.project = project;
          return zumboApi.createBoard(project.id).then(function(board) {
            sessionStore.state.board = board;
            return zumboApi.createTask(project.id, board.id);
          });
        })
        .then(function() { $state.go('app.dashboard'); })
        .catch(function() { vm.error = 'Demo çalışma alanı oluşturulamadı.'; })
        .finally(function() { vm.demoPending = false; });
    };
  })
  .controller('ForgotPasswordController', function($state, authService, mobilePwaService) {
    var vm = this;
    vm.email = '';
    vm.submit = function() {
      vm.error = null;
      if (mobilePwaService.state.offline) {
        vm.error = 'Çevrimdışıyken sıfırlama bağlantısı gönderilemez.';
        return;
      }
      vm.pending = true;
      authService.forgotPassword(vm.email).then(function() {
        vm.sent = true;
      }).catch(function() {
        vm.error = 'İstek gönderilemedi.';
      }).finally(function() {
        vm.pending = false;
      });
    };
    vm.back = function() { $state.go('login'); };
  })
  .controller('ResetPasswordController', function($state, $stateParams, authService, mobilePwaService) {
    var vm = this;
    vm.form = { newPassword: '', confirmPassword: '' };
    vm.submit = function() {
      vm.error = null;
      if (mobilePwaService.state.offline) {
        vm.error = 'Çevrimdışıyken parola değiştirilemez.';
        return;
      }
      if (!vm.form.newPassword || vm.form.newPassword !== vm.form.confirmPassword) {
        vm.error = 'Parolalar eşleşmiyor.';
        return;
      }
      vm.pending = true;
      authService.resetPassword($stateParams.token, vm.form.newPassword).then(function() {
        vm.reset = true;
      }).catch(function() {
        vm.error = 'Bağlantı geçersiz veya süresi dolmuş.';
      }).finally(function() {
        vm.pending = false;
      });
    };
    vm.back = function() { $state.go('login'); };
  });
})();
