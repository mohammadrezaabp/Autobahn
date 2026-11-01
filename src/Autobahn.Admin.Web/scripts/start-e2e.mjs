import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const host = path.join(root, 'src/Autobahn.Host');
const assembly = path.join(host, `bin/${process.env.ADMIN_E2E_CONFIGURATION ?? 'Debug'}/net10.0/Autobahn.Host.dll`);
if (!fs.existsSync(assembly)) throw new Error('Build Autobahn.sln before running the browser tests.');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'autobahn-e2e-'));
const child = spawn('dotnet', [assembly], {
  cwd: host,
  stdio: 'inherit',
  windowsHide: true,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://${process.env.ADMIN_E2E_HOST ?? '127.0.0.1'}:${process.env.ADMIN_E2E_PORT ?? '5050'}`,
    ConnectionStrings__GatewayDb: `InMemory:autobahn-e2e-${Date.now()}`,
    Gateway__Bootstrap__Enabled: 'true',
    Gateway__Bootstrap__AdminApiKey: process.env.ADMIN_E2E_KEY ?? 'sk-Autobahn-integration-admin-key',
    Gateway__Bootstrap__KeyPepper: 'autobahn-browser-test-pepper',
    Gateway__Security__KeyPepper: 'autobahn-browser-test-pepper',
    Gateway__ModelsConfigPath: path.join(root, 'config/models.ci.json'),
    Gateway__UpstreamSecretsPath: path.join(temporary, 'upstream-secrets.enc'),
    Gateway__Security__AuditLogPath: path.join(temporary, 'audit.jsonl'),
    Fleet__DataDirectory: path.join(temporary, 'fleet'),
  },
});
function stop() { if (!child.killed) child.kill(); }
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
child.on('exit', code => {
  if (temporary.startsWith(path.join(os.tmpdir(), 'autobahn-e2e-'))) {
    try { fs.rmSync(temporary, { recursive: true, force: true }); } catch { /* Open test handles are cleaned by the OS. */ }
  }
  process.exit(code ?? 0);
});
