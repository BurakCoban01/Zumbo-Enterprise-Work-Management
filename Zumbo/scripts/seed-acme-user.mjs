#!/usr/bin/env node
import { randomBytes, pbkdf2Sync } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { execSync } from 'node:child_process';

const PASSWORD = 'AcmeAdmin2026!';
const iterations = 210000;
const salt = randomBytes(16);
const hash = pbkdf2Sync(PASSWORD, salt, iterations, 32, 'sha256');
const saltB64 = salt.toString('base64');
const hashB64 = hash.toString('base64');
const stamp = randomBytes(16).toString('hex');
const userId = randomBytes(16).toString('hex');

const mongoScript = `
var d = db.getSiblingDB("ZumboIdentity");
d.users.insertOne({
  _id: "${userId}",
  Username: "acmeadmin",
  Email: "admin@acme.local",
  OrganizationId: "acme-tech",
  PasswordHash: "PBKDF2-SHA256$${iterations}$${saltB64}$${hashB64}",
  IsActive: true,
  SecurityStamp: "${stamp}",
  FailedLoginCount: 0,
  LockedUntil: null,
  PasswordResetTokenHash: null,
  PasswordResetTokenExpiresAt: null,
  MfaEnabled: false,
  MfaSecretProtected: null,
  PendingMfaSecretProtected: null,
  PendingMfaExpiresAt: null,
  MfaRecoveryCodeHashes: [],
  Roles: ["User","SystemAdmin"],
  RefreshTokens: [],
  CreatedAt: new Date(),
  Version: { high: 0, low: 1 }
});
var u = d.users.findOne({Email:"admin@acme.local"},{_id:1,Email:1,OrganizationId:1,Roles:1});
printjson(u);
`;

writeFileSync('/tmp/acme-user.mjs', mongoScript);

console.log('👤 Acme admin kullanıcı oluşturuluyor...');
try {
  execSync(`docker cp /tmp/acme-user.mjs zumbo-local-mongo-1:/tmp/acme-user.mjs`, { stdio: 'pipe' });
  execSync(`docker exec zumbo-local-mongo-1 mongosh --quiet --file /tmp/acme-user.mjs`, { stdio: 'inherit' });
  console.log('\n✅ admin@acme.local / AcmeAdmin2026! (acme-tech)');
} catch (e) {
  console.error('❌ Hata:', e.message);
  process.exit(1);
}
