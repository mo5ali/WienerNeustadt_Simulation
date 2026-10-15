"""
Synthetic inbound-train schedule for the Wiener Neustadt freight yard (Nb).
Writes a JSON input file in the InboundTrains template format plus companion CSVs.

Logic in one paragraph
----------------------
Every customer around Wr. Neustadt (siding / loading track) produces outbound
wagons and receives inbound wagons at a rate proportional to its published size
(see CUSTOMERS). Outbound wagons wait at the customer until a Reserve-1 siding
tour collects them and brings them into the yard (an inbound "tour" train).
Inbound wagons for customers queue at the feeding main-line node (Kledering,
the Villach through train, or the St. Veit block train) and arrive at Nb on
main-line trains. Timing (hour-of-day profile, delay spread, ad-hoc share,
cancellation rate) is taken from Digitraffic data for Kemi (FI). Volumes are
scaled so that the Mon-Fri mean wagon input matches the site-visit figure.

Sources of every number are listed next to it.  Values marked ASSUMPTION are
the modeller's choice and should be replaced when better data exist.

Usage:
    python generate_inbound.py                      # January 2025, seed 42
    python generate_inbound.py --year 2025 --month 11 --seed 7
"""

import argparse
import bisect
import calendar
import csv
import datetime as dt
import json
import math
import random
from collections import defaultdict
from zoneinfo import ZoneInfo

TZ = ZoneInfo("Europe/Vienna")

# --------------------------------------------------------------------------
# 1. Calibration target  (site-visit summary 22.12.2023)
# --------------------------------------------------------------------------
TARGET_WEEKDAY_MEAN = 240          # 230-250 wagons/day received (site visit)
OBSERVED_PEAK, OBSERVED_LOW = 361, 96

# --------------------------------------------------------------------------
# 2. Timing patterns derived from Digitraffic, Kemi (KEM), 1-3 Oct 2026
# --------------------------------------------------------------------------
# Scheduled local times (h) of non-cancelled freight events at KEM
KEMI_EVENT_HOURS = [7.40, 8.77, 9.10, 9.85, 13.52, 16.15, 20.33, 21.23, 23.18,
                    4.80, 7.40, 8.80, 9.85, 13.13, 14.13, 17.50, 20.05, 20.23, 20.33]
# Deviation actual - scheduled (min), quantiles over 388 freight timetable rows
DELAY_QUANTILES = [(0.00, -45), (0.10, -30), (0.25, -16), (0.50, -4),
                   (0.75, 0), (0.90, 3), (0.97, 6), (1.00, 30)]
KEMI_ADHOC_SHARE = 5 / 22          # ADHOC trains in the Kemi sample
KEMI_CANCEL_RATE = 3 / 22          # cancelled trains in the Kemi sample
PROFILE_FLOOR = 0.30               # ASSUMPTION: share of uniform background (sample is small)
PROFILE_SIGMA_H = 1.5              # ASSUMPTION: kernel smoothing of the sample (hours)

# --------------------------------------------------------------------------
# 3. Operating calendar  (site visit + Austrian public holidays)
# --------------------------------------------------------------------------
# Shunting works 3 shifts, stops Saturday morning, restarts Sunday 18:00.
MAINLINE_WINDOWS = {               # hours in which main-line arrivals are accepted
    "weekday": (0, 24),
    "saturday": (0, 8),            # ASSUMPTION: last arrivals before the weekend stop
    "sunday": (18, 24),            # yard restarts Sunday 18:00
    "holiday": (18, 24),           # ASSUMPTION: holidays treated like Sundays
}
AT_HOLIDAYS = {                    # fixed + 2025/2026 movable dates
    (1, 1), (1, 6), (5, 1), (8, 15), (10, 26), (11, 1), (12, 8), (12, 25), (12, 26),
}
MOVABLE_HOLIDAYS = {dt.date(2025, 4, 21), dt.date(2025, 5, 29), dt.date(2025, 6, 9),
                    dt.date(2025, 6, 19), dt.date(2026, 4, 6), dt.date(2026, 5, 14),
                    dt.date(2026, 5, 25), dt.date(2026, 6, 4)}

# --------------------------------------------------------------------------
# 4. Wagon types (length over buffers in m - approximate, verify with RCA catalogue)
# --------------------------------------------------------------------------
WAGON_LEN = {
    "Habbins": 23.24,   # sliding-wall wagon (paper rolls, bales, parts)
    "Eanos": 15.74,     # open bulk wagon (recovered paper, beet, scrap)
    "Snps": 21.0,       # stake wagon, roundwood (load length 19.6 m, waldwissen.net)
    "Roos": 19.9,       # stake wagon, roundwood (load length 18.4 m, waldwissen.net)
    "Tagnpps": 16.5,    # covered hopper, wood chips
    "Res": 19.9,        # flat wagon (boards, precast, military)
    "Shimmns": 12.04,   # coil wagon (steel)
    "Laaers": 27.0,     # double-deck car carrier
    "Zacns": 14.0,      # tank wagon (chemicals)
}

# --------------------------------------------------------------------------
# 5. Customers = sources AND sinks around Wr. Neustadt
# --------------------------------------------------------------------------
# base   : relative size, wagons/day each way at season factor 1 (mid scenario,
#          'Scale_Indicators_WN' document); scaled by K during calibration
# days   : weekdays on which wagons are produced/consumed (0 = Monday)
# season : month -> factor
# out    : destination split of wagons leaving the customer (sorted at Nb)
# via    : how inbound wagons reach Nb  (KLED = Kledering trains,
#          VIL = detached from Kledering-Villach through train, STV = St. Veit block)
# tour   : Reserve-1 tour that collects the customer's outbound wagons
FLAT = {m: 1.0 for m in range(1, 13)}
TIMBER = {1: 1.15, 2: 1.15, 3: 1.05, 4: 0.95, 5: 0.9, 6: 0.85, 7: 0.85, 8: 0.9,
          9: 1.0, 10: 1.05, 11: 1.1, 12: 1.0}            # ASSUMPTION: winter harvest peak
CONSTRUCTION = {1: 0.6, 2: 0.7, 3: 0.9, 4: 1.1, 5: 1.15, 6: 1.15, 7: 1.15, 8: 1.05,
                9: 1.15, 10: 1.1, 11: 0.95, 12: 0.7}     # ASSUMPTION
ALL7, MON_FRI, MON_SAT = set(range(7)), set(range(5)), set(range(6))


def beet_factor(day):
    """Sugar-beet campaign ~late Sep to mid/end Jan (AGRANA / ÖVZ). ASSUMPTION: ramp
    up 25 Sep-5 Oct, full until 10 Jan, ramp down to zero by 24 Jan."""
    y = day.year if day.month >= 7 else day.year - 1
    start, full = dt.date(y, 9, 25), dt.date(y, 10, 5)
    taper, end = dt.date(y + 1, 1, 10), dt.date(y + 1, 1, 24)
    if day < start or day >= end:
        return 0.0
    if day < full:
        return (day - start).days / (full - start).days
    if day < taper:
        return 1.0
    return 1 - (day - taper).days / (end - taper).days


CUSTOMERS = {
    "PIT": dict(name="Pitten (Hamburger)", base=19, days=ALL7, season=FLAT,
                types={"Habbins": 0.6, "Eanos": 0.4}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="ASPANG"),
    "NEU": dict(name="Neudörfl (FunderMax)", base=10, days=ALL7, season=TIMBER,
                types={"Snps": 0.35, "Tagnpps": 0.35, "Res": 0.30},
                out={"Wien Kledering": 0.6, "St. Veit an der Glan": 0.4},
                via={"KLED": 0.5, "STV": 0.5}, tour="EAST"),
    "HOE": dict(name="Wr. Neustadt (Hödlmayr)", base=9, days=MON_FRI, season=FLAT,
                types={"Laaers": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="LOCAL"),
    "EBF": dict(name="Ebenfurth (beet loading)", base=11, days=MON_SAT, season="beet",
                types={"Eanos": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="NORTHEAST"),
    "NK":  dict(name="Neunkirchen (loading tracks)", base=8, days=MON_FRI, season=TIMBER,
                types={"Snps": 0.6, "Roos": 0.4},
                out={"Wien Kledering": 0.85, "St. Veit an der Glan": 0.15},
                via={"KLED": 0.5, "VIL": 0.5}, tour="SOUTHWEST"),
    "TER": dict(name="Ternitz (Schoeller-Bleckmann)", base=3, days=MON_FRI, season=FLAT,
                types={"Shimmns": 0.6, "Res": 0.4}, out={"Wien Kledering": 1.0},
                via={"KLED": 0.5, "VIL": 0.5}, tour="SOUTHWEST"),
    "GLO": dict(name="Gloggnitz", base=2, days=MON_FRI, season=FLAT,
                types={"Eanos": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 0.5, "VIL": 0.5}, tour="SOUTHWEST"),
    "BRE": dict(name="Wr. Neustadt (Brenntag)", base=2, days=MON_FRI, season=FLAT,
                types={"Zacns": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="LOCAL"),
    "FUR": dict(name="Wr. Neustadt Ausfahrbf (Furtenbach)", base=2, days=MON_FRI,
                season=FLAT, types={"Res": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="LOCAL"),
    "FEL": dict(name="Felixdorf/Sollenau (Bundesheer, Maba)", base=3, days=MON_FRI,
                season=CONSTRUCTION, types={"Res": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="NORTH"),
    "STE": dict(name="Steinabrückl/Wöllersdorf", base=2, days=MON_FRI, season=FLAT,
                types={"Eanos": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="NORTH"),
    "OBE": dict(name="Obereggendorf (Hofer)", base=1, days=MON_FRI, season=FLAT,
                types={"Eanos": 1.0}, out={"Wien Kledering": 1.0},
                via={"KLED": 1.0}, tour="NORTHEAST"),
    # Residual for sidings / loading tracks not named in the sources (site visit:
    # "other local stations / Anschlussbahnen"). ASSUMPTION: ~15 % of customer volume.
    "OTS": dict(name="Other sidings south", base=4, days=MON_FRI, season=FLAT,
                types={"Eanos": 0.5, "Res": 0.3, "Habbins": 0.2},
                out={"Wien Kledering": 1.0}, via={"KLED": 0.5, "VIL": 0.5}, tour="SOUTHWEST"),
    "OTN": dict(name="Other sidings north", base=5, days=MON_FRI, season=FLAT,
                types={"Eanos": 0.5, "Res": 0.3, "Habbins": 0.2},
                out={"Wien Kledering": 1.0}, via={"KLED": 1.0}, tour="NORTH"),
    "OTL": dict(name="Other sidings local", base=3, days=MON_FRI, season=FLAT,
                types={"Eanos": 0.4, "Res": 0.3, "Zacns": 0.3},
                out={"Wien Kledering": 1.0}, via={"KLED": 1.0}, tour="LOCAL"),
}
DAILY_CV = 0.45                    # ASSUMPTION: day-to-day variation per customer

# --------------------------------------------------------------------------
# 6. Siding tours (Reserve 1, loco 2016; local sidings by yard loco 2070)
# --------------------------------------------------------------------------
# stops in collection order, return times at Nb (h), max length per run (m)
TOURS = {
    "SOUTHWEST": dict(origin="Siding tour south-west (Südbahn)",
                      stops=["NK", "TER", "OTS", "GLO"], returns=[11.5, 19.0],
                      max_len=450, loco="LOCO-2016-01"),   # site-visit tour order
    "ASPANG": dict(origin="Siding tour south (Aspangbahn)", stops=["PIT"], returns=[9.0, 15.5, 22.0],
                   max_len=400, loco="LOCO-2016-02"),
    "EAST": dict(origin="Siding tour east (Mattersburger Bahn)", stops=["NEU"], returns=[10.0, 17.0],
                 max_len=400, loco="LOCO-2016-02"),
    "NORTHEAST": dict(origin="Siding tour north-east (Pottendorfer Linie)", stops=["EBF", "OBE"],
                      returns=[16.5], max_len=500, loco="LOCO-2016-01"),
    "NORTH": dict(origin="Siding tour north (Südbahn)", stops=["FEL", "STE", "OTN"],
                  returns=[8.5], max_len=350, loco="LOCO-2016-02"),
    "LOCAL": dict(origin="Local sidings Wr. Neustadt", stops=["HOE", "BRE", "FUR", "OTL"],
                  returns=[7.5, 14.0, 21.0], max_len=300, loco="LOCO-2070-01"),
}
TOUR_MAX_WAGONS = 25               # ASSUMPTION: brake/handling limit per tour run

# --------------------------------------------------------------------------
# 7. Main-line trains
# --------------------------------------------------------------------------
N_REGULAR_KLED = 5                 # ASSUMPTION: regular Kledering -> Nb paths per day
KLED_MAX_LEN, KLED_MAX_WAGONS, KLED_MIN_WAGONS = 600, 35, 8   # 17-wagon example; ~24/train at ZVBf
KLED_ADHOC_TRIGGER = 16            # ASSUMPTION: queue left after regular paths -> extra ad-hoc train
VIL = dict(origin="Wien Kledering (Villach through train, detached group)", hour=3.7,
           max_wagons=(8, 18), days=MON_FRI)                     # site visit: wagons detached at Nb
STV = dict(origin="St. Veit an der Glan (block train)", hour=5.5, max_wagons=28,
           days={0, 2, 4})                                  # ASSUMPTION: Mon/Wed/Fri
WEIZ = dict(origin="Aspang (STLB Weiz-Bratislava, handover)", hour=18.5, wagons=(8, 14),
            days={0, 2, 4}, types={"Habbins": 1.0}, dest="Wien Kledering",
            include=True)          # Wikipedia (Aspangbahn), since 2002 - UNVERIFIED if still running
MAINLINE_LOCOS = [f"LOCO-1116-{i:03d}" for i in range(1, 9)] + \
                 [f"LOCO-1293-{i:03d}" for i in range(1, 5)]
FRAGMENT_PROB = 0.25               # ASSUMPTION: share of destination blocks split in two (imperfect pre-sorting)


# ==========================================================================
rng = random.Random()


def hourly_profile():
    w = [0.0] * 24
    for h in range(24):
        c = h + 0.5
        for e in KEMI_EVENT_HOURS:
            d = min(abs(c - e), 24 - abs(c - e))
            w[h] += math.exp(-0.5 * (d / PROFILE_SIGMA_H) ** 2)
    s = sum(w)
    return [(1 - PROFILE_FLOOR) * x / s + PROFILE_FLOOR / 24 for x in w]


PROFILE = hourly_profile()


def sample_hour(lo, hi):
    hours = list(range(int(lo), int(math.ceil(hi))))
    ws = [PROFILE[h] for h in hours]
    h = rng.choices(hours, ws)[0]
    return h + rng.random()


def sample_delay_min():
    u = rng.random()
    ps = [p for p, _ in DELAY_QUANTILES]
    i = max(1, bisect.bisect_left(ps, u))
    (p0, v0), (p1, v1) = DELAY_QUANTILES[i - 1], DELAY_QUANTILES[i]
    return v0 + (v1 - v0) * (u - p0) / (p1 - p0)


def daytype(day):
    if (day.month, day.day) in AT_HOLIDAYS or day in MOVABLE_HOLIDAYS:
        return "holiday"
    return {5: "saturday", 6: "sunday"}.get(day.weekday(), "weekday")


def gamma_count(mean):
    if mean <= 0:
        return 0
    k = 1 / DAILY_CV ** 2
    lam = rng.gammavariate(k, mean / k)
    # Poisson draw (Knuth for small, normal approx for large)
    if lam < 30:
        L, p, n = math.exp(-lam), 1.0, 0
        while True:
            p *= rng.random()
            if p <= L:
                return n
            n += 1
    return max(0, round(rng.gauss(lam, math.sqrt(lam))))


def season(c, day):
    s = c["season"]
    return beet_factor(day) if s == "beet" else s[day.month]


def pick(d):
    return rng.choices(list(d), list(d.values()))[0]


def make_wagon(cust, dest):
    t = pick(cust["types"])
    return dict(type=t, length=WAGON_LEN[t], dest=dest, src=cust["name"])


def to_groups(wagons, presorted):
    """Return list of (dest, [wagons]) blocks."""
    if presorted:
        by = defaultdict(list)
        for w in wagons:
            by[w["dest"]].append(w)
        blocks = []
        for dest, ws in by.items():
            if len(ws) >= 2 and rng.random() < FRAGMENT_PROB:
                cut = rng.randint(1, len(ws) - 1)
                blocks += [(dest, ws[:cut]), (dest, ws[cut:])]
            else:
                blocks.append((dest, ws))
        rng.shuffle(blocks)
    else:
        blocks = [(w["dest"], [w]) for w in wagons]
    merged = []
    for dest, ws in blocks:          # merge neighbours with equal destination
        if merged and merged[-1][0] == dest:
            merged[-1][1].extend(ws)
        else:
            merged.append((dest, list(ws)))
    return merged


def take(queue, max_len, max_wagons):
    out, length = [], 0.0
    while queue and len(out) < max_wagons and length + queue[0]["length"] <= max_len:
        w = queue.pop(0)
        out.append(w)
        length += w["length"]
    return out


def simulate(year, month, K, seed):
    rng.seed(seed)
    ndays = calendar.monthrange(year, month)[1]
    first = dt.date(year, month, 1)
    # warm-up week so queues/backlogs are realistic on day 1
    days = [first - dt.timedelta(days=7 - i) for i in range(7)] + \
           [first + dt.timedelta(days=i) for i in range(ndays)]

    # fixed monthly timetable for regular Kledering paths (shaped by Kemi profile)
    reg_hours = sorted(sample_hour(0, 24) for _ in range(N_REGULAR_KLED))

    backlog = {k: [] for k in CUSTOMERS}            # wagons waiting at the customer
    queues = {"KLED": [], "VIL": [], "STV": []}     # wagons waiting for a main-line train
    trains = []

    def add_train(day, hour, origin, kind, wagons, loco, presorted, regular=True, stations=None):
        if not wagons:
            return
        local = dt.datetime.combine(day, dt.time()) + dt.timedelta(hours=hour,
                                                                   minutes=sample_delay_min())
        trains.append(dict(local=local, origin=origin, kind=kind, loco=loco, stations=stations or [],
                           regular=regular, groups=to_groups(wagons, presorted)))

    for day in days:
        dtp = daytype(day)
        wd = day.weekday()
        # --- customers produce outbound wagons and order inbound wagons
        new_in = {q: [] for q in queues}
        for key, c in CUSTOMERS.items():
            if wd not in c["days"] or dtp == "holiday":
                continue
            mean = K * c["base"] * season(c, day)
            for _ in range(gamma_count(mean)):
                backlog[key].append(make_wagon(c, pick(c["out"])))
            for _ in range(gamma_count(mean)):
                new_in[pick(c["via"])].append(make_wagon(c, c["name"]))
        for q, ws in new_in.items():         # wagons reach the feeding node in mixed order
            rng.shuffle(ws)
            queues[q].extend(ws)

        is_ops = dtp == "weekday"
        # --- siding tours (working days only)
        if is_ops:
            for tname, t in TOURS.items():
                for h in t["returns"]:
                    run, length, visited = [], 0.0, []
                    for stop in t["stops"]:          # collect station by station
                        got = []
                        while backlog[stop] and len(run) + len(got) < TOUR_MAX_WAGONS and \
                                length + backlog[stop][0]["length"] <= t["max_len"]:
                            w = backlog[stop].pop(0)
                            got.append(w)
                            length += w["length"]
                        # customer places its wagons roughly by destination
                        if got:
                            visited.append(CUSTOMERS[stop]["name"])
                        for _, ws in to_groups(got, presorted=True):
                            run.extend(ws)
                    add_train(day, h, t["origin"], f"tour-{tname}", run, t["loco"], False,
                              stations=visited)

        # --- main line: Villach through train (detached group), St. Veit block, Weiz
        if is_ops and wd in VIL["days"]:
            add_train(day, VIL["hour"], VIL["origin"], "mainline-VIL",
                      take(queues["VIL"], 400, rng.randint(*VIL["max_wagons"])), rng.choice(MAINLINE_LOCOS), True)
        if is_ops and wd in STV["days"]:
            add_train(day, STV["hour"], STV["origin"], "mainline-STV",
                      take(queues["STV"], KLED_MAX_LEN, STV["max_wagons"]),
                      rng.choice(MAINLINE_LOCOS), True)
        if WEIZ["include"] and is_ops and wd in WEIZ["days"]:
            n = rng.randint(*WEIZ["wagons"])
            ws = [dict(type="Habbins", length=WAGON_LEN["Habbins"], dest=WEIZ["dest"])
                  for _ in range(n)]
            add_train(day, WEIZ["hour"], WEIZ["origin"], "mainline-WEIZ", ws, "LOCO-STLB-01", True)

        # --- main line: Kledering regular paths + ad-hoc extras
        lo, hi = MAINLINE_WINDOWS[dtp]
        for h in reg_hours:
            if not (lo <= h < hi) or rng.random() < KEMI_CANCEL_RATE:
                continue
            if len(queues["KLED"]) < KLED_MIN_WAGONS:
                continue
            add_train(day, h, "Wien Kledering (ZVBf)", "mainline-KLED",
                      take(queues["KLED"], KLED_MAX_LEN, rng.randint(14, KLED_MAX_WAGONS)),
                      rng.choice(MAINLINE_LOCOS), True)
        while len(queues["KLED"]) > KLED_ADHOC_TRIGGER and hi > lo:
            add_train(day, sample_hour(lo, hi), "Wien Kledering (ZVBf)", "mainline-KLED",
                      take(queues["KLED"], KLED_MAX_LEN, rng.randint(14, KLED_MAX_WAGONS)),
                      rng.choice(MAINLINE_LOCOS), True, regular=False)

    simulate.leftover = {k: len(v) for k, v in backlog.items()} | {q: len(v) for q, v in queues.items()}
    month_trains = [t for t in trains if t["local"].year == year and t["local"].month == month]
    month_trains.sort(key=lambda t: t["local"])
    return month_trains, reg_hours


def daily_stats(trains, year, month):
    ndays = calendar.monthrange(year, month)[1]
    per_day = {dt.date(year, month, d): 0 for d in range(1, ndays + 1)}
    for t in trains:
        d = t["local"].date()
        if d in per_day:
            per_day[d] += sum(len(ws) for _, ws in t["groups"])
    wk = [v for d, v in per_day.items() if daytype(d) == "weekday"]
    return per_day, sum(wk) / max(1, len(wk))


def calibrate(year, month, seed):
    """Fixed-point iteration on the actual seed: scale K until the Mon-Fri mean
    wagon input of the generated month is within 1 % of the target."""
    K = 1.0
    for _ in range(30):
        tr, _ = simulate(year, month, K, seed)
        _, mean = daily_stats(tr, year, month)
        if abs(mean / TARGET_WEEKDAY_MEAN - 1) < 0.01:
            break
        K *= (TARGET_WEEKDAY_MEAN / mean) ** 0.8
    return K


def write_outputs(trains, year, month, K, reg_hours, prefix):
    inbound, groups, wagons, rows = [], [], [], []
    for i, t in enumerate(trains):
        tid = str(10001 + i)
        gids = []
        for gi, (dest, ws) in enumerate(t["groups"], start=1):
            gid = f"{tid}{gi:02d}"
            wids = []
            for wi, w in enumerate(ws, start=1):
                wid = f"{gid}{wi:02d}"
                wids.append(wid)
                wagons.append({"ID": wid, "Length": w["length"]})
                rows.append(dict(train=tid, group=gid, wagon=wid, type=w["type"],
                                 length=w["length"], destination=dest))
            groups.append({"ID": gid, "WagonIds": wids, "Destination": dest})
            gids.append(gid)
        utc = t["local"].replace(tzinfo=TZ).astimezone(dt.timezone.utc)
        t["id"], t["utc"] = tid, utc
        inbound.append({"ID": tid, "WagonGroupIds": gids, "HasLoco": True,
                        "LocomotiveId": t["loco"], "Status": "Arriving",
                        "Designation": "Inbound", "Time": utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
                        "Origin": t["origin"],
                        "OriginType": "SidingTour" if t["kind"].startswith("tour") else "MainLine",
                        "PickupStations": t["stations"]})

    with open(f"{prefix}.json", "w", encoding="utf-8") as f:
        json.dump({"inboundTrains": inbound, "wagonGroups": groups, "wagons": wagons},
                  f, ensure_ascii=False, indent=2)

    with open(f"{prefix}_trains.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["TrainID", "Time_UTC", "Time_local", "Weekday", "Origin", "PickupStations", "Kind",
                    "Regular", "Locomotive", "Wagons", "Length_m", "Groups", "Destinations"])
        for t in trains:
            n = sum(len(ws) for _, ws in t["groups"])
            L = sum(x["length"] for _, ws in t["groups"] for x in ws)
            w.writerow([t["id"], t["utc"].strftime("%Y-%m-%dT%H:%M:%SZ"),
                        t["local"].strftime("%Y-%m-%d %H:%M"), t["local"].strftime("%a"),
                        t["origin"], " | ".join(t["stations"]), t["kind"], t["regular"], t["loco"], n, round(L, 1),
                        len(t["groups"]), " | ".join(f"{d} ({len(ws)})" for d, ws in t["groups"])])

    with open(f"{prefix}_wagons.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)

    per_day, mean = daily_stats(trains, year, month)
    with open(f"{prefix}_daily.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["Date", "Weekday", "DayType", "Trains", "Wagons"])
        cnt = defaultdict(int)
        for t in trains:
            cnt[t["local"].date()] += 1
        for d, v in per_day.items():
            w.writerow([d.isoformat(), d.strftime("%a"), daytype(d), cnt[d], v])
    return per_day, mean


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--year", type=int, default=2025)
    ap.add_argument("--month", type=int, default=1)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--K", type=float, default=None, help="volume scale (default: calibrate)")
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    K = a.K or calibrate(a.year, a.month, a.seed)
    trains, reg_hours = simulate(a.year, a.month, K, a.seed)
    prefix = a.out or f"InboundTrains_WN_{a.year}-{a.month:02d}"
    per_day, mean = write_outputs(trains, a.year, a.month, K, reg_hours, prefix)

    vals = list(per_day.values())
    print(f"K = {K:.3f}  regular Kledering paths (local h): "
          + ", ".join(f"{int(h):02d}:{int(h % 1 * 60):02d}" for h in reg_hours))
    print(f"trains = {len(trains)}, wagons = {sum(vals)}, weekday mean = {mean:.1f}, "
          f"min = {min(vals)}, max = {max(vals)}  (site visit: 230-250, low {OBSERVED_LOW}, "
          f"peak {OBSERVED_PEAK})")
    print("left waiting at month end:", simulate.leftover)


if __name__ == "__main__":
    main()
