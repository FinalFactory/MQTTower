# MQTTower

Public, open-source MQTTower. Pull requests are welcome.

## Operations (multi-broker)

- **API key rotation** — On **Brokers** → **Rotate key**, the dashboard tries to push the new key to the agent with `POST /api/agent/key` (authenticated with the previous key). If the agent is unreachable, the database is not updated. After a successful push, align the agent’s `appsettings.json` / environment so restarts keep the same key.

- **Remote broker watchers** — `WatcherEngine` evaluates rules for the broker id that matches the local/default profile. Watchers targeting a **remote** broker profile are **not** evaluated on the dashboard host.

- **Metrics history** — Older metric rows may have `BrokerId` null; per-broker charts filter by broker id and will not include those legacy points. New installs run a migration that backfills `MetricSnapshots.BrokerId` to the default local broker id where it was null.

- **Agent registration** — `MQTTower:RegistrationSecret` still works as a shared token. You can also mint **one-time tokens** in **Settings** (Admin) or via `POST /api/admin/registration-tokens` (returns plaintext once). At `POST /api/agents/register`, the agent may send either the shared secret or a valid one-time token. If the shared secret is **not** set, only one-time tokens are accepted (whitespace-only `RegistrationToken` returns 503).

### Local multi-broker debug (Visual Studio / Rider)

- Open **`MultiBroker.Debug.slnf`** (subset of projects) or **`MQTTower.sln`**. Start Mosquitto on **1883** (for example `docker compose up -d mosquitto` in `docker/`).
- **MQTTower.Web** — use launch profile **`multi-broker-debug`** in `Properties/launchSettings.json` (sets `MQTTower__RegistrationSecret` for agent registration).
- **MQTTower.Agent** — launch profiles **`http-agent-a`** (HTTP **5080**) and **`http-agent-b`** (**5081**). Run two agent processes with different profiles (Rider: multi-select **MQTTower.Web** and **MQTTower.Agent** → **Run Multiple Projects**, then add a second Agent run with the other profile; or two terminals: `dotnet run --project src/MQTTower.Agent/MQTTower.Agent.csproj --launch-profile http-agent-a` / `http-agent-b`). Approve pending brokers under **Brokers** after registration.

---

## mTLS (optional)

**Dashboard → agent (HTTPS):**

- Set `MQTTower:AgentClientCertPath` (and optional `MQTTower:AgentClientCertPassword`) to a client certificate the agent will accept when you enable mutual TLS on the agent.
- Set `MQTTower:AgentTlsServerCaCertPath` to a CA PEM/DER when you want server validation against your own CA instead of only thumbprint or TOFU behavior.

**Agent (Kestrel):**

- Set `Agent:RequireClientCertificate` to `true` and `Agent:TlsCaCertPath` to the CA that issued the dashboard client certificate. The agent must be HTTPS (`Agent:HttpsPort` > 0). If `RequireClientCertificate` is false or the CA path is missing, the agent uses the usual one-way TLS server certificate only.

**Verification:** Automated tests cover `AgentGatewayFactory.ValidateServerCertificate` and related TLS thumbprint checks. End-to-end mTLS between dashboard and agent is verified manually with the certificate paths above.

---

## Dashboard live updates (remote brokers)

For a selected **remote** broker, the Overview page tries the agent `GET /api/stats/stream` SSE stream first; when the stream ends or the agent does not support it, it falls back to polling `GET /api/stats` every 5 seconds. **Broker logs** and **Topic explorer** use `GET /api/logs/stream` and `GET /api/topics/stream` respectively, with REST fallback (logs: `GET /api/logs` every 5s; topics: `GET /api/topics` every 3s).

---

## Automated tests (SSE / DynSec)

- **SSE:** `MQTTower.Infrastructure.Tests` includes `AgentHttpClientSseTests` for `RunStatsStreamLoopAsync`, `RunLogsStreamLoopAsync`, and `RunTopicsStreamLoopAsync` against a mock `text/event-stream` response.
- **DynSec API:** `MQTTower.Web.Tests` includes `DynSecApiControllerTests` for `brokerId` resolution and default-local fallback.

---

## Logging

The web app and agent use **Serilog** with **console** and rolling **file** sinks under `logs/mqttower-web-.log` and `logs/mqttower-agent-.log` (relative to the process base directory). Tune levels via `Serilog` and `Logging` in `appsettings.json`.

---

## Future (Phase 8 — agent-side automation)

The following is **not implemented** and is intentionally out of scope for the current dashboard-hosted automation:

- Running **CronSchedulerService** / **WatcherEngine** logic **on the agent** next to Mosquitto.
- **Pushing** watcher or scheduler events to the dashboard (e.g. `POST /api/events`) or a notification hub.
- Agent-local **SQLite** or replicated state for remote-broker rules.

The dashboard continues to run schedulers and watchers only for the **default local** broker profile; remote-broker watchers remain data-only until Phase 8.

## License

See [LICENSE](LICENSE) (MIT unless otherwise stated).
