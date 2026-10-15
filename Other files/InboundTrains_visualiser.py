"""
InboundTrains_visualiser.py
---------------------------
Renders an inbound-train schedule JSON (e.g. InboundTrains_WN_2025-01.json) as a
multi-page PDF timetable:

    | Train ID | Arrival date | Arrival time | Source station | [LOCO][WG][WG][WG]... |

* Trains are listed in chronological order of arrival.
* Each wagon group (WG) is a box whose width is proportional to its length
  (sum of its wagons' lengths, or the WG's own "Length" if given). All trains
  share one length scale, so trains can be compared visually across pages.
* Each WG box shows its output destination and is coloured per destination;
  a legend with all destinations is printed on every page.

The PDF is written next to the JSON file, with the same name (".pdf").

Usage
    python InboundTrains_visualiser.py                       # default file below
    python InboundTrains_visualiser.py path\\to\\Inbound.json  # any schedule file
    python InboundTrains_visualiser.py a.json b.json          # several at once

Requires: matplotlib   (pip install matplotlib)
"""

import argparse
import json
import sys
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.backends.backend_pdf import PdfPages  # noqa: E402
from matplotlib.patches import Rectangle  # noqa: E402
from matplotlib.textpath import TextPath  # noqa: E402
from matplotlib.font_manager import FontProperties  # noqa: E402

# --------------------------------------------------------------------------- #
# Settings
# --------------------------------------------------------------------------- #
SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_JSON = SCRIPT_DIR.parent / "InputFiles" / "InboundTrains_WN_2025-01.json"

# Train-level keys that may hold the station the train comes from (first match wins)
SOURCE_KEYS = ["Source", "SourceStation", "Origin", "OriginStation", "From",
               "FromStation", "Departure", "DepartureStation", "Start", "StartStation"]

PAGE_SIZE = (16.54, 11.69)      # A3 landscape, inches (room for 16-WG trains)
ROWS_PER_PAGE = 28
LOCO_LENGTH_M = 19.0            # drawn length of a locomotive (for scale only)
MIN_FONT, MAX_FONT = 4.0, 7.0   # font size range for WG labels

# Column layout in figure-fraction units (0..1)
MARGIN_L, MARGIN_R = 0.025, 0.015
COL_X = {"id": 0.025, "date": 0.075, "time": 0.135, "source": 0.185}
DIAGRAM_X0 = 0.315
TABLE_TOP, TABLE_BOTTOM = 0.865, 0.075

# A colour-blind-friendly-ish qualitative palette (Tableau 20, light/dark pairs split)
PALETTE = [
    "#4E79A7", "#F28E2B", "#59A14F", "#E15759", "#76B7B2", "#EDC948", "#B07AA1",
    "#FF9DA7", "#9C755F", "#BAB0AC", "#A0CBE8", "#FFBE7D", "#8CD17D", "#F1CE63",
    "#86BCB6", "#FABFD2", "#D4A6C8", "#D7B5A6", "#79706E", "#B6992D",
]


# --------------------------------------------------------------------------- #
# Data loading
# --------------------------------------------------------------------------- #
def _get(d, *keys, default=None):
    """Case-insensitive dict lookup over several candidate keys."""
    lower = {k.lower(): v for k, v in d.items()}
    for k in keys:
        if k.lower() in lower and lower[k.lower()] not in (None, ""):
            return lower[k.lower()]
    return default


def parse_time(s):
    s = str(s).strip()
    if s.endswith("Z"):
        s = s[:-1] + "+00:00"
    return datetime.fromisoformat(s)


def load_schedule(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    trains_raw = _get(data, "inboundTrains", "trains", default=[])
    groups_raw = _get(data, "wagonGroups", default=[])
    wagons_raw = _get(data, "wagons", default=[])

    wagon_len = {str(w["ID"]): float(_get(w, "Length", default=0.0)) for w in wagons_raw}

    groups = {}
    for g in groups_raw:
        wagon_ids = [str(x) for x in _get(g, "WagonIds", default=[])]
        length = _get(g, "Length")
        if length is None:
            length = sum(wagon_len.get(w, 0.0) for w in wagon_ids)
        groups[str(g["ID"])] = {
            "id": str(g["ID"]),
            "dest": str(_get(g, "Destination", default="?")),
            "length": float(length),
            "n_wagons": len(wagon_ids),
        }

    trains = []
    missing = 0
    for t in trains_raw:
        wgs = []
        for gid in _get(t, "WagonGroupIds", default=[]):
            g = groups.get(str(gid))
            if g is None:
                missing += 1
                g = {"id": str(gid), "dest": "? (missing WG)", "length": 0.0, "n_wagons": 0}
            wgs.append(g)
        trains.append({
            "id": str(t["ID"]),
            "time": parse_time(_get(t, "Time", "ArrivalTime", default="1970-01-01T00:00:00")),
            "source": _get(t, *SOURCE_KEYS),
            "has_loco": bool(_get(t, "HasLoco", default=False)),
            "loco": _get(t, "LocomotiveId", default=""),
            "wgs": wgs,
            "length": sum(g["length"] for g in wgs),
        })

    trains.sort(key=lambda tr: (tr["time"], tr["id"]))
    if missing:
        print(f"  warning: {missing} wagon-group reference(s) not found in 'wagonGroups'")
    return trains


# --------------------------------------------------------------------------- #
# Drawing helpers
# --------------------------------------------------------------------------- #
def short_name(dest):
    """'Pitten (Hamburger)' -> 'Pitten';  'Wien Kledering' -> 'Wien Kledering'."""
    return dest.split(" (")[0].strip()


_WIDTH_CACHE = {}
_FONT = FontProperties()


def text_width_pts(text, size):
    """Rendered text width in points (measured once at 10 pt, then scaled linearly)."""
    if text not in _WIDTH_CACHE:
        _WIDTH_CACHE[text] = TextPath((0, 0), text, size=10, prop=_FONT).get_extents().width / 10
    return _WIDTH_CACHE[text] * size


def fit_label(candidates, box_w_pts, box_h_pts):
    """Return (text, fontsize) of the first candidate that fits the box, else (None, None)."""
    for text in candidates:
        size = MAX_FONT
        while size >= MIN_FONT:
            if text_width_pts(text, size) <= box_w_pts - 2 and size * 1.15 <= box_h_pts:
                return text, size
            size -= 0.5
    return None, None


def text_color_for(hex_color):
    r, g, b = (int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return "black" if (0.299 * r + 0.587 * g + 0.114 * b) > 0.55 else "white"


def build_colour_map(trains):
    counts = {}
    for t in trains:
        for g in t["wgs"]:
            counts[g["dest"]] = counts.get(g["dest"], 0) + 1
    dests = sorted(counts, key=lambda d: (-counts[d], d))
    cmap = {d: PALETTE[i % len(PALETTE)] for i, d in enumerate(dests)}
    codes = {d: f"D{i + 1}" for i, d in enumerate(dests)}
    return dests, counts, cmap, codes


def draw_header(fig, src_name, page, n_pages, n_trains, first, last, max_len):
    fig.text(MARGIN_L, 0.965, "Inbound train schedule", fontsize=16, weight="bold", va="top")
    fig.text(MARGIN_L, 0.935,
             f"{src_name}   ·   {n_trains} trains   ·   "
             f"{first:%d.%m.%Y} – {last:%d.%m.%Y}   ·   longest train {max_len:.0f} m",
             fontsize=9, color="#444", va="top")
    fig.text(1 - MARGIN_R, 0.965, f"Page {page}/{n_pages}", fontsize=9, ha="right", va="top")

    y = TABLE_TOP + 0.012
    for key, label in [("id", "Train ID"), ("date", "Arrival date"),
                       ("time", "Arrival time"), ("source", "Source station")]:
        fig.text(COL_X[key], y, label, fontsize=8.5, weight="bold", va="bottom")
    fig.text(DIAGRAM_X0, y, "Train composition  (loco ▸ wagon groups, front → rear; width ∝ length)",
             fontsize=8.5, weight="bold", va="bottom")
    fig.add_artist(plt.Line2D([MARGIN_L, 1 - MARGIN_R], [TABLE_TOP + 0.008] * 2,
                              color="black", lw=0.8))


def draw_legend(fig, dests, counts, cmap, codes):
    fig.text(MARGIN_L, 0.052, "Destinations:", fontsize=7.5, weight="bold", va="center")
    n_cols = 6
    col_w = (1 - MARGIN_L - MARGIN_R - 0.07) / n_cols
    for i, d in enumerate(dests):
        r, c = divmod(i, n_cols)
        x = MARGIN_L + 0.07 + c * col_w
        y = 0.052 - r * 0.014
        fig.add_artist(Rectangle((x, y - 0.0045), 0.010, 0.009, transform=fig.transFigure,
                                 facecolor=cmap[d], edgecolor="#333", lw=0.4))
        fig.text(x + 0.013, y, f"{codes[d]}  {d}  ({counts[d]})", fontsize=6.5, va="center")


def draw_train_row(fig, ax, t, y, row_h, scale, cmap, codes, fig_w_pts, fig_h_pts):
    # Text columns
    yc = y + row_h / 2
    fs = 8
    fig.text(COL_X["id"], yc, t["id"], fontsize=fs, va="center", family="monospace")
    fig.text(COL_X["date"], yc, f"{t['time']:%a %d.%m.%Y}", fontsize=fs, va="center")
    fig.text(COL_X["time"], yc, f"{t['time']:%H:%M:%S}", fontsize=fs, va="center")
    src = t["source"] if t["source"] else "—"
    src_txt, src_fs = fit_label([str(src)], (DIAGRAM_X0 - COL_X["source"] - 0.006) * fig_w_pts, 20)
    fig.text(COL_X["source"], yc, src_txt or str(src)[:28] + "…", fontsize=src_fs or 6,
             va="center", color="#999" if not t["source"] else "black")

    # Train diagram (figure-fraction coordinates)
    box_h = row_h * 0.62
    by = y + (row_h - box_h) / 2
    x = DIAGRAM_X0
    if t["has_loco"]:
        lw = LOCO_LENGTH_M * scale
        fig.add_artist(Rectangle((x, by), lw, box_h, transform=fig.transFigure,
                                 facecolor="#333", edgecolor="black", lw=0.5))
        fig.text(x + lw / 2, by + box_h / 2, "L", fontsize=5.5, color="white",
                 ha="center", va="center", weight="bold")
        x += lw + 0.0015

    for g in t["wgs"]:
        w = max(g["length"] * scale, 0.002)
        col = cmap[g["dest"]]
        fig.add_artist(Rectangle((x, by), w, box_h, transform=fig.transFigure,
                                 facecolor=col, edgecolor="black", lw=0.5))
        label, size = fit_label([g["dest"], short_name(g["dest"]), codes[g["dest"]]],
                                w * fig_w_pts, box_h * fig_h_pts)
        if label:
            fig.text(x + w / 2, by + box_h / 2, label, fontsize=size, ha="center", va="center",
                     color=text_color_for(col), clip_on=True)
        x += w

    # Summary at the end of the train
    n_wag = sum(g["n_wagons"] for g in t["wgs"])
    fig.text(x + 0.004, yc, f"{t['length']:.0f} m · {len(t['wgs'])} WG · {n_wag} wag",
             fontsize=6, color="#555", va="center")


# --------------------------------------------------------------------------- #
# Main rendering
# --------------------------------------------------------------------------- #
def render_pdf(json_path, pdf_path=None):
    json_path = Path(json_path)
    pdf_path = Path(pdf_path) if pdf_path else json_path.with_suffix(".pdf")
    print(f"Reading  {json_path}")
    trains = load_schedule(json_path)
    if not trains:
        print("  no trains found – nothing to draw")
        return None

    dests, counts, cmap, codes = build_colour_map(trains)
    if not any(t["source"] for t in trains):
        print("  note: no source-station field found on the trains "
              f"(looked for {', '.join(SOURCE_KEYS)}) – column shows '—'")

    max_len = max(t["length"] + (LOCO_LENGTH_M if t["has_loco"] else 0) for t in trains)
    diagram_w = 1 - MARGIN_R - DIAGRAM_X0 - 0.075          # leave room for summary text
    scale = diagram_w / max_len                              # figure-fraction per metre

    fig_w_pts, fig_h_pts = PAGE_SIZE[0] * 72, PAGE_SIZE[1] * 72
    row_h = (TABLE_TOP - TABLE_BOTTOM) / ROWS_PER_PAGE
    pages = [trains[i:i + ROWS_PER_PAGE] for i in range(0, len(trains), ROWS_PER_PAGE)]

    with PdfPages(pdf_path) as pdf:
        for p, chunk in enumerate(pages, start=1):
            fig = plt.figure(figsize=PAGE_SIZE)
            draw_header(fig, json_path.name, p, len(pages), len(trains),
                        trains[0]["time"], trains[-1]["time"], max_len)
            prev_date = None
            for r, t in enumerate(chunk):
                y = TABLE_TOP - (r + 1) * row_h
                if r % 2 == 1:
                    fig.add_artist(Rectangle((MARGIN_L - 0.005, y), 1 - MARGIN_L - MARGIN_R + 0.005,
                                             row_h, transform=fig.transFigure,
                                             facecolor="#F3F3F3", edgecolor="none", zorder=-1))
                if prev_date is not None and t["time"].date() != prev_date:   # day boundary
                    fig.add_artist(plt.Line2D([MARGIN_L - 0.005, 1 - MARGIN_R], [y + row_h] * 2,
                                              color="#777", lw=0.9, ls="--"))
                prev_date = t["time"].date()
                draw_train_row(fig, None, t, y, row_h, scale, cmap, codes, fig_w_pts, fig_h_pts)

            # Scale bar
            sb_len = 100 if max_len > 200 else 50
            sx, sy = 1 - MARGIN_R - sb_len * scale, TABLE_BOTTOM - 0.006
            fig.add_artist(plt.Line2D([sx, sx + sb_len * scale], [sy, sy], color="black", lw=1.2))
            for xx in (sx, sx + sb_len * scale):
                fig.add_artist(plt.Line2D([xx, xx], [sy - 0.003, sy + 0.003], color="black", lw=1))
            fig.text(sx - 0.004, sy, f"scale: {sb_len} m", fontsize=7, ha="right", va="center")

            draw_legend(fig, dests, counts, cmap, codes)
            fig.text(1 - MARGIN_R, 0.012,
                     f"Times as given in the input file (UTC 'Z' suffix). "
                     f"Generated {datetime.now():%d.%m.%Y %H:%M}",
                     fontsize=6, color="#777", ha="right")
            pdf.savefig(fig)
            plt.close(fig)

        info = pdf.infodict()
        info["Title"] = f"Inbound train schedule – {json_path.stem}"
        info["Subject"] = "Wiener Neustadt shunting yard DES – inbound trains"

    print(f"Wrote    {pdf_path}  ({len(trains)} trains, {len(pages)} pages)")
    return pdf_path


def main():
    ap = argparse.ArgumentParser(description="Visualise inbound train schedule JSON files as PDF.")
    ap.add_argument("json_files", nargs="*", default=[str(DEFAULT_JSON)],
                    help=f"input JSON file(s) (default: {DEFAULT_JSON})")
    args = ap.parse_args()
    for f in args.json_files:
        if not Path(f).is_file():
            print(f"File not found: {f}", file=sys.stderr)
            continue
        render_pdf(f)


if __name__ == "__main__":
    main()
