# Contributing to Autobahn

Open an issue for a proposed change, then submit a focused pull request from a feature branch or fork. Only the repository owner can push directly to `main`. All files are owned by `@mohammadrezaabp` for review.

## Development

Install the .NET 10 SDK, Node.js 22 and PowerShell 7. Run `./scripts/Start-Local.ps1` to initialize local credentials and start the workspace. Never put those credentials in an issue, commit or test fixture.

Run `./scripts/Test.ps1` before submitting. Browser checks run with `npm run test:e2e` in `src/Autobahn.Admin.Web` after the .NET solution and consoles are built. They use an isolated local server and synthetic credentials.

Follow [AGENTS.md](AGENTS.md) and [the architecture guide](docs/architecture.md): Domain has no framework dependencies; Application depends only on Domain; adapters depend inward. Put invariants in domain models, use cancellation tokens for asynchronous I/O, and put each public C# type in its own file.

Add meaningful coverage for changed behavior, especially tenant isolation, authorization, streaming, state transitions and failures. Keep dependency versions centralized and lockfiles up to date. Do not submit generated bundles, model data, environment-specific inventories or database files.

Report vulnerabilities through [the security policy](SECURITY.md), not public issues.
