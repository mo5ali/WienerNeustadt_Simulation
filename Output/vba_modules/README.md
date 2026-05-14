# VBA modules for SimulationAnalytics.xlsx

`analytics.py` produces a plain `.xlsx` file (openpyxl can't author a valid
`.xlsm` from scratch — it'd need a pre-existing template with a vbaProject).
To use the macros below, do this once after each fresh sim run:

1. Open `SimulationAnalytics.xlsx` in Excel.
2. **File → Save As → Excel Macro-Enabled Workbook (`.xlsm`)**.
3. Press **Alt+F11** to open the VBA editor.
4. **File → Import File…**, then import each `.bas` file in this folder.
5. Save the `.xlsm`. From now on you can run any macro via **Alt+F8**.

Each module is self-contained — they don't depend on the analytics sheets'
specific layout, so you can run them on any selection.

## Modules

- **`Module_QuickStats.bas`** — Select any range of numeric cells and run
  `QuickStats`. Shows count, mean, median, min, max, stdev in a popup.
- **`Module_HighlightOutliers.bas`** — Select one column of numbers and run
  `HighlightOutliers`. Cells more than 2σ above the mean are filled red,
  more than 2σ below are filled blue. Quick visual outlier scan.
- **`Module_Histogram.bas`** — Select one column of numbers and run
  `Histogram`. Bins the data into 10 equal-width buckets, dumps the table
  next to your selection, and inserts a column chart of the distribution.

After the first manual save-as-xlsm + import, future re-runs of `analytics.py`
will overwrite the data sheets but leave your macros intact ONLY if you
keep the .xlsm and update the data inside it manually (e.g. paste from the
freshly-generated .xlsx). For a fully automated path you'd need a template
.xlsm with macros pre-imported and have analytics.py load+populate it
(`openpyxl.load_workbook(template_path, keep_vba=True)`). Happy to wire
that up once you've authored the template once and committed it.
