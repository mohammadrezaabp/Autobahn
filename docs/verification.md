# Verification

The repository carries tests for gateway compatibility, streaming, tenant isolation, grants, policy, billing, persistence, observability, registry mutations and module boundaries. Fleet tests cover deployment/download state transitions, allocation policies, stale revisions, durable records, unsafe paths, checksum-verified downloads and console authorization.

Run `scripts/Test.ps1` to build/typecheck/test both frontends, build the .NET solution, enforce one public type per C# file and run the .NET suite. For browser flows, build the solution and both consoles, then run `npm run test:e2e` in `src/Autobahn.Admin.Web`. The browser server uses isolated development state and starts automatically.

Build order is gateway console followed by fleet console. Test model routes live in explicit fixtures and are not enabled for a fresh installation.

Hardware validation is separate: verify NVIDIA readings, model/image compatibility, container startup, actual streaming throughput, cancellation behavior and a transfer between configured hosts before operational rollout.
