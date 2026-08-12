interface TrustedHtmlPolicyFactory {
  createPolicy(name: 'default', rules: { createHTML(value: string): string }): unknown;
}

const forbiddenSvg = /<\s*(?:script|foreignObject|iframe|object|embed)\b|\son[a-z]+\s*=|javascript\s*:/i;

export function installIoniconsTrustedTypesPolicy(): void {
  const factory = (globalThis as typeof globalThis & { trustedTypes?: TrustedHtmlPolicyFactory }).trustedTypes;
  if (!factory) return;
  factory.createPolicy('default', {
    createHTML(value: string): string {
      const markup = iconMarkup(value);
      if (!/^<svg(?:\s|>)/i.test(markup) || forbiddenSvg.test(markup)) {
        throw new TypeError('Only sanitized Ionicons SVG content is accepted.');
      }
      return value;
    }
  });
}

function iconMarkup(value: string): string {
  const trimmed = value.trim();
  if (!trimmed.startsWith('data:image/svg+xml')) return trimmed;
  const separator = trimmed.indexOf(',');
  if (separator < 0) return '';
  try {
    return decodeURIComponent(trimmed.slice(separator + 1)).trim();
  } catch {
    return '';
  }
}
