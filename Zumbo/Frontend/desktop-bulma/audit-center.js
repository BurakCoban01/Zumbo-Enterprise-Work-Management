(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopAuditFeature', function($q, $window, apiClient) {
      var core = $window.ZumboAuditPrivacyCore;
      var actionLabels = {
        UserRegistered: 'Kullanıcı kaydı',
        UserRolesChanged: 'Kullanıcı rolleri değişti',
        OrganizationCreated: 'Organizasyon oluşturuldu',
        OrganizationUpdated: 'Organizasyon güncellendi',
        ProjectCreated: 'Proje oluşturuldu',
        ProjectUpdated: 'Proje güncellendi',
        ProjectMemberAdded: 'Proje üyesi eklendi',
        ProjectMemberRemoved: 'Proje üyesi kaldırıldı',
        BoardCreated: 'Pano oluşturuldu',
        BoardUpdated: 'Pano güncellendi',
        TeamCreated: 'Ekip oluşturuldu',
        TeamUpdated: 'Ekip güncellendi',
        WorkItemCreated: 'İş oluşturuldu',
        WorkItemUpdated: 'İş güncellendi',
        WorkItemMoved: 'İş taşındı',
        WorkItemBulkJobCompleted: 'Toplu iş tamamlandı',
        WorkItemBulkJobArtifactsExpired: 'Toplu iş dosyaları silindi',
        AccountAnonymized: 'Hesap anonimleştirildi'
      };

      return {
        install: function(vm, apiActionError) {
          var capabilityRequest;
          vm.auditRoles = [];
          vm.auditCenter = {
            loading: false,
            loadingMore: false,
            exporting: false,
            integrityLoading: false,
            permissionResolved: false,
            error: '',
            items: [],
            nextCursor: null,
            selected: null,
            integrity: null,
            filters: {
              actorUserId: '',
              action: '',
              entityType: '',
              entityId: '',
              from: dateInput(30),
              to: dateInput(0)
            }
          };

          vm.canViewAuditCenter = function() {
            return core.hasPermission(vm.session.currentUser, vm.auditRoles, 'AuditReadAll');
          };

          vm.loadAuditCapabilities = function() {
            if (!vm.session.currentUser) return $q.when([]);
            if (capabilityRequest) return capabilityRequest;
            capabilityRequest = apiClient.get('/api/auth/roles').then(function(roles) {
              vm.auditRoles = roles || [];
              return vm.auditRoles;
            }).catch(function() {
              vm.auditRoles = [];
              return [];
            }).finally(function() {
              capabilityRequest = null;
              vm.auditCenter.permissionResolved = true;
            });
            return capabilityRequest;
          };

          vm.loadAuditCenter = function(reset) {
            return vm.loadAuditCapabilities().then(function() {
              if (!vm.canViewAuditCenter()) {
                vm.auditCenter.items = [];
                vm.auditCenter.selected = null;
                vm.auditCenter.error = '';
                return [];
              }
              if (reset) {
                vm.auditCenter.items = [];
                vm.auditCenter.nextCursor = null;
                vm.auditCenter.selected = null;
              }
              var loadingMore = !reset && !!vm.auditCenter.nextCursor;
              vm.auditCenter.loading = !loadingMore;
              vm.auditCenter.loadingMore = loadingMore;
              vm.auditCenter.error = '';
              var url;
              try {
                url = core.auditSearchUrl(vm.auditCenter.filters, {
                  organizationId: vm.session.currentUser.organizationId,
                  pageSize: 50,
                  cursor: loadingMore ? vm.auditCenter.nextCursor : null
                });
              } catch (error) {
                vm.auditCenter.error = error.message;
                vm.auditCenter.loading = false;
                vm.auditCenter.loadingMore = false;
                return [];
              }
              return apiClient.get('/api/audit' + url.slice('/api/audit'.length), {
                scope: 'desktop-audit-center',
                replace: !!reset
              })
                .then(function(page) {
                  var incoming = page.items || [];
                  vm.auditCenter.items = loadingMore
                    ? vm.auditCenter.items.concat(incoming)
                    : incoming;
                  vm.auditCenter.nextCursor = page.nextCursor || null;
                  if (!vm.auditCenter.selected && vm.auditCenter.items.length) {
                    vm.selectAuditEntry(vm.auditCenter.items[0]);
                  }
                  return vm.auditCenter.items;
                }).catch(function(error) {
                  vm.auditCenter.error = apiActionError(error, 'Denetim kayıtları yüklenemedi.');
                  return [];
                }).finally(function() {
                  vm.auditCenter.loading = false;
                  vm.auditCenter.loadingMore = false;
                });
            });
          };

          vm.searchAudit = function() {
            return vm.loadAuditCenter(true);
          };

          vm.loadMoreAudit = function() {
            if (!vm.auditCenter.nextCursor || vm.auditCenter.loadingMore) return $q.when([]);
            return vm.loadAuditCenter(false);
          };

          vm.clearAuditFilters = function() {
            vm.auditCenter.filters = {
              actorUserId: '',
              action: '',
              entityType: '',
              entityId: '',
              from: dateInput(30),
              to: dateInput(0)
            };
            return vm.loadAuditCenter(true);
          };

          vm.selectAuditEntry = function(entry) {
            vm.auditCenter.selected = entry || null;
            vm.auditCenter.selectedChanges = core.safeAuditChanges(entry);
          };

          vm.auditActionLabel = function(action) {
            return actionLabels[action] || String(action || 'Bilinmeyen olay')
              .replace(/([a-z0-9])([A-Z])/g, '$1 $2');
          };

          vm.auditEntityLabel = function(entry) {
            if (!entry) return '';
            if (entry.entityType === 'WorkItem') {
              var task = vm.tasks.find(function(item) { return item.id === entry.entityId; });
              if (task) return task.title;
            }
            if (entry.entityType === 'Project') {
              var project = vm.projects.find(function(item) { return item.id === entry.entityId; });
              if (project) return project.name;
            }
            if (entry.entityType === 'Identity') return vm.userName(entry.entityId);
            return entry.entityType + ' · ' + shortId(entry.entityId);
          };
          vm.auditReferenceLabel = shortId;

          vm.auditFieldLabel = function(field) {
            var labels = {
              value: 'Değer',
              title: 'Başlık',
              name: 'Ad',
              status: 'Durum',
              priority: 'Öncelik',
              assigneeUserId: 'Sorumlu',
              role: 'Rol'
            };
            return labels[field] || String(field || 'Değişiklik')
              .replace(/([a-z0-9])([A-Z])/g, '$1 $2');
          };

          vm.exportAudit = function() {
            if (!vm.canViewAuditCenter() || vm.auditCenter.exporting) return $q.when(null);
            var url;
            try {
              url = core.auditExportUrl(vm.auditCenter.filters, {
                organizationId: vm.session.currentUser.organizationId
              });
            } catch (error) {
              vm.auditCenter.error = error.message;
              return $q.when(null);
            }
            vm.auditCenter.exporting = true;
            vm.auditCenter.error = '';
            return apiClient.download('/api/audit/export'
              + url.slice('/api/audit/export'.length)).then(function(blob) {
              download(blob, 'zumbo-denetim-' + dateInput(0).toISOString().slice(0, 10) + '.ndjson');
              vm.notify('success', 'Filtrelenen denetim kaydı dışa aktarıldı.');
            }).catch(function(error) {
              vm.auditCenter.error = apiActionError(error, 'Denetim dışa aktarımı tamamlanamadı.');
            }).finally(function() { vm.auditCenter.exporting = false; });
          };

          vm.verifyAuditIntegrity = function() {
            if (!vm.canViewAuditCenter() || vm.auditCenter.integrityLoading) return $q.when(null);
            vm.auditCenter.integrityLoading = true;
            vm.auditCenter.error = '';
            return apiClient.get('/api/audit/integrity/'
              + encodeURIComponent(vm.session.currentUser.organizationId))
              .then(function(result) {
                vm.auditCenter.integrity = result;
                vm.auditCenter.integrityState = core.integrityState(result);
                return result;
              }).catch(function(error) {
                vm.auditCenter.error = apiActionError(error, 'Denetim bütünlüğü doğrulanamadı.');
              }).finally(function() { vm.auditCenter.integrityLoading = false; });
          };

          function download(blob, fileName) {
            var url = $window.URL.createObjectURL(blob);
            var link = $window.document.createElement('a');
            link.href = url;
            link.download = fileName;
            link.click();
            $window.URL.revokeObjectURL(url);
          }
        }
      };

      function shortId(value) {
        value = String(value || '');
        return value.length > 18 ? value.slice(0, 8) + '…' + value.slice(-6) : value;
      }

      function dateInput(daysAgo) {
        var date = new Date();
        date.setHours(0, 0, 0, 0);
        date.setDate(date.getDate() - daysAgo);
        return date;
      }
    });
})();
