interface TrustedTypesFactory {
  createPolicy(name: string, rules: { createScriptURL(value: string): unknown }): {
    createScriptURL(value: string): unknown;
  };
}

export function trustedServiceWorkerScript(): string {
  const factory = (globalThis as typeof globalThis & { trustedTypes?: TrustedTypesFactory }).trustedTypes;
  if (!factory) return 'ngsw-worker.js';
  const policy = factory.createPolicy('zumbo#service-worker', {
    createScriptURL(value: string): string {
      if (value !== 'ngsw-worker.js') throw new Error('Unexpected service-worker script URL.');
      return value;
    }
  });
  return policy.createScriptURL('ngsw-worker.js') as string;
}
