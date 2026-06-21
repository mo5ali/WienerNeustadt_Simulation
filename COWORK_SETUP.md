# Onboarding for a new Cowork chat — Wiener Neustadt shunting-yard DES

You are joining an in-progress project. **Read this whole file before doing anything**, then read `Other files/Documentation.txt` end-to-end (it's the running technical record, ~2400 lines).

## 1. First moves

The user works on Windows. The repo lives at `C:\Users\moham\source\repos\WienerNeustadt_Simulation` on their machine.

1. **Request folder access** with `mcp__cowork__request_cowork_directory`, path `C:\Users\moham\source\repos\WienerNeustadt_Simulation`.
2. **Read `Other files/Documentation.txt`**. Entries `[INITIAL]` through `[28]` are a chronological log of every significant change. Pay attention to the latest `[2X]` entries — they're where the current state lives.
3. Skim the top-level layout: `Program.cs`, `Control/`, `Models/`, `Entities/`, `Engine/`, `Infrastructure/`, `Output/`.
4. Read this file's **Gotchas** section before making any non-trivial edits.

## 2. What this project is

C# / .NET 8 discrete-event simulation of the Wiener Neustadt railway flat shunting yard, for the user's masters thesis. Two artifacts:

- **C# engine** producing a JSON event log (`bin/Debug/net8.0/OutputFiles/SimulationLog.json`) per run.
- **Python analytics** (`Output/analytics.py`) that consumes the log and writes `SimulationAnalytics.xlsm` — a 10-sheet workbook with KPI averages, raw timelines, and VBA-driven charts.
- A standalone HTML5 visualizer (`Output/SimulationVisualizer.html`) is also part of the project — it replays the same JSON log over a yard background image.

Architecture follows Furian et al. 2015's **hierarchical control-unit pattern**:

- `Control/ArrivalControlUnit.cs` — inbound flow (entry queue, arrival drive, sorting, ITP, PushOff envelope).
- `Control/ClassificationControlUnit.cs` — classification-side activities (SEC, COP, completion check, OBT creation, OBTP, DEPD).
- `Control/ResourceControlUnit.cs` — central broker for workers, shunt locos, train locos, the passage track, the exit gate. FIFO-with-bypass queue dispatch.

Every activity derives from `Models/Activity.cs` and emits **Submitted → Started → Completed** canonical events into the JSON log. `MarkCommenced(now)` and `MarkCompleted(now)` are the only ways subclasses should set those timestamps — they're idempotent and they're what wires the analytics.

## 3. Current workbook layout (10 sheets, after entry [28])

```
1  Definitions              — KPI dictionary, formulas, glossary
2  Overview                 — run summary + KPI averages (Mean/Min/Max/Median)
3  Incoming Trains          — per-train lifecycle (20 cols, M-T are derived hh:mm:ss)
4  Wagon Groups             — per-WG lifecycle (19 cols, includes new "Idle time" KPI)
5  Outbound Trains          — per-OBT lifecycle (16 cols)
6  Drive Stats              — aggregated drive activities (count, dist, dur, speed)
7  WG Yard Times            — supervisor cross-check sheet
8  Activities (raw)         — one row per completed activity (incl. joints col for ITP)
9  OBTs (raw)               — flat OBT timestamps
10 WGs (raw)                — flat WG timestamps
```

Plus VBA-driven charts on a **single "Activity Charts"** sheet:

- 8 per-activity-type scatters (X = StartedAt, Y = Duration), colored by track / length / **joints (ITP only)**.
- Drive Length vs Duration scatter (3 colored series: ARRD / POD / DEPD).
- 3 KPI bar charts (Mean Incoming / Mean WG / Mean Outbound) at the bottom.

**Every duration cell in the workbook is rendered as `[h]:mm:ss`** — no more `(s)` or `(min)` mixed in. See entry [27] in Documentation.txt.

## 4. Convention notes

- **Activity ID format**: `Act_<ABBR>_<EntityId>_<HHMMSS>_<Location>`. Examples: `Act_ITP_12001_050000_703`, `Act_POD_1100502+1100402_050130_615`, `Act_OBTP_OBT060125080530-Vienna_080530_615`. PushOffDrive entities are `+`-joined WG ids for cuts; OBT ids contain a hyphen but never an underscore, so `parts[2]` recovers them intact.
- **Track ids** in the JSON are always strings (e.g. `"703"`, `"615"`).
- **Areas**: canonical short forms are `"Arrival"` and `"Classification"` (NOT `"ArrivalArea"` — that bug is fixed; see [11.10] in Documentation.txt).
- **JSON event types**: `TrainEvent`, `WagonGroupEvent`, `WorkerEvent`, `ActivityEvent` (the last carries a `status` field: `Submitted` / `Started` / `Completed` / `PassageClaimed` / `PassageReleased` / `ExitGateClaimed` / `ExitGateReleased`).
- **ITP duration formula** (recent change in [25]): `max(joints, 1) × 120s × (length/100m) × workerMultiplier`. Joints are computed in `ArrivalControlUnit.ComputeSeparationJoints` after sorting and passed to `ManipulationActivity` via constructor; the value also lands in the JSON as `joints=N` inside the Submitted-event `details` string.

## 5. Workflow that works

### Doing your job

- **Always create a TaskList** for any non-trivial work (the user expects to see progress). Mark subtasks completed as you go. The user said "break this down into many tasks to avoid running out of tokens in the middle" — honor that pacing.
- **Build the analytics workbook in-process** before believing your changes work. Cheap smoke test:
  ```python
  import openpyxl, importlib.util
  spec = importlib.util.spec_from_file_location('a', 'Output/analytics.py')
  a = importlib.util.module_from_spec(spec); spec.loader.exec_module(a)
  wb = openpyxl.Workbook(); wb.remove(wb.active)
  a.build_definitions_sheet(wb)
  # … etc per builder with empty lists or small synthetic data …
  wb.save('/tmp/test.xlsx')
  ```
  Catches missing-row errors, broken column-letter references, NoneType chains, etc.
- **`py_compile Output/analytics.py`** after every change to that file. Confirms no syntax errors. Doesn't catch runtime issues — pair with the smoke test.
- **The user emails things to their supervisor.** Keep KPI definitions in the Definitions sheet AND in Documentation.txt entries in lockstep — the user pastes from both.

### When editing files

| File | Edit tool reliability | Notes |
|---|---|---|
| `*.cs` | Reliable | Standard `Edit` works fine. |
| `Output/SimulationLogger.cs` | Reliable | |
| `Output/SimulationVisualizer.html` | Reliable | 2400 lines but Edit holds up. |
| `Output/vba_modules/*.bas` | Reliable | Small files. |
| `Output/analytics.py` | **Unreliable** | Edit tool has truncated this file silently multiple times. **Use bash python heredocs** for any change ~10+ lines. Re-check py_compile after every batch. |
| `Other files/Documentation.txt` | **Unreliable** | Same truncation issue. **Use bash python** for inserts; anchor on unique strings (e.g. `'14. FILE / MODULE INDEX\n'`). |

The bash python pattern for big edits to those two files:

```python
PATH = 'Output/analytics.py'
with open(PATH, 'rb') as f:
    raw = f.read()
crlf = b'\r\n' in raw                          # detect line ending
src = raw.decode('utf-8').replace('\r\n', '\n')  # normalize for matching

# … your str.replace(...) calls on src …

out = src.replace('\n', '\r\n') if crlf else src
with open(PATH, 'wb') as f:
    f.write(out.encode('utf-8'))
```

If a `str.replace` anchor doesn't match exactly, the script silently no-ops and the file write at the end persists OLD content. Always check after with `grep -c <expected_new_string>`.

### When editing the Excel/VBA pipeline

- **The user must have "Trust access to the VBA project object model" enabled in Excel** (File → Options → Trust Center → Trust Center Settings → Macro Settings). Without it, `run_analytics_post.vbs` can't import the .bas files. The .vbs prints the exact fix path on failure.
- **VBA chart gotchas** (learned the hard way; see [27.2] / [27.5]):
  - On XY scatter, marker face color via `Format.Fill.ForeColor` often only paints the border. Use `MarkerBackgroundColor` + `MarkerForegroundColor` instead.
  - Set `ChartType = xlXYScatter` **before** the first `SeriesCollection.NewSeries` call. `ChartObjects.Add` starts at column-clustered; adding a series first means `s.MarkerStyle` throws "Run-time error 1004 — this property is not valid for the current chart type".
  - Use **VBA arrays** for `s.XValues` / `s.Values`, not `Range` references. Range refs can come out blank on scatter.
- **All .bas / .cls files must be pure ASCII.** VBA imports them as Windows-1252 and mangles non-ASCII characters.

### When changing C# activities

- `Activity.cs` base class is the single source of truth for Submitted/Started/Completed. Don't write `CommencedAt` / `CompletedAt` directly anywhere; route through `MarkCommenced` / `MarkCompleted`.
- `Activity.CalculateDuration` is **virtual** (since [25]). Override in subclasses if a different formula applies (see `ManipulationActivity.CalculateDuration` for the ITP case).
- The base ctor accepts an optional `extraDetails` string that gets appended to the Submitted-event details (separator `;`). Used by `ManipulationActivity` to surface `joints=N` for ITP.

## 6. File path quirks

- **Two filesystems**: the Edit/Read/Write tools see the host path (`C:\Users\moham\...`); bash sees the sandbox mount (`/sessions/.../mnt/WienerNeustadt_Simulation`). Same file, different paths. Translate per call type — never pass `/sessions/...` paths to Read/Write/Edit.
- **bash output is authoritative for verifying disk state.** Edit tool sometimes reports success but writes incomplete bytes (the file-truncation bug). Always re-check via bash after non-trivial Edit/Write operations.
- The user's runtime output dir is `bin/Debug/net8.0/OutputFiles/`. The Python analytics consumes `SimulationLog.json` from there and writes `SimulationAnalytics.xlsm` back there. If Excel has the .xlsm open, the next analytics run will fail to overwrite — tell the user to close Excel first.

## 7. The user's communication style

- Direct, concise — see `<user_preferences>` if you're not sure. Match it.
- Comfortable being asked clarifying questions about formula constants, scoping decisions, naming. Use `AskUserQuestion` when something's genuinely ambiguous (e.g. ITP formula constants in [25]) — don't make those calls silently.
- Writes "do this and this and this" in compound requests. **Always honor "break this down into many tasks"** when they say it explicitly.
- Sometimes writes emails to their supervisor mid-session and pastes them as context. The KPI definitions they paste need to match what's in the Definitions sheet and Documentation.txt.

## 8. The latest open work in the project

As of entry [28], the deliverables explicitly defined by the supervisor (last meeting) are:

- ✅ Clear KPI definitions — Definitions sheet + Overview sheet + Documentation entries [25], [28].
- ✅ ITP duration driven by separation joints — [25]; tune `BASE_SECONDS_PER_JOINT=120` / `REF_LENGTH_M=100` in `Models/ManipulationActivity.cs` if calibration drifts.
- ✅ Drive times / lengths / speeds presentation — Drive Stats sheet [25] + Drive Length vs Duration scatter [22, 26, 27].
- ✅ Long WG classification dwells investigation — Sibling wait + Post-OBTP + DEPD + new Idle-time column [25, 28].

Open follow-ups noted across entries (deferred unless asked):

- The `Output/SimulationVisualizer.html` worker icon z-order (shunt loco renders in front of train, should be behind — see [12.2]).
- Standalone WGs not visually removed after DEPD ([12.3]).
- `Program.cs` ~line 102 has a `Thread.Sleep(30000)` inside the arrival-track-availability loop — real-clock blocking inside a DES event handler. Latent bug if two trains genuinely overlap on a starved yard.
- Static distance maps in `analytics.py` duplicate the C# values in `Models/*DriveActivity.cs` and `Models/PushOffActivity.cs`. Logged as intentional in [23.1].

## 9. The DON'Ts

- Don't refactor the JSON log format without a clear ask. Three consumers depend on it (C# logger, Python analytics, JS visualizer); breaking one is easy.
- Don't change activity-ID structure (`Act_<ABBR>_<entityId>_<HHMMSS>_<loc>`) — analytics depends on `parts[2]` for entity-id recovery in several parsers.
- Don't rename columns on the per-entity sheets without checking the VBA macros (`Module_PlotActivitiesByTrack.bas`, `Module_PlotKPIs.bas`) and the formula references on Overview. The VBAs look up columns by header NAME via `FindHeaderColumn`, so renames are tolerable if you update the lookups; positional changes are tolerable because the lookups don't care about column letters. **Both at once** is when things break silently.
- Don't add files unless asked. The user has been deliberate about pruning (entries [21.1], [23.2], [24.4]).
- Don't write to `bin/Debug/...` or `obj/` — those are build outputs. Only edit source files under `Control/`, `Models/`, `Engine/`, `Entities/`, `Infrastructure/`, `Output/`, `InputFiles/`, `Other files/`, and the top-level `Program.cs` / `*.csproj` / `*.sln`.

## 10. Quick reference

| Topic | File / location |
|---|---|
| Full chronological history | `Other files/Documentation.txt` (sections 13, entries `[INITIAL]`..`[28]`) |
| KPI dictionary | `Definitions` sheet in the workbook; mirrored in `[22.1]`, `[25.1]`, `[27.1]`, `[28.2]` of Documentation.txt |
| Activity lifecycle invariant | `Models/Activity.cs` |
| Activity subclasses | `Models/ManipulationActivity.cs` (ITP/SEC/COP/PO), `Models/PushOffActivity.cs`, `Models/ArrivalDriveActivity.cs`, `Models/DepartureDriveActivity.cs`, `Models/DrivingActivity.cs` (POD), `Models/OutboundTrainPreparationActivity.cs`, `Models/CouplingActivity.cs`, `Models/SecuringActivity.cs` |
| Resource broker (workers, locos, passage, gate) | `Control/ResourceControlUnit.cs` |
| Inbound flow | `Control/ArrivalControlUnit.cs` |
| Classification + outbound flow | `Control/ClassificationControlUnit.cs` |
| JSON log writer | `Output/SimulationLogger.cs` |
| Analytics + workbook builder | `Output/analytics.py` (~1700 lines) |
| HTML visualizer | `Output/SimulationVisualizer.html` |
| VBA chart modules | `Output/vba_modules/Module_PlotActivitiesByTrack.bas`, `Module_PlotDriveActivities.bas`, `Module_PlotKPIs.bas` |
| Excel post-processor (.bas import driver) | `Output/run_analytics_post.vbs` |
| Static distance maps (C#) | `Models/ArrivalDriveActivity.TrackDistanceMeters`, `Models/DepartureDriveActivity.TrackDistanceMeters`, `Models/PushOffActivity.ArrivalToDecouplingMeters` + `DecouplingToClassMeters` |
| Static distance maps (Python copy) | Top of `Output/analytics.py` |

---

That's everything. Start with Documentation.txt, then come back here if a gotcha surprises you.
