(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopPlanningViewsFeature', function($q, $window, apiClient) {
      var planningModes = ['calendar', 'timeline', 'roadmap'];

      function safeStoredViews(storage) {
        try {
          var value = JSON.parse(storage.getItem('zumbo.planningViews') || '[]');
          return Array.isArray(value) ? value.slice(0, 12) : [];
        } catch (_) {
          return [];
        }
      }

      function errorCode(error) {
        return error && (error.code || error.data && error.data.error && error.data.error.code);
      }

      return {
        install: function(vm, helpers) {
          var core = $window.ZumboPlanningCore;
          var storage = helpers.storage;
          var updateLocation = helpers.updateLocation;
          var apiActionError = helpers.apiActionError;
          var legacyRebuild = vm.rebuildAdvancedViews;

          vm.planningCalendarMode = 'month';
          vm.planningZoom = 'month';
          vm.planningAnchor = new Date();
          vm.planningTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
          vm.planningFilters = { query: '', assignee: '', team: '', type: '' };
          vm.planningSavedViews = safeStoredViews(storage);
          vm.planningSavedViewId = '';
          vm.planningSavedViewName = '';
          vm.planningModel = core.buildModel({});
          vm.planningScopeLoading = false;
          vm.planningScopeComplete = false;
          vm.planningScopeError = null;
          vm.planningMutationTaskId = null;
          vm.planningMutationMessage = null;
          vm.planningTableOpen = false;
          vm.unscheduledOpen = false;
          vm.planningFreshAt = null;

          vm.isPlanningView = function(mode) {
            return planningModes.indexOf(mode || vm.workMode) >= 0;
          };

          vm.rebuildAdvancedViews = function() {
            legacyRebuild();
            rebuildPlanningModel();
          };

          vm.preparePlanningView = function() {
            if (!vm.project || !vm.isPlanningView() || vm.planningScopeLoading) {
              rebuildPlanningModel();
              return $q.when(vm.planningModel);
            }
            if (vm.planningLoadedProjectId !== vm.project.id) {
              vm.planningLoadedProjectId = vm.project.id;
              vm.planningScopeComplete = false;
            }
            vm.planningScopeLoading = true;
            vm.planningScopeError = null;
            return (vm.activeTaskLoad || $q.when())
              .then(loadEveryTaskPage)
              .then(loadEverySprintPage)
              .then(function() {
                vm.planningScopeComplete = !vm.hasMoreTasks && !vm.sprintNextCursor;
                vm.planningFreshAt = new Date();
                rebuildPlanningModel();
                return vm.planningModel;
              }).catch(function(error) {
                vm.planningScopeComplete = false;
                vm.planningScopeError = apiActionError(error, 'Planın tüm kayıtları yüklenemedi. Kullanılabilen kapsam gösteriliyor.');
                rebuildPlanningModel();
                return vm.planningModel;
              }).finally(function() {
                vm.planningScopeLoading = false;
              });
          };

          vm.setPlanningCalendarMode = function(mode) {
            if (['month', 'week', 'list'].indexOf(mode) < 0) return;
            vm.planningCalendarMode = mode;
            rebuildPlanningModel();
            syncLocation();
          };

          vm.setPlanningZoom = function(zoom) {
            if (['week', 'month', 'quarter'].indexOf(zoom) < 0) return;
            vm.planningZoom = zoom;
            rebuildPlanningModel();
            syncLocation();
          };

          vm.shiftPlanningWindow = function(direction) {
            var amount = vm.workMode === 'calendar'
              ? vm.planningCalendarMode === 'week' ? 7 : 28
              : vm.planningZoom === 'quarter' ? 90 : vm.planningZoom === 'month' ? 28 : 14;
            setAnchor(core.addDays(core.dateKey(vm.planningAnchor), amount * direction));
          };

          vm.planningToday = function() {
            vm.planningAnchor = new Date();
            rebuildPlanningModel();
            syncLocation();
          };

          vm.applyPlanningFilters = function() {
            vm.planningSavedViewId = '';
            rebuildPlanningModel();
            syncLocation();
          };

          vm.clearPlanningFilters = function() {
            vm.planningFilters = { query: '', assignee: '', team: '', type: '' };
            vm.applyPlanningFilters();
          };

          vm.planningTypeOptions = function() {
            return unique((vm.tasks || []).map(function(task) { return task.type; }));
          };

          vm.planningTeamOptions = function() {
            return (vm.projectTeams ? vm.projectTeams() : []).slice();
          };

          vm.planningTaskTitle = function(id) {
            var task = (vm.tasks || []).find(function(candidate) { return candidate.id === id; });
            return task ? task.title : 'Erişilemeyen iş';
          };

          vm.planningDateLabel = function(key) {
            return core.formatDate(key, 'tr-TR');
          };

          vm.planningDayIsToday = function(key) {
            return key === core.dateKey(new Date(), vm.planningTimeZone, true);
          };

          vm.planningCalendarListEvents = function() {
            var days = vm.planningModel.calendarDays || [];
            if (!days.length) return [];
            var first = days[0].key;
            var last = days[days.length - 1].key;
            return (vm.planningModel.calendarEvents || []).filter(function(event) {
              return event.key >= first && event.key <= last;
            });
          };

          vm.planningWindowLabel = function() {
            if (vm.workMode === 'calendar') {
              var days = vm.planningModel.calendarDays || [];
              if (days.length) {
                return core.formatDate(days[0].key, 'tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
                  + ' - ' + core.formatDate(days[days.length - 1].key, 'tr-TR', { day: '2-digit', month: 'short', year: 'numeric' });
              }
            }
            var window = vm.planningModel.window;
            return core.formatDate(window.startKey, 'tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
              + ' - ' + core.formatDate(window.endKey, 'tr-TR', { day: '2-digit', month: 'short', year: 'numeric' });
          };

          vm.timelineVisibleRows = function() {
            return (vm.planningModel.timelineRows || []).filter(function(row) { return row.inWindow; });
          };

          vm.roadmapVisibleRows = function() {
            return (vm.planningModel.roadmapRows || []).filter(function(row) { return row.inWindow; });
          };

          vm.timelineColumnClass = function(row) {
            return 'planning-col-' + row.column + ' planning-span-' + row.span;
          };

          vm.reschedulePlanningTask = function(task, value) {
            if (!task || !vm.canEditWorkItems() || vm.pwa.offline || vm.planningMutationTaskId) return $q.when(false);
            var key = core.dateKey(value, null, true);
            if (!key) return $q.when(false);
            var snapshot = angular.copy(task);
            apiClient.remember('/api/work-items/' + task.id, task);
            task.dueDate = key + 'T00:00:00.000Z';
            vm.planningMutationTaskId = task.id;
            vm.planningMutationMessage = null;
            rebuildPlanningModel();
            return apiClient.put('/api/work-items/' + task.id, {
              title: task.title,
              description: task.description || '',
              priority: task.priority,
              dueDate: task.dueDate
            }).then(function(updated) {
              angular.extend(task, updated);
              vm.planningMutationMessage = { kind: 'success', text: 'Bitiş tarihi ' + core.formatDate(key, 'tr-TR') + ' olarak güncellendi.' };
              vm.notify('success', 'Plan tarihi kaydedildi.');
              vm.planningFreshAt = new Date();
              rebuildPlanningModel();
              return true;
            }).catch(function(error) {
              angular.extend(task, snapshot);
              var conflict = errorCode(error) === 'CONCURRENCY_CONFLICT';
              vm.planningMutationMessage = {
                kind: 'error',
                text: conflict
                  ? 'Tarih başka bir kullanıcı tarafından değiştirildi. Önceki yerel değer geri alındı ve güncel plan yükleniyor.'
                  : apiActionError(error, 'Tarih kaydedilemedi; önceki değer geri yüklendi.')
              };
              rebuildPlanningModel();
              return vm.loadTasks().then(function() {
                return vm.preparePlanningView().then(function() { return false; });
              });
            }).finally(function() {
              vm.planningMutationTaskId = null;
            });
          };

          vm.dropPlanningTask = function(taskId, key) {
            var task = (vm.tasks || []).find(function(candidate) { return candidate.id === taskId; });
            return vm.reschedulePlanningTask(task, key);
          };

          vm.savePlanningView = function() {
            var name = String(vm.planningSavedViewName || '').trim();
            if (!name) return;
            var saved = {
              id: 'planning-' + Date.now(),
              name: name.slice(0, 60),
              mode: vm.workMode,
              calendarMode: vm.planningCalendarMode,
              zoom: vm.planningZoom,
              filters: angular.copy(vm.planningFilters)
            };
            vm.planningSavedViews.unshift(saved);
            vm.planningSavedViews = vm.planningSavedViews.slice(0, 12);
            vm.planningSavedViewId = saved.id;
            vm.planningSavedViewName = '';
            persistViews();
          };

          vm.applySavedPlanningView = function() {
            var saved = vm.planningSavedViews.find(function(candidate) { return candidate.id === vm.planningSavedViewId; });
            if (!saved) return;
            vm.planningCalendarMode = saved.calendarMode || 'month';
            vm.planningZoom = saved.zoom || 'month';
            vm.planningFilters = angular.extend({ query: '', assignee: '', team: '', type: '' }, angular.copy(saved.filters || {}));
            if (planningModes.indexOf(saved.mode) >= 0 && saved.mode !== vm.workMode) vm.setProjectView(saved.mode);
            else {
              rebuildPlanningModel();
              syncLocation();
            }
          };

          vm.deleteSavedPlanningView = function() {
            if (!vm.planningSavedViewId) return;
            vm.planningSavedViews = vm.planningSavedViews.filter(function(candidate) { return candidate.id !== vm.planningSavedViewId; });
            vm.planningSavedViewId = '';
            persistViews();
          };

          vm.applyPlanningViewLocation = function(params) {
            var calendarMode = params.get('calendar');
            var zoom = params.get('zoom');
            var anchor = params.get('anchor');
            if (['month', 'week', 'list'].indexOf(calendarMode) >= 0) vm.planningCalendarMode = calendarMode;
            if (['week', 'month', 'quarter'].indexOf(zoom) >= 0) vm.planningZoom = zoom;
            if (/^\d{4}-\d{2}-\d{2}$/.test(anchor || '')) vm.planningAnchor = core.inputDate(anchor);
            vm.planningFilters = {
              query: params.get('planQuery') || '',
              assignee: params.get('assignee') || '',
              team: params.get('team') || '',
              type: params.get('type') || ''
            };
            rebuildPlanningModel();
          };

          function rebuildPlanningModel() {
            vm.planningModel = core.buildModel({
              tasks: vm.tasks || [],
              sprints: vm.sprints || [],
              project: vm.project || {},
              filters: vm.planningFilters,
              anchorDate: vm.planningAnchor,
              calendarMode: vm.planningCalendarMode === 'list' ? 'month' : vm.planningCalendarMode,
              zoom: vm.planningZoom,
              timeZone: vm.planningTimeZone
            });
          }

          function loadEveryTaskPage() {
            if (!vm.hasMoreTasks) return $q.when();
            return $q.when(vm.loadMoreTasks()).then(loadEveryTaskPage);
          }

          function loadEverySprintPage() {
            if (!vm.sprintNextCursor || !vm.project) return $q.when();
            var cursor = vm.sprintNextCursor;
            return apiClient.get('/api/sprints/projects/' + vm.project.id + '?pageSize=50&after=' + encodeURIComponent(cursor))
              .then(function(data) {
                (data.items || []).forEach(function(item) {
                  if (!vm.sprints.some(function(existing) { return existing.id === item.id; })) vm.sprints.push(item);
                });
                vm.sprintNextCursor = data.nextCursor || null;
                return loadEverySprintPage();
              });
          }

          function setAnchor(key) {
            vm.planningAnchor = core.inputDate(key);
            rebuildPlanningModel();
            syncLocation();
          }

          function syncLocation() {
            updateLocation(vm.activeSection, null, false);
          }

          function persistViews() {
            storage.setItem('zumbo.planningViews', JSON.stringify(vm.planningSavedViews));
          }

          function unique(values) {
            return Array.from(new Set(values.filter(Boolean))).sort(function(left, right) {
              return left.localeCompare(right, 'tr-TR');
            });
          }

          rebuildPlanningModel();
        }
      };
    })
    .directive('planningDraggable', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          attrs.$observe('planningDraggable', function(value) {
            element.attr('draggable', value ? 'true' : 'false');
          });
          element.on('dragstart', function(event) {
            if (!attrs.planningDraggable) {
              event.preventDefault();
              return;
            }
            var nativeEvent = event.originalEvent || event;
            nativeEvent.dataTransfer.effectAllowed = 'move';
            nativeEvent.dataTransfer.setData('text/zumbo-work-item', attrs.planningDraggable);
          });
        }
      };
    })
    .directive('planningDropDate', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('dragover', function(event) {
            event.preventDefault();
            (event.originalEvent || event).dataTransfer.dropEffect = 'move';
          });
          element.on('drop', function(event) {
            event.preventDefault();
            var nativeEvent = event.originalEvent || event;
            var taskId = nativeEvent.dataTransfer.getData('text/zumbo-work-item');
            if (!taskId) return;
            scope.$applyAsync(function() {
              scope.$eval(attrs.planningDropDate, { $taskId: taskId });
            });
          });
        }
      };
    });
})();
