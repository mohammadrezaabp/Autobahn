# Deployment

## Gateway

Use `docker compose up --build -d` after running the initialization command in the README. The gateway image runs as the non-root application user and keeps its state in `/app/data`. Its published port is bound to loopback by default.

For a binary deployment:

```sh
cd src/Autobahn.Admin.Web && npm ci --ignore-scripts && npm run build
cd ../Autobahn.Fleet.Web && npm ci --ignore-scripts && npm run build
cd ../..
dotnet publish src/Autobahn.Host -c Release -p:SkipFrontendBuild=true -o artifacts/gateway
```

The normal publish target builds both consoles itself when `SkipFrontendBuild` is omitted. Keep their build order: the gateway console owns the parent asset directory, and fleet assets are built into its child directory.

Set environment variables as described in [configuration](configuration.md). Supply an empty `config/models.json` on first installation, or register routes through the console. Static credentials and model routes are not embedded in the release.

Use a TLS reverse proxy when exposing the gateway. Configure explicit trusted proxies before accepting forwarded headers. Restrict metrics and agent access. Keep one gateway instance for a given SQLite database; this deployment does not provide coordinated multi-writer scaling.

## GPU host agent

```sh
dotnet publish src/Autobahn.Agent -c Release -o artifacts/agent
```

Copy the output to `/opt/autobahn-agent`, create `/var/lib/autobahn-agent` and `/srv/autobahn/models`, and configure `/etc/autobahn/agent.env` using `deploy/systemd/agent.env.example`. Install the provided systemd unit after reviewing its paths and access. The agent requires Docker CLI/daemon access and NVIDIA management tools on GPU hosts.

The alternative `agent` Docker target includes Docker CLI. Running it requires access to the host Docker socket, the same absolute model-store paths inside and outside the agent container, persistent agent state and NVIDIA tooling. The native systemd service avoids those path-mapping concerns.

The agent controls model containers on its host. Its token is separate from gateway API keys. Start with deletion and model-server management disabled, verify status/storage readings, then enable the intended GPU and image policy.

## Reviewed local deployment

1. Add a node and restart the gateway.
2. Download a model or place a plain weight directory in the model store.
3. Describe a server from the Models or GPUs page.
4. Review the dry-run result and save the change request.
5. Use **Apply reviewed change** on the GPUs page.

The agent enforces its policy again, starts a container and waits for the expected model to answer. Autobahn then stores its upstream credential encrypted and publishes a managed backend in the gateway registry. Health checks continue to gate inference traffic.

For replacements, the affected backend is removed before its container is stopped. Existing client requests can be interrupted by removal. Failed deployments do not automatically restore removed containers; inspect the activity log and prepare a new reviewed change.

The vLLM launcher uses `vllm serve` and `VLLM_API_KEY` with an agent-owned environment file. See the [vLLM quickstart](https://docs.vllm.ai/en/latest/getting_started/quickstart/) for its authentication and serving interface. Pin and validate the runtime image against your GPU and model before deployment.

## Git-managed deployment

The inventory repository must contain `inventory/servers/`. Store one JSON specification per server with `name`, `host`, `model`, `revision`, `gpus`, `port`, `image`, `servedName`, `args`, `apiKey` and `shared`.

Use the console to open a merge request. After review and merge:

```sh
# Set AUTOBAHN_OPERATOR_KEY securely in the command's environment.
dotnet run --project tools/Autobahn.Fleet.Cli -- plan --url https://gateway.example.com
dotnet run --project tools/Autobahn.Fleet.Cli -- apply --url https://gateway.example.com --commit <planned-commit>
```

Apply rechecks the branch after preflight and reconciles managed containers only. A changed commit or allocation conflict refuses the operation. The command can also produce read-only import candidates from an existing host:

```sh
dotnet run --project tools/Autobahn.Fleet.Cli -- import --url https://gateway.example.com --node node-a
```

Review imported candidates' paths, arguments, ports and GPU assignments before proposing them. Import does not stop or adopt running containers automatically.

## Operations

`/health/live` reports process liveness. `/health/ready` and `/health` report gateway readiness. An empty registry may be unready while the consoles remain available. `/metrics` requires the configured scrape token or operator authorization.

The optional Compose monitoring profile provisions gateway dashboards and alert rules. OTel, Loki, Tempo and Alloy configuration examples are under `deploy/`; enable and configure those services separately if required.

Back up the gateway database, encrypted upstream file, fleet database and Data Protection key directories together. Agent records and keys also need persistent storage. Keep host clocks synchronized for quotas, audit timestamps and session expiration.

GPU container launches and real multi-host transfers require a configured hardware environment. Local automated tests use isolated stores and simulated upstream responses.
