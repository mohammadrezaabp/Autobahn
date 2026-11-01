# Security model

Autobahn has three trust boundaries: client to gateway, operator to control plane, and gateway to GPU agent. An operator can reconfigure routes and deploy containers. Treat this role as infrastructure administration.

## Identity and tenant isolation

Gateway credentials are hashed with a configured pepper; plaintext issuance is a one-time operation. Tenant contexts, model grants, provider policies and administrative roles are enforced server-side. Inference credentials cannot open the fleet administration session. Tenant-admin keys manage their tenant; shared compute mutations require an operator key. A short validation cache means revocation can take up to `Gateway:Security:CacheTtlMinutes` to propagate.

The web workspace stores the gateway key in browser local storage. The separate fleet API session is encrypted with ASP.NET Data Protection and uses an HTTP-only, same-site cookie. Switching keys and signing out clear that session. State-changing fleet calls also require a custom request header; sign-in is rate limited. Serve the console over HTTPS on a trusted origin and avoid shared browser profiles.

## Agent authority

Each agent has an independent token of at least 32 characters and defaults to loopback. Tokens are compared in constant time. Agents access local model files and Docker; possession of a node token carries substantial host authority. Use network isolation and encrypted transport between machines. The application does not establish an encrypted network for you.

Path validation rejects traversal and symbolic links. Model deletion is disabled by default and checks serving models, running mounts and downloads. Deployment policies restrict approved images, GPU assignments, ports and reserved arguments. Inventory revisions prevent applying stale plans. Test these rules against the actual host and filesystem before production use.

## Transport and secrets

Upstream TLS verification is enabled. Configure known proxies before trusting forwarded headers, and allow only explicit browser origins. Console responses include a same-origin Content Security Policy, frame denial, no-sniff and no-referrer headers. Metrics require a token unless anonymous scraping is explicitly enabled.

Use ignored local settings, environment injection or your deployment's secret manager. Upstream credentials are encrypted at rest; model-server keys use Data Protection and temporary restricted environment files. Database backups and encryption keys must be access-controlled and backed up together. Debug logs, traces, CI artifacts and API examples can contain sensitive input: use synthetic data and review before sharing.

## Validation and maintenance

CI checks dependency advisories at all levels, runs CodeQL, scans Git history with Gitleaks, rejects private/generated files, and exercises unit, integration, architecture and browser tests. Dependency updates arrive as pull requests. Workflows do not deploy automatically or use production secrets.

These controls do not verify the security of downloaded model code, NVIDIA drivers, third-party containers, reverse proxies or a running host. Keep those patched and validate hardware-backed operations separately. See [SECURITY.md](../SECURITY.md) for reporting and deployment requirements.
