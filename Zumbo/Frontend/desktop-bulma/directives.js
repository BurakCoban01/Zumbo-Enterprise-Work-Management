(function() {
  'use strict';

  angular.module('zumboDesktop')
    .component('zumboFeedback', {
      bindings: { feedback: '=' },
      template: [
        '<div class="toast" ng-if="$ctrl.feedback" ng-class="$ctrl.feedback.kind" role="status" aria-live="polite">',
        '  <i data-lucide="{{$ctrl.feedback.kind === \'success\' ? \'circle-check\' : \'triangle-alert\'}}" lucide-icon></i>',
        '  <span>{{$ctrl.feedback.message}}</span>',
        '  <button class="delete is-small" ng-click="$ctrl.feedback = null" aria-label="Bildirimi kapat"></button>',
        '</div>'
      ].join('')
    })
    .directive('fileChange', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('change', function(event) {
            scope.$apply(function() { scope.$eval(attrs.fileChange, { file: event.target.files[0] }); });
          });
        }
      };
    })
    .directive('lucideIcon', function($timeout) {
      return {
        restrict: 'A',
        link: function() {
          $timeout(function() {
            if (window.lucide) window.lucide.createIcons({ attrs: { 'stroke-width': 1.8 } });
          });
        }
      };
    })
    .directive('commandFocus', function($timeout) {
      return {
        link: function(scope, element) {
          var previous = window.document.activeElement;
          var timer = $timeout(function() { element[0].focus(); });
          scope.$on('$destroy', function() {
            $timeout.cancel(timer);
            var target = previous && previous.isConnected && previous !== window.document.body
              ? previous
              : window.document.querySelector('.create-button');
            if (target && target.focus) $timeout(function() { target.focus(); });
          });
        }
      };
    })
    .directive('draggableTask', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          attrs.$observe('draggableTask', function(taskId) {
            element.attr('draggable', taskId ? 'true' : 'false');
          });
          element.on('dragstart', function(event) {
            if (!attrs.draggableTask) {
              event.preventDefault();
              return;
            }
            var nativeEvent = event.originalEvent || event;
            nativeEvent.dataTransfer.effectAllowed = 'move';
            nativeEvent.dataTransfer.setData('text/plain', attrs.draggableTask);
            element.addClass('dragging');
            window.document.body.classList.add('board-dragging');
          });
          element.on('dragend', function() {
            element.removeClass('dragging');
            clearBoardDragState();
          });
        }
      };
    })
    .directive('dropLane', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('dragover', function(event) {
            event.preventDefault();
            var nativeEvent = event.originalEvent || event;
            nativeEvent.dataTransfer.dropEffect = 'move';
            element.addClass('drag-target');
            autoScrollLane(nativeEvent, element[0]);
          });
          element.on('dragleave', function(event) {
            var nativeEvent = event.originalEvent || event;
            if (!element[0].contains(nativeEvent.relatedTarget)) element.removeClass('drag-target');
          });
          element.on('drop', function(event) {
            event.preventDefault();
            var nativeEvent = event.originalEvent || event;
            var taskId = nativeEvent.dataTransfer.getData('text/plain');
            clearBoardDragState();
            scope.$apply(function() { scope.$eval(attrs.dropLane, { taskId: taskId, placement: 'end' }); });
          });
        }
      };
    })
    .directive('dropTaskBefore', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('dragover', function(event) {
            event.preventDefault();
            event.stopPropagation();
            var nativeEvent = event.originalEvent || event;
            nativeEvent.dataTransfer.dropEffect = 'move';
            var bounds = element[0].getBoundingClientRect();
            var placement = nativeEvent.clientY >= bounds.top + bounds.height / 2 ? 'after' : 'before';
            clearTaskDropMarkers();
            element.addClass(placement === 'after' ? 'drop-after' : 'drop-before');
            var lane = element[0].closest('.column-lane');
            if (lane) lane.classList.add('drag-target');
            autoScrollLane(nativeEvent, element[0]);
          });
          element.on('dragleave', function() {
            element.removeClass('drop-before');
            element.removeClass('drop-after');
          });
          element.on('drop', function(event) {
            event.preventDefault();
            event.stopPropagation();
            var nativeEvent = event.originalEvent || event;
            var taskId = nativeEvent.dataTransfer.getData('text/plain');
            var bounds = element[0].getBoundingClientRect();
            var placement = nativeEvent.clientY >= bounds.top + bounds.height / 2 ? 'after' : 'before';
            clearBoardDragState();
            scope.$apply(function() { scope.$eval(attrs.dropTaskBefore, { taskId: taskId, placement: placement }); });
          });
        }
      };
    });

  function clearTaskDropMarkers() {
    Array.prototype.forEach.call(window.document.querySelectorAll('.task.drop-before, .task.drop-after'), function(task) {
      task.classList.remove('drop-before', 'drop-after');
    });
  }

  function clearBoardDragState() {
    clearTaskDropMarkers();
    Array.prototype.forEach.call(window.document.querySelectorAll('.column-lane.drag-target'), function(lane) {
      lane.classList.remove('drag-target');
    });
    window.document.body.classList.remove('board-dragging');
  }

  function autoScrollLane(event, target) {
    var laneContent = target.closest('.lane-content');
    if (!laneContent) return;
    var bounds = laneContent.getBoundingClientRect();
    var edge = Math.min(72, Math.max(36, bounds.height * 0.16));
    if (event.clientY < bounds.top + edge) laneContent.scrollTop -= 18;
    else if (event.clientY > bounds.bottom - edge) laneContent.scrollTop += 18;
  }
})();
