# Configuration

Autobahn uses ASP.NET Core configuration. Environment variables use `__` between sections and array indexes, for example `Fleet__Nodes__0__Id`. Keep real credentials and host addresses outside tracked files.

## Gateway bootstrap

| Setting | Purpose |
|---|---|
| `ConnectionStrings__GatewayDb` | SQLite connection string; default `Data Source=data/autobahn.db` |
| `Gateway__Bootstrap__AdminApiKey` | Initial operator-admin key; only used when the database has no tenants |
| `Gateway__Bootstrap__KeyPepper` | HMAC secret used when bootstrapping the first key |
| `Gateway__Security__KeyPepper` | Matching runtime HMAC and upstream-encryption secret |
| `Gateway__Security__OperatorTenantSlug` | Tenant allowed to operate global settings; defaults to the bootstrap tenant |
| `Gateway__Metrics__ScrapeToken` | Independent bearer token for `/metrics` |
| `Gateway__ModelsConfigPath` | Model route seed/fallback file; default `config/models.json` |
| `Gateway__UpstreamSecretsPath` | Encrypted upstream credentials; default `data/upstream-secrets.enc` |
| `Gateway__Security__AuditLogPath` | Gateway admin audit log |

Initialize local credentials with `dotnet run --project tools/Autobahn.Fleet.Cli -- init`. Initialization preserves existing credentials. Keep the pepper stable: changing it invalidates existing key hashes and upstream encryption. Bootstrap does not rotate an existing database's keys.

The production host requires authentication storage and strong secrets. Do not commit `.env`, local settings, databases or encryption keys. Gateway settings, provider settings and model routes can be administered through the console after initial setup.

## Fleet nodes

```text
Fleet__Nodes__0__Id=node-a
Fleet__Nodes__0__Address=https://node-a.example.com:9455
Fleet__Nodes__0__InferenceAddress=https://node-a.example.com
Fleet__Nodes__0__Token=<same random token as Agent__Token>
```

`Address` is the agent service. `InferenceAddress` supplies the host and scheme used to publish model-server routes; the server's allocated port is applied to it. Use an address reachable by the gateway. Node IDs must be unique. Tokens need at least 32 characters. Node settings take effect after a restart.

`Fleet__DataDirectory` defaults to `data/fleet`. It holds fleet records and the host's Data Protection keys. Polling defaults to 15 seconds, and fleet sessions to 8 hours. Operator keys can change fleet resources; other tenant-admin keys have read access.

## Agent

```text
ASPNETCORE_URLS=http://127.0.0.1:9455
Agent__Name=node-a
Agent__Token=<random token of at least 32 characters>
Agent__ModelDirectory=/srv/autobahn/models
Agent__DataDirectory=/var/lib/autobahn-agent
Agent__MinFreeBytes=10737418240
Agent__AllowDelete=false
Agent__ServerPolicy__Enabled=true
Agent__ServerPolicy__Gpus__0=0
Agent__ServerPolicy__Gpus__1=1
Agent__ServerPolicy__SharedGpus__0=1
Agent__ServerPolicy__Images__0=vllm/vllm-openai
Agent__ServerPolicy__Ports__0=8000
Agent__ServerPolicy__Ports__1=8999
```

Server management and deletion are disabled by default. GPU sharing requires both a host allowance and an explicit shared flag on each server. Image repositories are allowlisted independently of tags. Choose a pinned image version when proposing a server.

`Agent__HuggingFaceToken` supports gated/private model downloads. Peer copies use `Agent__Peers__0__Id`, `Address` and `Token` with the source agent's credentials. Both agents must be reachable over a trusted network or TLS.

The agent stores completed model revisions under `owner/model/commit`, marks partial downloads explicitly, and also lists plain directories of weights. Deployments mount only validated paths under the model directory. It does not automatically convert other model-cache layouts.

## Git-managed inventory

Set `Fleet__GitOps__Url`, `Project`, `Branch` and `Token` to use a GitLab repository containing `inventory/servers/*.json`. `Project` accepts an ID or encoded project path. With Git operations disabled, Autobahn stores reviewed changes locally and exposes an explicit apply action in the fleet console.

Git operations create branches, commits and merge requests; Autobahn does not approve or merge them. After a merge, plan and apply the exact inventory commit with the fleet command. See [deployment](deployment.md).

## Inference

Create an inference key, assign model grants and configure a healthy route in the gateway console. Use the standard OpenAI client base URL `http://127.0.0.1:8080/v1`.

```sh
curl http://127.0.0.1:8080/v1/chat/completions \
  -H "Authorization: Bearer $AUTOBAHN_INFERENCE_KEY" \
  -H 'Content-Type: application/json' \
  -d '{"model":"local-chat","messages":[{"role":"user","content":"Hello"}],"stream":true}'
```

`config/models.example.json` shows a private local upstream. `config/billing.example.json` demonstrates optional scheduled provider pricing; no provider-specific price multiplier is enabled by default.
