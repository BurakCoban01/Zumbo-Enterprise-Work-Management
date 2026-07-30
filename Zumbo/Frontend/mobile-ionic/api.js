(function() {
  'use strict';

  angular.module('zumboMobile')
  .factory('mobileActionError', function() {
    return function(error, fallback) {
      var code = error && error.data && error.data.error && error.data.error.code;
      if (code === 'CONCURRENCY_CONFLICT') {
        return 'Bu kayıt başka bir kullanıcı tarafından değiştirildi. Güncel veriler yüklendi; değişikliğinizi yeniden uygulayın.';
      }
      if (code === 'STALE_RESPONSE') {
        return 'Çalışma alanı değişti. Güncel görünümü yeniden açın.';
      }
      if (code === 'FORBIDDEN') {
        return 'Bu işlem için yetkiniz yok.';
      }
      if (error && error.canceled) return fallback;
      return error && error.data && error.data.error && error.data.error.message
        ? error.data.error.message
        : fallback;
    };
  })
  .factory('zumboApi', function(apiClient, sessionStore) {
    return {
      projects: function(archived) { return apiClient.get('/api/projects?organizationId=' + sessionStore.state.currentUser.organizationId + (archived ? '&archived=true' : '')); },
      project: function(projectId) { return apiClient.get('/api/projects/' + projectId); },
      createOrganization: function() { return apiClient.post('/api/organizations', { name: 'Zumbo Mobil Demo', tenantKey: sessionStore.state.currentUser.organizationId }); },
      createProject: function(draft) {
        draft = draft || {};
        return apiClient.post('/api/projects', {
          organizationId: sessionStore.state.currentUser.organizationId,
          key: draft.key || 'MOB' + String(Date.now()).slice(-7),
          name: draft.name || 'Mobil Teslimat',
          ownerUserId: sessionStore.state.currentUser.id
        });
      },
      updateProject: function(projectId, draft) { return apiClient.put('/api/projects/' + projectId, draft); },
      upsertProjectTemplate: function(projectId, templateId, draft) {
        return templateId
          ? apiClient.put('/api/projects/' + projectId + '/templates/' + templateId, draft)
          : apiClient.post('/api/projects/' + projectId + '/templates', draft);
      },
      archiveProjectTemplate: function(projectId, templateId) { return apiClient.delete('/api/projects/' + projectId + '/templates/' + templateId); },
      workTemplates: function(projectId, includeArchived) { return apiClient.get('/api/work-items/templates?projectId=' + encodeURIComponent(projectId) + '&page=1&pageSize=100&includeArchived=' + (includeArchived ? 'true' : 'false')); },
      createWorkTemplate: function(draft) { return apiClient.post('/api/work-items/templates', draft); },
      updateWorkTemplate: function(templateId, draft) { return apiClient.put('/api/work-items/templates/' + templateId, draft); },
      archiveWorkTemplate: function(templateId) { return apiClient.delete('/api/work-items/templates/' + templateId); },
      workRecurrences: function(projectId, includeArchived) { return apiClient.get('/api/work-items/recurrences?projectId=' + encodeURIComponent(projectId) + '&page=1&pageSize=100&includeArchived=' + (includeArchived ? 'true' : 'false')); },
      previewWorkRecurrence: function(draft) { return apiClient.post('/api/work-items/recurrences/preview', draft); },
      createWorkRecurrence: function(draft) { return apiClient.post('/api/work-items/recurrences', draft); },
      setWorkRecurrenceState: function(recurrenceId, active) { return apiClient.patch('/api/work-items/recurrences/' + recurrenceId + '/state', { active: active }); },
      archiveWorkRecurrence: function(recurrenceId) { return apiClient.delete('/api/work-items/recurrences/' + recurrenceId); },
      workRecurrenceOccurrences: function(recurrenceId) { return apiClient.get('/api/work-items/recurrences/' + recurrenceId + '/occurrences?page=1&pageSize=50'); },
      automationRules: function(projectId, includeArchived) {
        return apiClient.get('/api/automations?projectId=' + encodeURIComponent(projectId)
          + '&page=1&pageSize=100&includeArchived=' + (includeArchived ? 'true' : 'false'));
      },
      automationRule: function(ruleId, draft) {
        return apiClient.get('/api/automations/' + ruleId + (draft ? '?draft=true' : ''));
      },
      createAutomationRule: function(draft) { return apiClient.post('/api/automations', draft); },
      updateAutomationRuleDraft: function(ruleId, draft) {
        return apiClient.put('/api/automations/' + ruleId + '/draft', draft);
      },
      publishAutomationRule: function(ruleId) {
        return apiClient.post('/api/automations/' + ruleId + '/publish', {});
      },
      setAutomationRuleState: function(ruleId, active) {
        return apiClient.patch('/api/automations/' + ruleId + '/state', { active: active });
      },
      archiveAutomationRule: function(ruleId) { return apiClient.delete('/api/automations/' + ruleId); },
      dryRunAutomationRule: function(ruleId, context) {
        return apiClient.post('/api/automations/' + ruleId + '/dry-run', context);
      },
      automationRuns: function(projectId, status) {
        return apiClient.get('/api/automations/runs?projectId=' + encodeURIComponent(projectId)
          + '&page=1&pageSize=50' + (status ? '&status=' + encodeURIComponent(status) : ''));
      },
      replayAutomationRun: function(runId) {
        return apiClient.post('/api/automations/runs/' + runId + '/replay', {});
      },
      createProjectComponent: function(projectId, draft) { return apiClient.post('/api/projects/' + projectId + '/components', draft); },
      updateProjectComponent: function(projectId, componentId, draft) { return apiClient.put('/api/projects/' + projectId + '/components/' + componentId, draft); },
      archiveProjectComponent: function(projectId, componentId) { return apiClient.delete('/api/projects/' + projectId + '/components/' + componentId); },
      createProjectVersion: function(projectId, draft) { return apiClient.post('/api/projects/' + projectId + '/versions', draft); },
      archiveProjectVersion: function(projectId, versionId) { return apiClient.delete('/api/projects/' + projectId + '/versions/' + versionId); },
      createProjectRelease: function(projectId, draft) { return apiClient.post('/api/projects/' + projectId + '/releases', draft); },
      approveProjectRelease: function(projectId, releaseId) { return apiClient.post('/api/projects/' + projectId + '/releases/' + releaseId + '/approve', {}); },
      publishProjectRelease: function(projectId, releaseId) { return apiClient.post('/api/projects/' + projectId + '/releases/' + releaseId + '/publish', {}); },
      createProjectMilestone: function(projectId, draft) { return apiClient.post('/api/projects/' + projectId + '/milestones', draft); },
      updateProjectMilestone: function(projectId, milestoneId, draft) { return apiClient.put('/api/projects/' + projectId + '/milestones/' + milestoneId, draft); },
      completeProjectMilestone: function(projectId, milestoneId) { return apiClient.post('/api/projects/' + projectId + '/milestones/' + milestoneId + '/complete', {}); },
      users: function() { return apiClient.get('/api/auth/users'); },
      roles: function() { return apiClient.get('/api/auth/roles'); },
      addProjectMember: function(projectId, draft) { return apiClient.post('/api/projects/' + projectId + '/members', draft); },
      changeProjectMemberRole: function(projectId, userId, role) {
        return apiClient.patch('/api/projects/' + projectId + '/members/' + userId + '/role', { role: role });
      },
      removeProjectMember: function(projectId, userId) { return apiClient.delete('/api/projects/' + projectId + '/members/' + userId); },
      archiveProject: function(projectId) { return apiClient.delete('/api/projects/' + projectId); },
      restoreProject: function(projectId) { return apiClient.post('/api/projects/' + projectId + '/restore', {}); },
      createBoard: function(projectId, draft) {
        draft = draft || {};
        return apiClient.post('/api/boards', { projectId: projectId, name: draft.name || 'Mobil Pano', type: draft.type || 'Kanban' });
      },
      boards: function(projectId, archived) { return apiClient.get('/api/boards/by-project/' + projectId + (archived ? '?archived=true' : '')); },
      updateBoard: function(boardId, draft) { return apiClient.put('/api/boards/' + boardId, draft); },
      archiveBoard: function(boardId) { return apiClient.delete('/api/boards/' + boardId); },
      restoreBoard: function(boardId) { return apiClient.post('/api/boards/' + boardId + '/restore', {}); },
      teams: function(archived) { return apiClient.get('/api/teams?organizationId=' + sessionStore.state.currentUser.organizationId + (archived ? '&archived=true' : '')); },
      createTeam: function(name) { return apiClient.post('/api/teams', { organizationId: sessionStore.state.currentUser.organizationId, name: name, ownerUserId: sessionStore.state.currentUser.id }); },
      updateTeam: function(teamId, name) { return apiClient.put('/api/teams/' + teamId, { name: name }); },
      inviteTeamMember: function(teamId, email, role) { return apiClient.post('/api/teams/' + teamId + '/members', { email: email, role: role }); },
      removeTeamMember: function(teamId, memberKey) { return apiClient.delete('/api/teams/' + teamId + '/members/' + encodeURIComponent(memberKey)); },
      archiveTeam: function(teamId) { return apiClient.delete('/api/teams/' + teamId); },
      restoreTeam: function(teamId) { return apiClient.post('/api/teams/' + teamId + '/restore', {}); },
      audit: function(entityType, entityId) { return apiClient.get('/api/audit/entity/' + entityType + '/' + entityId); },
      tasks: function(projectId, status, page, pageSize) {
        return apiClient.post('/api/work-items/search', {
          projectId: projectId,
          assigneeUserId: sessionStore.state.currentUser.id,
          status: status || null,
          page: page || 1,
          pageSize: pageSize || 50
        },
          { scope: 'mobile-task-load', replace: true });
      },
      projectTasks: function(projectId, status, page, pageSize) {
        return apiClient.post('/api/work-items/search', {
          projectId: projectId,
          status: status || null,
          page: page || 1,
          pageSize: pageSize || 100
        },
          { scope: 'mobile-project-task-load', replace: true });
      },
      searchWork: function(projectId, text, page, pageSize) {
        return apiClient.post('/api/work-items/search', {
          projectId: projectId,
          text: String(text || '').trim(),
          page: page || 1,
          pageSize: pageSize || 50
        },
          { scope: 'mobile-global-search', replace: true });
      },
      sprints: function(projectId, after) { return apiClient.get('/api/sprints/projects/' + projectId + '?pageSize=50' + (after ? '&after=' + encodeURIComponent(after) : '')); },
      backlog: function(projectId, after) { return apiClient.get('/api/sprints/projects/' + projectId + '/backlog?pageSize=100' + (after ? '&after=' + encodeURIComponent(after) : '')); },
      createSprint: function(projectId, draft) {
        return apiClient.post('/api/sprints', {
          projectId: projectId,
          name: draft.name,
          goal: draft.goal || null,
          startDate: draft.startDate,
          endDate: draft.endDate
        });
      },
      planSprintItem: function(sprintId, item) {
        apiClient.remember('/api/work-items/' + item.id, item);
        return apiClient.put('/api/sprints/' + sprintId + '/items/' + item.id, { estimatePoints: item.estimatePoints || 0 });
      },
      unplanSprintItem: function(sprintId, item) {
        apiClient.remember('/api/work-items/' + item.id, item);
        return apiClient.delete('/api/sprints/' + sprintId + '/items/' + item.id);
      },
      startSprint: function(sprintId) { return apiClient.post('/api/sprints/' + sprintId + '/start', {}); },
      completeSprint: function(sprintId, carryoverSprintId) { return apiClient.post('/api/sprints/' + sprintId + '/complete', { carryoverSprintId: carryoverSprintId || null }); },
      sprintBurndown: function(sprintId) { return apiClient.get('/api/sprints/' + sprintId + '/burndown'); },
      workItemSchema: function(projectId) { return apiClient.get('/api/work-item-schemas/' + projectId); },
      createTask: function(projectId, boardId, draft) {
        draft = draft || {};
        return apiClient.post('/api/work-items', {
          projectId: projectId,
          boardId: boardId,
          title: draft.title || 'Mobil takip ' + new Date().toLocaleTimeString(),
          type: draft.type || 'Task',
          priority: draft.priority || 'Medium',
          assigneeUserId: sessionStore.state.currentUser.id,
          customFields: draft.customFields || []
        });
      },
      task: function(taskId) { return apiClient.get('/api/work-items/' + taskId); },
      bulkJobs: function(projectId, page, pageSize) {
        return apiClient.get('/api/work-items/bulk/jobs?projectId=' + encodeURIComponent(projectId)
          + '&page=' + (page || 1) + '&pageSize=' + (pageSize || 50),
        { scope: 'mobile-bulk-jobs', replace: true });
      },
      submitBulkImport: function(request, idempotencyKey) {
        return apiClient.post('/api/work-items/bulk/jobs/import', request, { idempotencyKey: idempotencyKey });
      },
      submitBulkExport: function(request, idempotencyKey) {
        return apiClient.post('/api/work-items/bulk/jobs/export', request, { idempotencyKey: idempotencyKey });
      },
      cancelBulkJob: function(jobId) { return apiClient.post('/api/work-items/bulk/jobs/' + jobId + '/cancel', {}); },
      retryBulkJob: function(jobId) { return apiClient.post('/api/work-items/bulk/jobs/' + jobId + '/retry', {}); },
      downloadBulkJobArtifact: function(jobId, errors) {
        return apiClient.download('/api/work-items/bulk/jobs/' + jobId + '/' + (errors ? 'errors' : 'result'));
      },
      taskCollaboration: function(taskId) { return apiClient.get('/api/work-items/' + taskId + '/collaboration'); },
      setTaskWatch: function(taskId, watching) { return apiClient.put('/api/work-items/' + taskId + '/watch', { watching: watching }); },
      setTaskVote: function(taskId, voted) { return apiClient.put('/api/work-items/' + taskId + '/vote', { voted: voted }); },
      taskActivity: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/activity?page=' + (page || 1) + '&pageSize=50'); },
      taskComments: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/comments?page=' + (page || 1) + '&pageSize=50'); },
      taskAttachments: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/attachments?page=' + (page || 1) + '&pageSize=50'); },
      taskWorkLogs: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/worklogs?page=' + (page || 1) + '&pageSize=50'); },
      taskApprovals: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/approvals?page=' + (page || 1) + '&pageSize=50'); },
      taskTimeline: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/timeline?page=' + (page || 1) + '&pageSize=50'); },
      taskDevelopmentLinks: function(taskId) { return apiClient.get('/api/work-items/' + taskId + '/development-links'); },
      taskDevelopmentMappings: function(taskId) { return apiClient.get('/api/work-items/' + taskId + '/development-links/mappings'); },
      createTaskDevelopmentLink: function(taskId, request) { return apiClient.post('/api/work-items/' + taskId + '/development-links', request); },
      deleteTaskDevelopmentLink: function(taskId, linkId, version) {
        return apiClient.delete('/api/work-items/' + taskId + '/development-links/' + linkId
          + '?expectedVersion=' + encodeURIComponent(version));
      },
      updateTask: function(taskId, draft) { return apiClient.put('/api/work-items/' + taskId, draft); },
      assignTask: function(taskId, assigneeUserId) { return apiClient.patch('/api/work-items/' + taskId + '/assignee', { assigneeUserId: assigneeUserId }); },
      setTaskTeam: function(taskId, teamId) { return apiClient.patch('/api/work-items/' + taskId + '/team', { teamId: teamId || null }); },
      setTaskParent: function(taskId, parentId) { return apiClient.patch('/api/work-items/' + taskId + '/parent', { parentId: parentId || null }); },
      setTaskPlanning: function(taskId, sprintId, estimatePoints) { return apiClient.patch('/api/work-items/' + taskId + '/planning', { sprintId: sprintId || null, estimatePoints: estimatePoints == null ? null : estimatePoints }); },
      setTaskCustomFields: function(taskId, values) { return apiClient.put('/api/work-items/' + taskId + '/custom-fields', { values: values }); },
      workflow: function(projectId) { return apiClient.get('/api/workflows/' + projectId); },
      moveTask: function(taskId, status) { return apiClient.patch('/api/work-items/' + taskId + '/status', { status: status }); },
      addComment: function(taskId, body, mentions) { return apiClient.post('/api/work-items/' + taskId + '/comments', { body: body, mentions: mentions || [] }); },
      editComment: function(taskId, commentId, body) { return apiClient.put('/api/work-items/' + taskId + '/comments/' + commentId, { body: body }); },
      deleteComment: function(taskId, commentId) { return apiClient.delete('/api/work-items/' + taskId + '/comments/' + commentId); },
      addChecklist: function(taskId, text) { return apiClient.post('/api/work-items/' + taskId + '/checklist', { text: text }); },
      completeChecklist: function(taskId, itemId, completed) { return apiClient.patch('/api/work-items/' + taskId + '/checklist/' + itemId, { completed: completed }); },
      addLabel: function(taskId, label) { return apiClient.post('/api/work-items/' + taskId + '/labels', { label: label }); },
      addWorkLog: function(taskId, hours, note) {
        return apiClient.post('/api/work-items/' + taskId + '/worklogs', {
          userId: sessionStore.state.currentUser.id,
          hours: hours,
          note: note || null
        });
      },
      uploadAttachment: function(taskId, file) { return apiClient.upload('/api/work-items/' + taskId + '/attachments/upload', file); },
      deleteAttachment: function(taskId, attachmentId) { return apiClient.delete('/api/work-items/' + taskId + '/attachments/' + attachmentId); },
      downloadAttachment: function(taskId, attachmentId) { return apiClient.download('/api/work-items/' + taskId + '/attachments/' + attachmentId + '/download'); },
      addTaskRelation: function(taskId, draft) { return apiClient.post('/api/work-items/' + taskId + '/relations', draft); },
      removeTaskRelation: function(taskId, relation) { return apiClient.delete('/api/work-items/' + taskId + '/relations/' + relation.relatedWorkItemId + '?relationType=' + encodeURIComponent(relation.relationType)); },
      requestTaskApproval: function(taskId, status) { return apiClient.post('/api/work-items/' + taskId + '/approvals', { targetStatus: status }); },
      decideTaskApproval: function(taskId, approvalId, approved, note) { return apiClient.post('/api/work-items/' + taskId + '/approvals/' + approvalId + '/decision', { approved: approved, note: note || null }); },
      summary: function(projectId) { return apiClient.get('/api/work-items/reports/project-summary/' + projectId); },
      notifications: function() { return apiClient.get('/api/notifications/' + sessionStore.state.currentUser.id); },
      webhookSubscriptions: function() { return apiClient.get('/api/integrations/webhooks'); },
      webhookSubscription: function(id) { return apiClient.get('/api/integrations/webhooks/' + id); },
      webhookMetrics: function() { return apiClient.get('/api/integrations/webhooks/metrics'); },
      createWebhookSubscription: function(draft) { return apiClient.post('/api/integrations/webhooks', draft); },
      updateWebhookSubscription: function(id, draft) { return apiClient.put('/api/integrations/webhooks/' + id, draft); },
      rotateWebhookSecret: function(id, version) { return apiClient.post('/api/integrations/webhooks/' + id + '/rotate-secret', { expectedVersion: version }); },
      setWebhookActive: function(id, active, version) {
        return active
          ? apiClient.post('/api/integrations/webhooks/' + id + '/enable', { expectedVersion: version })
          : apiClient.post('/api/integrations/webhooks/' + id + '/disable', { expectedVersion: version });
      },
      sendWebhookTest: function(id) { return apiClient.post('/api/integrations/webhooks/' + id + '/test-delivery', {}); },
      webhookDeliveries: function(id, cursor) { return apiClient.get('/api/integrations/webhooks/' + id + '/deliveries?pageSize=30' + (cursor ? '&cursor=' + encodeURIComponent(cursor) : '')); },
      webhookDelivery: function(id) { return apiClient.get('/api/integrations/webhooks/deliveries/' + id); },
      replayWebhookDelivery: function(id) { return apiClient.post('/api/integrations/webhooks/deliveries/' + id + '/replay', {}); },
      developmentConnections: function() { return apiClient.get('/api/integrations/development'); },
      developmentConnection: function(id) { return apiClient.get('/api/integrations/development/' + id); },
      createDevelopmentConnection: function(request) { return apiClient.post('/api/integrations/development', request); },
      developmentMappings: function(id) { return apiClient.get('/api/integrations/development/' + id + '/mappings'); },
      developmentRepositories: function(id) { return apiClient.get('/api/integrations/development/' + id + '/repositories'); },
      createDevelopmentMapping: function(id, request) { return apiClient.post('/api/integrations/development/' + id + '/mappings', request); },
      deleteDevelopmentMapping: function(id, version) {
        return apiClient.delete('/api/integrations/development/mappings/' + id
          + '?expectedVersion=' + encodeURIComponent(version));
      },
      checkDevelopmentHealth: function(id) { return apiClient.post('/api/integrations/development/' + id + '/health', {}); },
      rotateDevelopmentCredential: function(id, accessToken, version) {
        return apiClient.post('/api/integrations/development/' + id + '/rotate-credential', {
          accessToken: accessToken,
          expectedVersion: version
        });
      },
      rotateDevelopmentSecret: function(id, version) {
        return apiClient.post('/api/integrations/development/' + id + '/rotate-webhook-secret', {
          expectedVersion: version
        });
      },
      disconnectDevelopmentConnection: function(id, version) {
        return apiClient.post('/api/integrations/development/' + id + '/disconnect', {
          expectedVersion: version
        });
      },
      deleteDevelopmentConnection: function(id, version) {
        return apiClient.delete('/api/integrations/development/' + id
          + '?expectedVersion=' + encodeURIComponent(version));
      },
      read: function(id) { return apiClient.patch('/api/notifications/' + id + '/read', {}); },
      notificationPreferences: function() { return apiClient.get('/api/notifications/preferences/me'); },
      saveNotificationPreferences: function(draft) { return apiClient.put('/api/notifications/preferences/me', draft); },
      mfaStatus: function() { return apiClient.get('/api/auth/mfa'); },
      beginMfaSetup: function(password) { return apiClient.post('/api/auth/mfa/setup', { password: password }); },
      confirmMfaSetup: function(code) {
        apiClient.cancelPending('mfa-confirm-session-rotation');
        return apiClient.post('/api/auth/mfa/confirm', { code: code });
      },
      disableMfa: function(draft) {
        apiClient.cancelPending('mfa-disable-session-rotation');
        return apiClient.post('/api/auth/mfa/disable', draft);
      },
      regenerateMfaRecoveryCodes: function(draft) {
        apiClient.cancelPending('mfa-recovery-session-rotation');
        return apiClient.post('/api/auth/mfa/recovery-codes', draft);
      },
      sessions: function() { return apiClient.get('/api/auth/sessions'); },
      revokeSession: function(sessionId) { return apiClient.delete('/api/auth/sessions/' + sessionId); },
      exportPrivacyData: function() { return apiClient.download('/api/auth/privacy/export.ndjson'); },
      createPrivacyJob: function(request) { return apiClient.post('/api/auth/privacy/anonymization-jobs', request); },
      privacyJobStatus: function(jobId, statusToken) {
        return apiClient.get('/api/auth/privacy/jobs/' + encodeURIComponent(jobId) + '/status', {
          refresh: false,
          privacyStatusToken: statusToken,
          scope: 'mobile-privacy-status',
          replace: true
        });
      },
      privacyJob: function(jobId) { return apiClient.get('/api/auth/privacy/jobs/' + encodeURIComponent(jobId)); },
      retryPrivacyJob: function(jobId) {
        return apiClient.post('/api/auth/privacy/jobs/' + encodeURIComponent(jobId) + '/retry', {});
      },
      reconcilePrivacyJob: function(jobId) {
        return apiClient.post('/api/auth/privacy/jobs/' + encodeURIComponent(jobId) + '/reconcile', {});
      }
    };
  });
})();
