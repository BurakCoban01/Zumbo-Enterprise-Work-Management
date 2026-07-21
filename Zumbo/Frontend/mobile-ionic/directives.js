(function() {
  'use strict';

  angular.module('zumboMobile')
  .directive('fileChange', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.on('change', function(event) {
          scope.$apply(function() {
            scope.$eval(attrs.fileChange, { file: event.target.files[0] });
          });
        });
      }
    };
  });
})();
