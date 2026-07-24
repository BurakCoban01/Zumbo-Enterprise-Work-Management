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
        return task.completedAt || ['done', 'completed', 'tamamlandı'].indexOf(String(task.status || '').toLocaleLowerCase('tr-TR')) >= 0;
      }

      function isBlocked(task) {
        if (String(task.status || '').toLocaleLowerCase('tr-TR').indexOf('block') >= 0) return true;
        return (task.relations || []).some(function(relation) {
          return ['blockedby', 'dependson'].indexOf(String(relation.relationType || '').toLowerCase()) >= 0;
        });
      }

      return {
        install: function(vm, helpers) {
          vm.personalTasks = [];
          vm.personalMode = 'assigned';
          vm.inboxMode = 'unread';
          vm.personalLoading = false;
          vm.personalError = null;
          vm.personalPartial = false;
          vm.personalPage = 1;
          vm.personalHasMore = false;
          vm.personalFreshAt = null;
          vm.savedPersonalViews = JSON.parse(window.localStorage.getItem('zumbo.personalViews') || '[]');
          vm.personalViewDraft = '';

          vm.sectionLabel = function(section) {
            return {
              home: 'Ana sayfa', mywork: 'İşlerim', inbox: 'Gelen kutusu', board: 'Pano', projects: 'Projeler',
              teams: 'Ekipler', reports: 'Raporlar', audit: 'Denetim', archive: 'Arşiv', settings: 'Ayarlar'
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
            if (vm.personalMode === 'due') return vm.personalDue();
            if (vm.personalMode === 'blocked') return vm.personalBlocked();
            if (vm.personalMode === 'recent') return vm.personalRecent();
            return vm.personalAssigned();
          };
          vm.pendingApprovals = function() {
            return vm.personalTasks.filter(function(task) {
              return (task.approvals || []).some(function(approval) { return approval.status === 'Pending'; });
            });
          };
          vm.inboxNotifications = function() {
            if (vm.inboxMode === 'all') return vm.notifications;
            if (vm.inboxMode === 'actions') {
              return vm.notifications.filter(function(item) { return /approval|mention|onay|bahset/i.test(item.type); });
            }
            return vm.notifications.filter(function(item) { return !item.read; });
          };
          vm.setPersonalMode = function(mode) { vm.personalMode = mode; };
          vm.setInboxMode = function(mode) { vm.inboxMode = mode; };
          vm.loadMorePersonalWork = function() {
            if (!vm.personalLoading && vm.personalHasMore) return vm.loadPersonalWork(vm.personalPage + 1, true);
          };
          vm.savePersonalView = function() {
            var name = String(vm.personalViewDraft || '').trim();
            if (!name) return;
            vm.savedPersonalViews = [{ id: String(Date.now()), name: name, mode: vm.personalMode }]
              .concat(vm.savedPersonalViews.filter(function(view) { return view.name !== name; })).slice(0, 8);
            window.localStorage.setItem('zumbo.personalViews', JSON.stringify(vm.savedPersonalViews));
            vm.personalViewDraft = '';
          };
          vm.applyPersonalView = function(view) { if (view) vm.personalMode = view.mode; };
          vm.removePersonalView = function(view) {
            vm.savedPersonalViews = vm.savedPersonalViews.filter(function(item) { return item.id !== view.id; });
            window.localStorage.setItem('zumbo.personalViews', JSON.stringify(vm.savedPersonalViews));
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
