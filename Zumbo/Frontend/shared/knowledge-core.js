/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ZumboKnowledgeCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  function scopeOptions(projects, portfolios, userId) {
    var result = [];
    (projects || []).forEach(function(project) {
      var membership = (project.members || []).find(function(member) {
        return member.userId === userId;
      });
      if (!membership || ['ProjectOwner', 'ProjectAdmin'].indexOf(membership.role) < 0) return;
      result.push({
        key: 'Project:' + project.id,
        type: 'Project',
        id: project.id,
        label: project.key + ' · ' + project.name,
        projectIds: [project.id]
      });
    });
    (portfolios || []).forEach(function(portfolio) {
      (portfolio.initiatives || []).forEach(function(initiative) {
        if (!portfolio.canEdit && !initiative.canUpdateStatus
            && initiative.ownerUserId !== userId) return;
        result.push({
          key: 'Initiative:' + initiative.id,
          type: 'Initiative',
          id: initiative.id,
          label: portfolio.name + ' · ' + initiative.name,
          projectIds: initiative.projectIds || []
        });
      });
    });
    return result;
  }

  function draft(scope) {
    return {
      id: null,
      scopeKey: scope && scope.key || '',
      scopeType: scope && scope.type || '',
      scopeId: scope && scope.id || '',
      title: '',
      contentMarkdown: '',
      tagsText: '',
      workItemIds: [],
      userIds: [],
      changeSummary: ''
    };
  }

  function hydrate(item) {
    return {
      id: item.id,
      scopeKey: item.scopeType + ':' + item.scopeId,
      scopeType: item.scopeType,
      scopeId: item.scopeId,
      title: item.title,
      contentMarkdown: item.contentMarkdown || '',
      tagsText: (item.tags || []).join(', '),
      workItemIds: (item.workItemIds || []).slice(),
      userIds: (item.userIds || []).slice(),
      changeSummary: ''
    };
  }

  function applyScope(value, scopes) {
    var selected = (scopes || []).find(function(scope) { return scope.key === value; });
    return selected || null;
  }

  function createPayload(value, scope) {
    var version = versionPayload(value);
    version.scopeType = scope.type;
    version.scopeId = scope.id;
    return version;
  }

  function versionPayload(value) {
    return {
      title: String(value.title || '').trim(),
      contentMarkdown: String(value.contentMarkdown || '').trim(),
      tags: unique(String(value.tagsText || '').split(',').map(function(tag) {
        return tag.trim();
      }).filter(Boolean), true),
      workItemIds: unique(value.workItemIds || []),
      userIds: unique(value.userIds || []),
      changeSummary: String(value.changeSummary || '').trim()
    };
  }

  function validate(value, scope) {
    if (!scope) return 'Proje veya initiative kapsamı seçin.';
    if (!String(value.title || '').trim()) return 'Doküman başlığı gereklidir.';
    if (String(value.title || '').trim().length > 160) return 'Doküman başlığı 160 karakteri aşamaz.';
    if (String(value.contentMarkdown || '').length > 40000) return 'İçerik 40.000 karakteri aşamaz.';
    if (!String(value.changeSummary || '').trim()) return 'Sürüm özeti gereklidir.';
    if ((value.workItemIds || []).length > 50) return 'En fazla 50 iş bağlanabilir.';
    if ((value.userIds || []).length > 30) return 'En fazla 30 kullanıcı bağlanabilir.';
    return null;
  }

  function parseMarkdown(value) {
    var lines = String(value || '').replace(/\r\n?/g, '\n').split('\n');
    var blocks = [];
    var index = 0;
    while (index < lines.length) {
      var line = lines[index];
      if (!line.trim()) { index += 1; continue; }
      if (/^```/.test(line)) {
        var language = line.slice(3).trim();
        var code = [];
        index += 1;
        while (index < lines.length && !/^```/.test(lines[index])) {
          code.push(lines[index]);
          index += 1;
        }
        if (index < lines.length) index += 1;
        blocks.push({ type: 'code', language: language, text: code.join('\n') });
        continue;
      }
      var heading = line.match(/^(#{1,3})\s+(.+)$/);
      if (heading) {
        blocks.push({
          type: 'heading',
          level: heading[1].length,
          segments: parseInline(heading[2])
        });
        index += 1;
        continue;
      }
      var list = line.match(/^(\s*)([-*]|\d+\.)\s+(.+)$/);
      if (list) {
        var ordered = /\d+\./.test(list[2]);
        var items = [];
        while (index < lines.length) {
          var item = lines[index].match(/^(\s*)([-*]|\d+\.)\s+(.+)$/);
          if (!item || /\d+\./.test(item[2]) !== ordered) break;
          items.push(parseInline(item[3]));
          index += 1;
        }
        blocks.push({ type: 'list', ordered: ordered, items: items });
        continue;
      }
      if (/^>\s?/.test(line)) {
        blocks.push({
          type: 'quote',
          segments: parseInline(line.replace(/^>\s?/, ''))
        });
        index += 1;
        continue;
      }
      var paragraph = [line.trim()];
      index += 1;
      while (index < lines.length && lines[index].trim()
          && !/^(#{1,3})\s+|^```|^(\s*)([-*]|\d+\.)\s+|^>\s?/.test(lines[index])) {
        paragraph.push(lines[index].trim());
        index += 1;
      }
      blocks.push({
        type: 'paragraph',
        segments: parseInline(paragraph.join(' '))
      });
    }
    return blocks;
  }

  function parseInline(value) {
    var result = [];
    var pattern = /(\[([^\]]+)\]\(([^)\s]+)\)|`([^`]+)`|\*\*([^*]+)\*\*)/g;
    var cursor = 0;
    var match;
    while ((match = pattern.exec(value))) {
      if (match.index > cursor) result.push({ type: 'text', text: value.slice(cursor, match.index) });
      if (match[2]) {
        var target = safeLink(match[3]);
        result.push(target
          ? { type: 'link', text: match[2], href: target }
          : { type: 'text', text: match[2] });
      } else if (match[4]) {
        result.push({ type: 'code', text: match[4] });
      } else {
        result.push({ type: 'strong', text: match[5] });
      }
      cursor = pattern.lastIndex;
    }
    if (cursor < value.length) result.push({ type: 'text', text: value.slice(cursor) });
    return result;
  }

  function safeLink(value) {
    var normalized = String(value || '').trim();
    if (/^(\/|#)/.test(normalized)) return normalized;
    try {
      var parsed = new URL(normalized);
      return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? normalized : null;
    } catch (_) {
      return null;
    }
  }

  function unique(values, ignoreCase) {
    var seen = {};
    return values.filter(function(value) {
      var key = ignoreCase ? String(value).toLowerCase() : String(value);
      if (!value || seen[key]) return false;
      seen[key] = true;
      return true;
    });
  }

  return {
    scopeOptions: scopeOptions,
    draft: draft,
    hydrate: hydrate,
    applyScope: applyScope,
    createPayload: createPayload,
    versionPayload: versionPayload,
    validate: validate,
    parseMarkdown: parseMarkdown,
    safeLink: safeLink
  };
});
