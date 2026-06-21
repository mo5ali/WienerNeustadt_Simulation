"""
analytics.py — process-duration, train-timeline, and OBT/WG formation metrics
for the WienerNeustadt simulation, with native Excel charts baked in.

Reads:  ../bin/Debug/net8.0/OutputFiles/SimulationLog.json
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

import json
import os
import sys
from collections import defaultdict
from datetime import datetime
from statistics import mean, pstdev

try:
    from openpyxl import Workbook
    from openpyxl.chart import BarChart, Reference
    from openpyxl.styles import Alignment, Font, PatternFill
except ImportError:
    sys.exit("openpyxl not installed. Run: pip install openpyxl")


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))
LOG_PATH = os.path.join(REPO_ROOT, "bin", "Debug", "net8.0", "OutputFiles",
                        "SimulationLog.json")
OUT_PATH = os.path.join(REPO_ROOT, "bin", "Debug", "net8.0", "OutputFiles",
                        "SimulationAnalytics.xlsx")

FONT = Font(name="Arial", size=10)
FONT_BOLD = Font(name="Arial", size=10, bold=True)
FONT_HEADER = Font(name="Arial", size=11, bold=True, color="FFFFFF")
FILL_HEADER = PatternFill("solid", start_color="305496")
ALIGN_TOP_WRAP = Alignment(vertical="top", wrap_text=True)

# When True, the finished workbook is opened in the OS default app
# (Excel / LibreOffice / Numbers) at the end of the run. Flip to False to
# disable the auto-open.
AUTO_OPEN_RESULT = True


# ── JSON parsing ──────────────────────────────────────────────────────────────
def _parse_length(raw):
    """Parse a length value out of an event's `details` string into a float, or
    None on failure.

    The C# logger writes lengths into the free-form `details` string with a
    locale-dependent decimal separator. TrainEvent rows log an integer
    (`length=224`), but ActivityEvent rows log a German-formatted decimal with
    a comma (`length=208,0`). `float("208,0")` raises ValueError. Normalise
    the comma to a dot before converting. The logger does not emit thousands
    separators, so a plain comma->dot swap is sufficient.
    """
    if raw is None:
        return None
    s = str(raw).strip().replace(",", ".")
    if not s:
        return None
    try:
        return float(s)
    except ValueError:
        return None


# Cache the loaded JSON so multiple parse_* calls in main() don't re-read the
# file. Keyed by absolute path so re-pointing the path invalidates the cache.
_LOG_CACHE = {}


# ── Driving-activity distance maps ─────────────────────────────────────────────
# Ported from the C# Activity subclasses. Duplicated here so analytics is
# self-contained (no need to re-run the sim if a map changes; just edit both
# sides). Sources:
#   Models/ArrivalDriveActivity.cs    -> ARRIVAL_DRIVE_DISTANCES
#   Models/DepartureDriveActivity.cs  -> DEPARTURE_DRIVE_DISTANCES
#   Models/PushOffActivity.cs         -> PUSH_ARRIVAL_LEG, PUSH_CLASS_LEG
# Used by parse_activity_durations to fill the From / To / Drive length
# columns on the Activities (raw) sheet.
ARRIVAL_DRIVE_DISTANCES = {
    "703": 766.25, "705": 765.77, "707": 670.93, "709": 629.33,
    "711": 587.72, "713": 548.41, "715": 521.53, "717": 471.68,
    "719": 505.47, "721": 395.90, "723": 435.33, "725": 476.05,
    "727": 556.20, "729": 587.14, "731": 587.14,
}
ARRIVAL_DRIVE_FALLBACK = 550.0

DEPARTURE_DRIVE_DISTANCES = {
    "605": 286.14, "607": 286.14, "609": 259.16, "611": 231.94,
    "613": 205.90, "615": 223.61, "617": 262.93, "619": 262.93,
    "621": 259.31, "623": 259.31, "625": 240.31, "627": 188.33,
    "629": 182.11,
}
DEPARTURE_DRIVE_FALLBACK = 245.0

PUSH_ARRIVAL_LEG = {
    "703": 193.03, "705": 193.03, "707": 165.50, "709": 139.11,
    "711": 111.74, "713": 84.51,  "715": 57.70,  "717": 29.68,
    "719": 35.20,  "721": 104.62, "723": 131.97, "725": 158.84,
    "727": 186.20, "729": 277.92, "731": 277.92,
}
PUSH_ARRIVAL_LEG_FALLBACK = 139.0

PUSH_CLASS_LEG = {
    "605": 212.40, "607": 185.37, "609": 157.84, "611": 131.32,
    "613": 104.40, "615": 76.89,  "617": 106.08, "619": 266.93,
    "621": 266.93, "623": 219.02, "625": 284.72, "627": 257.39,
    "629": 257.39,
}
PUSH_CLASS_LEG_FALLBACK = 212.0


def _load_log(log_path):
    """Load the JSON simulation log into a dict and cache it.

    Document shape:
        { "metadata": {...},
          "entities": { "inboundTrains": {...}, "wagonGroups": {...}, "outboundTrains": {...} },
          "events":   [ {...}, {...}, ... ] }
    """
    abs_path = os.path.abspath(log_path)
    if abs_path in _LOG_CACHE:
        return _LOG_CACHE[abs_path]
    if not os.path.exists(abs_path):
        sys.exit(f"Log not found: {abs_path}\n"
                 "Run the simulation first to produce SimulationLog.json.")
    with open(abs_path, encoding="utf-8") as f:
        doc = json.load(f)
    _LOG_CACHE[abs_path] = doc
    return doc


def _iter_events(log_path):
    """Yield event dicts in the order they were written, skipping anything
    that doesn't have a simTime (defensive)."""
    doc = _load_log(log_path)
    for ev in doc.get("events", []) or []:
        if not ev.get("simTime"):
            continue
        yield ev


def parse_activity_durations(log_path):
    """Build one record per activity that has both Started and Completed,
    plus a Submitted timestamp + entity + length + (for driving activities)
    From / To / Drive length. Output dict keys, used by build_activities_raw_sheet:

      activityType, activityId, entityId, length, location,
      submittedAt, startedAt, completedAt, durationSec,
      fromLocation, toLocation, driveLength   (driving activities only).

    Driving activities = ArrivalDrive, DepartureDrive, PushOffDrive. From/To/
    distance for the first two come from the static per-track maps at the top
    of this file. PushOffDrive needs the parent train's arrival track to know
    where the push started; we recover it via the parent-train id stored on
    the first WG of the cut (entities.wagonGroups[<wgId>].parentTrainId) and
    a trainId -> arrival-track lookup built from TrainEvent AssignedArrivalTrack
    rows. Total push distance = arrival-leg + class-leg."""
    doc = _load_log(log_path)
    wgs_meta = (doc.get("entities") or {}).get("wagonGroups") or {}

    train_arrival_track = {}
    by_id = {}

    for ev in _iter_events(log_path):
        et = ev.get("type")

        if et == "TrainEvent":
            if ev.get("action") == "AssignedArrivalTrack":
                tid = ev.get("entityId", "")
                track = ev.get("details", "") or ""
                if tid and track:
                    train_arrival_track[tid] = track
            continue

        if et != "ActivityEvent":
            continue

        status = ev.get("status")
        if status not in ("Submitted", "Started", "Completed"):
            continue
        try:
            ts = datetime.fromisoformat(ev["simTime"])
        except (ValueError, KeyError):
            continue

        activity_id = ev.get("activityId", "")
        activity_type = ev.get("activityType", "")
        details = ev.get("details", "") or ""

        entry = by_id.setdefault(activity_id, {
            "activityType": activity_type,
            "activityId": activity_id,
            "submittedAt": None,
            "startedAt": None,
            "completedAt": None,
            "entityId": None,
            "length": None,
            "location": None,
        })

        if status == "Submitted":
            entry["submittedAt"] = ts
            # Submitted details: entity=X;length=Y;location=Z;cu=W
            # ITP also carries joints=N (separation-joint count from
            # ManipulationActivity; see C# ArrivalControlUnit.ComputeSeparationJoints).
            for part in details.split(";"):
                if part.startswith("entity="):
                    entry["entityId"] = part.split("=", 1)[1]
                elif part.startswith("length="):
                    entry["length"] = _parse_length(part.split("=", 1)[1])
                elif part.startswith("location="):
                    entry["location"] = part.split("=", 1)[1]
                elif part.startswith("joints="):
                    try:
                        entry["separationJoints"] = int(part.split("=", 1)[1])
                    except ValueError:
                        pass
        elif status == "Started":
            entry["startedAt"] = ts
        elif status == "Completed":
            entry["completedAt"] = ts

    # Post-process: From / To / Drive length for driving activities.
    for entry in by_id.values():
        atype = entry["activityType"]
        loc = entry.get("location") or ""

        if atype == "ArrivalDrive":
            entry["fromLocation"] = "Entry gate"
            entry["toLocation"] = loc
            entry["driveLength"] = ARRIVAL_DRIVE_DISTANCES.get(loc, ARRIVAL_DRIVE_FALLBACK)

        elif atype == "DepartureDrive":
            entry["fromLocation"] = loc
            entry["toLocation"] = "Exit gate"
            entry["driveLength"] = DEPARTURE_DRIVE_DISTANCES.get(loc, DEPARTURE_DRIVE_FALLBACK)

        elif atype == "PushOffDrive":
            # entityId is "wg1+wg2+..."; find parent train via the first WG
            # that resolves in the entity dict, then look up its arrival track.
            ent = entry.get("entityId") or ""
            parent_train = None
            for wgid in ent.split("+"):
                pid = (wgs_meta.get(wgid) or {}).get("parentTrainId")
                if pid:
                    parent_train = pid
                    break
            from_track = train_arrival_track.get(parent_train or "")
            entry["fromLocation"] = from_track or "?"
            entry["toLocation"] = loc
            arr_leg = PUSH_ARRIVAL_LEG.get(from_track or "", PUSH_ARRIVAL_LEG_FALLBACK)
            class_leg = PUSH_CLASS_LEG.get(loc, PUSH_CLASS_LEG_FALLBACK)
            entry["driveLength"] = arr_leg + class_leg

    out = []
    for e in by_id.values():
        if e["startedAt"] and e["completedAt"]:
            e["durationSec"] = (e["completedAt"] - e["startedAt"]).total_seconds()
            out.append(e)
    return out


def parse_train_timeline(log_path):
    trains = {}
    for ev in _iter_events(log_path):
        if ev.get("type") != "TrainEvent":
            continue
        event_name = ev.get("action", "")
        train_id = ev.get("entityId", "")
        details = ev.get("details", "") or ""
        try:
            ts = datetime.fromisoformat(ev["simTime"])
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
                    entry["length"] = _parse_length(part.split("=", 1)[1])
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


def parse_obts_and_wgs(log_path):
    """Parse OBT lifecycle events and WG arrivals. Then match WGs to OBTs by
    track + time window: an OBT created at time T on track X owns all WGs that
    arrived on track X at or before T and weren't already claimed by a
    previous OBT on that track. Returns (obts_list, wgs_list)."""
    obts = {}
    wgs = {}

    for ev in _iter_events(log_path):
        try:
            ts = datetime.fromisoformat(ev["simTime"])
        except ValueError:
            continue
        et = ev.get("type")

        if et == "WagonGroupEvent":
            wg_id = ev.get("entityId", "")
            event_name = ev.get("action", "")
            details = ev.get("details", "") or ""
            if event_name == "ArrivedClassificationTrack":
                wgs.setdefault(wg_id, {"wgId": wg_id, "obtId": None})
                wgs[wg_id]["arrivedAt"] = ts
                wgs[wg_id]["trackId"] = details

        elif et == "TrainEvent":
            event_name = ev.get("action", "")
            train_id = ev.get("entityId", "")
            details = ev.get("details", "") or ""
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

        elif et == "ActivityEvent":
            activity_id = ev.get("activityId", "")
            activity_type = ev.get("activityType", "")
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


def parse_incoming_trains(log_path):
    """One record per inbound train with the 10 columns for the Incoming Trains
    sheet: ID, length, entry to station, at arrival track, ITP init/starts/ends,
    PushOff init/starts/ends.

    Length comes from the entity dict (entities.inboundTrains[id].length, which
    the C# logger now writes — sum-of-WG-lengths). Lifecycle timestamps come
    from the event stream:
      * Entry / ArrivedArrivalTrack: TrainEvent rows for the train.
      * ITP init / starts / ends: ActivityEvent for activityType
        IncomingTrainPreparation with status Submitted / Started / Completed.
      * PushOff init / starts / ends: ActivityEvent for activityType PushOff
        with status Submitted / Started / Completed.
    The trainId is recovered from the ActivityId, which is structured as
    Act_<ABBR>_<trainId>_<HHMMSS>_<trackId>."""
    doc = _load_log(log_path)
    inbound_entities = (doc.get("entities") or {}).get("inboundTrains") or {}

    by_train = {}

    for ev in _iter_events(log_path):
        try:
            ts = datetime.fromisoformat(ev["simTime"])
        except (ValueError, KeyError):
            continue

        et = ev.get("type")

        if et == "TrainEvent":
            tid = ev.get("entityId", "")
            if tid.startswith("OBT"):
                continue
            entry = by_train.setdefault(tid, {"id": tid})
            action = ev.get("action", "")
            if action == "Entry":
                entry["entryToStation"] = ts
            elif action == "AssignedArrivalTrack":
                # Captures the arrival track id from the TrainEvent
                # details field (= the RealLifeID of the assigned track).
                entry["assignedTrack"] = ev.get("details", "") or None
            elif action == "ArrivedArrivalTrack":
                entry["atArrivalTrack"] = ts

        elif et == "ActivityEvent":
            activity_type = ev.get("activityType", "")
            if activity_type not in ("ArrivalDrive", "IncomingTrainPreparation", "PushOff"):
                continue
            activity_id = ev.get("activityId", "")
            status = ev.get("status", "")
            # Recover trainId from Activity ID: Act_<ABBR>_<trainId>_<HHMMSS>_<trackId>
            parts = activity_id.split("_")
            if len(parts) < 5:
                continue
            tid = parts[2]
            if tid.startswith("OBT"):
                continue
            entry = by_train.setdefault(tid, {"id": tid})
            if activity_type == "ArrivalDrive":
                # ArrivalDrive ends == "At arrival track" (already captured as
                # the TrainEvent ArrivedArrivalTrack above), so we only record
                # the Started timestamp here per the column spec.
                if status == "Started":
                    entry["arrivalDriveStart"] = ts
            elif activity_type == "IncomingTrainPreparation":
                if status == "Submitted":
                    entry["itpInit"] = ts
                elif status == "Started":
                    entry["itpStarts"] = ts
                elif status == "Completed":
                    entry["itpEnds"] = ts
            elif activity_type == "PushOff":
                if status == "Submitted":
                    entry["pushOffInit"] = ts
                elif status == "Started":
                    entry["pushOffStarts"] = ts
                elif status == "Completed":
                    entry["pushOffEnds"] = ts

    # Stitch in the length from the entity dict written by SimulationLogger.
    # Falls back to None for any train that for some reason has no entity
    # entry (shouldn't happen, but the sheet handles a blank cell gracefully).
    for tid, rec in by_train.items():
        meta = inbound_entities.get(tid) or {}
        rec["length"] = meta.get("length")

    # Drop trains that never made it past Entry (rare; defensive).
    out = [r for r in by_train.values() if r.get("entryToStation") is not None]
    out.sort(key=lambda r: r["entryToStation"])
    return out


def parse_wagon_groups(log_path, trains, obts):
    """One record per wagon group with the 12 columns for the Wagon Groups
    sheet: ID, parent incoming train, length, destination, at arrival track,
    PushOffDrive starts/ends, parent outbound train, OBTP starts/ends,
    departure drive starts/ends.

    Static fields (id, parentTrainId, length, destination) come from the entity
    dict the C# logger writes (entities.wagonGroups[<id>]). The arrival-track
    timestamp is inherited from the parent train's ArrivedArrivalTrack event —
    a WG physically arrives on the arrival track when its parent train does,
    so we re-use parse_train_timeline's arrivedTrack value here. The
    PushOffDrive timestamps come from PushOffDrive ActivityEvents; the activity
    covers a consecutive same-destination CUT of WGs (ActivityId has the form
    Act_POD_<wg1+wg2+...>_<HHMMSS>_<destTrack>), so we split the combined
    entityId on '+' and credit each WG individually. The OBT-side timestamps
    are read off the matching OBT record produced by parse_obts_and_wgs."""
    doc = _load_log(log_path)
    wgs_meta = (doc.get("entities") or {}).get("wagonGroups") or {}

    # Parent-train arrival lookup: trainId -> ArrivedArrivalTrack timestamp.
    train_arrived = {t["trainId"]: t.get("arrivedTrack") for t in trains}

    # OBT lookups: obtId -> full obt record (for OBTP/DEPD timestamps) and
    # wgId -> obtId (for the parent-outbound-train column).
    obt_by_id = {o["obtId"]: o for o in obts}
    wg_to_obt = {}
    for o in obts:
        for wgid in o.get("wgIds", []):
            wg_to_obt[wgid] = o["obtId"]

    by_wg = {}

    # Seed records from the entity dict so every declared WG shows up in the
    # sheet, even those that for some reason produced no events at runtime.
    for wgid, meta in wgs_meta.items():
        parent = meta.get("parentTrainId") or None
        by_wg[wgid] = {
            "id": wgid,
            "parentIncomingTrain": parent,
            "length": meta.get("length"),
            "destination": meta.get("destination"),
            "atArrivalTrack": train_arrived.get(parent),
        }

    # PushOffDrive timestamps. We iterate the event stream once and split each
    # PushOffDrive activity across its constituent WGs.
    for ev in _iter_events(log_path):
        if ev.get("type") != "ActivityEvent":
            continue
        activity_type = ev.get("activityType", "")
        status = ev.get("status", "")
        try:
            ts = datetime.fromisoformat(ev["simTime"])
        except (ValueError, KeyError):
            continue
        activity_id = ev.get("activityId", "")
        parts = activity_id.split("_")
        if len(parts) < 5:
            continue

        if activity_type == "PushOffDrive" and status in ("Started", "Completed"):
            # entityId = "wg1+wg2+..." for the cut; credit each WG.
            for wgid in parts[2].split("+"):
                entry = by_wg.setdefault(wgid, {"id": wgid})
                if status == "Started" and "pushOffDriveStart" not in entry:
                    entry["pushOffDriveStart"] = ts
                elif status == "Completed" and "pushOffDriveEnd" not in entry:
                    entry["pushOffDriveEnd"] = ts

        elif activity_type in ("Securing", "Coupling") and status == "Completed":
            # ActivityId = Act_SEC_<wgId>_<HHMMSS>_<trackId> or
            # Act_COP_<wgId>_<HHMMSS>_<trackId>. Each SEC/COP activity is
            # per-WG so there's no "+" splitting. Whichever fires (SEC for
            # the first WG on a track, COP for the rest), the timestamp
            # marks the end of the WG's hands-on prep period; from here
            # until OBTP starts, the WG sits idle.
            wgid = parts[2]
            entry = by_wg.setdefault(wgid, {"id": wgid})
            entry["secCopEnd"] = ts
            # Remember whether this WG was the first on the track (Securing)
            # or joined an existing rake (Coupling); drives the narrative
            # prefix in the Classification context column.
            entry["secCopType"] = activity_type

    # Stitch OBT-side fields onto each WG.
    for wgid, rec in by_wg.items():
        obtid = wg_to_obt.get(wgid)
        rec["parentOutboundTrain"] = obtid
        o = obt_by_id.get(obtid, {}) if obtid else {}
        rec["obtpStart"] = o.get("obtpStart")
        rec["obtpEnd"] = o.get("obtpEnd")
        rec["depdStart"] = o.get("depdStart")
        rec["depdEnd"] = o.get("depdEnd")

    # -- Classification context narrative --------------------------------
    # One plain-language sentence per WG describing its life on the
    # classification track: how it joined (secured first, or coupled to the
    # sibling already there), the wait before each subsequent sibling got
    # coupled, then the OBTP window and the departure. Siblings are ordered
    # by SEC/COP completion. Wait gaps render as H:MM:SS (can exceed 24h);
    # OBTP/departure render as HH:MM:SS clock times.
    def _ctx_dur(delta):
        total = int(round(delta.total_seconds()))
        if total < 0:
            total = 0
        h = total // 3600
        m = (total % 3600) // 60
        s = total % 60
        return f"{h}:{m:02d}:{s:02d}"

    def _ctx_clock(dt):
        return dt.strftime("%H:%M:%S") if dt else "?"

    by_obt_group = defaultdict(list)
    for rec in by_wg.values():
        obtid = rec.get("parentOutboundTrain")
        if obtid:
            by_obt_group[obtid].append(rec)

    for obtid, sibs in by_obt_group.items():
        # Order siblings by SEC/COP completion; any missing it sink to the end.
        sibs.sort(key=lambda r: (r.get("secCopEnd") is None,
                                 r.get("secCopEnd") or datetime.max))
        for i, rec in enumerate(sibs):
            wg_i = rec.get("id")
            ctype = rec.get("secCopType")
            if ctype == "Coupling" and i > 0:
                sentence = (f"WG {wg_i} entered track got coupled to "
                            f"WG {sibs[i-1].get('id')}")
            elif ctype == "Coupling":
                sentence = f"WG {wg_i} entered track got coupled"
            else:
                sentence = f"WG {wg_i} entered track got secured"
            for j in range(i + 1, len(sibs)):
                prev_end = sibs[j-1].get("secCopEnd")
                this_end = sibs[j].get("secCopEnd")
                wait = _ctx_dur(this_end - prev_end) if (prev_end and this_end) else "?"
                sentence += (f" then waited {wait} for "
                             f"WG {sibs[j].get('id')} to get coupled")
            sentence += (f", OBTP started on {_ctx_clock(rec.get('obtpStart'))}"
                         f" finished on {_ctx_clock(rec.get('obtpEnd'))}"
                         f" and then left at {_ctx_clock(rec.get('depdEnd'))}")
            rec["classificationContext"] = sentence

    out = list(by_wg.values())
    # Sort by parent inbound train, then WG id, so consecutive WGs of one
    # train stay grouped together.
    out.sort(key=lambda r: (r.get("parentIncomingTrain") or "", r.get("id") or ""))
    return out


def parse_outbound_trains(log_path):
    """One record per outbound train with the 9 columns for the Outbound Trains
    sheet: ID, destination, length, OBTP init/starts/ends, departure drive
    init/starts/ends. Length + destination come from the entity dict; the six
    lifecycle timestamps come from OBTP and DepartureDrive ActivityEvents."""
    doc = _load_log(log_path)
    obt_meta = (doc.get("entities") or {}).get("outboundTrains") or {}

    by_obt = {}

    # Seed records from the entity dict so every declared OBT shows up.
    for obtid, meta in obt_meta.items():
        by_obt[obtid] = {
            "id": obtid,
            "destination": meta.get("destination"),
            "length": meta.get("length"),
            # The classification track the OBT formed on. Same field the C#
            # logger writes when CreateOutboundTrain runs.
            "trackId": meta.get("trackId"),
        }

    for ev in _iter_events(log_path):
        if ev.get("type") != "ActivityEvent":
            continue
        activity_type = ev.get("activityType", "")
        if activity_type not in ("OutboundTrainPreparation", "DepartureDrive"):
            continue
        status = ev.get("status", "")
        if status not in ("Submitted", "Started", "Completed"):
            continue
        try:
            ts = datetime.fromisoformat(ev["simTime"])
        except (ValueError, KeyError):
            continue
        # ActivityId: Act_<ABBR>_<obtId>_<HHMMSS>_<trackId>.
        # The OBT id contains a hyphen ("OBT...-Vienna") but never an
        # underscore, so parts[2] recovers it intact.
        activity_id = ev.get("activityId", "")
        parts = activity_id.split("_")
        if len(parts) < 5:
            continue
        obtid = parts[2]
        entry = by_obt.setdefault(obtid, {"id": obtid})

        if activity_type == "OutboundTrainPreparation":
            if status == "Submitted":
                entry["obtpInit"] = ts
            elif status == "Started":
                entry["obtpStart"] = ts
            elif status == "Completed":
                entry["obtpEnd"] = ts
        elif activity_type == "DepartureDrive":
            if status == "Submitted":
                entry["depdInit"] = ts
            elif status == "Started":
                entry["depdStart"] = ts
            elif status == "Completed":
                entry["depdEnd"] = ts

    out = list(by_obt.values())
    # Sort by OBTP init (earliest first); fall back to id for stable order
    # when a record has no OBTP init (e.g. log truncated before Submitted).
    out.sort(key=lambda r: (r.get("obtpInit") or datetime.max, r.get("id") or ""))
    return out


def parse_drive_stats(activities):
    """Aggregate driving-activity records (ArrivalDrive, DepartureDrive,
    PushOffDrive) by (activityType, fromLocation, toLocation). For each
    bucket compute count + mean/min/max for distance (m) and duration (s),
    plus the derived speed (km/h = distance / duration * 3.6).

    Input is the activities list returned by parse_activity_durations
    (which already attaches fromLocation / toLocation / driveLength to
    every driving record). Non-driving activities are skipped."""
    driving_types = ("ArrivalDrive", "DepartureDrive", "PushOffDrive")
    by_key = {}
    for a in activities:
        if a.get("activityType") not in driving_types:
            continue
        key = (a["activityType"],
               a.get("fromLocation") or "?",
               a.get("toLocation") or "?")
        by_key.setdefault(key, []).append({
            "distance": a.get("driveLength"),
            "duration": a.get("durationSec"),
        })

    rows = []
    for (atype, frm, to), items in by_key.items():
        dists = [it["distance"] for it in items if it.get("distance") is not None]
        durs = [it["duration"] for it in items if it.get("duration") is not None]
        # Per-instance speed so the mean isn't biased by mean(d)/mean(t).
        speeds = [
            (it["distance"] / it["duration"]) * 3.6
            for it in items
            if it.get("distance") is not None
            and it.get("duration")
            and it["duration"] > 0
        ]
        rows.append({
            "activityType": atype,
            "from": frm,
            "to": to,
            "count": len(items),
            "distMean": mean(dists) if dists else None,
            "distMin": min(dists) if dists else None,
            "distMax": max(dists) if dists else None,
            "durMean": mean(durs) if durs else None,
            "durMin": min(durs) if durs else None,
            "durMax": max(durs) if durs else None,
            "speedMean": mean(speeds) if speeds else None,
            "speedMin": min(speeds) if speeds else None,
            "speedMax": max(speeds) if speeds else None,
            "speedStd": pstdev(speeds) if len(speeds) > 1 else 0.0,
        })
    type_order = {t: i for i, t in enumerate(driving_types)}
    rows.sort(key=lambda r: (type_order.get(r["activityType"], 99),
                             r["from"], r["to"]))
    return rows


# ── Sheet styling helper ─────────────────────────────────────────────────────
def _style_header_row(ws, row=1):
    # Header row: 3× normal height (default ~15pt -> 45pt), centered both
    # axes, wrap_text so long header labels (e.g. "Total on classification
    # track (s)") don't get cropped at the new narrow column widths.
    ws.row_dimensions[row].height = 45
    for cell in ws[row]:
        cell.font = FONT_HEADER
        cell.fill = FILL_HEADER
        cell.alignment = Alignment(horizontal="center", vertical="center",
                                   wrap_text=True)


def _apply_universal_layout(ws, timestamp_cols=()):
    """Workbook-wide cosmetic layout. Call at the end of every sheet builder;
    overrides any prior column-width settings.

      * Every data cell (rows 2..end, every column) centered both axes.
        No wrap on data cells — long IDs may clip.
      * Column widths: 19.0 for letters in timestamp_cols, 8.2 otherwise.
        timestamp_cols is an iterable of column-letter strings ('D','E',...).

    Header row's height + wrap + alignment is applied by _style_header_row,
    which should be called BEFORE this helper."""
    from openpyxl.utils import get_column_letter
    data_align = Alignment(horizontal="center", vertical="center")
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row,
                            max_col=ws.max_column):
        for cell in row:
            cell.alignment = data_align
    ts_set = {c.upper() for c in timestamp_cols}
    for col_idx in range(1, ws.max_column + 1):
        letter = get_column_letter(col_idx)
        ws.column_dimensions[letter].width = 19.0 if letter in ts_set else 8.2


# ── Sheet builders ────────────────────────────────────────────────────────────
def build_overview_sheet(wb, trains, obts, wgs, activities):
    """One-glance Overview sheet. Sits as tab #2 (right after Definitions).

    Top block: Run summary (counts + simulation duration).
    Next three blocks: KPI averages per category (Incoming Trains,
    Wagon Groups, Outbound Trains). Each KPI is a row with Excel
    AVERAGE/MIN/MAX/MEDIAN formulas referencing the source per-entity
    sheet, so editing data on those sheets auto-recomputes Overview.

    All durations rendered as [h]:mm:ss; sim duration computed in
    Python (min/max of timestamps) and written as a fraction-of-day."""
    ws = wb.create_sheet("Overview", 1)

    # ── Title ─────────────────────────────────────────────────────────
    ws.append(["Wiener Neustadt Shunting Yard — Overview"])
    ws.append([])

    # ── Run summary block ────────────────────────────────────────────
    ws.append(["— Run summary —", "Value"])

    # Counts via COUNTA on identity columns of the per-entity sheets
    # (minus the header row).
    ws.append(["Inbound trains processed",
               "=COUNTA('Incoming Trains'!A:A)-1"])
    ws.append(["OBTs created",
               "=COUNTA('Outbound Trains'!A:A)-1"])
    ws.append(["WGs classified",
               "=COUNTA('Wagon Groups'!A:A)-1"])
    ws.append(["Activity instances logged",
               "=COUNTA('Activities (raw)'!A:A)-1"])

    # Sim duration: min Entry-to-station -> max DEPD-ends.
    sim_start = None
    sim_end = None
    for t in trains:
        e = t.get("entry")
        if e is not None:
            if sim_start is None or e < sim_start: sim_start = e
            if sim_end is None or e > sim_end: sim_end = e
        po = t.get("pushOffEnd")
        if po is not None and (sim_end is None or po > sim_end): sim_end = po
    for o in obts:
        d = o.get("depdEnd")
        if d is not None and (sim_end is None or d > sim_end): sim_end = d

    sim_duration_frac = None
    if sim_start is not None and sim_end is not None:
        sim_duration_frac = (sim_end - sim_start).total_seconds() / 86400.0

    ws.append(["Sim duration (hh:mm:ss)", sim_duration_frac])

    # ── Incoming Trains KPIs (means + spread, all hh:mm:ss) ──────────
    ws.append([])
    ws.append(["— Incoming Trains KPIs (per-train averages) —",
               "Mean", "Min", "Max", "Median"])

    # Each tuple: (display name, column letter on 'Incoming Trains').
    INCOMING_KPIS = [
        ("Entry Queue wait",                 "M"),
        ("Arrival drive duration",           "N"),
        ("ITP wait",                         "O"),
        ("ITP duration",                     "P"),
        ("PushOff wait",                     "Q"),
        ("PushOff duration",                 "R"),
        ("Total in arrival yard",            "S"),
        ("Total in system",                  "T"),
    ]
    for label, col in INCOMING_KPIS:
        ws.append([
            label,
            f"=IFERROR(AVERAGE('Incoming Trains'!{col}:{col}),0)",
            f"=IFERROR(MIN('Incoming Trains'!{col}:{col}),0)",
            f"=IFERROR(MAX('Incoming Trains'!{col}:{col}),0)",
            f"=IFERROR(MEDIAN('Incoming Trains'!{col}:{col}),0)",
        ])

    # ── Wagon Groups KPIs ────────────────────────────────────────────
    ws.append([])
    ws.append(["— Wagon Groups KPIs (per-WG averages) —",
               "Mean", "Min", "Max", "Median"])
    WG_KPIS = [
        ("Time in arrival yard",                  "M"),
        ("Time in classification yard",           "N"),
        ("Idle time on classification track",     "P"),
    ]
    for label, col in WG_KPIS:
        ws.append([
            label,
            f"=IFERROR(AVERAGE('Wagon Groups'!{col}:{col}),0)",
            f"=IFERROR(MIN('Wagon Groups'!{col}:{col}),0)",
            f"=IFERROR(MAX('Wagon Groups'!{col}:{col}),0)",
            f"=IFERROR(MEDIAN('Wagon Groups'!{col}:{col}),0)",
        ])

    # ── Outbound Trains KPIs ─────────────────────────────────────────
    ws.append([])
    ws.append(["— Outbound Trains KPIs (per-OBT averages) —",
               "Mean", "Min", "Max", "Median"])
    OUTBOUND_KPIS = [
        ("OBTP wait",                       "K"),
        ("OBTP duration",                   "L"),
        ("DEPD gate wait",                  "M"),
        ("DEPD duration",                   "N"),
        ("Total on classification track",   "O"),
    ]
    for label, col in OUTBOUND_KPIS:
        ws.append([
            label,
            f"=IFERROR(AVERAGE('Outbound Trains'!{col}:{col}),0)",
            f"=IFERROR(MIN('Outbound Trains'!{col}:{col}),0)",
            f"=IFERROR(MAX('Outbound Trains'!{col}:{col}),0)",
            f"=IFERROR(MEDIAN('Outbound Trains'!{col}:{col}),0)",
        ])

    # ── Styling pass ─────────────────────────────────────────────────
    # Walk every populated row, classify it (title / section header /
    # data row / blank), and format accordingly. Done in one sweep so
    # the rules stay co-located.
    for r in range(1, ws.max_row + 1):
        first = ws.cell(row=r, column=1).value
        if not first:
            continue
        text = str(first)
        if r == 1:
            # Title row -- bold, larger.
            ws.cell(row=r, column=1).font = Font(name="Arial", size=14, bold=True)
        elif text.startswith("—"):
            # Section header row -- bold for label + value-column subheaders.
            for c in range(1, 6):
                cell = ws.cell(row=r, column=c)
                if cell.value is not None:
                    cell.font = FONT_BOLD
        else:
            # Data row -- normal font + [h]:mm:ss formatting on numeric columns.
            ws.cell(row=r, column=1).font = FONT
            for c in range(2, 6):
                cell = ws.cell(row=r, column=c)
                if cell.value is None:
                    continue
                cell.font = FONT
                # Counts (Inbound trains processed, OBTs created, WGs
                # classified, Activity instances logged) are pure integer
                # formulas -- detect by the surrounding label.
                if text in ("Inbound trains processed", "OBTs created",
                            "WGs classified", "Activity instances logged"):
                    cell.number_format = "0"
                else:
                    cell.number_format = "[h]:mm:ss"

    # Column widths: A is wide for KPI names, B-E narrow for values.
    ws.column_dimensions["A"].width = 44
    for col in ("B", "C", "D", "E"):
        ws.column_dimensions[col].width = 14
    ws.freeze_panes = "B2"
    return ws


def build_definitions_sheet(wb):
    """KPI dictionary. Lists every metric the workbook surfaces, what it
    means in one sentence, and the exact formula (in terms of either
    column letters on a per-entity sheet, or simulation timestamps).
    Aligned with the current 8-sheet workbook layout — kept in sync with
    every sheet-builder change."""
    ws = wb.create_sheet("Definitions", 0)
    ws.append(["Metric", "Definition", "Formula / source"])
    rows = [
        ("— Sheets overview —", "", ""),
        ("Incoming Trains",
         "One row per inbound train, full lifecycle on a single line "
         "(identity, timestamps, derived waits/durations).",
         "tab 2; built by build_incoming_trains_sheet."),
        ("Wagon Groups",
         "One row per wagon group, full lifecycle (parent train → "
         "classification → parent OBT → departure).",
         "tab 3; built by build_wagon_groups_sheet."),
        ("Outbound Trains",
         "One row per outbound train (OBT), full lifecycle on the "
         "classification side (OBTP, exit gate, DEPD).",
         "tab 4; built by build_outbound_trains_sheet."),
        ("WG Yard Times",
         "Per-WG cross-check of arrival-yard and classification-yard "
         "dwell times, with all the supporting timestamps shown.",
         "built by build_wg_yard_times_sheet."),
        ("Activities (raw)",
         "One row per completed activity instance. Identity (type, id, "
         "entity, length), drive context (from/to/distance), timestamps "
         "(initialized/started/completed), duration.",
         "built by build_activities_raw_sheet."),
        ("OBTs (raw) / WGs (raw)",
         "Flat rows for outbound trains and classified wagon groups; "
         "redundant with the per-entity sheets but kept for legacy "
         "cross-references.",
         "built by build_obts_raw_sheet / build_wgs_raw_sheet."),

        ("— Incoming Trains KPIs —", "", ""),
        ("Entry Queue wait (hh:mm:ss)",
         "Time the train sat in the entry queue waiting for an arrival "
         "track to free up. Pure queue time — does NOT include the drive.",
         "Incoming Trains!M = (E − D)  "
         "= ArrivalDriveStart − EntryToStation"),
        ("Arrival drive duration (hh:mm:ss)",
         "Time the train spent physically driving from the entry gate to "
         "its assigned arrival track.",
         "Incoming Trains!N = (F − E)  "
         "= AtArrivalTrack − ArrivalDriveStart"),
        ("ITP wait (hh:mm:ss)",
         "Time between ITP being submitted to ResourceCU and the work "
         "actually commencing. Captures worker + shunt-loco allocation "
         "+ travel-to-site time.",
         "Incoming Trains!O = (H − G)  "
         "= ITP_Starts − ITP_Init"),
        ("ITP duration (hh:mm:ss)",
         "Time the ITP work itself ran, once resources arrived.",
         "Incoming Trains!P = (I − H)  "
         "= ITP_Ends − ITP_Starts"),
        ("PushOff wait (hh:mm:ss)",
         "Time between PushOff being submitted and the work actually "
         "commencing. Captures worker re-allocation + passage-track wait.",
         "Incoming Trains!Q = (K − J)  "
         "= PushOff_Starts − PushOff_Init"),
        ("PushOff duration (hh:mm:ss)",
         "Time the entire PushOff envelope ran (all sub-drives + "
         "loco pull-backs between them).",
         "Incoming Trains!R = (L − K)  "
         "= PushOff_Ends − PushOff_Starts"),
        ("Total in arrival yard (hh:mm:ss)",
         "Total time the inbound train physically occupied an arrival "
         "track (from arrival to last WG pushed off).",
         "Incoming Trains!S = (L − F)  "
         "= PushOff_Ends − AtArrivalTrack"),
        ("Total in system (hh:mm:ss)",
         "End-to-end inbound time: from entry-queue arrival until "
         "PushOff completed.",
         "Incoming Trains!T = (L − D)  "
         "= PushOff_Ends − EntryToStation"),

        ("— Wagon Groups KPIs —", "", ""),
        ("Time in arrival yard (hh:mm:ss)",
         "Time the WG was on the arrival track as part of its parent "
         "train, from arrival to being pushed off.",
         "Wagon Groups!M = (G − E)  "
         "= PushOffDriveEnds − AtArrivalTrack"),
        ("Time in classification yard (hh:mm:ss)",
         "Time the WG sat on its classification track, from arriving "
         "there (= PushOffDrive ends) until the parent OBT physically "
         "departed.",
         "Wagon Groups!N = (L − G)  "
         "= DepartureDriveEnds − PushOffDriveEnds"),
        ("Idle time on classification track (hh:mm:ss)",
         "Time the WG waits after getting coupled or secured until the "
         "OBTP activity starts — the 'doing nothing' window after its own "
         "prep is done but before OBT-wide work begins.",
         "Wagon Groups!P = (I − O)  "
         "= OBTP_Starts − SEC/COP_End"),
        ("Classification context",
         "Plain-language narrative of the WG's time on the classification "
         "track: how it joined (secured first, or coupled to the sibling "
         "already there), the wait before each later sibling coupled, the "
         "OBTP window, and departure.",
         "Wagon Groups!Q; built in parse_wagon_groups."),

        ("— Outbound Trains KPIs —", "", ""),
        ("OBTP wait (hh:mm:ss)",
         "Time between OBTP being submitted and actually starting. "
         "Captures worker + train-loco allocation + travel.",
         "Outbound Trains!K = (F − E)  "
         "= OBTP_Starts − OBTP_Init"),
        ("OBTP duration (hh:mm:ss)",
         "Time the OBTP work itself ran.",
         "Outbound Trains!L = (G − F)  "
         "= OBTP_Ends − OBTP_Starts"),
        ("DEPD gate wait (hh:mm:ss)",
         "Time the OBT was ready to depart (DEPD submitted) but stuck "
         "waiting for the single exit-gate resource. Headline bottleneck "
         "metric given exitGateCount = 1.",
         "Outbound Trains!M = (I − H)  "
         "= DEPD_Starts − DEPD_Init"),
        ("DEPD duration (hh:mm:ss)",
         "Time the train spent physically driving from its classification "
         "track to the exit gate.",
         "Outbound Trains!N = (J − I)  "
         "= DEPD_Ends − DEPD_Starts"),
        ("Total on classification track (hh:mm:ss)",
         "End-to-end outbound time, from OBT-creation moment (≈ OBTP init) "
         "until DEPD completes.",
         "Outbound Trains!O = (J − E)  "
         "= DEPD_Ends − OBTP_Init"),

        ("— Activities (raw) reference —", "", ""),
        ("Activity row",
         "One row per completed activity (Started AND Completed present). "
         "Pending / cancelled activities are excluded from this sheet.",
         "parse_activity_durations in analytics.py."),
        ("From / To / Drive length",
         "Populated only for the three driving activity types "
         "(ArrivalDrive, DepartureDrive, PushOffDrive). Distance values "
         "come from static maps near the top of analytics.py (ported from "
         "C# Models/*DriveActivity.cs and Models/PushOffActivity.cs).",
         "ARRIVAL_DRIVE_DISTANCES + DEPARTURE_DRIVE_DISTANCES + "
         "PUSH_ARRIVAL_LEG + PUSH_CLASS_LEG (in analytics.py)."),
        ("Duration (hh:mm:ss)",
         "Wall-clock time the activity spent in its 'running' state. "
         "Rendered as hh:mm:ss across the whole workbook.",
         "Activities (raw)!K = Completed − Started"),

        ("— Activity type glossary —", "", ""),
        ("Activity type abbreviations",
         "ARRD = ArrivalDrive (train enters yard on own loco); "
         "ITP = IncomingTrainPreparation (workers prep arriving train); "
         "PO = PushOff (parent envelope); "
         "POD = PushOffDrive (one sub-drive per consecutive-same-destination "
         "cut of WGs); "
         "SEC = Securing (first WG on a classification track); "
         "COP = Coupling (joining WG to existing WGs on track); "
         "OBTP = OutboundTrainPreparation (workers + train loco prep OBT); "
         "DEPD = DepartureDrive (OBT exits through gate).",
         "(reference — see Models/Activity.cs GetActivityAbbreviation)."),

        ("— WG → OBT matching —", "", ""),
        ("How parent OBT is derived for a WG",
         "WGs are matched to OBTs by classification track + time window: "
         "an OBT created at time T on track X owns every WG that arrived "
         "on track X at or before T and wasn't already claimed by an "
         "earlier OBT on the same track. Greedy, earliest-OBT-first.",
         "parse_obts_and_wgs in analytics.py."),
    ]
    for r in rows:
        ws.append(r)
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=3):
        is_section = row[0].value and str(row[0].value).startswith("—")
        for cell in row:
            cell.font = FONT_BOLD if is_section else FONT
            cell.alignment = ALIGN_TOP_WRAP
    _apply_universal_layout(ws, timestamp_cols=[])
    ws.freeze_panes = "A2"
    return ws


def build_activities_raw_sheet(wb, activities):
    """Per-activity row. 11 columns: identity, drive context (for driving
    activities), three lifecycle timestamps, one duration (hh:mm:ss).

      A ActivityType   B ActivityId      C Entity         D Length (m)
      E From           F To              G Drive length (m)
      H InitializedAt  I StartedAt       J CompletedAt
      K Duration (hh:mm:ss)

    Duration is stored as a fraction of a day so Excel's [h]:mm:ss format
    displays it as hours:minutes:seconds. The header name "Duration (hh:mm:ss)"
    is what the chart-building VBA macros now look up via FindHeader."""
    ws = wb.create_sheet("Activities (raw)")
    ws.append([
        "ActivityType", "ActivityId", "Entity", "Length (m)",
        "From", "To", "Drive length (m)",
        "InitializedAt", "StartedAt", "CompletedAt",
        "Duration (hh:mm:ss)",
        "Number of separation joints",
    ])
    for a in sorted(activities, key=lambda x: (x["activityType"], x["startedAt"])):
        dur = a.get("durationSec")
        ws.append([
            a["activityType"],
            a["activityId"],
            a.get("entityId"),
            a.get("length"),
            a.get("fromLocation"),
            a.get("toLocation"),
            a.get("driveLength"),
            a.get("submittedAt"),
            a["startedAt"],
            a["completedAt"],
            dur / 86400.0 if dur is not None else None,
            (a.get("separationJoints")
             if a["activityType"] == "IncomingTrainPreparation"
             else None),
        ])

    for r in range(2, ws.max_row + 1):
        ws.cell(row=r, column=4).number_format = "0.0"             # Length (m)
        ws.cell(row=r, column=7).number_format = "0.0"             # Drive length (m)
        ws.cell(row=r, column=8).number_format = "yyyy-mm-dd hh:mm:ss"
        ws.cell(row=r, column=9).number_format = "yyyy-mm-dd hh:mm:ss"
        ws.cell(row=r, column=10).number_format = "yyyy-mm-dd hh:mm:ss"
        ws.cell(row=r, column=11).number_format = "[h]:mm:ss"      # Duration
        ws.cell(row=r, column=12).number_format = "0"             # Joints (integer)

    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT

    ws.freeze_panes = "C2"
    _apply_universal_layout(ws, timestamp_cols=["H", "I", "J"])
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_incoming_trains_sheet(wb, records):
    """Per-train lifecycle sheet: one row per inbound train, 20 columns.
    Identity (A-C), full timestamp lifecycle (D-L) and Excel-formula
    duration / wait columns (M-T) derived from those timestamps.

    Column layout:
      A ID         B Length (m)            C Track
      D Entry to station                   E Arrival drive start
      F At arrival track
      G ITP init   H ITP starts            I ITP ends
      J PushOff init   K PushOff starts    L PushOff ends
      M Queue wait (s)         = (E - D) * 86400
      N Arrival drive dur (s)  = (F - E) * 86400
      O ITP wait (s)           = (H - G) * 86400
      P ITP duration (s)       = (I - H) * 86400
      Q PushOff wait (s)       = (K - J) * 86400
      R PushOff duration (s)   = (L - K) * 86400
      S Total in arrival yard  = (L - F) * 86400
      T Total in system (s)    = (L - D) * 86400

    "Wait" columns measure the gap between request submission and the work
    actually commencing (resources allocated + travelled). "Duration" columns
    measure the work itself. Queue wait is pure entry-queue time (legacy
    Trains (raw) sheet's Queue Wait conflated this with the arrival drive,
    which is now its own column N)."""
    ws = wb.create_sheet("Incoming Trains", 1)
    headers = [
        "ID", "Length (m)", "Track",
        "Entry to station", "Arrival drive start", "At arrival track",
        "ITP init", "ITP starts", "ITP ends",
        "PushOff init", "PushOff starts", "PushOff ends",
        "Entry Queue wait (hh:mm:ss)", "Arrival drive dur (hh:mm:ss)",
        "ITP wait (hh:mm:ss)", "ITP duration (hh:mm:ss)",
        "PushOff wait (hh:mm:ss)", "PushOff duration (hh:mm:ss)",
        "Total in arrival yard (hh:mm:ss)", "Total in system (hh:mm:ss)",
    ]
    ws.append(headers)
    for rec in records:
        ws.append([
            rec.get("id"),
            rec.get("length"),
            rec.get("assignedTrack"),
            rec.get("entryToStation"),
            rec.get("arrivalDriveStart"),
            rec.get("atArrivalTrack"),
            rec.get("itpInit"),
            rec.get("itpStarts"),
            rec.get("itpEnds"),
            rec.get("pushOffInit"),
            rec.get("pushOffStarts"),
            rec.get("pushOffEnds"),
        ])

    last_data_row = ws.max_row
    for r in range(2, last_data_row + 1):
        # Identity / number formats
        ws[f"B{r}"].number_format = "0.0"
        # Timestamp formats (D..L = 9 datetime columns)
        for col in ("D", "E", "F", "G", "H", "I", "J", "K", "L"):
            ws[f"{col}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        # Excel-formula duration / wait columns.
        # ISNUMBER() check guards against blank cells from records that
        # never reached that lifecycle stage (rare; defensive).
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(D{r}),ISNUMBER(E{r})),(E{r}-D{r}),"")'  # Queue wait
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(F{r})),(F{r}-E{r}),"")'  # Arrival drive dur
        ws[f"O{r}"] = f'=IF(AND(ISNUMBER(G{r}),ISNUMBER(H{r})),(H{r}-G{r}),"")'  # ITP wait
        ws[f"P{r}"] = f'=IF(AND(ISNUMBER(H{r}),ISNUMBER(I{r})),(I{r}-H{r}),"")'  # ITP duration
        ws[f"Q{r}"] = f'=IF(AND(ISNUMBER(J{r}),ISNUMBER(K{r})),(K{r}-J{r}),"")'  # PushOff wait
        ws[f"R{r}"] = f'=IF(AND(ISNUMBER(K{r}),ISNUMBER(L{r})),(L{r}-K{r}),"")'  # PushOff dur
        ws[f"S{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(L{r})),(L{r}-F{r}),"")'  # Total in arrival yard
        ws[f"T{r}"] = f'=IF(AND(ISNUMBER(D{r}),ISNUMBER(L{r})),(L{r}-D{r}),"")'  # Total in system
        for col in ("M", "N", "O", "P", "Q", "R", "S", "T"):
            ws[f"{col}{r}"].number_format = "[h]:mm:ss"

    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT

    _apply_universal_layout(ws, timestamp_cols=["D", "E", "F", "G", "H", "I", "J", "K", "L"])
    ws.freeze_panes = "D2"   # ID + Length + Track stay visible while scrolling
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_wagon_groups_sheet(wb, records):
    """Per-WG lifecycle sheet. 17 columns: identity (A-D), full timestamp
    lifecycle (E-L), KPI columns (M-P), and a plain-language narrative (Q).

    Column layout:
      A ID                B Parent incoming train       C Length (m)   D Destination
      E At arrival track  F PushOffDrive starts         G PushOffDrive ends
      H Parent outbound train
      I OBTP starts       J OBTP ends
      K Departure drive starts                          L Departure drive ends
      M Time in arrival yard (hh:mm:ss)         = G - E
        Time the WG was on the arrival track as part of its parent train,
        from arrival to being pushed off.
      N Time in classification yard (hh:mm:ss)  = L - G
        Time the WG sat on its classification track, from arriving there
        (= PushOffDrive ends) until the parent OBT physically departed.
      O SEC/COP end                             (raw timestamp, anchors P)
      P Idle time on classification track (hh:mm:ss) = I - O
        Time the WG waits after getting coupled or secured until the OBTP
        activity starts -- the "doing nothing" window after its own prep
        is done but before OBT-wide work begins."""
    ws = wb.create_sheet("Wagon Groups", 2)
    headers = [
        "ID", "Parent incoming train", "Length (m)", "Destination",
        "At arrival track",
        "PushOffDrive starts", "PushOffDrive ends",
        "Parent outbound train",
        "OBTP starts", "OBTP ends",
        "Departure drive starts", "Departure drive ends",
        "Time in arrival yard (hh:mm:ss)", "Time in classification yard (hh:mm:ss)",
        "SEC/COP end",
        "Idle time on classification track (hh:mm:ss)",
        "Classification context",
    ]
    ws.append(headers)
    for rec in records:
        ws.append([
            rec.get("id"),
            rec.get("parentIncomingTrain"),
            rec.get("length"),
            rec.get("destination"),
            rec.get("atArrivalTrack"),
            rec.get("pushOffDriveStart"),
            rec.get("pushOffDriveEnd"),
            rec.get("parentOutboundTrain"),
            rec.get("obtpStart"),
            rec.get("obtpEnd"),
            rec.get("depdStart"),
            rec.get("depdEnd"),
            None, None,             # M, N filled by formulas below
            rec.get("secCopEnd"),   # O
            None,                   # P filled by formula below
            rec.get("classificationContext"),   # Q narrative
        ])

    for r in range(2, ws.max_row + 1):
        ws[f"C{r}"].number_format = "0.0"
        for col in ("E", "F", "G", "I", "J", "K", "L", "O"):
            ws[f"{col}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(G{r})),(G{r}-E{r}),"")'
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(G{r}),ISNUMBER(L{r})),(L{r}-G{r}),"")'
        # Idle time on classification track = OBTP starts (I) - SEC/COP end (O).
        # The actual "doing nothing" period for the WG between its hands-on
        # prep finishing and OBTP commencing.
        ws[f"P{r}"] = f'=IF(AND(ISNUMBER(I{r}),ISNUMBER(O{r})),(I{r}-O{r}),"")'
        for col in ("M", "N", "P"):
            ws[f"{col}{r}"].number_format = "[h]:mm:ss"

    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT

    _apply_universal_layout(ws, timestamp_cols=["E", "F", "G", "I", "J", "K", "L", "O"])
    # Classification context (Q) is a long sentence: widen it and switch to
    # left alignment with wrap so it reads naturally instead of being
    # centered in an 8.2-wide column.
    ws.column_dimensions["Q"].width = 100
    for r in range(2, ws.max_row + 1):
        ws[f"Q{r}"].alignment = Alignment(horizontal="left", vertical="center",
                                          wrap_text=True)
    ws.freeze_panes = "E2"
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_outbound_trains_sheet(wb, records):
    """Per-OBT lifecycle sheet. Identity (A-D), full timestamp lifecycle
    (E-J) and Excel-formula KPI columns (K-O) derived from those
    timestamps.

    Column layout:
      A ID            B Destination          C Length (m)       D Track
      E OBTP init     F OBTP starts          G OBTP ends
      H DEPD init     I DEPD starts          J DEPD ends
      K OBTP wait (hh:mm:ss)                      = F - E
      L OBTP duration (hh:mm:ss)                  = G - F
      M DEPD gate wait (hh:mm:ss)                 = I - H
      N DEPD duration (hh:mm:ss)                  = J - I
      O Total on classification track (hh:mm:ss)  = J - E

    "OBTP init" anchors the "Total on classification track" calculation --
    in this simulation CreateOutboundTrain runs RequestOutboundTrainPreparation
    synchronously, so OBTP init ~ the moment the OBT first exists as an
    entity. "DEPD gate wait" captures the period the train was ready to
    depart but blocked on the single exit-gate resource."""
    ws = wb.create_sheet("Outbound Trains", 3)
    headers = [
        "ID", "Destination", "Length (m)", "Track",
        "OBTP init", "OBTP starts", "OBTP ends",
        "Departure drive init", "Departure drive starts", "Departure drive ends",
        "OBTP wait (hh:mm:ss)", "OBTP duration (hh:mm:ss)",
        "DEPD gate wait (hh:mm:ss)", "DEPD duration (hh:mm:ss)",
        "Total on classification track (hh:mm:ss)",
    ]
    ws.append(headers)
    for rec in records:
        ws.append([
            rec.get("id"),
            rec.get("destination"),
            rec.get("length"),
            rec.get("trackId"),
            rec.get("obtpInit"),
            rec.get("obtpStart"),
            rec.get("obtpEnd"),
            rec.get("depdInit"),
            rec.get("depdStart"),
            rec.get("depdEnd"),
        ])

    for r in range(2, ws.max_row + 1):
        ws[f"C{r}"].number_format = "0.0"
        # 6 timestamp columns E..J
        for col in ("E", "F", "G", "H", "I", "J"):
            ws[f"{col}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        # Excel-formula KPI columns K..O
        ws[f"K{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(F{r})),(F{r}-E{r}),"")'  # OBTP wait
        ws[f"L{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(G{r})),(G{r}-F{r}),"")'  # OBTP duration
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(H{r}),ISNUMBER(I{r})),(I{r}-H{r}),"")'  # DEPD gate wait
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(I{r}),ISNUMBER(J{r})),(J{r}-I{r}),"")'  # DEPD duration
        ws[f"O{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(J{r})),(J{r}-E{r}),"")'  # Total on classif track
        for col in ("K", "L", "M", "N", "O"):
            ws[f"{col}{r}"].number_format = "[h]:mm:ss"

    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT

    _apply_universal_layout(ws, timestamp_cols=["E", "F", "G", "H", "I", "J"])
    ws.freeze_panes = "E2"   # ID + Destination + Length + Track stay visible
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_drive_stats_sheet(wb, rows):
    """Per-(ActivityType × From → To) aggregation of driving activities.
    Slots as tab #5 (after Outbound Trains) so the supervisor's
    "table of drive times / lengths / speeds" lives next to the per-entity
    sheets it summarises.

    Column layout:
      A Activity type      B From          C To            D Count
      E Mean dist (m)      F Min dist      G Max dist
      H Mean dur (s)       I Min dur       J Max dur
      K Mean speed (km/h)  L Min speed     M Max speed     N Speed std
    """
    ws = wb.create_sheet("Drive Stats", 4)
    ws.append([
        "Activity Type", "From", "To", "Count",
        "Mean Dist (m)", "Min Dist (m)", "Max Dist (m)",
        "Mean Duration (hh:mm:ss)", "Min Duration (hh:mm:ss)", "Max Duration (hh:mm:ss)",
        "Mean Speed (km/h)", "Min Speed (km/h)", "Max Speed (km/h)",
        "Speed Std (km/h)",
    ])
    for r in rows:
        ws.append([
            r["activityType"], r["from"], r["to"], r["count"],
            r["distMean"], r["distMin"], r["distMax"],
            (r["durMean"]/86400 if r.get("durMean") else None),
            (r["durMin"]/86400 if r.get("durMin") else None),
            (r["durMax"]/86400 if r.get("durMax") else None),
            r["speedMean"], r["speedMin"], r["speedMax"], r["speedStd"],
        ])
    for row in range(2, ws.max_row + 1):
        for col in ("E", "F", "G"):
            ws[f"{col}{row}"].number_format = "0.00"
        for col in ("H", "I", "J"):
            ws[f"{col}{row}"].number_format = "[h]:mm:ss"
        for col in ("K", "L", "M", "N"):
            ws[f"{col}{row}"].number_format = "0.0"
        ws[f"D{row}"].number_format = "0"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    _apply_universal_layout(ws, timestamp_cols=[])
    ws.freeze_panes = "E2"
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


# ── OBT / WG sheets (Step 3) ─────────────────────────────────────────────────
def build_obts_raw_sheet(wb, obts):
    ws = wb.create_sheet("OBTs (raw)")
    headers = ["OBT Id", "Destination", "Track", "WG Count",
               "First WG Arrival", "OBT Created",
               "OBTP Start", "OBTP End", "DEPD Start", "DEPD End",
               "Formation Time (hh:mm:ss)", "Pre-OBTP Wait (hh:mm:ss)",
               "Gate Wait (hh:mm:ss)", "OBT Total on Classif Track (hh:mm:ss)"]
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
        ws[f"K{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(F{r})),(F{r}-E{r}),"")'
        ws[f"L{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(G{r})),(G{r}-F{r}),"")'
        ws[f"M{r}"] = f'=IF(AND(ISNUMBER(H{r}),ISNUMBER(I{r})),(I{r}-H{r}),"")'
        ws[f"N{r}"] = f'=IF(AND(ISNUMBER(E{r}),ISNUMBER(J{r})),(J{r}-E{r}),"")'
        for col in "KLMN":
            ws[f"{col}{r}"].number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    _apply_universal_layout(ws, timestamp_cols=["E","F","G","H","I","J"])
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions
    return ws


def build_wgs_raw_sheet(wb, wgs):
    ws = wb.create_sheet("WGs (raw)")
    headers = ["WG Id", "Track", "Arrived At", "OBT Id",
               "OBT Created", "OBT Departed",
               "Dwell Until OBT Created (hh:mm:ss)", "Total in Classification (hh:mm:ss)"]
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
        ws[f"G{r}"] = f'=IF(AND(ISNUMBER(C{r}),ISNUMBER(E{r})),(E{r}-C{r}),"")'
        ws[f"H{r}"] = f'=IF(AND(ISNUMBER(C{r}),ISNUMBER(F{r})),(F{r}-C{r}),"")'
        for col in "GH":
            ws[f"{col}{r}"].number_format = "[h]:mm:ss"
    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    _apply_universal_layout(ws, timestamp_cols=["C","E","F"])
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
    # Generous timeout: building the scatter chart sheets via Excel COM is
    # slow (many series x marker styling). If this is killed mid-run, the
    # hidden Excel is orphaned, keeps the .xlsm locked, and the user gets a
    # "file in use" dialog + missing charts. 300s gives ample headroom.
    try:
        result = subprocess.run(
            ["cscript", "//Nologo", vbs],
            capture_output=True, text=True, timeout=300,
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
        print("  (post-processor timed out after 300s — skipped; charts may be incomplete)")


def _open_result_file():
    """Open the finished analytics workbook in the OS default application
    (Excel on Windows, LibreOffice/Numbers elsewhere). Prefers the .xlsm
    produced by the Windows post-processor and falls back to the .xlsx if
    the .xlsm wasn't created. Best-effort — any failure just prints a note
    and never crashes the run. Controlled by the AUTO_OPEN_RESULT flag."""
    if not AUTO_OPEN_RESULT:
        return
    import platform
    import subprocess
    import time
    xlsm = os.path.splitext(OUT_PATH)[0] + ".xlsm"
    target = xlsm if os.path.exists(xlsm) else OUT_PATH
    if not os.path.exists(target):
        return
    # The post-processor's hidden Excel may take a moment to release the file
    # after it quits. Poll until we can open it read-write (i.e. nobody holds
    # an exclusive lock) so the user doesn't get a "file in use by me" dialog.
    deadline = time.time() + 20
    while time.time() < deadline:
        try:
            with open(target, "r+b"):
                break          # lock released
        except (PermissionError, OSError):
            time.sleep(0.5)
    print(f"Opening {target} ...")
    try:
        system = platform.system()
        if system == "Windows":
            os.startfile(target)  # type: ignore[attr-defined]
        elif system == "Darwin":
            subprocess.Popen(["open", target])
        else:
            subprocess.Popen(["xdg-open", target])
    except Exception as e:  # noqa: BLE001 — best-effort launch
        print(f"  (could not auto-open the file: {e})")


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print(f"Reading: {LOG_PATH}")
    activities = parse_activity_durations(LOG_PATH)
    trains = parse_train_timeline(LOG_PATH)
    obts, wgs = parse_obts_and_wgs(LOG_PATH)
    wg_yard = parse_wg_yard_times(LOG_PATH, obts)
    incoming_trains = parse_incoming_trains(LOG_PATH)
    outbound_trains = parse_outbound_trains(LOG_PATH)
    wagon_groups = parse_wagon_groups(LOG_PATH, trains, obts)
    drive_stats = parse_drive_stats(activities)
    print(f"Parsed {len(activities)} activity instances, "
          f"{len(trains)} inbound trains, {len(obts)} OBTs, "
          f"{len(wgs)} classified WGs, {len(wg_yard)} WG yard-time rows, "
          f"{len(incoming_trains)} incoming-train rows, "
          f"{len(wagon_groups)} wagon-group rows, "
          f"{len(outbound_trains)} outbound-train rows.")
    if not activities:
        sys.exit("No activities found — nothing to write.")

    wb = Workbook()
    wb.remove(wb.active)
    build_definitions_sheet(wb)
    build_incoming_trains_sheet(wb, incoming_trains)
    build_wagon_groups_sheet(wb, wagon_groups)
    build_outbound_trains_sheet(wb, outbound_trains)
    build_drive_stats_sheet(wb, drive_stats)
    # Overview is built LAST among the front tabs so its create_sheet
    # ("Overview", 1) lands cleanly between Definitions and Incoming
    # Trains, instead of being bumped rightward by later inserts.
    build_overview_sheet(wb, trains, obts, wgs, activities)
    build_wg_yard_times_sheet(wb, wg_yard)
    build_activities_raw_sheet(wb, activities)
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

    # Launch the finished workbook in the OS default app (last step so the
    # console summary above has already printed).
    _open_result_file()


# ── WG yard-time sheet (per-WG arrival/classification dwell) ──────────────────
def parse_wg_yard_times(log_path, obts):
    """One record per wagon group with the 10 timestamps/IDs the supervisor's
    'time in each yard area' metric needs:
        WG id, parent train id, train spawn, train-arrived-at-arrival-track,
        WG push-off-drive start, WG arrived-at-classification-track,
        parent OBT id, OBT formed, OBT leaves classif (DEPD start),
        train exits station (Departed).
    OBT-side fields come from the already-matched `obts` list."""
    trains = {}
    wg = {}
    for event in _iter_events(log_path):
        try:
            ts = datetime.fromisoformat(event["simTime"])
        except ValueError:
            continue
        et = event.get("type")
        if et == "TrainEvent":
            ev = event.get("action", "")
            tid = event.get("entityId", "")
            details = event.get("details", "") or ""
            if tid.startswith("OBT"):
                continue
            t = trains.setdefault(tid, {})
            if ev == "Entry":
                t["spawn"] = ts
                for part in details.split("|"):
                    if part.startswith("wgIds="):
                        t["wgIds"] = [x for x in part.split("=", 1)[1].split(",") if x]
            elif ev == "ArrivedArrivalTrack":
                t["arrivedTrack"] = ts
        elif et == "WagonGroupEvent":
            wgid = event.get("entityId", "")
            ev = event.get("action", "")
            if ev == "PushingToTrack":
                for one in wgid.split("+"):
                    w = wg.setdefault(one, {})
                    if w.get("pushStart") is None or ts < w["pushStart"]:
                        w["pushStart"] = ts
            elif ev == "ArrivedClassificationTrack":
                wg.setdefault(wgid, {})["arrivedClassif"] = ts

    for tid, t in trains.items():
        for one in t.get("wgIds", []):
            wg.setdefault(one, {})["parentTrain"] = tid

    obt_by_id = {o["obtId"]: o for o in obts}
    wg_to_obt = {}
    for o in obts:
        for one in o.get("wgIds", []):
            wg_to_obt[one] = o["obtId"]

    out = []
    for wgid, w in wg.items():
        if not w.get("arrivedClassif"):
            continue
        parent = w.get("parentTrain")
        t = trains.get(parent, {})
        obtId = wg_to_obt.get(wgid)
        o = obt_by_id.get(obtId, {})
        out.append({
            "wgId": wgid, "parentTrain": parent,
            "spawn": t.get("spawn"), "arrivedTrack": t.get("arrivedTrack"),
            "pushStart": w.get("pushStart"), "arrivedClassif": w.get("arrivedClassif"),
            "obtId": obtId, "obtFormed": o.get("createdAt"),
            "obtLeaves": o.get("depdStart"), "trainExit": o.get("depdEnd"),
        })
    out.sort(key=lambda x: (x["arrivedClassif"], x["wgId"]))
    return out


def build_wg_yard_times_sheet(wb, records):
    ws = wb.create_sheet("WG Yard Times")
    headers = [
        "WG Id", "Parent Train", "Train Spawn", "Train Arrived Track",
        "WG PushOff Drive Start", "WG Arrived Classif",
        "Parent OBT", "OBT Formed", "OBT Leaves Classif (DEPD start)",
        "Train Exits Station",
        "Arrival Yard (hh:mm:ss)", "Classif Yard (hh:mm:ss)",
    ]
    ws.append(headers)
    for rec in records:
        ws.append([
            rec["wgId"], rec.get("parentTrain"),
            rec.get("spawn"), rec.get("arrivedTrack"),
            rec.get("pushStart"), rec.get("arrivedClassif"),
            rec.get("obtId"), rec.get("obtFormed"),
            rec.get("obtLeaves"), rec.get("trainExit"),
        ])
    for r in range(2, ws.max_row + 1):
        for col in ("C", "D", "E", "F", "H", "I", "J"):
            ws[f"{col}{r}"].number_format = "yyyy-mm-dd hh:mm:ss"
        # Arrival yard = WG PushOff Drive Start (E) - Train Arrived Track (D)
        ws[f"K{r}"] = f'=IF(AND(ISNUMBER(D{r}),ISNUMBER(E{r})),(E{r}-D{r}),"")'
        # Classification yard = OBT Leaves Classif (I) - WG Arrived Classif (F)
        ws[f"L{r}"] = f'=IF(AND(ISNUMBER(F{r}),ISNUMBER(I{r})),(I{r}-F{r}),"")'
        ws[f"K{r}"].number_format = "[h]:mm:ss"
        ws[f"L{r}"].number_format = "[h]:mm:ss"

    _style_header_row(ws)
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row, max_col=ws.max_column):
        for cell in row:
            cell.font = FONT
    _apply_universal_layout(ws, timestamp_cols=["C", "D", "E", "F", "H", "I", "J"])
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions

    last = ws.max_row
    ws["P1"] = "Summary (hh:mm:ss)"
    ws["P1"].font = FONT_BOLD
    summ = [
        ("Arrival yard mean",   f"=IFERROR(AVERAGE(K2:K{last}),0)"),
        ("Arrival yard median", f"=IFERROR(MEDIAN(K2:K{last}),0)"),
        ("Arrival yard min",    f"=IFERROR(MIN(K2:K{last}),0)"),
        ("Arrival yard max",    f"=IFERROR(MAX(K2:K{last}),0)"),
        ("Classif yard mean",   f"=IFERROR(AVERAGE(L2:L{last}),0)"),
        ("Classif yard median", f"=IFERROR(MEDIAN(L2:L{last}),0)"),
        ("Classif yard min",    f"=IFERROR(MIN(L2:L{last}),0)"),
        ("Classif yard max",    f"=IFERROR(MAX(L2:L{last}),0)"),
    ]
    for i, (label, formula) in enumerate(summ, start=2):
        ws.cell(row=i, column=16, value=label).font = FONT
        c = ws.cell(row=i, column=17, value=formula)
        c.font = FONT
        c.number_format = "[h]:mm:ss"
    ws.column_dimensions["P"].width = 20
    ws.column_dimensions["Q"].width = 12
    return ws


if __name__ == "__main__":
    main()
