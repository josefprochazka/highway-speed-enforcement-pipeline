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
- [What I'd do next](#what-id-do-next)

---

## Quick start

### Prerequisites

- **.NET SDK 9.0** (`dotnet --info` should list a 9.0.x SDK)
- **Docker Desktop** (for the processor container)
- **Windows** (for the WPF client)

### 1. Run the backend (generator + processor) in Docker

```powershell
docker compose up --build
```

This starts:

- `processor` – REST on <http://localhost:5080>, gRPC on `localhost:5081`
- `generator` – immediately starts streaming ~1 000 telemetry messages per second

Useful URLs once it is up:

- `http://localhost:5080/api/traffic-snapshot` – the raw snapshot JSON
- `http://localhost:5080/scalar/v1` – interactive API reference
- `http://localhost:5080/health` – liveness probe

### 2. Run the WPF client (on the Windows host)

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

### 1. Telemetry Data Generator (`HighwaySpeed.Generator`)

A `Host`‑based console app. The heart is `VehicleRegistry` – a deliberately
**single‑threaded, predictable state machine** that runs the three spec steps in
order once per second (`TrafficSimulator`, a `BackgroundService`, drives the
clock with a `PeriodicTimer`):

1. **Influx** – `SpawnNewVehicles(100)`: 100 vehicles with fresh unique plates,
   camera counter `0`.
2. **Progression & emission** – `AdvanceAndCapture()`: every vehicle's counter
   `+= 1`; it produces one `CameraCapture`. A vehicle whose counter has just
   moved past 10 does **not** emit (see
   [interpretation notes](#interpreting-the-ambiguous-bits-of-the-spec)).
3. **Exit** – `RemoveDepartedVehicles()`: drop everything with counter `> 10`.

Supporting pieces:

- **`NumberPlateFactory`** – plates are a deterministic bijection of an
  incrementing serial (`2AB-1041` shape), so uniqueness is guaranteed by
  construction with zero bookkeeping.
- **`SpeedModel`** – each driver draws a personal *cruising speed* around the
  130 km/h limit; between cameras the speed does a mean‑reverting random walk
  (Box–Muller Gaussian noise, clamped). Values wander realistically without
  drifting off.
- **`GrpcTelemetryPublisher`** – the simulation loop only writes into a
  **bounded in‑memory outbox** (`Channel`), so it never blocks on the network. A
  background pump drains the outbox into a single long‑lived gRPC
  **client‑streaming** call and **reconnects with capped exponential back‑off**
  if the stream drops.

### 2. Telemetry Data Processor (`HighwaySpeed.Processor`)

ASP.NET Core, two Kestrel endpoints on two ports:

- **:5081 – gRPC / HTTP‑2** – `TelemetryIngestService.PublishTelemetry` reads the
  client stream and hands each message to `TelemetryIngestPipeline`.
- **:5080 – REST / HTTP‑1.1** – `GET /api/traffic-snapshot` returns the
  `TrafficSnapshot` DTO.

**Ingestion pipeline** (`TelemetryIngestPipeline`)

```
gRPC stream(s) ──► bounded Channel ──► N worker tasks ──► ITrafficAnalytics.Record(...)
```

`N = max(2, processor count)`. The channel is bounded and set to *wait* when
full, so if analytics ever fall behind the back‑pressure propagates to the gRPC
client instead of the process eating memory.

**Analytics** (`TrafficAnalytics` – a singleton, holds no lock of its own):

| Concern | Type | Thread‑safety strategy |
|---|---|---|
| Per‑camera Top 10 (instantaneous speed) | `CameraSpeedBoard` × 10 | **one lock per board** – 10 boards never contend; each critical section is a sort of ≤ 11 items |
| Running average per vehicle + 3‑camera gate | `VehicleAverageTracker` | **`ConcurrentDictionary`**, value is an **immutable struct** swapped atomically in `AddOrUpdate` – lock‑free, no read‑modify‑write race |
| Global Top 10 (highest average, live traffic) | `GlobalAverageBoard` | single lock over a small dictionary keyed by plate |

For every reading `Record()` does three things:

1. Offer the instantaneous speed to that camera's board.
2. Fold the speed into the vehicle's running average; if it has now passed
   **≥ 3 cameras**, (re)publish it to the global board.
3. If this was **camera 10**: remove the vehicle from the global board (it is no
   longer *live active traffic*), and **purge** its running‑average state *unless*
   it still holds a place on some camera board (i.e. it "scored").

**Tie‑breaking** everywhere: equal speed → **ordinal `NumberPlate` order**, so
the ranking is deterministic and reproducible.

`BuildSnapshot()` copies each board under its own lock. Boards are individually
consistent; they may be microseconds apart. A globally atomic snapshot across all
11 boards would need a coarser reader/writer scheme and isn't worth it for a live
dashboard.

### 3. WPF Desktop Client (`HighwaySpeed.Client`)

Hand‑rolled MVVM (a 20‑line `ObservableObject`, a `RelayCommand`) – no framework,
so every line is visible.

- **`DashboardViewModel`** owns a background polling loop
  (`PeriodicTimer(3 s)` on a `Task.Run`). The HTTP call runs on a background
  thread (`ConfigureAwait(false)`); the **only** UI‑thread touch is
  `ApplySnapshot`, marshalled via the captured `Dispatcher`. That matches the
  spec's "UI thread must only be invoked to update the visual data‑bindings".
- The camera picker just re‑projects the **last snapshot already in memory** – no
  extra request when you switch cameras.
- `TrafficApiClient` wraps `HttpClient.GetFromJsonAsync` so the view‑model is
  trivial to unit‑test with a fake.

---

## Design decisions & rationale

**Transport: gRPC, client‑streaming, generator → processor.**
The generator emits a high‑frequency, uniform stream (~1 000 msg/s). A single
client‑streaming call keeps per‑message overhead near zero (no request per camera
capture) while still giving a **strongly typed, versioned contract**
(`telemetry.proto`). REST would mean batching hacks; a message broker (MQTT/
RabbitMQ) would model the decoupling nicely but adds an extra container to
operate. gRPC is the best fit for *this* shape of traffic and is a modern default
for internal service‑to‑service links.

**One shared contract project (`HighwaySpeed.Contracts`).**
It holds the REST DTOs (plain immutable `record`s) **and** the `.proto` file. The
`.proto` is compiled by the generator (client stubs) and the processor (server
stubs) separately, so the Contracts project itself stays dependency‑free and the
WPF client can reference it for the DTOs without dragging in gRPC.

**Locks, not lock‑free everywhere.**
The spec asks for "non‑blocking, thread‑safe collections *or* fine‑grained
locking". The realistic load is ~1 000 readings/s spread over 10 camera boards –
~100/s per board, each update a handful of comparisons. **Fine‑grained locking
(one lock per board) is simpler to read and obviously correct**, and the lock is
held for microseconds. The one genuinely hot, per‑vehicle path (the running
average) *is* lock‑free via `ConcurrentDictionary` + immutable value. If sustained
load were 100× higher the camera boards would move to a bounded min‑heap; the
public shape wouldn't change.

**`Channel<T>` for both buffering points.**
Generator outbox and processor ingestion are both bounded `Channel`s. Generator
sheds the oldest telemetry when full (telemetry is lossy by nature, keep the
simulation smooth); processor *waits* when full (propagate back‑pressure, never
grow unbounded).

**`TimeProvider` for all time.**
Both the generator's tick timer and the processor's snapshot timestamp go through
`TimeProvider`, so timing is injectable and tests are deterministic.

**Modern, but readable.**
.NET 9, minimal APIs, `PeriodicTimer`, `Channel`, `TimeProvider`, `record`
DTOs, OpenAPI + Scalar UI. Deliberately *not* used: primary constructors on
services, source‑generated MVVM, "clever" LINQ – the goal was code that reads
top‑to‑bottom on first sight.

---

## Interpreting the ambiguous bits of the spec

The brief leaves a few things open. Every choice below is isolated in one place
and covered by a test.

**"Advance by 1 and emit … drop when counter exceeds 10" – what about the 11th tick?**
Taken literally, a vehicle at counter 10 would advance to 11 and emit an invalid
"Camera 11" message before being dropped. The brief's own note fixes each vehicle
at **exactly 10 messages, cameras 1–10**, so `AdvanceAndCapture` increments the
counter but **emits only while it is 1…10**; the vehicle is evicted on the
following tick. (`VehicleRegistryTests`)

**Global board – "Live Active Traffic Only".**
The global board contains only vehicles that are **still on the stretch**
(counter 1…10) and have passed **≥ 3 cameras**. When a vehicle passes camera 10
it drops off the global board immediately. (`TrafficAnalyticsTests`)

**Global board – "strictly bounded Top 10".**
Internally `GlobalAverageBoard` keeps **every** currently‑qualifying live
vehicle; the REST snapshot exposes the **top 10**. Keeping the full live set
(a few hundred small structs) means a vehicle that briefly drops out of the
visible ten and then speeds up is ranked correctly again with no re‑admission
bookkeeping. Making it strictly bounded is a one‑line change.

**"The Purge … if it does not score on any leaderboard."**
On a vehicle's camera‑10 reading, its running‑average state is purged **unless**
its plate still occupies a slot on one of the 10 camera boards – that camera
record is the enforcement evidence and is kept. A vehicle that topped no board
leaves no trace. (`TrafficAnalyticsTests`)

**Snapshot consistency.**
Per‑board, not global – see [above](#2-telemetry-data-processor-highwayspeedprocessor).

---

## Project layout

```
HighwaySpeed.sln
├─ Directory.Build.props            shared MSBuild settings (LangVersion, Nullable, …)
├─ docker-compose.yml               processor + generator
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

---

## What I'd do next

- **Push instead of poll** for the client (SignalR / gRPC server‑streaming) once
  "3‑second polling" is no longer a hard requirement.
- **Bounded min‑heap** for the camera boards if real throughput is far higher.
- **Immutable snapshot swap** in the analytics engine for a globally atomic
  snapshot and fully lock‑free reads.
- **Retry the in‑flight message** on the generator after a reconnect (today the
  message being written when the stream drops is lost – acceptable for
  telemetry, not for billing).
- **Metrics** (`Meter` / OpenTelemetry): ingestion rate, queue depth, drop count.
- **TTL / cap** on retained "scored" vehicles so a very long run cannot grow
  memory without bound.
