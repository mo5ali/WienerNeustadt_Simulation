# VBA modules for SimulationAnalytics

After every simulation run you should get a `SimulationAnalytics.xlsm` in
`bin\Debug\net8.0\OutputFiles\` with these macro modules pre-imported and
ready to use (via **Alt+F8**).

## Automated workflow (Windows only)

`analytics.py` does the heavy lifting in two steps:

1. **Python phase** — writes `SimulationAnalytics.xlsx` using openpyxl
   (sheets, formulas, native charts).
2. **Windows post-processor** — invokes `Output\run_analytics_post.vbs`
   via cscript, which:
   - Opens the `.xlsx` in a hidden Excel COM instance.
   - **Save-As** as `SimulationAnalytics.xlsm` (macro-enabled,
     `XlFileFormat = 52`).
   - Imports every `Module_*.bas` from this folder into the workbook's
     VBA project (replacing same-named modules so re-runs don't pile up
     duplicates).
   - Saves and closes Excel.
   - Deletes the intermediate `.xlsx`.

End state: only `SimulationAnalytics.xlsm` in `OutputFiles`, with macros
ready.

### One-time setup

Programmatic VBA import requires Excel's Trust Center setting:

> **File → Options → Trust Center → Trust Center Settings → Macro Settings**
> → check **"Trust access to the VBA project object model"**

Without that, the `.vbs` errors with "Programmatic access to Visual Basic
Project is not trusted." and exits with code 2. The script prints that
exact fix to the console when it happens.

On non-Windows machines (or any machine without Excel installed), the
post-processor is silently skipped and you just get the `.xlsx`. The data
sheets are the same — you only lose the macros.

### Heads-up

If the `.xlsm` is open in Excel when the next sim run fires, the
post-processor can't overwrite it and bails with a clear error. Close
Excel before re-running.

## Modules

- **`Module_QuickStats.bas`** — Select any range of numeric cells, run
  `QuickStats`. Popup shows count, mean, median, min, max, stdev.
- **`Module_HighlightOutliers.bas`** — Select a column of numbers, run
  `HighlightOutliers`. Cells > mean+2σ get red fill, < mean−2σ get blue
  fill, plus a summary popup. Run again after sorting/filtering to refresh.
- **`Module_Histogram.bas`** — Select a column of numbers, run
  `Histogram`. Drops a 10-bin frequency table two columns to the right of
  the selection and inserts a column chart over it.

All three modules are layout-agnostic — they operate on the current
selection, so they work on any sheet (raw or summary).

## Adding your own macros

Drop a new `Module_*.bas` file in this folder. On the next sim run it
gets imported automatically. The naming convention is just a convention
— the post-processor imports every `.bas` regardless of name — but
`Module_<purpose>.bas` keeps the VBA project tidy in the editor.

The first line of each module should be the `Attribute VB_Name = "..."`
directive so Excel uses the right module name; the existing
`Module_*.bas` files here all have it as a template.
