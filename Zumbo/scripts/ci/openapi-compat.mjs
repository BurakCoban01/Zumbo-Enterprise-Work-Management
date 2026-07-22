import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const [baselinePath, currentPath] = process.argv.slice(2);
if (!baselinePath || !currentPath) {
  throw new Error('Usage: node openapi-compat.mjs <baseline.json> <current.json>');
}

const baseline = JSON.parse(readFileSync(baselinePath, 'utf8'));
const current = JSON.parse(readFileSync(currentPath, 'utf8'));
assert.equal(baseline.openapi, current.openapi, 'OpenAPI document version changed.');

const failures = [];
const methods = new Set(['get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace']);

for (const [path, baselinePathItem] of Object.entries(baseline.paths || {})) {
  const currentPathItem = current.paths?.[path];
  if (!currentPathItem) {
    failures.push(`Removed path: ${path}`);
    continue;
  }

  for (const [method, baselineOperation] of Object.entries(baselinePathItem)) {
    if (!methods.has(method)) continue;
    const currentOperation = currentPathItem[method];
    if (!currentOperation) {
      failures.push(`Removed operation: ${method.toUpperCase()} ${path}`);
      continue;
    }

    compareParameters(path, method, baselinePathItem.parameters, currentPathItem.parameters);
    compareParameters(path, method, baselineOperation.parameters, currentOperation.parameters);
    compareRequestBody(path, method, baselineOperation.requestBody, currentOperation.requestBody);

    for (const [status, baselineResponse] of Object.entries(baselineOperation.responses || {})) {
      const currentResponse = currentOperation.responses?.[status];
      if (!currentResponse) {
        failures.push(`Removed response ${status}: ${method.toUpperCase()} ${path}`);
        continue;
      }
      compareContentSchemas(
        `response ${status} for ${method.toUpperCase()} ${path}`,
        baselineResponse.content,
        currentResponse.content,
        'response');
    }
  }
}

if (failures.length > 0) {
  throw new Error(`OpenAPI breaking changes detected:\n- ${failures.join('\n- ')}`);
}
console.log(`OpenAPI compatibility passed: ${Object.keys(baseline.paths || {}).length} baseline paths preserved.`);

function compareParameters(path, method, baselineParameters = [], currentParameters = []) {
  for (const baselineParameter of baselineParameters || []) {
    const currentParameter = (currentParameters || []).find(candidate =>
      candidate.name === baselineParameter.name && candidate.in === baselineParameter.in);
    const label = `${baselineParameter.in} parameter '${baselineParameter.name}' for ${method.toUpperCase()} ${path}`;
    if (!currentParameter) {
      failures.push(`Removed ${label}`);
      continue;
    }
    compareSchema(label, baselineParameter.schema, currentParameter.schema, 'request', new Set());
  }

  for (const currentParameter of currentParameters || []) {
    const existed = (baselineParameters || []).some(candidate =>
      candidate.name === currentParameter.name && candidate.in === currentParameter.in);
    if (!existed && currentParameter.required) {
      failures.push(`Added required ${currentParameter.in} parameter '${currentParameter.name}' for ${method.toUpperCase()} ${path}`);
    }
  }
}

function compareRequestBody(path, method, baselineBody, currentBody) {
  const label = `request body for ${method.toUpperCase()} ${path}`;
  if (!baselineBody) {
    if (currentBody?.required) failures.push(`Added required ${label}`);
    return;
  }
  if (!currentBody) {
    failures.push(`Removed ${label}`);
    return;
  }
  if (!baselineBody.required && currentBody.required) failures.push(`Made ${label} required`);
  compareContentSchemas(label, baselineBody.content, currentBody.content, 'request');
}

function compareContentSchemas(label, baselineContent = {}, currentContent = {}, direction) {
  for (const [mediaType, baselineMedia] of Object.entries(baselineContent || {})) {
    const currentMedia = currentContent?.[mediaType];
    if (!currentMedia) {
      failures.push(`Removed media type ${mediaType} from ${label}`);
      continue;
    }
    compareSchema(`${label} (${mediaType})`, baselineMedia.schema, currentMedia.schema, direction, new Set());
  }
}

function compareSchema(label, baselineSchema, currentSchema, direction, visited) {
  if (!baselineSchema || !currentSchema) {
    if (baselineSchema && !currentSchema) failures.push(`Removed schema from ${label}`);
    return;
  }
  if (baselineSchema.$ref !== currentSchema.$ref) {
    failures.push(`Changed schema reference for ${label}: ${baselineSchema.$ref || '(inline)'} -> ${currentSchema.$ref || '(inline)'}`);
    return;
  }
  if (baselineSchema.$ref) {
    const visitKey = `${direction}:${baselineSchema.$ref}:${currentSchema.$ref}`;
    if (visited.has(visitKey)) return;
    visited.add(visitKey);
    const resolvedBaseline = resolveLocalReference(baseline, baselineSchema.$ref);
    const resolvedCurrent = resolveLocalReference(current, currentSchema.$ref);
    if (!resolvedBaseline || !resolvedCurrent) {
      failures.push(`Unresolved schema reference for ${label}: ${baselineSchema.$ref}`);
      return;
    }
    compareSchema(`${label} -> ${baselineSchema.$ref}`, resolvedBaseline, resolvedCurrent, direction, visited);
    return;
  }
  if (baselineSchema.type && baselineSchema.type !== currentSchema.type) {
    failures.push(`Changed type for ${label}: ${baselineSchema.type} -> ${currentSchema.type || '(missing)'}`);
  }
  if (baselineSchema.format && baselineSchema.format !== currentSchema.format) {
    failures.push(`Changed format for ${label}: ${baselineSchema.format} -> ${currentSchema.format || '(missing)'}`);
  }

  const baselineEnum = baselineSchema.enum || [];
  const baselineEnumSet = new Set(baselineEnum);
  const currentEnum = new Set(currentSchema.enum || []);
  if (direction === 'response') {
    for (const value of currentEnum) if (!baselineEnumSet.has(value)) failures.push(`Added response enum value '${value}' to ${label}`);
  } else if (baselineEnum.length > 0) {
    for (const value of baselineEnum) if (!currentEnum.has(value)) failures.push(`Removed accepted request enum value '${value}' from ${label}`);
  }

  const baselineRequired = new Set(baselineSchema.required || []);
  const currentRequired = new Set(currentSchema.required || []);
  if (direction === 'response') {
    for (const name of baselineRequired) if (!currentRequired.has(name)) failures.push(`Response property '${name}' is no longer required in ${label}`);
  } else {
    for (const name of currentRequired) if (!baselineRequired.has(name)) failures.push(`Added required request property '${name}' to ${label}`);
  }

  for (const [name, baselineProperty] of Object.entries(baselineSchema.properties || {})) {
    const currentProperty = currentSchema.properties?.[name];
    if (!currentProperty) {
      failures.push(`Removed property '${name}' from ${label}`);
      continue;
    }
    compareSchema(`${label}.${name}`, baselineProperty, currentProperty, direction, visited);
  }
  compareSchema(`${label}[]`, baselineSchema.items, currentSchema.items, direction, visited);
}

function resolveLocalReference(document, reference) {
  if (!reference.startsWith('#/')) return undefined;
  return reference.slice(2).split('/').reduce((value, segment) => {
    const key = segment.replaceAll('~1', '/').replaceAll('~0', '~');
    return value?.[key];
  }, document);
}
