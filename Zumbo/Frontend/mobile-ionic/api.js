(function() {
  'use strict';

  angular.module('zumboMobile')
  .factory('mobileActionError', function() {
    return function(error, fallback) {
  var code = error && error.data && error.data.error && error.data.error.code;
  if (code === 'CONCURRENCY_CONFLICT') {
    return 'Bu kayıt başka bir kullanıcı tarafından değiştirildi. Güncel veriler yüklendi; değişikliğinizi yeniden uygulayın.';
  }
  return error && error.data && error.data.error && error.data.error.message
    ? error.data.error.message
    : fallback;
    };
  })
  .factory('zumboApi', function(apiClient, sessionStore) {
    return {
      projects: function(archived) { return apiClient.get('/api/projects?organizationId=' + sessionStore.state.currentUser.organizationId + (archived ? '&archived=true' : '')); },
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
      users: function() { return apiClient.get('/api/auth/users'); },
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
      sprints: function(projectId) { return apiClient.get('/api/sprints/projects/' + projectId + '?pageSize=50'); },
      backlog: function(projectId) { return apiClient.get('/api/sprints/projects/' + projectId + '/backlog?pageSize=100'); },
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
      updateTask: function(taskId, draft) { return apiClient.put('/api/work-items/' + taskId, draft); },
      setTaskCustomFields: function(taskId, values) { return apiClient.put('/api/work-items/' + taskId + '/custom-fields', { values: values }); },
      workflow: function(projectId) { return apiClient.get('/api/workflows/' + projectId); },
      moveTask: function(taskId, status) { return apiClient.patch('/api/work-items/' + taskId + '/status', { status: status }); },
      addComment: function(taskId, body) { return apiClient.post('/api/work-items/' + taskId + '/comments', { body: body, mentions: [] }); },
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
      summary: function(projectId) { return apiClient.get('/api/work-items/reports/project-summary/' + projectId); },
      notifications: function() { return apiClient.get('/api/notifications/' + sessionStore.state.currentUser.id); },
      read: function(id) { return apiClient.patch('/api/notifications/' + id + '/read', {}); }
    };
  });
})();
