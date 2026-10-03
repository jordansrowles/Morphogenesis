# Visual laboratory

The visual laboratory runs canonical Morphogenesis experiments through the same serial simulation used by the headless runner. It adds session control, frame display and visual recording around the simulation without moving scientific rules into a user interface.

## Architecture

`Rowles.Morphogenesis.Laboratory` is a namespace in the main `Rowles.Morphogenesis` assembly and package. It owns UI-neutral sessions, measurements, frame publication, cell inspection, and visual recording/playback.

`Rowles.Morphogenesis.Server` is a tooling host under `tools/`. It serves the web interface, the canonical experiment catalogue, session APIs, recordings and SQLite persistence. The web interface talks to the same session service used by other clients. `Rowles.Morphogenesis.Desktop` is an Avalonia tooling host under `tools/`; it is a remote client that uses HTTP and WebSocket and has no project reference to the simulation package.

The canonical catalogue is shipped from `experiments/canonical/`. Clients choose an experiment and replicate index; the server validates the request and constructs the canonical serial run. Neither client chooses a kernel or implements seeding, initialisation, energy, connectivity or acceptance rules.

## Start the applications

From the repository root, start the server:

```sh
dotnet run --project tools/Rowles.Morphogenesis.Server --configuration Release
```

The default address is `http://127.0.0.1:5080`. Open that address in a browser to use the web laboratory.

In another terminal, start the desktop client:

```sh
dotnet run --project tools/Rowles.Morphogenesis.Desktop --configuration Release
```

The desktop client defaults to `http://127.0.0.1:5080`. Change and save its server address in the connection preferences to use another server. The desktop client does not run a local simulation.

## Experiments and sessions

The experiment screen lists the canonical manifests. Choose an experiment, a valid replicate index, whether to record frames, and the live publication rate, then create a session.

Sessions move through these states:

| State | Meaning |
|---|---|
| Created | Ready to start. A Created session expires after one hour of inactivity. |
| Running | The canonical serial kernel is advancing complete Monte Carlo steps (MCS). |
| Paused | The simulation is stopped at a completed MCS and can resume or step once. An inactive Paused session is cancelled after 24 hours. |
| Completed | The target MCS was reached. |
| Cancelled | The run was stopped or cancelled by resource policy. |
| Failed | The simulation failed. Check the displayed failure detail and server logs. |
| Interrupted | The server restarted while this session was live. It cannot be resumed. |

Terminal state and recording finalisation are separate boundaries. A terminal session with an active recording is shown as **Finalising** until the recorder writes its final frame and completes or fails. The server then persists the authoritative terminal snapshot and disposes the live simulation owner. The SQLite run history remains available for the configured 30-day terminal retention period.

After a server restart, persisted Created, Running and Paused sessions are marked Interrupted because their in-memory owners no longer exist. An Active recording is marked Failed with a stable recovery reason; frames already written remain available. Already-terminal runs keep their simulation status, while any Active recording is marked Failed.

MCS counts completed Monte Carlo steps. It is simulation time, not physical time.

Start, pause, resume, step and stop commands carry a fresh command ID and the revision currently shown by the client. If another client has changed the session, the server returns HTTP 409 with authoritative state. The client refreshes its controls and reports the conflict; review the new state before issuing another command.

The session workspace displays the lattice, status, current and target MCS, revision, recording state, and scientific metrics. Available views show cell identity, cell type, or four-neighbour boundaries. Zoom, pan and cell selection stay in the client. Selecting a positive cell ID requests its type, area, perimeter and target parameters from the server.

## Troubleshooting

- **Interrupted session:** the server restarted and its in-memory simulation was lost. The stored run and completed recording frames remain historical data; create a new session to run again.
- **Failed recording:** the simulation state is reported separately. Review the recording failure detail and server logs; already-written frames remain readable where the recording format permits.
- **HTTP 409 command conflict:** another client advanced the session revision. Use the authoritative state returned with the response, refresh the page if needed, then issue a new command with a fresh command ID.

## Live frames and metrics

The web and desktop clients receive presentation-only full frames. A frame contains cell IDs, an MCS, a sequence number and its dimensions. Static cell-type and boundary metadata comes from the session API. The WebSocket starts with the latest authoritative frame, then publishes recent full frames. Slow clients may miss intermediate displays; they do not slow the simulation. Disconnecting a client does not pause or stop a run.

Live frames use binary protocol version 1, with message type 1 for a full frame. Historical frames use the same wire layout through the recording HTTP endpoint. The initial routes have no `/v1` prefix; future incompatible protocol changes require an explicit version or migration and must not reinterpret stored data.

Metrics are labelled by MCS and include heterotypic interface fraction, mean cell area, mean cell perimeter, and type A/B counts. Display rate controls frame publication only; they do not change simulation or measurement cadence.

## Recording and playback

Recording is optional and is off by default. It stores visual playback data, not a deterministic replay log or restart checkpoint. Deterministic reruns use the experiment manifest, replicate index and canonical serial kernel; a visual recording cannot resume a simulation after a server restart.

The initial recording contract is:

| Item | Value |
|---|---|
| Recording schema | 1 |
| MessagePack envelope | 1 |
| Keyframe codec | 1 |
| Delta codec | 1 |
| Compression | MessagePack LZ4 Block Array |
| Keyframe lattice values | Signed 32-bit cell IDs, little-endian |
| Default recording cadence | Every 5 MCS, plus initial and final frames |
| Keyframe cadence | Every 20 recorded frames |
| Delta promotion | Use a keyframe when the raw delta is at least 75% of a raw keyframe payload |

A keyframe contains the full lattice. A delta contains sorted changed-site index differences and the resulting cell IDs encoded as unsigned varints. The server reconstructs requested historical frames; clients do not decode the database envelope. The player offers play, pause, forward/backward step, seek and rates from 0.25× through 4×. It keeps only the previous, current and next reconstructed frames in its client cache.

If a recording fails or reaches a storage limit, the session reports the recording failure separately. The simulation remains governed by its own session state.

Historical frame requests reconstruct only the requested frame through a pooled workspace. The server limits simultaneous reconstructions to two, including the time needed to write each response.

If a simulation command throws while applying at an MCS boundary, the server returns HTTP 500 with the authoritative Failed session state and the failure detail. The desktop client refreshes to that terminal state.

## Server API and protocol versions

The current route set is the initial stable visual-laboratory API. It is intentionally not prefixed with `/v1`.

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Server/database health |
| GET | `/api/info` | Server and protocol information |
| GET | `/api/diagnostics` | Aggregate operational diagnostics |
| GET | `/api/experiments` | List canonical experiments |
| GET | `/api/experiments/{id}` | Read a canonical manifest |
| POST | `/api/sessions` | Create a session |
| GET | `/api/sessions?take={count}` | List recent sessions |
| GET | `/api/sessions/{id}` | Read authoritative session state |
| POST | `/api/sessions/{id}/start` | Start a Created session |
| POST | `/api/sessions/{id}/pause` | Pause at an MCS boundary |
| POST | `/api/sessions/{id}/resume` | Resume a Paused session |
| POST | `/api/sessions/{id}/step` | Run one MCS while Paused |
| POST | `/api/sessions/{id}/stop` | Stop a live session |
| GET | `/api/sessions/{id}/metrics` | Read persisted/live metric samples |
| GET | `/api/sessions/{id}/cells/{cellId}` | Inspect a cell |
| GET | `/api/sessions/{id}/recording` | Read recording metadata and frame index |
| GET | `/api/sessions/{id}/recording/frame?mcs={mcs}` | Reconstruct a historical frame |
| GET | `/api/sessions/{id}/stream` | Receive full frames over WebSocket |
| GET | `/api/sessions/{id}/result` | Read the terminal summary |

The canonical kernel identity is `canonical-serial-v1`. SQLite uses `PRAGMA user_version = 1`. Recording schema, envelope, keyframe and delta codecs are all version 1. Existing files and frame payloads must not be silently reinterpreted if a later version is introduced.

## Data, configuration and deployment

By default, relative to the server working directory:

- SQLite data is in `./data/laboratory.db`;
- rolling logs are in `./logs/`;
- the canonical manifest catalogue is loaded from the server's `experiments/canonical/` directory.

The server reads options from the `Laboratory` configuration section. Environment variables use double underscores, for example:

```sh
Laboratory__BindUrl=http://127.0.0.1:5080
Laboratory__DataDirectory=./data
Laboratory__LogDirectory=./logs
Laboratory__DatabaseFileName=laboratory.db
```

SQLite uses WAL journalling, NORMAL synchronous mode and a 5,000 ms busy timeout. The server serialises database writes through a bounded queue. For Debian service installation, directory ownership, systemd commands and trusted-LAN binding, see [`../deploy/systemd/README.md`](../deploy/systemd/README.md).

The server has no authentication. Keep the default loopback binding for local use. If remote access is needed, use only a trusted network with an appropriate firewall or reverse proxy. Do not expose the server to the public internet. The desktop client warns when using unauthenticated HTTP outside loopback.

## Resource limits

The server enforces these default maxima:

| Resource | Limit |
|---|---:|
| Grid width and height | 512 × 512 |
| Grid sites | 262,144 |
| Running sessions | 4 |
| Paused sessions | 8 |
| Resident live sessions total | 16 |
| Live subscribers per session | 8 |
| Concurrent historical frame reconstructions | 2 |
| Live publication rate | 20 FPS (10 FPS default) |
| Session command queue | 64 requests |
| Reusable live-frame buffers per session | 10 |
| Recording writer queue | 8 frames |
| SQLite writer queue | 128 writes |
| Manifest JSON | 1 MiB |
| HTTP request body | 2 MiB |
| Recording per run | 2 GiB |
| Stored recordings | 20 GiB total |
| Persisted sessions | 500 |
| Created-session idle lifetime | 1 hour |
| Paused-session idle lifetime | 24 hours |
| Persisted terminal history | 30 days |

Resident capacity includes Created, Running and Paused sessions. Completed, Failed, Interrupted and Cancelled live owners are removed after recording finalisation and terminal persistence; persisted history retention is independent. The server rejects dimensions, request bodies, queue configurations and recording writes beyond their supported limits. Capacity and stale-revision errors leave simulation state under the session owner.

## Extension boundary

The base visual frame remains lattice cell IDs with its sequence and MCS, accompanied by static session metadata and scientific metric samples. Future cell-lifecycle work can add explicit lifecycle metadata or events for creation, death and division; it need not place complete per-cell metadata in every frame. Future scalar-field work can add explicit field-layer metadata and field channels. Neither extension changes lattice ownership or session command semantics.
