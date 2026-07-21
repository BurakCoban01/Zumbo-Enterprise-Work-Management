(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopWorkItemFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var updateLocation = helpers.updateLocation;
          var nextStatusFor = helpers.nextStatusFor;
          var apiActionError = helpers.apiActionError;
    vm.selectTask = function(task, skipLocation) {
      if (!task) return;
      apiClient.get('/api/work-items/' + task.id).then(function(detail) {
        vm.selectedTask = detail;
        vm.taskDraft = {
          title: detail.title,
          description: detail.description || '',
          priority: detail.priority,
          dueDate: detail.dueDate ? new Date(detail.dueDate) : null,
          assigneeUserId: detail.assigneeUserId || '',
          teamId: detail.teamId || '',
          sprintId: detail.sprintId || '',
          estimatePoints: detail.estimatePoints,
          parentId: detail.parentId || '',
          customFieldValues: customFieldModel(detail.customFields)
        };
        vm.nextStatus = nextStatusFor(detail.status);
        if (!skipLocation) updateLocation('board', detail.id, true);
      });
      apiClient.get('/api/audit/entity/WorkItem/' + task.id).then(function(audit) {
        vm.audit = audit;
      });
    };

    vm.closeTask = function() {
      vm.selectedTask = null;
      vm.taskDraft = null;
      vm.audit = [];
      updateLocation(vm.activeSection, null, false);
    };

    vm.saveSelectedTask = function() {
      if (!vm.selectedTask || !vm.taskDraft || vm.taskSaving) return;
      vm.taskSaving = true;
      var taskId = vm.selectedTask.id;
      var current = vm.selectedTask;
      var assigneeUserId = vm.taskDraft.assigneeUserId || null;
      var teamId = vm.taskDraft.teamId || null;
      var sprintId = vm.taskDraft.sprintId || null;
      var estimatePoints = vm.taskDraft.estimatePoints == null ? null : vm.taskDraft.estimatePoints;
      var parentId = vm.taskDraft.parentId || null;
      return apiClient.put('/api/work-items/' + taskId, {
        title: vm.taskDraft.title,
        description: vm.taskDraft.description,
        priority: vm.taskDraft.priority,
        dueDate: vm.taskDraft.dueDate || null
      }).then(function(task) {
        if (assigneeUserId && assigneeUserId !== (current.assigneeUserId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/assignee', { assigneeUserId: assigneeUserId });
        }
        return task;
      }).then(function() {
        if (teamId !== (current.teamId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/team', { teamId: teamId });
        }
        return null;
      }).then(function() {
        if (parentId !== (current.parentId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/parent', { parentId: parentId });
        }
        return null;
      }).then(function() {
        if (sprintId !== (current.sprintId || null) || estimatePoints !== current.estimatePoints) {
          return apiClient.patch('/api/work-items/' + taskId + '/planning', {
            sprintId: sprintId,
            estimatePoints: estimatePoints
          });
        }
        return null;
      }).then(function() {
        return apiClient.put('/api/work-items/' + taskId + '/custom-fields', {
          values: vm.customFieldRequests(current.type, vm.taskDraft.customFieldValues)
        });
      }).then(function() {
        return apiClient.get('/api/work-items/' + taskId);
      }).then(function(task) {
        vm.selectedTask = task;
        vm.notify('success', 'Görev ayrıntıları kaydedildi.');
        return vm.loadTasks();
      }).catch(function() {
        vm.notify('error', 'Görev kaydedilemedi; alanları kontrol edin.');
      }).finally(function() { vm.taskSaving = false; });
    };

    vm.archiveSelectedTask = function() {
      if (!vm.selectedTask || vm.taskSaving) return;
      var id = vm.selectedTask.id;
      vm.taskSaving = true;
      return apiClient.delete('/api/work-items/' + id).then(function() {
        vm.closeTask();
        vm.notify('success', 'Görev arşive taşındı.');
        return vm.loadTasks();
      }).catch(function(error) {
        vm.notify('error', apiActionError(error, 'Görev arşivlenemedi.'));
      }).finally(function() { vm.taskSaving = false; });
    };

    vm.addComment = function() {
      if (!vm.selectedTask || !vm.commentBody.trim()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/comments', { body: vm.commentBody, mentions: [] })
        .then(function(task) { vm.selectedTask = task; vm.commentBody = ''; vm.notify('success', 'Yorum eklendi.'); });
    };

    vm.editComment = function(comment) {
      comment.editing = true;
      comment.draftBody = comment.body;
    };

    vm.saveComment = function(comment) {
      if (!comment || !comment.draftBody || !comment.draftBody.trim()) return;
      return apiClient.put('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id, { body: comment.draftBody })
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Yorum güncellendi.'); });
    };

    vm.deleteComment = function(comment) {
      if (!comment) return;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id)
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Yorum silindi.'); });
    };

    vm.addLabel = function() {
      if (!vm.selectedTask || !vm.labelText.trim()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/labels', { label: vm.labelText })
        .then(function(task) { vm.selectedTask = task; vm.labelText = ''; vm.notify('success', 'Etiket eklendi.'); });
    };

    vm.removeLabel = function(label) {
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/labels/' + encodeURIComponent(label))
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Etiket kaldırıldı.'); });
    };

    vm.addWorkLog = function() {
      if (!vm.selectedTask || !vm.workLogDraft.hours) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/worklogs', {
        userId: vm.session.currentUser.id,
        hours: vm.workLogDraft.hours,
        note: vm.workLogDraft.note || null
      }).then(function(task) {
        vm.selectedTask = task;
        vm.workLogDraft = { hours: null, note: '' };
        vm.notify('success', 'İş günlüğü eklendi.');
      });
    };

    vm.addChecklist = function() {
      if (!vm.selectedTask || !vm.checklistText.trim()) return;
      apiClient.post('/api/work-items/' + vm.selectedTask.id + '/checklist', { text: vm.checklistText })
        .then(function(task) { vm.selectedTask = task; vm.checklistText = ''; });
    };

    vm.toggleChecklist = function(item) {
      apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/checklist/' + item.id, { completed: !item.completed })
        .then(function(task) { vm.selectedTask = task; });
    };

    vm.uploadAttachment = function() {
      if (!vm.selectedTask || !vm.attachmentFile) return;
      apiClient.upload('/api/work-items/' + vm.selectedTask.id + '/attachments/upload', vm.attachmentFile)
        .then(function(task) { vm.selectedTask = task; vm.attachmentFile = null; });
    };

    vm.deleteAttachment = function(attachment) {
      apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/attachments/' + attachment.id)
        .then(function(task) { vm.selectedTask = task; });
    };

    vm.downloadAttachment = function(attachment) {
      apiClient.download('/api/work-items/' + vm.selectedTask.id + '/attachments/' + attachment.id + '/download')
        .then(function(blob) {
          var url = $window.URL.createObjectURL(blob);
          var link = $window.document.createElement('a');
          link.href = url;
          link.download = attachment.fileName;
          link.click();
          $window.URL.revokeObjectURL(url);
        });
    };

    vm.moveSelected = function() {
      if (!vm.selectedTask) {
        return;
      }

      apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/status', { status: vm.nextStatus })
        .then(function(task) {
          vm.selectedTask = task;
          vm.nextStatus = nextStatusFor(task.status);
          return vm.loadTasks();
        });
    };

    vm.selectedTransition = function() {
      if (!vm.selectedTask || !vm.workflow) return null;
      return (vm.workflow.transitions || []).find(function(transition) {
        return transition.fromStatus === vm.selectedTask.status && transition.toStatus === vm.nextStatus;
      }) || null;
    };

    vm.taskTitle = function(taskId) {
      var task = vm.tasks.find(function(item) { return item.id === taskId; });
      return task ? task.title : taskId;
    };

    vm.taskLinkCandidates = function() {
      if (!vm.selectedTask) return [];
      return vm.tasks.filter(function(task) { return task.id !== vm.selectedTask.id && !task.archived; });
    };

    vm.loadWorkItemSchema = function(projectId) {
      projectId = projectId || (vm.project && vm.project.id);
      if (!projectId) return $q.when(null);
      return apiClient.get('/api/work-item-schemas/' + projectId).then(function(schema) {
        if (vm.project && vm.project.id === projectId) vm.workItemSchema = schema;
        return schema;
      }).catch(function(error) {
        vm.notify('error', apiActionError(error, 'İş türü şeması yüklenemedi.'));
        return null;
      });
    };

    vm.activeIssueTypes = function() {
      return (vm.workItemSchema.issueTypes || []).filter(function(type) { return type.active; });
    };

    vm.defaultIssueType = function() {
      var types = vm.activeIssueTypes();
      var task = types.find(function(type) { return type.key === 'Task'; });
      return (task || types[0] || { key: 'Task' }).key;
    };

    vm.issueTypeDefinition = function(typeKey) {
      return (vm.workItemSchema.issueTypes || []).find(function(type) {
        return type.key.toLowerCase() === String(typeKey || '').toLowerCase();
      }) || null;
    };

    vm.canHaveParent = function(typeKey) {
      var definition = vm.issueTypeDefinition(typeKey);
      return definition && definition.hierarchyLevel !== 'Epic';
    };

    vm.customFieldsFor = function(typeKey) {
      var layout = (vm.workItemSchema.layouts || []).find(function(item) {
        return item.issueTypeKey.toLowerCase() === String(typeKey || '').toLowerCase();
      });
      var keys = layout ? layout.fieldKeys : [];
      return keys.map(function(key) {
        return (vm.workItemSchema.customFields || []).find(function(field) { return field.key === key; });
      }).filter(Boolean);
    };

    function dateOnly(value) {
      if (!value) return null;
      if (typeof value === 'string') return value.slice(0, 10);
      var year = value.getFullYear();
      var month = String(value.getMonth() + 1).padStart(2, '0');
      var day = String(value.getDate()).padStart(2, '0');
      return year + '-' + month + '-' + day;
    }

    vm.customFieldRequests = function(typeKey, values) {
      values = values || {};
      return vm.customFieldsFor(typeKey).filter(function(field) {
        var value = values[field.key];
        return value !== undefined && value !== null && value !== '';
      }).map(function(field) {
        var request = { fieldKey: field.key };
        if (field.type === 'Text') request.textValue = values[field.key];
        if (field.type === 'Number') request.numberValue = values[field.key];
        if (field.type === 'Boolean') request.booleanValue = values[field.key];
        if (field.type === 'Date') request.dateValue = dateOnly(values[field.key]);
        if (field.type === 'Select') request.optionKey = values[field.key];
        return request;
      });
    };

    function customFieldModel(values) {
      var model = {};
      (values || []).forEach(function(value) {
        var fieldValue = value.textValue;
        if (value.type === 'Number') fieldValue = value.numberValue;
        if (value.type === 'Boolean') fieldValue = value.booleanValue;
        if (value.type === 'Date') fieldValue = value.dateValue ? new Date(value.dateValue + 'T00:00:00') : null;
        if (value.type === 'Select') fieldValue = value.optionKey;
        model[value.fieldKey] = fieldValue;
      });
      return model;
    }

    vm.parentCandidates = function(type) {
      var definition = vm.issueTypeDefinition(type);
      if (!definition || definition.hierarchyLevel === 'Epic') return [];
      var parentLevel = definition.hierarchyLevel === 'Subtask' ? 'Standard' : 'Epic';
      return vm.tasks.filter(function(task) {
        var taskDefinition = vm.issueTypeDefinition(task.type);
        return taskDefinition && taskDefinition.hierarchyLevel === parentLevel && !task.archived;
      });
    };

    vm.addRelation = function() {
      if (!vm.selectedTask || !vm.relationDraft.relatedWorkItemId || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/relations', vm.relationDraft)
        .then(function(task) {
          vm.selectedTask = task;
          vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
          vm.notify('success', 'Görev ilişkisi eklendi.');
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi eklenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.removeRelation = function(relation) {
      if (!vm.selectedTask || !relation || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/relations/' + relation.relatedWorkItemId + '?relationType=' + encodeURIComponent(relation.relationType))
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Görev ilişkisi kaldırıldı.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi kaldırılamadı.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.requestApproval = function() {
      if (!vm.selectedTask || !vm.nextStatus || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals', { targetStatus: vm.nextStatus })
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Geçiş onayı istendi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Geçiş onayı istenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.decideApproval = function(approval, approved) {
      if (!vm.selectedTask || !approval || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals/' + approval.id + '/decision', {
        approved: approved,
        note: vm.approvalNote || null
      }).then(function(task) {
        vm.selectedTask = task;
        vm.approvalNote = '';
        vm.notify('success', approved ? 'Geçiş onaylandı.' : 'Geçiş reddedildi.');
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Onay kararı kaydedilemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

        }
      };
    });
})();
