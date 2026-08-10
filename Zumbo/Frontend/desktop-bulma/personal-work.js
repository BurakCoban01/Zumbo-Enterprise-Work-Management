(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopPersonalWorkFeature', function($q, apiClient) {
      function settled(request, project) {
        return request.then(
          function(data) { return { ok: true, data: data, project: project }; },
          function(error) { return { ok: false, error: error, project: project }; }
        );
      }

      function latestActivity(task) {
        var dates = [];
        if (task.completedAt) dates.push(task.completedAt);
        (task.statusHistory || []).forEach(function(item) { dates.push(item.changedAt); });
        (task.comments || []).forEach(function(item) { dates.push(item.editedAt || item.createdAt); });
        (task.workLogs || []).forEach(function(item) { dates.push(item.createdAt); });
        return dates.filter(Boolean).sort().reverse()[0] || task.dueDate || '';
      }

      function isDone(task) {
        return !!task.completedAt;
      }

      function isBlocked(task) {
        return (task.relations || []).some(function(relation) {
          return ['blockedby', 'isblockedby', 'dependson'].indexOf(String(relation.relationType || '').toLowerCase()) >= 0;
        });
      }

      function preference(storage, key, fallback) {
        var value = storage.getItem('zumbo.personal.' + key);
        return value || fallback;
      }

      return {
        install: function(vm, helpers) {
          var storage = window.localStorage;
          vm.personalTasks = [];
          vm.personalMode = preference(storage, 'mode', 'assigned');
          vm.personalSort = preference(storage, 'sort', 'urgency');
          vm.inboxMode = preference(storage, 'inboxMode', 'unread');
          vm.personalLoading = false;
          vm.personalError = null;
          vm.personalPartial = false;
          vm.personalPage = 1;
          vm.personalHasMore = false;
          vm.personalFreshAt = null;
          vm.savedPersonalViews = JSON.parse(storage.getItem('zumbo.personalViews') || '[]');
          vm.personalViewDraft = '';
          vm.notificationsLoading = false;
          vm.notificationsPage = 1;
          vm.notificationsHasMore = false;

          vm.sectionLabel = function(section) {
            return {
              home: 'Ana sayfa', mywork: 'İşlerim', inbox: 'Gelen kutusu', board: 'Pano', projects: 'Projeler',
              portfolios: 'Portföyler', goals: 'Hedefler', capacity: 'Kapasite', knowledge: 'Bilgi', teams: 'Ekipler', reports: 'Raporlar', audit: 'Denetim', archive: 'Arşiv', settings: 'Ayarlar'
            }[section] || section;
          };

          vm.loadPersonalWork = function(page, append) {
            if (!vm.session.currentUser) return $q.when();
            page = Number.isInteger(page) && page > 0 ? page : 1;
            append = append === true;
            var pageSize = 50;
            var projects = vm.projects.filter(helpers.membershipFor);
            vm.personalLoading = true;
            vm.personalError = null;
            return $q.all(projects.map(function(project) {
              return settled(apiClient.post('/api/work-items/search', {
                projectId: project.id,
                assigneeUserId: vm.session.currentUser.id,
                page: page,
                pageSize: pageSize
              }, { scope: 'desktop-personal-work:' + project.id, replace: true }), project);
            })).then(function(results) {
              var successful = results.filter(function(result) { return result.ok; });
              var next = successful.reduce(function(items, result) {
                return items.concat((result.data.items || []).map(function(task) {
                  return angular.extend({}, task, { projectName: result.project.name });
                }));
              }, []);
              vm.personalPartial = successful.length !== results.length;
              vm.personalHasMore = successful.some(function(result) {
                var count = result.data.totalCount == null ? (result.data.items || []).length : result.data.totalCount;
                return count > page * pageSize;
              });
              vm.personalPage = page;
              vm.personalTasks = append
                ? vm.personalTasks.concat(next.filter(function(task) {
                    return !vm.personalTasks.some(function(existing) { return existing.id === task.id; });
                  }))
                : next;
              vm.personalTasks.forEach(function(task) { task.personalActivityAt = latestActivity(task); });
              vm.personalFreshAt = new Date();
              if (!successful.length && projects.length) vm.personalError = 'Kişisel iş görünümü yüklenemedi.';
            }).finally(function() { vm.personalLoading = false; });
          };

          vm.personalAssigned = function() {
            return vm.personalTasks.filter(function(task) { return !isDone(task); });
          };
          vm.personalDue = function() {
            return vm.personalAssigned().filter(function(task) { return task.dueDate; }).sort(function(left, right) {
              return new Date(left.dueDate) - new Date(right.dueDate);
            });
          };
          vm.personalOverdue = function() {
            var now = Date.now();
            return vm.personalDue().filter(function(task) { return new Date(task.dueDate).getTime() < now; });
          };
          vm.personalBlocked = function() { return vm.personalAssigned().filter(isBlocked); };
          vm.personalRecent = function() {
            return vm.personalTasks.slice().sort(function(left, right) {
              return String(right.personalActivityAt).localeCompare(String(left.personalActivityAt));
            });
          };
          vm.personalList = function() {
            var items = vm.personalMode === 'due' ? vm.personalDue()
              : vm.personalMode === 'blocked' ? vm.personalBlocked()
                : vm.personalMode === 'recent' ? vm.personalRecent()
                  : vm.personalAssigned();
            if (vm.personalSort === 'project') {
              return items.slice().sort(function(left, right) {
                return String(left.projectName).localeCompare(String(right.projectName), 'tr');
              });
            }
            if (vm.personalSort === 'recent') return vm.personalRecent().filter(function(task) { return items.indexOf(task) >= 0; });
            return items.slice().sort(function(left, right) {
              var leftBlocked = isBlocked(left) ? 0 : 1;
              var rightBlocked = isBlocked(right) ? 0 : 1;
              if (leftBlocked !== rightBlocked) return leftBlocked - rightBlocked;
              var leftDue = left.dueDate ? new Date(left.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
              var rightDue = right.dueDate ? new Date(right.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
              return leftDue - rightDue;
            });
          };
          vm.personalTaskBlocked = isBlocked;
          vm.personalTaskOverdue = function(task) {
            return !isDone(task) && !!task.dueDate && new Date(task.dueDate).getTime() < Date.now();
          };
          vm.pendingApprovals = function() {
            return vm.personalTasks.filter(function(task) {
              return (task.approvals || []).some(function(approval) { return approval.status === 'Pending'; });
            });
          };
          vm.inboxNotifications = function() {
            if (vm.inboxMode === 'all') return vm.notifications;
            if (vm.inboxMode === 'actions') {
              return vm.notifications.filter(function(item) { return item.category === 'Action'; });
            }
            return vm.notifications.filter(function(item) { return !item.read; });
          };
          vm.notificationLabel = function(notification) {
            return {
              Mention: 'Bahsetme', Assignment: 'Atama', ApprovalRequest: 'Onay isteği',
              Approval: 'Onay sonucu', DueDateReminder: 'Tarih hatırlatması',
              TeamInvitation: 'Ekip daveti'
            }[notification.type] || 'Bildirim';
          };
          vm.setPersonalMode = function(mode) {
            vm.personalMode = mode;
            storage.setItem('zumbo.personal.mode', mode);
          };
          vm.setPersonalSort = function(sort) {
            vm.personalSort = sort;
            storage.setItem('zumbo.personal.sort', sort);
          };
          vm.setInboxMode = function(mode) {
            vm.inboxMode = mode;
            storage.setItem('zumbo.personal.inboxMode', mode);
          };
          vm.loadMorePersonalWork = function() {
            if (!vm.personalLoading && vm.personalHasMore) return vm.loadPersonalWork(vm.personalPage + 1, true);
          };
          vm.savePersonalView = function() {
            var name = String(vm.personalViewDraft || '').trim();
            if (!name) return;
            vm.savedPersonalViews = [{ id: String(Date.now()), name: name, mode: vm.personalMode }]
              .concat(vm.savedPersonalViews.filter(function(view) { return view.name !== name; })).slice(0, 8);
            storage.setItem('zumbo.personalViews', JSON.stringify(vm.savedPersonalViews));
            vm.personalViewDraft = '';
          };
          vm.applyPersonalView = function(view) { if (view) vm.personalMode = view.mode; };
          vm.removePersonalView = function(view) {
            vm.savedPersonalViews = vm.savedPersonalViews.filter(function(item) { return item.id !== view.id; });
            storage.setItem('zumbo.personalViews', JSON.stringify(vm.savedPersonalViews));
          };

          vm.loadNotifications = function(page, append) {
            if (!vm.session.currentUser || vm.notificationsLoading) return $q.when();
            page = Number.isInteger(page) && page > 0 ? page : 1;
            vm.notificationsLoading = true;
            return apiClient.get('/api/notifications?page=' + page + '&pageSize=50').then(function(notifications) {
              vm.notifications = append
                ? vm.notifications.concat(notifications.filter(function(item) {
                    return !vm.notifications.some(function(existing) { return existing.id === item.id; });
                  }))
                : notifications;
              vm.notificationsPage = page;
              vm.notificationsHasMore = notifications.length === 50;
              vm.unreadCount = vm.notifications.filter(function(notification) { return !notification.read; }).length;
            }).finally(function() { vm.notificationsLoading = false; });
          };
          vm.loadMoreNotifications = function() {
            if (vm.notificationsHasMore) return vm.loadNotifications(vm.notificationsPage + 1, true);
          };
          vm.readNotification = function(notification) {
            if (!notification || notification.read) return $q.when(notification);
            return apiClient.patch('/api/notifications/' + notification.id + '/read', {}).then(function() {
              notification.read = true;
              vm.unreadCount = Math.max(0, vm.unreadCount - 1);
              return notification;
            });
          };
          vm.readAllNotifications = function() {
            return $q.all(vm.notifications.filter(function(item) { return !item.read; }).map(vm.readNotification));
          };
          vm.openNotificationSource = function(notification) {
            var task = notification && notification.sourceId && vm.personalTasks.find(function(item) {
              return item.id === notification.sourceId;
            });
            if (notification && notification.actionKind === 'OpenWorkItem' && task) return vm.openPersonalTask(task);
            if (notification && notification.actionKind === 'OpenTeam' && notification.sourceId) {
              vm.showSection('teams');
              var team = (vm.teams || []).find(function(item) { return item.id === notification.sourceId; });
              if (team) return vm.selectTeam(team);
            }
            return $q.when(null);
          };
          vm.triageNotification = function(notification) {
            return vm.readNotification(notification).then(function() {
              return vm.openNotificationSource(notification);
            });
          };
          vm.openPersonalTask = function(task) {
            var project = vm.projects.find(function(item) { return item.id === task.projectId; });
            if (!project || !helpers.membershipFor(project)) return;
            return vm.selectProject(project, true).then(function() {
              var board = vm.boards.find(function(item) { return item.id === task.boardId; });
              return board && (!vm.board || vm.board.id !== board.id) ? vm.selectBoard(board, true) : null;
            }).then(function() { return vm.selectTask(task); });
          };
        }
      };
    });
})();
