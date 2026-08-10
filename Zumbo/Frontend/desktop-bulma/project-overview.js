(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopProjectOverviewFeature', function($timeout) {
      var views = [
        view('overview', 'Genel bakış', 'layout-dashboard', 'board', false, 'primary'),
        view('board', 'Pano', 'kanban', 'board', true, 'primary'),
        view('list', 'Liste', 'list', 'board', true, 'primary'),
        view('backlog', 'Backlog', 'inbox', 'board', true, 'primary'),
        view('sprint', 'Sprint', 'timer', 'board', true, 'primary'),
        view('calendar', 'Takvim', 'calendar-days', 'board', true, 'plan'),
        view('timeline', 'Zaman çizelgesi', 'chart-gantt', 'board', true, 'plan'),
        view('roadmap', 'Yol haritası', 'route', 'board', true, 'plan'),
        view('catalog', 'Teslimat', 'package-open', 'board', false, 'plan'),
        view('intake', 'Intake', 'clipboard-list', 'board', false, 'operate'),
        view('automation', 'Otomasyon', 'repeat-2', 'board', false, 'operate'),
        view('jobs', 'İş merkezi', 'database-zap', 'board', false, 'operate'),
        view('workload', 'İş yükü', 'users', 'reports', false, 'insights'),
        view('reports', 'Raporlar', 'chart-no-axes-combined', 'reports', false, 'insights'),
        view('dashboards', 'Dashboardlar', 'panels-top-left', 'reports', false, 'insights')
      ];

      function view(id, label, icon, section, requiresBoard, group) {
        return { id: id, label: label, icon: icon, section: section, requiresBoard: requiresBoard === true, group: group };
      }

      function firstByDate(items, field) {
        return items.slice().sort(function(left, right) {
          var leftTime = left[field] ? new Date(left[field]).getTime() : Number.MAX_SAFE_INTEGER;
          var rightTime = right[field] ? new Date(right[field]).getTime() : Number.MAX_SAFE_INTEGER;
          return leftTime - rightTime;
        })[0] || null;
      }

      function readPreference(key) {
        return window.localStorage ? window.localStorage.getItem(key) : null;
      }

      function writePreference(key, value) {
        if (window.localStorage) window.localStorage.setItem(key, value);
      }

      return {
        install: function(vm, helpers) {
          var updateLocation = helpers.updateLocation;
          var secondaryGroupsCacheKey = '';
          var secondaryGroupsCache = [];
          vm.projectViews = views;
          vm.projectMoreOpen = false;

          vm.projectViewAvailable = function(candidate) {
            return !!candidate && !!vm.project && !!vm.projectMembership
              && (!candidate.requiresBoard || !!vm.board);
          };
          vm.availableProjectViews = function() {
            return vm.projectViews.filter(vm.projectViewAvailable);
          };
          vm.primaryProjectViews = function() {
            return vm.availableProjectViews().filter(function(candidate) { return candidate.group === 'primary'; });
          };
          vm.secondaryProjectViewGroups = function() {
            var available = vm.availableProjectViews();
            var cacheKey = available.map(function(candidate) { return candidate.id; }).join('|');
            if (cacheKey === secondaryGroupsCacheKey) return secondaryGroupsCache;
            secondaryGroupsCacheKey = cacheKey;
            secondaryGroupsCache = [
              { id: 'plan', label: 'Planlama' },
              { id: 'operate', label: 'Operasyon' },
              { id: 'insights', label: 'İçgörüler' }
            ].map(function(group) {
              return {
                id: group.id,
                label: group.label,
                views: available.filter(function(candidate) { return candidate.group === group.id; })
              };
            }).filter(function(group) { return group.views.length; });
            return secondaryGroupsCache;
          };
          vm.currentProjectView = function() {
            return vm.projectViews.find(function(candidate) { return candidate.id === vm.workMode; }) || vm.projectViews[0];
          };

          vm.setProjectView = function(viewId, skipLocation) {
            var requested = vm.projectViews.find(function(candidate) { return candidate.id === viewId; });
            var target = vm.projectViewAvailable(requested) ? requested : vm.availableProjectViews()[0];
            if (!target) {
              vm.activeSection = 'projects';
              if (!skipLocation) updateLocation('projects', null, true);
              return null;
            }
            vm.workMode = target.id;
            vm.activeSection = target.section;
            vm.projectMoreOpen = false;
            if (vm.project) writePreference('zumbo.projectView.' + vm.project.id, target.id);
            vm.clearSelection();
            vm.selectedTask = null;
            vm.taskDraft = null;
            vm.rebuildAdvancedViews();
            if (target.id === 'overview') vm.loadTimeline();
            if (target.id === 'intake' && vm.loadIntake) vm.loadIntake();
            if (target.id === 'automation' && vm.loadWorkAutomation) vm.loadWorkAutomation();
            if (target.id === 'jobs' && vm.loadBulkJobs) vm.loadBulkJobs();
            if (vm.isPlanningView && vm.isPlanningView(target.id)) vm.preparePlanningView();
            if (vm.isReportingView && vm.isReportingView(target.id)) vm.prepareReportingView();
            if (!skipLocation) updateLocation(target.section, null, true);
            return target;
          };
          vm.setWorkMode = vm.setProjectView;

          vm.syncProjectViewContext = function(reloadTasks) {
            updateLocation(vm.activeSection, null, false);
            if (reloadTasks) return vm.loadTasks();
            vm.refreshBoardModel();
          };

          vm.applyProjectViewLocation = function(params) {
            var priority = params.get('priority') || '';
            var search = params.get('query') || '';
            var searchChanged = vm.search !== search;
            vm.priorityFilter = ['', 'Critical', 'High', 'Medium', 'Low'].indexOf(priority) >= 0 ? priority : '';
            vm.search = search;
            var remembered = vm.project && readPreference('zumbo.projectView.' + vm.project.id);
            var requested = params.get('view') || remembered || (params.get('section') === 'reports' ? 'reports' : 'overview');
            var selected = vm.setProjectView(requested, true);
            if (vm.applyPlanningViewLocation) vm.applyPlanningViewLocation(params);
            if (vm.applyReportingLocation) vm.applyReportingLocation(params);
            if (searchChanged && vm.project && !vm.loading) vm.loadTasks();
            return selected;
          };

          vm.handleProjectViewKeydown = function(event, index) {
            var available = vm.availableProjectViews();
            var next = event.key === 'Home' ? 0
              : event.key === 'End' ? available.length - 1
                : event.key === 'ArrowRight' ? (index + 1) % available.length
                  : event.key === 'ArrowLeft' ? (index - 1 + available.length) % available.length
                    : -1;
            if (next < 0) return;
            event.preventDefault();
            vm.setProjectView(available[next].id);
            $timeout(function() {
              var tab = window.document.querySelector('.project-view-switcher [aria-selected="true"]');
              if (tab) tab.focus();
            });
          };

          vm.projectOwnerName = function() {
            var owner = (vm.project && vm.project.members || []).find(function(member) {
              return member.role === 'ProjectOwner';
            });
            return owner ? memberName(owner) : 'Proje sahibi';
          };
          vm.projectMemberName = memberName;
          vm.projectContributors = function() {
            return (vm.project && vm.project.members || []).filter(function(member) {
              return member.role !== 'ProjectOwner';
            }).slice(0, 4);
          };
          vm.activeProjectSprint = function() {
            return (vm.sprints || []).find(function(sprint) { return sprint.status === 'Active'; }) || null;
          };
          vm.nextProjectMilestone = function() {
            return firstByDate((vm.project && vm.project.milestones || []).filter(function(milestone) {
              return milestone.status !== 'Completed';
            }), 'dueAt');
          };
          vm.nextProjectRelease = function() {
            return firstByDate((vm.project && vm.project.releases || []).filter(function(release) {
              return release.status !== 'Published';
            }), 'scheduledAt');
          };
          vm.projectHealth = function() {
            if (vm.loading) return { level: 'loading', label: 'Güncelleniyor', detail: 'Proje göstergeleri yenileniyor.' };
            if (vm.loadError) return { level: 'unknown', label: 'Veri alınamadı', detail: 'Göstergeleri yenilemek için tekrar deneyin.' };
            if ((vm.summary.overdue || 0) > 0) return { level: 'danger', label: 'Takip gerekli', detail: vm.summary.overdue + ' geciken iş bulunuyor.' };
            if ((vm.dueDateRisks || []).length > 0) return { level: 'warning', label: 'Yakın risk var', detail: vm.dueDateRisks.length + ' iş yakın tarihte risk taşıyor.' };
            if (vm.activeProjectSprint()) return { level: 'healthy', label: 'Plan üzerinde', detail: 'Aktif sprint ve gecikmeyen teslimat görünümü.' };
            return { level: 'neutral', label: 'Planlama açık', detail: 'Aktif sprint bulunmuyor.' };
          };
          vm.recentProjectActivity = function() { return (vm.timelineEntries || []).slice(0, 6); };
          vm.activityActorName = function(entry) {
            var member = (vm.project && vm.project.members || []).find(function(candidate) {
              return candidate.userId === entry.actorUserId;
            });
            return member ? memberName(member) : 'Proje üyesi';
          };

          function memberName(member) {
            var user = (vm.users || []).find(function(candidate) { return candidate.id === member.userId; });
            if (user) return user.username || user.email || 'Proje üyesi';
            if (vm.session.currentUser && vm.session.currentUser.id === member.userId) {
              return vm.session.currentUser.username || vm.session.currentUser.email || 'Siz';
            }
            return 'Proje üyesi';
          }
        }
      };
    });
})();
