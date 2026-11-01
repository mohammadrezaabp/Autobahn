# Security policy

Security fixes target the current `main` branch. Run a supported .NET 10 runtime and keep the container host, Docker and NVIDIA tooling patched.

## Report a vulnerability

Use [GitHub private vulnerability reporting](https://github.com/mohammadrezaabp/Autobahn/security/advisories/new). Include the affected commit, reproduction steps, impact and a minimal example with synthetic data. Do not open public issues containing credentials, exploit details, logs with customer data or private host addresses.

## Deployment requirements

- Generate independent gateway keys, key peppers, monitoring credentials and agent tokens. Example configuration contains no usable production credentials. Never reuse the explicit test fixtures outside tests.
- Keep `.env`, local configuration, databases, model files, host inventories, logs, encryption keys and certificates outside Git. The local initialization script writes random credentials to ignored files.
- Terminate HTTPS before exposing the gateway. Restrict admin access to trusted users and networks. Configure only specific trusted reverse proxies and CORS origins. Upstream certificate validation stays enabled.
- Keep agents on a private network or an authenticated encrypted tunnel. A node token permits model and container management; treat it as a privileged host credential. Never expose Docker's socket or agents to the public Internet.
- Keep destructive agent operations and model-server deployment disabled until the node policy is configured. Pin approved model-server images and review plans before applying changes. Download and run only model artifacts you trust.
- Browser admin keys are stored in local storage; use a dedicated browser profile and sign out on shared machines. Fleet sessions use encrypted, HTTP-only, same-site cookies and require the console request header for writes.
- Run a single gateway writer for SQLite state. Back up state and the matching encryption key directories together, with restrictive permissions. Protect backups as credentials.

See [the security model](docs/security.md) for boundaries and operational limitations.

## Repository controls

CI runs tests, architecture and public-type layout checks, dependency audits, secret scanning and CodeQL analysis. Actions use pinned commit hashes and read-only permissions unless a job needs to upload security findings. Contributions use pull requests and owner review; the owner retains direct-push access to `main`.

Automated scans and tests reduce risk; they do not establish that a deployment is free of vulnerabilities. Hardware and infrastructure need separate validation in the intended environment.
