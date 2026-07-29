(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopSettingsFeature', function($q, $window, apiClient) {
      var securityCore = $window.ZumboAccountSecurityCore;
      return {
        install: function(vm, apiActionError) {
    vm.sessions = [];
    vm.securityLoadError = '';
    vm.sessionActionId = null;
    vm.mfaRecoveryDraft = { password: '', code: '' };
    vm.clearSettingsOneTimeSecrets = function() {
      securityCore.clearOneTimeSecrets(vm);
    };

    vm.loadSettings = function() {
      if (!vm.session.currentUser) return $q.when();
      if (vm.entitySaving || vm.mfaSetup || vm.recoveryCodes.length) return $q.when();
      vm.settingsLoading = true;
      vm.securityLoadError = '';
      return $q.all([
        apiClient.get('/api/organizations').then(function(organizations) {
          vm.organizations = organizations;
          vm.organization = organizations.find(function(item) {
            return item.tenantKey === vm.session.currentUser.organizationId || item.id === vm.session.currentUser.organizationId;
          }) || organizations[0] || null;
          vm.organizationDraft = vm.organization
            ? { name: vm.organization.name, tenantKey: vm.organization.tenantKey }
            : { name: '', tenantKey: vm.session.currentUser.organizationId };
          return vm.loadOrganizationAudit();
        }).catch(function() { vm.organizations = []; vm.organization = null; }),
        apiClient.get('/api/auth/mfa').then(function(status) { vm.mfaStatus = status; }).catch(securityLoadFailed),
        apiClient.get('/api/auth/sessions').then(function(sessions) { vm.sessions = sessions; }).catch(function(error) {
          vm.sessions = [];
          securityLoadFailed(error);
        }),
        apiClient.get('/api/auth/api-keys').then(function(keys) { vm.apiKeys = keys; }).catch(function() { vm.apiKeys = []; }),
        apiClient.get('/api/notifications/preferences/me').then(function(preferences) {
          vm.notificationPreferences = preferences;
          vm.mutedTypesText = (preferences.mutedTypes || []).join(', ');
        }).catch(angular.noop),
        vm.loadPrivacyWorkflowStatus(),
        vm.loadIntegrationCapabilities(),
        vm.loadUsers(),
        vm.loadRoleAdministration()
      ]).finally(function() { vm.settingsLoading = false; });
    };

    function securityLoadFailed(error) {
      vm.securityLoadError = apiActionError(error, 'Güvenlik bilgileri yüklenemedi.');
    }

    function currentUserHasPermission(permission) {
      var currentRoles = (vm.session.currentUser && vm.session.currentUser.roles) || [];
      if (currentRoles.some(function(role) { return role === 'SystemAdmin'; })) return true;
      if (permission === 'UserRoleManage'
          && currentRoles.some(function(role) { return role === 'OrganizationAdmin'; })) return true;
      return vm.roles.some(function(role) {
        return currentRoles.indexOf(role.name) >= 0
          && (role.permissions || []).some(function(value) { return value === '*' || value === permission; });
      });
    }

    function refreshManagementCapabilities() {
      var currentRoles = (vm.session.currentUser && vm.session.currentUser.roles) || [];
      vm.isSystemAdmin = currentRoles.indexOf('SystemAdmin') >= 0;
      vm.canManageRoles = currentUserHasPermission('UserRoleManage');
      vm.canManageOrganization = vm.isSystemAdmin
        || currentRoles.indexOf('OrganizationAdmin') >= 0
        || currentUserHasPermission('OrganizationManage')
        || !!(vm.organization && vm.organization.ownerUserId === vm.session.currentUser.id);
      if (!vm.canManageRoles && vm.settingsTab === 'access') vm.settingsTab = 'account';
    }

    function prepareRoleAdministration() {
      vm.roles.forEach(function(role) {
        role.editName = role.name;
        role.editPermissions = (role.permissions || []).slice();
      });
      vm.userRoleDrafts = {};
      vm.users.forEach(function(user) { vm.userRoleDrafts[user.id] = (user.roles || []).slice(); });
      refreshManagementCapabilities();
    }

    vm.loadRoleAdministration = function() {
      if (!vm.session.currentUser) return $q.when([]);
      vm.roleAdminLoading = true;
      return apiClient.get('/api/auth/roles').then(function(roles) {
        vm.roles = roles;
        prepareRoleAdministration();
        return roles;
      }).catch(function() {
        vm.roles = [];
        refreshManagementCapabilities();
        return [];
      }).finally(function() { vm.roleAdminLoading = false; });
    };

    vm.permissionSelected = function(role, permission) {
      return ((role && role.permissions) || []).indexOf(permission) >= 0;
    };

    vm.togglePermission = function(role, permission) {
      var permissions = role.permissions || (role.permissions = []);
      var index = permissions.indexOf(permission);
      if (index >= 0) permissions.splice(index, 1);
      else permissions.push(permission);
    };

    vm.createRole = function() {
      if (!vm.canManageRoles || !vm.roleDraft.name || !vm.roleDraft.permissions.length || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/roles', {
        name: vm.roleDraft.name,
        organizationId: vm.session.currentUser.organizationId,
        permissions: vm.roleDraft.permissions
      }).then(function() {
        vm.roleDraft = { name: '', permissions: ['WorkItemView'] };
        vm.notify('success', 'Özel rol oluşturuldu.');
        return vm.loadRoleAdministration();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol oluşturulamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveRole = function(role) {
      if (!vm.canManageRoles || !role || role.isSystem || !role.editName || !role.editPermissions.length || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/auth/roles/' + role.id, {
        name: role.editName,
        permissions: role.editPermissions
      }).then(function() {
        vm.notify('success', 'Rol güncellendi.');
        return $q.all([vm.loadRoleAdministration(), vm.loadUsers()]).then(prepareRoleAdministration);
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.deleteRole = function(role) {
      if (!vm.canManageRoles || !role || role.isSystem || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/auth/roles/' + role.id).then(function() {
        vm.notify('success', 'Rol kaldırıldı.');
        return vm.loadRoleAdministration();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.userHasDraftRole = function(user, roleName) {
      return ((user && vm.userRoleDrafts[user.id]) || []).indexOf(roleName) >= 0;
    };

    vm.toggleUserRole = function(user, roleName) {
      if (!user || user.id === vm.session.currentUser.id) return;
      var roles = vm.userRoleDrafts[user.id] || (vm.userRoleDrafts[user.id] = ['User']);
      var index = roles.indexOf(roleName);
      if (index >= 0) roles.splice(index, 1);
      else roles.push(roleName);
      if (roles.indexOf('User') < 0) roles.unshift('User');
    };

    vm.canAssignRole = function(role) {
      if (!role || vm.isSystemAdmin) return true;
      return !role.isSystem || ['SystemAdmin', 'OrganizationAdmin', 'AuditReader'].indexOf(role.name) < 0;
    };

    vm.saveUserRoles = function(user) {
      if (!vm.canManageRoles || !user || user.id === vm.session.currentUser.id || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/auth/users/' + user.id + '/roles', {
        roles: vm.userRoleDrafts[user.id] || ['User']
      }).then(function(updated) {
        var index = vm.users.findIndex(function(item) { return item.id === updated.id; });
        if (index >= 0) vm.users[index] = updated;
        vm.userRoleDrafts[updated.id] = (updated.roles || []).slice();
        vm.notify('success', 'Kullanıcı rolleri güncellendi.');
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Kullanıcı rolleri güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.loadOrganizationAudit = function() {
      if (!vm.organization) return $q.when([]);
      return apiClient.get('/api/audit/entity/Organization/' + vm.organization.id)
        .then(function(audit) { vm.organizationAudit = audit; refreshManagementCapabilities(); return audit; })
        .catch(function() { vm.organizationAudit = []; return []; });
    };

    vm.saveOrganization = function() {
      if (!vm.organizationDraft.name || vm.entitySaving) return;
      vm.entitySaving = true;
      var request = vm.organization
        ? apiClient.put('/api/organizations/' + vm.organization.id, { name: vm.organizationDraft.name })
        : apiClient.post('/api/organizations', vm.organizationDraft);
      return request.then(function(organization) {
        vm.organization = organization;
        vm.organizationDraft = { name: organization.name, tenantKey: organization.tenantKey };
        vm.notify('success', 'Organizasyon kaydedildi.');
        return vm.loadOrganizationAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Organizasyon kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.addDepartment = function() {
      if (!vm.organization || !vm.departmentDraft.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/organizations/' + vm.organization.id + '/departments', vm.departmentDraft)
        .then(function(organization) {
          vm.organization = organization;
          vm.departmentDraft = { name: '', parentDepartmentId: null };
          vm.notify('success', 'Departman eklendi.');
          return vm.loadOrganizationAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Departman eklenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveDepartment = function(department) {
      if (!vm.organization || !department || !department.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/organizations/' + vm.organization.id + '/departments/' + department.id, {
        name: department.name,
        parentDepartmentId: department.parentDepartmentId || null
      }).then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman kaydedildi.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.deleteDepartment = function(department) {
      if (!vm.organization || !department || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/organizations/' + vm.organization.id + '/departments/' + department.id)
        .then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman kaldırıldı.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.assignDepartmentMember = function() {
      var draft = vm.departmentMemberDraft;
      if (!vm.organization || !draft.departmentId || !draft.userId || !draft.position || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/organizations/' + vm.organization.id + '/departments/' + draft.departmentId + '/members', {
        userId: draft.userId,
        position: draft.position
      }).then(function(organization) {
        vm.organization = organization;
        vm.departmentMemberDraft = { departmentId: '', userId: '', position: '' };
        vm.notify('success', 'Departman üyesi atandı.');
        return vm.loadOrganizationAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Departman üyesi atanamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeDepartmentMember = function(department, member) {
      if (!vm.organization || !department || !member || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/organizations/' + vm.organization.id + '/departments/' + department.id + '/members/' + member.userId)
        .then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman üyesi kaldırıldı.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman üyesi kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.changePassword = function() {
      if (!vm.passwordDraft.currentPassword || !vm.passwordDraft.newPassword || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/change-password', vm.passwordDraft)
        .then(function() { vm.passwordDraft = { currentPassword: '', newPassword: '' }; vm.notify('success', 'Parola değiştirildi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Parola değiştirilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.beginMfaSetup = function() {
      if (!vm.mfaDraft.password || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/mfa/setup', { password: vm.mfaDraft.password })
        .then(function(setup) { vm.mfaSetup = setup; vm.notify('success', 'MFA sırrı oluşturuldu.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'MFA kurulumu başlatılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.confirmMfaSetup = function() {
      if (!vm.mfaDraft.code || vm.entitySaving) return;
      vm.entitySaving = true;
      apiClient.cancelPending('mfa-confirm-session-rotation');
      return apiClient.post('/api/auth/mfa/confirm', { code: vm.mfaDraft.code })
        .then(function(result) {
          vm.mfaStatus = { enabled: result.enabled, remainingRecoveryCodes: result.recoveryCodes.length };
          vm.recoveryCodes = result.recoveryCodes;
          vm.mfaSetup = null;
          vm.mfaDraft = { password: '', code: '' };
          vm.notify('success', 'MFA etkinleştirildi; kurtarma kodlarını güvenli yerde saklayın.');
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'MFA doğrulanamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.disableMfa = function() {
      if (!vm.mfaDraft.password || !vm.mfaDraft.code || vm.entitySaving) return;
      vm.entitySaving = true;
      apiClient.cancelPending('mfa-disable-session-rotation');
      return apiClient.post('/api/auth/mfa/disable', { password: vm.mfaDraft.password, code: vm.mfaDraft.code })
        .then(function(status) { vm.mfaStatus = status; vm.mfaDraft = { password: '', code: '' }; vm.notify('success', 'MFA devre dışı bırakıldı.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'MFA devre dışı bırakılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.regenerateMfaRecoveryCodes = function() {
      if (!vm.mfaRecoveryDraft.password || !vm.mfaRecoveryDraft.code || vm.entitySaving) return;
      if (!$window.confirm('Mevcut kurtarma kodları hemen geçersiz olacak. Yeni kodlar oluşturulsun mu?')) return;
      vm.entitySaving = true;
      vm.recoveryCodes = [];
      apiClient.cancelPending('mfa-recovery-session-rotation');
      return apiClient.post('/api/auth/mfa/recovery-codes', vm.mfaRecoveryDraft)
        .then(function(result) {
          vm.recoveryCodes = result.recoveryCodes || [];
          vm.mfaStatus.remainingRecoveryCodes = vm.recoveryCodes.length;
          vm.mfaRecoveryDraft = { password: '', code: '' };
          vm.notify('success', 'Yeni kurtarma kodları oluşturuldu; bu kodlar yeniden gösterilmez.');
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Kurtarma kodları yenilenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.dismissRecoveryCodes = function() {
      vm.recoveryCodes = [];
    };

    vm.sessionIsActive = function(session) {
      return securityCore.isSessionActive(session);
    };

    vm.visibleSessions = function() {
      return securityCore.selectVisibleSessions(vm.sessions);
    };

    vm.revokeSession = function(session) {
      if (!session || !vm.sessionIsActive(session) || vm.sessionActionId) return;
      var prompt = session.isCurrent
        ? 'Bu cihazdaki oturum kapatılacak. Devam edilsin mi?'
        : (session.deviceName || 'Bu cihaz') + ' oturumu kapatılsın mı?';
      if (!$window.confirm(prompt)) return;
      vm.sessionActionId = session.id;
      return apiClient.delete('/api/auth/sessions/' + session.id)
        .then(function() {
          session.revokedAt = new Date().toISOString();
          if (session.isCurrent) {
            apiClient.clearSession('current-session-revoked');
            vm.project = null;
            vm.board = null;
            vm.tasks = [];
            return;
          }
          vm.notify('success', 'Seçilen cihaz oturumu kapatıldı.');
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Oturum kapatılamadı.')); })
        .finally(function() { vm.sessionActionId = null; });
    };

    vm.createApiKey = function() {
      if (!vm.apiKeyDraft.name || !vm.apiKeyDraft.password || vm.entitySaving) return;
      vm.entitySaving = true;
      var expiresAt = new Date();
      expiresAt.setDate(expiresAt.getDate() + Number(vm.apiKeyDraft.expiresInDays || 90));
      return apiClient.post('/api/auth/api-keys', {
        name: vm.apiKeyDraft.name,
        password: vm.apiKeyDraft.password,
        mfaCode: vm.apiKeyDraft.mfaCode || null,
        expiresAt: expiresAt.toISOString(),
        scopes: ['api:full']
      }).then(function(created) {
        vm.createdApiKey = created;
        vm.apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 };
        return apiClient.get('/api/auth/api-keys').then(function(keys) { vm.apiKeys = keys; });
      }).then(function() { vm.notify('success', 'API anahtarı oluşturuldu; tam değer yalnızca bir kez gösterilir.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'API anahtarı oluşturulamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.revokeApiKey = function(key) {
      if (!key || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/auth/api-keys/' + key.id)
        .then(function() { return apiClient.get('/api/auth/api-keys'); })
        .then(function(keys) {
          vm.apiKeys = keys;
          vm.createdApiKey = null;
          vm.notify('success', 'API anahtarı iptal edildi.');
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'API anahtarı iptal edilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.copyText = function(value) {
      if (!value || !$window.navigator.clipboard) return;
      return $window.navigator.clipboard.writeText(value).then(function() { vm.notify('success', 'Değer panoya kopyalandı.'); });
    };

    vm.saveNotificationPreferences = function() {
      if (vm.entitySaving) return;
      vm.entitySaving = true;
      var mutedTypes = securityCore.normalizeMutedTypes(vm.mutedTypesText);
      return apiClient.put('/api/notifications/preferences/me', {
        inAppEnabled: vm.notificationPreferences.inAppEnabled,
        emailEnabled: vm.notificationPreferences.emailEnabled,
        mutedTypes: mutedTypes
      }).then(function(preferences) { vm.notificationPreferences = preferences; vm.notify('success', 'Bildirim tercihleri kaydedildi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Bildirim tercihleri kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

        }
      };
    });
})();
