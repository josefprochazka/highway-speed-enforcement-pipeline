# Highway Speed Enforcement Pipeline

A prototype of a real‑time speed‑enforcement pipeline for a stretch of urban
highway fitted with **10 sequential speed cameras**. It is the solution to the
*Senior C# / .NET* coding assignment for **Yunex s.r.o. – Traffic Tools**
(the original brief is in [`assignment/`](assignment/)).

The system is three decoupled applications:

| # | Application | Project | Runs as | Talks to |
|---|-------------|---------|---------|----------|
| 1 | **Telemetry Data Generator** | `src/HighwaySpeed.Generator` | .NET console app | → Processor over **gRPC** |
| 2 | **Telemetry Data Processor** | `src/HighwaySpeed.Processor` | ASP.NET Core service in **Docker** | ingests gRPC, serves **REST** |
| 3 | **Desktop Client** | `src/HighwaySpeed.Client` | **WPF** app on Windows | ← Processor over **REST** |

```
┌────────────────────────┐   gRPC (HTTP/2, :5081)   ┌────────────────────────────┐   REST (HTTP/1.1, :5080)   ┌────────────────────┐
│  Telemetry Generator   │  client‑streaming        │     Telemetry Processor     │   GET /api/traffic‑        │    WPF Dashboard    │
│  (console)             │ ───────────────────────► │     (Docker container)      │        snapshot           │    (desktop)       │
│                        │  TelemetryMessage        │                            │ ◄──────────────────────── │                    │
│  · 100 new vehicles/s  │  { plate, ticks,         │  · concurrent ingestion    │   TrafficSnapshot DTO     │  · polls every 3 s │
│  · advance 1 camera/s  │    speed, cameraId }     │  · 10 per‑camera Top‑10     │   { 10 camera boards,     │  · global board    │
│  · emit 10 msgs/vehicle│                          │  · 1 global average Top‑10  │     global board,         │  · per‑camera board│
└────────────────────────┘                          │  · thread‑safe, lock‑light │     generatedAtUtc }      │    (camera picker) │
                                                    └────────────────────────────┘                           └────────────────────┘
```

---

## Table of contents

- [Quick start](#quick-start)
- [How each part works](#how-each-part-works)
- [Design decisions & rationale](#design-decisions--rationale)
- [Interpreting the ambiguous bits of the spec](#interpreting-the-ambiguous-bits-of-the-spec)
- [Project layout](#project-layout)
- [Testing](#testing)

---

## Quick start

### Prerequisites

- **.NET SDK 9.0** (`dotnet --info` should list a 9.0.x SDK)
- **Docker Desktop** (for the processor container)
- **Windows** (for the WPF client)

### 1. Run the processor in Docker

```powershell
docker compose up --build
```

This starts the `processor` container – REST on <http://localhost:5080>, gRPC on
`localhost:5081`. Per the spec, only the processor is a Docker service; the
generator and client both run natively on the host.

Useful URLs once it is up:

- `http://localhost:5080/api/traffic-snapshot` – the raw snapshot JSON
- `http://localhost:5080/scalar/v1` – interactive API reference
- `http://localhost:5080/health` – liveness probe

### 2. Run the generator (on the host)

```powershell
dotnet run --project src/HighwaySpeed.Generator
```

It immediately starts streaming ~1 000 telemetry messages per second to the
processor at `http://localhost:5081` (the default in `appsettings.json`, no
configuration needed).

### 3. Run the WPF client (on the host)

```powershell
dotnet run --project src/HighwaySpeed.Client
```

The dashboard polls every 3 seconds and shows the global Top 10 plus a per‑camera
Top 10 with a camera picker.

### Run everything without Docker (for debugging)

```powershell
# terminal 1 – processor
dotnet run --project src/HighwaySpeed.Processor

# terminal 2 – generator
dotnet run --project src/HighwaySpeed.Generator

# terminal 3 – client
dotnet run --project src/HighwaySpeed.Client
```

The generator defaults to `http://localhost:5081`, the client to
`http://localhost:5080`, so no configuration is needed for the local case.

---

## How each part works

**Generator** (`HighwaySpeed.Generator`) – `VehicleRegistry` is a single‑threaded
state machine that runs one tick per second: spawn 100 new vehicles, advance
every active vehicle's camera counter by 1 and emit a message for it, then drop
vehicles that just passed camera 10. Each driver's speed wanders around
130 km/h. Messages go through a bounded `Channel` and a single gRPC
client‑streaming call to the processor, reconnecting with back‑off if the
stream drops.

**Processor** (`HighwaySpeed.Processor`) – ASP.NET Core service with two ports:
gRPC (`:5081`) for ingest, REST (`:5080`) for `GET /api/traffic-snapshot`.
Incoming messages flow through a bounded `Channel` into a worker pool that
updates `TrafficAnalytics`:

- 10 per‑camera Top 10 boards (instantaneous speed), one lock each.
- One running average per vehicle in a lock‑free `ConcurrentDictionary` – the
  only hot per‑vehicle path, so it's the one piece that's lock‑free.
- One global Top 10 board (single lock) for vehicles that have passed ≥ 3
  cameras.
- Ties on any board are broken alphabetically by plate.

**Client** (`HighwaySpeed.Client`) – WPF, hand‑rolled MVVM (no framework, so
every line is visible). A background loop polls the REST endpoint every 3
seconds; the only UI‑thread touch is applying the new snapshot to the bound
collections.

---

## Design decisions & rationale

- **gRPC, client‑streaming, generator → processor.** The generator emits a
  high‑frequency, uniform stream (~1 000 msg/s); one long‑lived streaming call
  keeps per‑message overhead near zero and gives a typed, versioned contract
  (`telemetry.proto`). REST would mean batching hacks; a message broker would
  add an extra container for no real benefit here.
- **Locks, not lock‑free, for the boards.** The spec allows either. Each
  camera board's critical section is tiny (a handful of comparisons) and the
  10 boards never contend with each other, so a lock per board is simpler and
  obviously correct. The one genuinely hot path – the per‑vehicle running
  average – *is* lock‑free (`ConcurrentDictionary` + an immutable value).
- **Bounded `Channel<T>` at both buffering points.** The generator sheds the
  oldest telemetry when full (it's lossy by nature); the processor *waits*
  when full, turning back‑pressure into the gRPC client instead of growing
  memory without bound.
- **Shared `Contracts` project.** Holds the REST DTOs and the `.proto` file,
  so the WPF client can reference the DTOs without depending on gRPC.
- **`TimeProvider` for all timing**, so both loops and the snapshot timestamp
  are deterministic in tests.

---

## Interpreting the ambiguous bits of the spec

The brief leaves a few things open. Each choice below is isolated in one place
and covered by a test.

- **The 11th tick.** Taken literally, a vehicle at counter 10 would advance to
  11 and emit an invalid "Camera 11" message before being dropped. The brief's
  own note fixes each vehicle at exactly 10 messages, cameras 1–10, so a
  message is only emitted while the counter is 1…10; the vehicle is evicted on
  the following tick. (`VehicleRegistryTests`)
- **Global board = "Live Active Traffic Only".** It holds only vehicles still
  on the stretch (counter 1…10) that have passed ≥ 3 cameras. Passing camera
  10 removes a vehicle from it immediately. (`TrafficAnalyticsTests`)
- **The purge, "if it does not score on any leaderboard".** On a vehicle's
  camera‑10 reading, its running‑average state is purged unless its plate
  still occupies a slot on one of the 10 camera boards – that record is the
  enforcement evidence, so it's kept. (`TrafficAnalyticsTests`)

---

## Project layout

```
HighwaySpeed.sln
├─ Directory.Build.props            shared MSBuild settings (LangVersion, Nullable, …)
├─ docker-compose.yml               processor (the only Docker service per spec)
├─ assignment/                      the original PDF brief
├─ src/
│  ├─ HighwaySpeed.Contracts/       REST DTOs + Protos/telemetry.proto  (no dependencies)
│  ├─ HighwaySpeed.Generator/       console app
│  │  ├─ Simulation/                VehicleRegistry, SpeedModel, NumberPlateFactory, TrafficSimulator
│  │  └─ Publishing/                GrpcTelemetryPublisher (+ ITelemetryPublisher)
│  ├─ HighwaySpeed.Processor/       ASP.NET Core service + Dockerfile
│  │  ├─ Ingestion/                 gRPC service + bounded worker‑pool pipeline
│  │  └─ Analytics/                 CameraSpeedBoard, VehicleAverageTracker, GlobalAverageBoard, TrafficAnalytics
│  └─ HighwaySpeed.Client/          WPF dashboard
│     ├─ Infrastructure/            ObservableObject, RelayCommand
│     ├─ Services/                  TrafficApiClient
│     └─ ViewModels/                DashboardViewModel
└─ tests/
   ├─ HighwaySpeed.Generator.Tests/   plate uniqueness, speed model, the state machine
   └─ HighwaySpeed.Processor.Tests/   each board, the analytics rules, a concurrency stampede, a REST integration test
```

---

## Testing

```powershell
dotnet test
```

**37 tests**, xUnit. Highlights:

- `VehicleRegistryTests` – the spec state machine: 100 in/out per tick, steady
  state of 10 cohorts, every vehicle captured once per camera 1→10 then gone,
  no camera id ever outside 1–10, deterministic under a fixed seed.
- `CameraSpeedBoardTests` / `GlobalAverageBoardTests` – bounded to 10, ordered,
  alphabetical tie‑break, in‑place update per plate.
- `VehicleAverageTrackerTests` – the 3‑camera gate, cumulative average maths,
  purge.
- `TrafficAnalyticsTests` – gate, "live active only", the conditional purge on
  camera 10 (scoring vs non‑scoring vehicle).
- `TrafficAnalyticsConcurrencyTests` – 16 threads × 20 000 readings and a
  targeted stampede on one camera; asserts every invariant still holds and the
  true Top 10 survives.
- `TrafficSnapshotApiTests` – boots the processor in‑memory
  (`WebApplicationFactory`) and checks the REST contract.
