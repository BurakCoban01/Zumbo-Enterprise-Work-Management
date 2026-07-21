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
          element.attr('draggable', 'true');
          element.on('dragstart', function(event) {
            var nativeEvent = event.originalEvent || event;
            nativeEvent.dataTransfer.effectAllowed = 'move';
            nativeEvent.dataTransfer.setData('text/plain', attrs.draggableTask);
          });
        }
      };
    })
    .directive('dropLane', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('dragover', function(event) { event.preventDefault(); });
          element.on('drop', function(event) {
            event.preventDefault();
            var nativeEvent = event.originalEvent || event;
            var taskId = nativeEvent.dataTransfer.getData('text/plain');
            scope.$apply(function() { scope.$eval(attrs.dropLane, { taskId: taskId }); });
          });
        }
      };
    })
    .directive('dropTaskBefore', function() {
      return {
        restrict: 'A',
        link: function(scope, element, attrs) {
          element.on('dragover', function(event) { event.preventDefault(); });
          element.on('drop', function(event) {
            event.preventDefault();
            event.stopPropagation();
            var nativeEvent = event.originalEvent || event;
            var taskId = nativeEvent.dataTransfer.getData('text/plain');
            scope.$apply(function() { scope.$eval(attrs.dropTaskBefore, { taskId: taskId }); });
          });
        }
      };
    });
})();
