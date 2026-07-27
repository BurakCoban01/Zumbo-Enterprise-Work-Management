/* global module */
(function(root) {
  'use strict';

  var limits = Object.freeze({
    maxInputItems: 5000,
    maxInputBytes: 5 * 1024 * 1024,
    maxTitleLength: 200
  });
  var terminalStates = Object.freeze(['Completed', 'CompletedWithErrors', 'Cancelled']);

  function parseImport(text, byteLength) {
    if (Number(byteLength) > limits.maxInputBytes) {
      return invalid('Dosya 5 MB sınırını aşıyor.');
    }
    var parsed;
    try {
      parsed = JSON.parse(String(text || ''));
    } catch (_) {
      return invalid('Dosya geçerli JSON içermiyor.');
    }
    var rows = Array.isArray(parsed) ? parsed : parsed && Array.isArray(parsed.items) ? parsed.items : null;
    if (!rows) return invalid('JSON kökünde bir satır dizisi veya items dizisi bulunmalı.');
    if (!rows.length || rows.length > limits.maxInputItems) {
      return invalid('Dosya 1 ile 5000 satır arasında olmalı.');
    }

    var keys = Object.create(null);
    var normalized = [];
    var errors = [];
    rows.forEach(function(row, index) {
      if (!row || typeof row !== 'object' || Array.isArray(row)) {
        errors.push((index + 1) + '. satır bir nesne olmalı.');
        return;
      }
      var item = {
        sourceKey: value(row.sourceKey),
        boardId: value(row.boardId),
        title: value(row.title),
        type: value(row.type) || 'Task',
        priority: value(row.priority) || 'Medium',
        assigneeUserId: optional(row.assigneeUserId),
        dueDate: optional(row.dueDate),
        parentId: optional(row.parentId),
        teamId: optional(row.teamId),
        customFields: Array.isArray(row.customFields) ? row.customFields : []
      };
      if (!item.sourceKey || !item.boardId || !item.title) {
        errors.push((index + 1) + '. satırda sourceKey, boardId ve title zorunlu.');
      } else if (item.title.length > limits.maxTitleLength) {
        errors.push((index + 1) + '. satır başlığı 200 karakteri aşıyor.');
      } else if (keys[item.sourceKey]) {
        errors.push((index + 1) + '. satırda yinelenen sourceKey: ' + item.sourceKey);
      } else {
        keys[item.sourceKey] = true;
        normalized.push(item);
      }
    });
    return {
      valid: errors.length === 0,
      rows: normalized,
      errors: errors.slice(0, 20),
      totalErrors: errors.length
    };
  }

  function progress(job) {
    var total = Math.max(0, Number(job && job.totalItems) || 0);
    var processed = Math.min(total, Math.max(0, Number(job && job.processedItems) || 0));
    return total ? Math.round(processed * 100 / total) : (isTerminal(job) ? 100 : 0);
  }

  function isTerminal(job) {
    return terminalStates.indexOf(String(job && job.state || '')) >= 0;
  }

  function canCancel(job) {
    return !!job && !isTerminal(job) && job.state !== 'Failed' && !job.cancelRequested;
  }

  function canRetry(job) {
    return !!job && ['Failed', 'CompletedWithErrors'].indexOf(job.state) >= 0;
  }

  function artifactsExpired(job, now) {
    if (!job || !job.artifactsExpireAt) return false;
    var expires = new Date(job.artifactsExpireAt).getTime();
    return Number.isFinite(expires) && expires <= (now ? new Date(now).getTime() : Date.now());
  }

  function state(job, now) {
    if (artifactsExpired(job, now)) return { label: 'Dosyalar süresi doldu', tone: 'muted' };
    var value = String(job && job.state || '');
    return {
      Pending: { label: 'Sırada', tone: 'neutral' },
      Running: { label: job.cancelRequested ? 'İptal bekleniyor' : 'Çalışıyor', tone: 'info' },
      Completed: { label: job && job.dryRun ? 'Önizleme tamamlandı' : 'Tamamlandı', tone: 'success' },
      CompletedWithErrors: { label: 'Kısmen tamamlandı', tone: 'warning' },
      Cancelled: { label: 'İptal edildi', tone: 'muted' },
      Failed: { label: 'Başarısız', tone: 'danger' }
    }[value] || { label: value || 'Bilinmiyor', tone: 'neutral' };
  }

  function typeLabel(job) {
    if (!job) return 'Toplu iş';
    if (job.type === 'Import') return job.dryRun ? 'İçe aktarım önizlemesi' : 'İçe aktarım';
    if (job.type === 'Export') return job.dryRun ? 'Dışa aktarım önizlemesi' : 'Dışa aktarım';
    return ({ Move: 'Durum değişikliği', Assign: 'Atama', Archive: 'Arşivleme' })[job.operation] || 'Toplu işlem';
  }

  function importRequest(projectId, parsed, dryRun) {
    return { projectId: projectId, items: parsed.rows, dryRun: dryRun === true };
  }

  function idempotencyKey(prefix) {
    var random = root.crypto && typeof root.crypto.randomUUID === 'function'
      ? root.crypto.randomUUID()
      : String(Date.now()) + '-' + Math.random().toString(16).slice(2);
    return String(prefix || 'job') + '-' + random;
  }

  function value(input) {
    return input === undefined || input === null ? '' : String(input).trim();
  }

  function optional(input) {
    var result = value(input);
    return result || null;
  }

  function invalid(message) {
    return { valid: false, rows: [], errors: [message], totalErrors: 1 };
  }

  var api = Object.freeze({
    limits: limits,
    parseImport: parseImport,
    progress: progress,
    isTerminal: isTerminal,
    canCancel: canCancel,
    canRetry: canRetry,
    artifactsExpired: artifactsExpired,
    state: state,
    typeLabel: typeLabel,
    importRequest: importRequest,
    idempotencyKey: idempotencyKey
  });

  root.ZumboBulkJobCore = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
