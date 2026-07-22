import { resolve } from 'node:path';
import { parseEnvironmentFile, requireArgument } from './qa002-common.mjs';

const environment = parseEnvironmentFile(resolve(requireArgument('--environment')));
const sensitive = Object.entries(environment).filter(([name]) =>
  /(PASSWORD|TOKEN|SECRET|SIGNING_KEY|ROOT_USER|REPLICA_KEY)/.test(name));
if (sensitive.length < 10) throw new Error('Expected synthetic sensitive values were not generated.');
for (const [, value] of sensitive) {
  if (!value || /replace-with|example/i.test(value)) throw new Error('A sensitive environment value is missing or still a placeholder.');
  console.log(`::add-mask::${value}`);
}
console.log(`QA-002 masking registered for ${sensitive.length} synthetic sensitive values.`);
