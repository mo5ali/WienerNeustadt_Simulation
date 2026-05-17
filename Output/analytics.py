"""
analytics.py — process-duration, train-timeline, and OBT/WG formation metrics
for the WienerNeustadt simulation, with native Excel charts baked in.

Reads:  ../bin/Debug/net8.0/OutputFiles/SimulationLog.csv
Writes: ../bin/Debug/net8.0/OutputFiles/SimulationAnalytics.xlsx

Usage:
    pip install openpyxl
    python analytics.py

Sheets produced:
    1. Definitions          — every metric, what it means, the exact formula.
    2. Throughput           — sim duration, trains in / out per hour, etc.
    3. Process Durations    — per-activity-type duration stats + bar chart.
    4. Train Timeline       — per-phase wait/duration stats + bar chart.
    5. OBT Formation        — outbound-train formation, OBTP wait, gate wait,
                              total dwell on classification track + bar chart.
    6. Activities (raw)     — one row per completed activity (sortable).
    7. Trains (raw)         — one row per inbound train, all timestamps + live
                              duration formulas (sortable).
    8. OBTs (raw)           — one row per outbound train (sortable).
    9. WGs (raw)            — one row per wagon group on a classification track
                              (sortable; only the ones that became part of an OBT).
"""

import os
import sys
from collections import defaultdict
from datetime import datetime
from statistics import pstdev

try:
    from openpyxl import Workbook
    from openpyxl.chart import BarChart, Reference
    from openpyxl.styles import Alignment, Font, PatternFill
except ImportError:
    sys.exit("openpyxl not installed. Run: pip install openpyxl")


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))
CSV_PATH = os.path.join(REPO_ROOT, "bin", "Debug", "net8.0", "OutputFiles",
                        "SimulationLog.csv")
OUT_PATH = os.path.join(REPO_ROOT, "bin", "Debug", "net8.0", "OutputFiles",
                        "SimulationAnalytics.xlsx")

FONT = Font(name="Arial", size=10)
FONT_BOLD = Font(name="Arial", size=10, bold=True)
FONT_HEADER = Font(name="Arial", size=11, bold=True, color="FFFFFF")
FILL_HEADER = PatternFill("solid", start_color="305496")
ALIGN_TOP_WRAP = Alignment(vertical="top", wrap_text=True)


# ── CSV parsing ───────────────────────────────────────────────────────────────
def _iter_rows(csv_path):
    """Yield (sim_time, event_type, c2, c3, details) per non-meta CSV row.
    The C# logger emits each row type with a different column order:
      TrainEvent:       SimTime ; TrainEvent ; eventName ; trainId ; details
      WagonGroupEvent:  SimTime ; WagonGroupEvent ; wgId ; eventName ; details
      WorkerEvent:      SimTime ; WorkerEvent ; workerId ; eventName ; details
      ActivityEvent:    SimTime ; ActivityEvent ; activityId ; activityType ; status|details
    So caller must interpret c2 / c3 according to event_type. Header row is
    'EventType;EntityId;Event;SimTime;Details' (a leftover bug in the C#
    logger) — we parse positionally and ignore it."""
    if not os.path.exists(csv_path):
        sys.exit(f"CSV not found: {csv_path}\n"
                 "Run the simulation first to produce SimulationLog.csv.")
    with open(csv_path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\r\n")
            if not line or line.startswith("#") or line.startswith("EventType"):
                continue
            parts = line.split(";", 4)
            if len(parts) < 5:
                continue
            yield parts


def parse_activity_durations(csv_path):
    by_id = {}
    for sim_time, event_type, c2, c3, details in _iter_rows(csv_path):
        if event_type != "ActivityEvent":
            continue
        activity_id = c2
        activity_type = c3
        status = details.split("|", 1)[0]
        if status not in ("Started", "Completed"):
            continue
        try:
            ts = datetime.fromisoformat(sim_time)
        except ValueError:
            continue
        entry = by_id.setdefault(activity_id, {
            "activityType": activity_type,
            "activityId": activity_id,
            "startedAt": None,
            "completedAt": None,
        })
        if status == "Started":
            entry["startedAt"] = ts
        else:
            entry["completedAt"] = ts
    out = []
    for e in by_id.values():
        if e["startedAt"] and e["completedAt"]:
            e["durationSec"] = (e["completedAt"] - e["startedAt"]).total_seconds()
            out.append(e)
    return out


def parse_train_timeline(csv_path):
    trains = {}
    for sim_time, event_type, c2, c3, details in _iter_rows(csv_path):
        if event_type != "TrainEvent":
            continue
        event_name = c2
        train_id = c3
        try:
            ts = datetime.fromisoformat(sim_time)
        except ValueError:
            continue
        # Skip outbound-train events (OBT IDs look like "OBT...-Graz")
        if train_id.startswith("OBT"):
            continue
        entry = trains.setdefault(train_id, {"trainId": train_id})
        if event_name == "Entry":
            entry["entry"] = ts
            for part in details.split("|"):
                if part.startswith("length="):
                    try:
                        entry["length"] = float(part.split("=", 1)[1])
                    except ValueError:
                        pass
        elif event_name == "ArrivedArrivalTrack":
            entry["arrivedTrack"] = ts
            entry["assignedTrack"] = details
        elif event_name == "PreparationStarted":
            entry["itpStart"] = ts
        elif event_name == "PreparationComplete":
            entry["itpEnd"] = ts
        elif event_name == "PushOffStarted":
            entry["pushOffStart"] = ts
        elif event_name == "PushOffComplete":
            entry["pushOffEnd"] = ts
    out = [t for t in trains.values() if "entry" in t]
    out.sort(key=lambda t: t["entry"])
    return out


def parse_obts_and_wgs(csv_path):
    """Parse OBT lifecycle events and WG arrivals. Then match WGs to OBTs by
    track + time window: an OBT created at time T on track X owns all WGs that
    arrived on track X at or before T and weren't already claimed by a
    previous OBT on that track. Returns (obts_list, wgs_list)."""
    obts = {}
    wgs = {}

    for sim_time, event_type, c2, c3, details in _iter_rows(csv_path):
        try:
            ts = datetime.fromisoformat(sim_time)
        except ValueError:
            continue

        if event_type == "WagonGroupEvent":
            wg_id = c2
            event_name = c3
            if event_name == "ArrivedClassificationTrack":
                wgs.setdefault(wg_id, {"wgId": wg_id, "obtId": None})
                wgs[wg_id]["arrivedAt"] = ts
                wgs[wg_id]["trackId"] = details

        elif event_type == "TrainEvent":
            event_name = c2
            train_id = c3
            if not train_id.startswith("OBT"):
                continue  # only outbound-train events here
            entry = obts.setdefault(train_id, {"obtId": train_id, "wgIds": []})
            if event_name == "OutboundTrainCreated":
                entry["createdAt"] = ts
                entry["destination"] = details
            elif event_name == "OBTPStarted":
                entry["obtpStart"] = ts
            elif event_name == "OBTPComplete":
                entry["obtpEnd"] = ts
            elif event_name == "DEPDRequested":
                entry["depdRequested"] = ts
            elif event_name == "DEPDStarted":
                entry["depdStart"] = ts
            elif event_name == "Departed":
                entry["depdEnd"] = ts

        elif event_type == "ActivityEvent":
            activity_id = c2
            activity_type = c3
            # OBTP activity ID is Act_OBTP_<obtId>_<HHMMSS>_<trackId>;
            # use it to recover the track the OBT was formed on.
            if activity_type == "OutboundTrainPreparation":
                parts = activity_id.split("_")
                if len(parts) >= 5:
                    obt_id = parts[2]
                    track_id = parts[-1]
                    entry = obts.setdefault(obt_id, {"obtId": obt_id, "wgIds": []})
                    entry["trackId"] = track_id

    # Filter to OBTs we know enough about
    obts_list = [o for o in obts.values()
                 if "createdAt" in o and "trackId" in o]
    obts_list.sort(key=lambda o: o["createdAt"])

    # Assign WGs → OBTs by track + time window (greedy, earliest OBT first)
    for obt in obts_list:
        track = obt["trackId"]
        t_created = obt["createdAt"]
        for wg in wgs.values():
            if wg.get("obtId"):
                continue
            if wg.get("trackId") == track and wg.get("arrivedAt") \
                    and wg["arrivedAt"] <= t_created:
                wg["obtId"] = obt["obtId"]
                obt["wgIds"].append(wg["wgId"])

    # First-WG arrival per OBT
    for obt in obts_list:
        if obt["wgIds"]:
            obt["firstWgArrival"] = min(wgs[w]["arrivedAt"] for w in obt["wgIds"])

    wgs_list = [w for w in wgs.values() if w.get("arrivedAt") and w.get("obtId")]
    wgs_list.sort(key=lambda w: w["arrivedAt"])
    # Stitch the OBT departed timestamp onto each WG so WG-total-time formulas work
    for w in wgs_list:
        owning = next((o for o in obts_list if o["obtId"] == w["obtId"]), None)
        if owning:
            w["obtCreatedAt"] = owning.get("createdAt")
            w["obtDepartedAt"] = owning.get("depdEnd")

    return obts_list, wgs_list


# ── Sheet styling helper ─────────────────────────────────────────────────────
def _style_header_row(ws, row=1):
    for cell in ws[row]:
        cell.font = FONT_HEADER
        cell.fill = FILL_HEADER
        cell.alignment = Alignment(horizontal="left", vertical="center")


# ── Sheet builders ────────────────────────────────────────────────────────────
def build_definitions_sheet(wb):
    ws = wb.create_sheet("Definitions", 0)
    ws.append(["Metric", "Definition", "Formula / source"])
    rows = [
        ("— Process duration block —", "", ""),
        ("Process duration",
         "Wall-clock time the activity spent in its 'running' state, from "
         "its Started event to its Completed event. Excludes pre-activity "
         "waiting and post-activity worker travel.",
         "duration_seconds = Completed_timestamp - Started_timestamp"),
        ("Mean / Min / Max / Std",
         "Aggregations of process duration across all instances of an "
         "activity type. Std is population stdev.",
         "AVERAGEIF / MINIFS / MAXIFS over Activities (raw) duration column."),

        ("— Train timeline block —", "", ""),
        ("Queue wait (Q4)",
         "Time an inbound train spent in the entry queue before its arrival.",
         "queue_wait = ArrivedArrivalTrack - Entry"),
        ("Pre-ITP wait (Q5)",
         "Time on arrival track AFTER physically arriving but BEFORE ITP "
         "actually started (worker + shunt-loco allocation + travel).",
         "pre_itp_wait = PreparationStarted - ArrivedArrivalTrack"),
        ("ITP duration",
         "Per-train ITP run-time, aligned with other phases for correlation.",
         "itp_duration = PreparationComplete - PreparationStarted"),
        ("Pre-PushOff wait (Q6)",
         "Time between ITP finishing and PushOff actually starting (passage "
         "track + worker re-allocation).",
         "pre_pushoff_wait = PushOffStarted - PreparationComplete"),
        ("PushOff duration",
         "Time from PushOff start to complete (all WG sub-drives + pull-backs).",
         "pushoff_duration = PushOffComplete - PushOffStarted"),
        ("Total in arrival yard",
         "Total time the inbound train physically occupied an arrival track.",
         "total_in_arrival_yard = PushOffComplete - ArrivedArrivalTrack"),
        ("Total in system (Q7)",
         "End-to-end inbound time, queue entry until PushOff finished.",
         "total_in_system = PushOffComplete - Entry"),

        ("— OBT / WG formation block —", "", ""),
        ("WG dwell on classification track",
         "Time a wagon group sat on its classification track from arrival "
         "until the OBT containing it was created. Captures inactivity while "
         "the WG waits for siblings to accumulate.",
         "wg_dwell = obt.createdAt - wg.arrivedAt"),
        ("WG total time in classification area",
         "Time from a WG arriving on its classification track until the OBT "
         "containing it physically departed the system.",
         "wg_total = obt.depdEnd - wg.arrivedAt"),
        ("OBT formation time",
         "Time from the FIRST WG arriving on the future-OBT track until the "
         "OBT was actually created. The 'how long does it take to gather a "
         "train's worth of cargo' number.",
         "formation_time = obt.createdAt - min(wg.arrivedAt for wg in obt)"),
        ("OBT pre-OBTP wait",
         "Time between the OBT being created and its OBTP activity actually "
         "starting (worker + train-loco allocation).",
         "pre_obtp_wait = obt.obtpStart - obt.createdAt"),
        ("OBT gate wait (exit-gate queue time)",
         "Time the OBT was ready to depart (OBTP done) but stuck waiting "
         "for the single exit gate to free up. Headline bottleneck metric "
         "given the exitGateCount = 1 constraint.",
         "gate_wait = obt.depdStart - obt.obtpEnd"),
        ("OBT total on classification track",
         "Total time from first WG arrival until DEPD completed (= OBT left "
         "the system). End-to-end 'how long does an outbound train spend on "
         "the outbound side' number.",
         "obt_total = obt.depdEnd - obt.firstWgArrival"),

        ("— Throughput block —", "", ""),
        ("Sim duration (h)",
         "Wall-clock simulated time covered by the log.",
         "(max_event_time - min_event_time) / 3600"),
        ("Trains per hour entered",
         "Average rate of inbound trains entering.",
         "total_entries / sim_duration_hours"),
        ("Trains per hour exited",
         "Average rate of inbound trains finishing PushOff.",
         "total_pushoff_completions / sim_duration_hours"),
        ("OBTs per hour created",
         "Average rate of outbound trains being formed.",
         "total_obts_created / sim_duration_hours"),
        ("OBTs per hour departed",
         "Average rate of outbound trains physically exiting.",
         "total_depd_completed / sim_duration_hours"),

        ("— Reference —", "", ""),
        ("Activity type glossary",
         "ARRD = ArrivalDrive; ITP = IncomingTrainPreparation; "
         "PushOff = parent envelope of all sub-drives for one inbound train; "
         "PushOffDrive (POD) = one sub-drive of one WG group; "
         "Securing / Coupling = per-WG prep on classification track; "
         "OBTP = OutboundTrainPreparation; "
         "DEPD = DepartureDrive (OBT exiting through the gate).",
         "(reference, no formula)"),
        ("WG → OBT mapping",
         "WGs are matched to OBTs by track + time window: an OBT created at "
         "time T on track X owns all WGs that arrived on track X at or before "
         "T and weren't already claimed by an earlier OBT on the same track.",
         "(derivation, see parse_obts_and_wgs in analytics.py)"),
        ("Roadmap",
         "Future steps: queue-length sampling over time, "
         "worker / loco utilization (idle / travel / busy fractions).",
         "(roadmap)"),
    ]
    for r in rows:
        ws.append(r)
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=3):
        is_section = row[0].value and str(row[0].value).startswith("—")
        for cell in row:
            cell.font = FONT_BOLD if is_section else FONT
            cell.alignment = ALIGN_TOP_WRAP
    ws.column_dimensions["A"].width = 32
    ws.column_dimensions["B"].width = 70
    ws.column_dimensions["C"].width = 60
    ws.freeze_panes = "A2"
    return ws


def build_activities_raw_sheet(wb, activities):
    ws = wb.create_sheet("Activities (raw)")
    ws.append(["ActivityType", "ActivityId", "StartedAt", "CompletedAt",
               "Duration (s)", "Duration (min)", "Duration (hh:mm:ss)"])
    for a in sorted(activities, key=lambda x: (x["activityType"], x["startedAt"])):
        ws.append([
            a["activityType"], a["activityId"],
            # IMPORTANT: pass the datetime objects directly (not strftime
            # strings) so Excel stores them as numeric date serials. That
            # lets you scatter-plot with StartedAt on the X axis — text
            # cells fail silently and produce a random-looking 1..N axis.
            a["startedAt"],
            a["completedAt"],
            a["durationSec"], None, None,
        ])
    for r in range(2, ws.max_row + 1):
        ws.cell(row=r, column=6, value=f"=E{r}/60")
        # hh:mm:ss as a fraction of a day; [h] lets it exceed 24h.
        ws.cell(row=r, column=7, value=f"=E{r}/86400")
        # StartedAt / CompletedAt formatted as datetime (the cells hold
        # real Excel date serials thanks to the datetime objects above).
        ws.cell(row=r, column=3).number_format = "yyyy-mm-dd hh:mm:ss"
        ws.cell(row=r, column=4).number_format = "yyyy-mm-dd hh:mm:ss"
        ws.cell(row=r, column=5).number_format = "0.00"
        ws.cell(row=r, column=6).number_format = "0.00"
        ws.cell(row=r, column=7).number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    ws.column_dimensions["A"].width = 28
    ws.column_dimensions["B"].width = 42
    ws.column_dimensions["C"].width = 22
    ws.column_dimensions["D"].width = 22
    ws.column_dimensions["E"].width = 14
    ws.column_dimensions["F"].width = 14
    ws.column_dimensions["G"].width = 16
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_process_durations_sheet(wb, activities):
    ws = wb.create_sheet("Process Durations")
    ws.append(["ActivityType", "Count", "Mean (s)", "Mean (min)",
               "Min (s)", "Max (s)", "Std (s)", "Mean (hh:mm:ss)"])
    by_type = defaultdict(list)
    for a in activities:
        by_type[a["activityType"]].append(a["durationSec"])
    types = sorted(by_type.keys())
    raw_last = len(activities) + 1
    raw_type = f"'Activities (raw)'!$A$2:$A${raw_last}"
    raw_dur = f"'Activities (raw)'!$E$2:$E${raw_last}"
    for i, t in enumerate(types, start=2):
        ws.append([
            t,
            f"=COUNTIF({raw_type},$A{i})",
            f"=IFERROR(AVERAGEIF({raw_type},$A{i},{raw_dur}),0)",
            f"=C{i}/60",
            f"=IFERROR(MINIFS({raw_dur},{raw_type},$A{i}),0)",
            f"=IFERROR(MAXIFS({raw_dur},{raw_type},$A{i}),0)",
            pstdev(by_type[t]) if len(by_type[t]) >= 2 else 0.0,
            f"=C{i}/86400",  # Mean as fraction-of-day for hh:mm:ss format
        ])
    _style_header_row(ws)
    for r in range(2, ws.max_row + 1):
        for c in range(1, ws.max_column + 1):
            cell = ws.cell(row=r, column=c)
            cell.font = FONT
            if c >= 3 and c <= 7:
                cell.number_format = "0.00"
        ws.cell(row=r, column=2).number_format = "0"
        ws.cell(row=r, column=8).number_format = "[h]:mm:ss"
    ws.column_dimensions["A"].width = 30
    for col in "BCDEFG":
        ws.column_dimensions[col].width = 14
    ws.column_dimensions["H"].width = 16
    ws.freeze_panes = "A2"
    chart = BarChart()
    chart.type = "bar"
    chart.style = 11
    chart.title = "Mean process duration (s) by activity type"
    chart.y_axis.title = "Activity type"
    chart.x_axis.title = "Mean duration (s)"
    last = ws.max_row
    data = Reference(ws, min_col=3, max_col=3, min_row=1, max_row=last)
    cats = Reference(ws, min_col=1, max_col=1, min_row=2, max_row=last)
    chart.add_data(data, titles_from_data=True)
    chart.set_categories(cats)
    chart.height = 10
    chart.width = 18
    ws.add_chart(chart, "I2")
    return ws


def build_trains_raw_sheet(wb, trains):
    ws = wb.create_sheet("Trains (raw)")
    headers = ["TrainId", "Length (m)", "Track", "Entry", "ArrivedAtTrack",
               "ITP Start", "ITP End", "PushOff Start", "PushOff End",
               "Queue Wait (s)", "Pre-ITP Wait (s)", "ITP Duration (s)",
               "Pre-PushOff Wait (s)", "PushOff Duration (s)",
               "Total in Arrival Yard (s)", "Total in System (s)",
               "Total in System (min)", "Total in System (hh:mm:ss)"]
    ws.append(headers)
    for t in trains:
        ws.append([
            t["trainId"], t.get("length"), t.get("assignedTrack"),
            t.get("entry"), t.get("arrivedTrack"),
            t.get("itpStart"), t.get("itpEnd"),
            t.get("pushOffStart"), t.get("pushOffEnd"),
        ])
    for r in range(2, ws.max_row + 1):
        for col_letter in ("D", "E", "F", "G", "H", "I"):
            ws[f"{col_letter}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        ws[f"J{r}"] = f'=IF(AND(ISNUMBER(D{r}),ISNUMBER(E{r})),(E{r}-D{r})*86400,"")'
        ws[f"K{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(F{r})),(F{r}-E{r})*86400,"")'
        ws[f"L{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(G{r})),(G{r}-F{r})*86400,"")'
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(G{r}),ISNUMBER(H{r})),(H{r}-G{r})*86400,"")'
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(H{r}),ISNUMBER(I{r})),(I{r}-H{r})*86400,"")'
        ws[f"O{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(I{r})),(I{r}-E{r})*86400,"")'
        ws[f"P{r}"] = f'=IF(AND(ISNUMBER(D{r}),ISNUMBER(I{r})),(I{r}-D{r})*86400,"")'
        ws[f"Q{r}"] = f'=IF(ISNUMBER(P{r}),P{r}/60,"")'
        # Total in System rendered as hh:mm:ss (P holds it in seconds).
        ws[f"R{r}"] = f'=IF(ISNUMBER(P{r}),P{r}/86400,"")'
        for col in "JKLMNOPQ":
            ws[f"{col}{r}"].number_format = "0.00"
        ws[f"R{r}"].number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    ws.column_dimensions["A"].width = 10
    ws.column_dimensions["B"].width = 11
    ws.column_dimensions["C"].width = 8
    for col in "DEFGHI":
        ws.column_dimensions[col].width = 19
    for col in "JKLMNOPQ":
        ws.column_dimensions[col].width = 14
    ws.column_dimensions["R"].width = 16
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions
    return ws


TIMELINE_METRICS = [
    ("Queue wait (Q4)",            "J"),
    ("Pre-ITP wait (Q5)",          "K"),
    ("ITP duration",               "L"),
    ("Pre-PushOff wait (Q6)",      "M"),
    ("PushOff duration",           "N"),
    ("Total in arrival yard",      "O"),
    ("Total in system (Q7)",       "P"),
]


def build_train_timeline_sheet(wb, trains):
    ws = wb.create_sheet("Train Timeline")
    ws.append(["Metric", "Count", "Mean (s)", "Mean (min)", "Min (s)", "Max (s)",
               "Mean (hh:mm:ss)"])
    raw_last = len(trains) + 1
    for i, (label, col) in enumerate(TIMELINE_METRICS, start=2):
        col_range = f"'Trains (raw)'!${col}$2:${col}${raw_last}"
        ws.append([
            label,
            f"=COUNT({col_range})",
            f"=IFERROR(AVERAGE({col_range}),0)",
            f"=C{i}/60",
            f"=IFERROR(MIN({col_range}),0)",
            f"=IFERROR(MAX({col_range}),0)",
            f"=C{i}/86400",
        ])
    _style_header_row(ws)
    for r in range(2, ws.max_row + 1):
        for c in range(1, ws.max_column + 1):
            cell = ws.cell(row=r, column=c)
            cell.font = FONT
            if c >= 3 and c <= 6:
                cell.number_format = "0.00"
        ws.cell(row=r, column=2).number_format = "0"
        ws.cell(row=r, column=7).number_format = "[h]:mm:ss"
    ws.column_dimensions["A"].width = 26
    for col in "BCDEF":
        ws.column_dimensions[col].width = 14
    ws.column_dimensions["G"].width = 16
    ws.freeze_panes = "A2"
    chart = BarChart()
    chart.type = "bar"
    chart.style = 12
    chart.title = "Mean duration (s) per train-timeline phase"
    chart.y_axis.title = "Phase"
    chart.x_axis.title = "Mean duration (s)"
    last = ws.max_row
    data = Reference(ws, min_col=3, max_col=3, min_row=1, max_row=last)
    cats = Reference(ws, min_col=1, max_col=1, min_row=2, max_row=last)
    chart.add_data(data, titles_from_data=True)
    chart.set_categories(cats)
    chart.height = 10
    chart.width = 18
    # Anchored at I2 (was H2) so the chart doesn't sit on top of the new
    # Mean (hh:mm:ss) column G.
    ws.add_chart(chart, "I2")
    return ws


# ── OBT / WG sheets (Step 3) ─────────────────────────────────────────────────
def build_obts_raw_sheet(wb, obts):
    ws = wb.create_sheet("OBTs (raw)")
    headers = ["OBT Id", "Destination", "Track", "WG Count",
               "First WG Arrival", "OBT Created",
               "OBTP Start", "OBTP End", "DEPD Start", "DEPD End",
               "Formation Time (s)", "Pre-OBTP Wait (s)",
               "Gate Wait (s)", "OBT Total on Classif Track (s)",
               "OBT Total (hh:mm:ss)"]
    ws.append(headers)
    for o in obts:
        ws.append([
            o["obtId"], o.get("destination"), o.get("trackId"),
            len(o.get("wgIds", [])),
            o.get("firstWgArrival"), o.get("createdAt"),
            o.get("obtpStart"), o.get("obtpEnd"),
            o.get("depdStart"), o.get("depdEnd"),
        ])
    for r in range(2, ws.max_row + 1):
        for col_letter in ("E", "F", "G", "H", "I", "J"):
            ws[f"{col_letter}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        # Derived (live formulas):
        # Formation = OBT Created (F) - First WG Arrival (E)
        # Pre-OBTP   = OBTP Start (G) - OBT Created (F)
        # Gate Wait  = DEPD Start (I) - OBTP End (H)
        # Total      = DEPD End (J)   - First WG Arrival (E)
        ws[f"K{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(F{r})),(F{r}-E{r})*86400,"")'
        ws[f"L{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(G{r})),(G{r}-F{r})*86400,"")'
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(H{r}),ISNUMBER(I{r})),(I{r}-H{r})*86400,"")'
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(J{r})),(J{r}-E{r})*86400,"")'
        # hh:mm:ss view of the OBT-total seconds (column N).
        ws[f"O{r}"] = f'=IF(ISNUMBER(N{r}),N{r}/86400,"")'
        for col in "KLMN":
            ws[f"{col}{r}"].number_format = "0.00"
        ws[f"O{r}"].number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    ws.column_dimensions["A"].width = 30
    ws.column_dimensions["B"].width = 12
    ws.column_dimensions["C"].width = 8
    ws.column_dimensions["D"].width = 10
    for col in "EFGHIJ":
        ws.column_dimensions[col].width = 19
    for col in "KLMN":
        ws.column_dimensions[col].width = 14
    ws.column_dimensions["O"].width = 16
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_wgs_raw_sheet(wb, wgs):
    ws = wb.create_sheet("WGs (raw)")
    headers = ["WG Id", "Track", "Arrived At", "OBT Id",
               "OBT Created", "OBT Departed",
               "Dwell Until OBT Created (s)", "Total in Classification (s)",
               "Total in Classification (hh:mm:ss)"]
    ws.append(headers)
    for w in wgs:
        ws.append([
            w["wgId"], w.get("trackId"), w.get("arrivedAt"),
            w.get("obtId"), w.get("obtCreatedAt"), w.get("obtDepartedAt"),
        ])
    for r in range(2, ws.max_row + 1):
        for col_letter in ("C", "E", "F"):
            ws[f"{col_letter}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        # Dwell = OBT Created (E) - Arrived At (C)
        # Total = OBT Departed (F) - Arrived At (C)
        ws[f"G{r}"] = f'=IF(AND(ISNUMBER(C{r}),ISNUMBER(E{r})),(E{r}-C{r})*86400,"")'
        ws[f"H{r}"] = f'=IF(AND(ISNUMBER(C{r}),ISNUMBER(F{r})),(F{r}-C{r})*86400,"")'
        # hh:mm:ss view of the WG total seconds (column H).
        ws[f"I{r}"] = f'=IF(ISNUMBER(H{r}),H{r}/86400,"")'
        for col in "GH":
            ws[f"{col}{r}"].number_format = "0.00"
        ws[f"I{r}"].number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    ws.column_dimensions["A"].width = 12
    ws.column_dimensions["B"].width = 8
    for col in "CEF":
        ws.column_dimensions[col].width = 19
    ws.column_dimensions["D"].width = 30
    for col in "GH":
        ws.column_dimensions[col].width = 22
    ws.column_dimensions["I"].width = 18
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions
    return ws


# (label, source-sheet, source-column-letter)
OBT_FORMATION_METRICS = [
    ("OBT formation time",                "OBTs (raw)", "K"),
    ("OBT pre-OBTP wait",                 "OBTs (raw)", "L"),
    ("OBT gate wait (exit-gate queue)",   "OBTs (raw)", "M"),
    ("OBT total on classif track",        "OBTs (raw)", "N"),
    ("WG dwell on classif track",         "WGs (raw)",  "G"),
    ("WG total in classif area",          "WGs (raw)",  "H"),
]


def build_obt_formation_sheet(wb, obts, wgs):
    ws = wb.create_sheet("OBT Formation")
    ws.append(["Metric", "Count", "Mean (s)", "Mean (min)", "Min (s)", "Max (s)",
               "Mean (hh:mm:ss)"])
    obt_last = len(obts) + 1
    wg_last = len(wgs) + 1
    for i, (label, sheet, col) in enumerate(OBT_FORMATION_METRICS, start=2):
        last = obt_last if sheet == "OBTs (raw)" else wg_last
        col_range = f"'{sheet}'!${col}$2:${col}${last}"
        ws.append([
            label,
            f"=COUNT({col_range})",
            f"=IFERROR(AVERAGE({col_range}),0)",
            f"=C{i}/60",
            f"=IFERROR(MIN({col_range}),0)",
            f"=IFERROR(MAX({col_range}),0)",
            f"=C{i}/86400",
        ])
    _style_header_row(ws)
    for r in range(2, ws.max_row + 1):
        for c in range(1, ws.max_column + 1):
            cell = ws.cell(row=r, column=c)
            cell.font = FONT
            if c >= 3 and c <= 6:
                cell.number_format = "0.00"
        ws.cell(row=r, column=2).number_format = "0"
        ws.cell(row=r, column=7).number_format = "[h]:mm:ss"
    ws.column_dimensions["A"].width = 32
    for col in "BCDEF":
        ws.column_dimensions[col].width = 14
    ws.column_dimensions["G"].width = 16
    ws.freeze_panes = "A2"
    chart = BarChart()
    chart.type = "bar"
    chart.style = 13
    chart.title = "Mean OBT / WG formation phase durations (s)"
    chart.y_axis.title = "Phase"
    chart.x_axis.title = "Mean duration (s)"
    last = ws.max_row
    data = Reference(ws, min_col=3, max_col=3, min_row=1, max_row=last)
    cats = Reference(ws, min_col=1, max_col=1, min_row=2, max_row=last)
    chart.add_data(data, titles_from_data=True)
    chart.set_categories(cats)
    chart.height = 10
    chart.width = 18
    # Chart anchored at I2 (was H2) so it doesn't sit on top of the new
    # Mean (hh:mm:ss) column G.
    ws.add_chart(chart, "I2")
    return ws


def build_throughput_sheet(wb, trains, obts):
    ws = wb.create_sheet("Throughput")
    ws.append(["Metric", "Value", "Notes"])
    entries = [t["entry"] for t in trains if "entry" in t]
    pushoff_ends = [t["pushOffEnd"] for t in trains if "pushOffEnd" in t]
    obt_created = [o["createdAt"] for o in obts if "createdAt" in o]
    obt_departed = [o["depdEnd"] for o in obts if "depdEnd" in o]
    if not entries:
        sim_start = sim_end = None
    else:
        sim_start = min(entries)
        sim_end = max(pushoff_ends + obt_departed + entries)
    sim_hours = ((sim_end - sim_start).total_seconds() / 3600.0) if sim_start else 0.0
    rate_in = len(entries) / sim_hours if sim_hours else 0.0
    rate_out = len(pushoff_ends) / sim_hours if sim_hours else 0.0
    rate_obt_in = len(obt_created) / sim_hours if sim_hours else 0.0
    rate_obt_out = len(obt_departed) / sim_hours if sim_hours else 0.0
    rows = [
        ("Sim start", sim_start.strftime("%Y-%m-%d %H:%M:%S") if sim_start else "—",
         "Earliest train Entry timestamp."),
        ("Sim end", sim_end.strftime("%Y-%m-%d %H:%M:%S") if sim_end else "—",
         "Latest event timestamp (Entry / PushOffComplete / Departed)."),
        ("Sim duration (h)", round(sim_hours, 2), "(Sim end - Sim start) / 3600"),
        ("Trains entered (Q2)", len(entries), "Count of TrainEvent;Entry rows."),
        ("Trains exited (Q3)", len(pushoff_ends), "Count of TrainEvent;PushOffComplete rows."),
        ("Trains/hour entered", round(rate_in, 3), "Trains entered / Sim duration (h)"),
        ("Trains/hour exited", round(rate_out, 3), "Trains exited / Sim duration (h)"),
        ("OBTs created (Q1 input)", len(obt_created), "Count of OutboundTrainCreated rows."),
        ("OBTs departed (Q1)", len(obt_departed), "Count of TrainEvent;Departed rows."),
        ("OBTs/hour created", round(rate_obt_in, 3), "OBTs created / Sim duration (h)"),
        ("OBTs/hour departed", round(rate_obt_out, 3), "OBTs departed / Sim duration (h)"),
    ]
    for r in rows:
        ws.append(r)
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=3):
        for cell in row:
            cell.font = FONT
            cell.alignment = Alignment(vertical="top", wrap_text=True)
    ws.column_dimensions["A"].width = 26
    ws.column_dimensions["B"].width = 22
    ws.column_dimensions["C"].width = 60
    ws.freeze_panes = "A2"
    return ws


# ── Optional Windows post-processor ──────────────────────────────────────────
def _maybe_run_xlsm_postprocessor():
    """Invoke Output/run_analytics_post.vbs via cscript to convert the
    just-written .xlsx into a macro-enabled .xlsm with the .bas modules in
    Output/vba_modules/ pre-imported. Windows-only — silently no-ops on
    other platforms. Failures are reported but don't crash analytics: the
    .xlsx is already a complete deliverable on its own."""
    import platform
    import subprocess
    if platform.system() != "Windows":
        return
    vbs = os.path.join(SCRIPT_DIR, "run_analytics_post.vbs")
    if not os.path.exists(vbs):
        return
    print(f"\nWindows detected — running post-processor: {vbs}")
    try:
        result = subprocess.run(
            ["cscript", "//Nologo", vbs],
            capture_output=True, text=True, timeout=90,
        )
        if result.stdout:
            print(result.stdout.rstrip())
        if result.stderr:
            print(result.stderr.rstrip())
        if result.returncode != 0:
            print(f"  (post-processor exited {result.returncode}; .xlsx is still good)")
    except FileNotFoundError:
        print("  (cscript not found on PATH — skipped; install Windows Script Host)")
    except subprocess.TimeoutExpired:
        print("  (post-processor timed out after 90s — skipped)")


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print(f"Reading: {CSV_PATH}")
    activities = parse_activity_durations(CSV_PATH)
    trains = parse_train_timeline(CSV_PATH)
    obts, wgs = parse_obts_and_wgs(CSV_PATH)
    print(f"Parsed {len(activities)} activity instances, "
          f"{len(trains)} inbound trains, {len(obts)} OBTs, "
          f"{len(wgs)} classified WGs.")
    if not activities:
        sys.exit("No activities found — nothing to write.")

    wb = Workbook()
    wb.remove(wb.active)
    build_definitions_sheet(wb)
    build_throughput_sheet(wb, trains, obts)
    build_process_durations_sheet(wb, activities)
    build_train_timeline_sheet(wb, trains)
    build_obt_formation_sheet(wb, obts, wgs)
    build_activities_raw_sheet(wb, activities)
    build_trains_raw_sheet(wb, trains)
    build_obts_raw_sheet(wb, obts)
    build_wgs_raw_sheet(wb, wgs)

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    wb.save(OUT_PATH)
    print(f"Wrote:   {OUT_PATH}")

    # Windows-only: convert .xlsx → .xlsm + import .bas modules so the
    # user gets a macro-enabled file ready to go. Silently skipped on
    # non-Windows or if Excel/cscript aren't available — the .xlsx alone
    # is still fully usable, the user just won't have the VBA macros.
    _maybe_run_xlsm_postprocessor()

    # Console previews
    by_type = defaultdict(list)
    for a in activities:
        by_type[a["activityType"]].append(a["durationSec"])
    print()
    print("Process durations (s):")
    print(f"  {'ActivityType':28s} {'N':>4s} {'Mean':>10s} {'Min':>9s} {'Max':>9s}")
    for t in sorted(by_type):
        v = by_type[t]
        print(f"  {t:28s} {len(v):4d} {sum(v)/len(v):10.2f} {min(v):9.2f} {max(v):9.2f}")

    def _diff(a, b):
        return (b - a).total_seconds() if a and b else None

    print()
    print("Train timeline (s):")
    metrics = {label: [] for label, _ in TIMELINE_METRICS}
    for t in trains:
        d = {
            "Queue wait (Q4)":            _diff(t.get("entry"),    t.get("arrivedTrack")),
            "Pre-ITP wait (Q5)":          _diff(t.get("arrivedTrack"), t.get("itpStart")),
            "ITP duration":               _diff(t.get("itpStart"), t.get("itpEnd")),
            "Pre-PushOff wait (Q6)":      _diff(t.get("itpEnd"),   t.get("pushOffStart")),
            "PushOff duration":           _diff(t.get("pushOffStart"), t.get("pushOffEnd")),
            "Total in arrival yard":      _diff(t.get("arrivedTrack"), t.get("pushOffEnd")),
            "Total in system (Q7)":       _diff(t.get("entry"),    t.get("pushOffEnd")),
        }
        for k, v in d.items():
            if v is not None:
                metrics[k].append(v)
    print(f"  {'Phase':28s} {'N':>4s} {'Mean':>10s} {'Min':>9s} {'Max':>9s}")
    for label, _ in TIMELINE_METRICS:
        v = metrics[label]
        if v:
            print(f"  {label:28s} {len(v):4d} {sum(v)/len(v):10.2f} {min(v):9.2f} {max(v):9.2f}")

    print()
    print("OBT / WG formation (s):")
    obt_formation = []
    obt_pre_obtp = []
    obt_gate_wait = []
    obt_total = []
    wg_dwell = []
    wg_total = []
    for o in obts:
        d = _diff(o.get("firstWgArrival"), o.get("createdAt"))
        if d is not None: obt_formation.append(d)
        d = _diff(o.get("createdAt"), o.get("obtpStart"))
        if d is not None: obt_pre_obtp.append(d)
        d = _diff(o.get("obtpEnd"), o.get("depdStart"))
        if d is not None: obt_gate_wait.append(d)
        d = _diff(o.get("firstWgArrival"), o.get("depdEnd"))
        if d is not None: obt_total.append(d)
    for w in wgs:
        d = _diff(w.get("arrivedAt"), w.get("obtCreatedAt"))
        if d is not None: wg_dwell.append(d)
        d = _diff(w.get("arrivedAt"), w.get("obtDepartedAt"))
        if d is not None: wg_total.append(d)
    print(f"  {'Phase':32s} {'N':>4s} {'Mean':>10s} {'Min':>9s} {'Max':>9s}")
    for label, vals in [
        ("OBT formation time",          obt_formation),
        ("OBT pre-OBTP wait",           obt_pre_obtp),
        ("OBT gate wait",               obt_gate_wait),
        ("OBT total on classif track",  obt_total),
        ("WG dwell on classif track",   wg_dwell),
        ("WG total in classif area",    wg_total),
    ]:
        if vals:
            print(f"  {label:32s} {len(vals):4d} {sum(vals)/len(vals):10.2f} "
                  f"{min(vals):9.2f} {max(vals):9.2f}")


if __name__ == "__main__":
    main()
