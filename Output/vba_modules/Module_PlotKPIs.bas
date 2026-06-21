Attribute VB_Name = "Module_PlotKPIs"
Option Explicit

' ---------------------------------------------------------------------------
' KPI summary bar charts on the "Activity Charts" sheet.
'
' Three horizontal bar charts pulled from the Overview sheet:
'   KPIIncoming     - 8 bars for Incoming Trains mean KPIs   (Overview!A11:A18,B11:B18)
'   KPIWagonGroups  - 3 bars for Wagon Groups mean KPIs       (Overview!A21:A23,B21:B23)
'   KPIOutbound     - 5 bars for Outbound Trains mean KPIs    (Overview!A26:A30,B26:B30)
'
' Each chart's value axis renders as [h]:mm:ss so the durations read
' naturally regardless of how short or long they are (fraction-of-a-day
' values from the Overview formulas).
'
' Entry points (Alt+F8):
'   PlotKPISummaries        - prompts on completion.
'   PlotKPISummariesSilent  - silent variant for the .vbs post-processor.
'
' Module_PlotActivitiesByTrack.PlotAllSilent calls the silent variant
' after building the per-activity-type charts, so the whole
' "Activity Charts" sheet is produced in one .vbs run.
'
' Placement: below the Drive Length vs Duration scatter
' (Module_PlotDriveActivities puts that at top=1950, height=520),
' starting at top=2520 in a 2-col layout.
'
' NOTE: keep this file PURE ASCII. VBA imports .bas as Windows-1252.
' ---------------------------------------------------------------------------

' KPI block coordinates on the Overview sheet (must match the row layout
' that build_overview_sheet emits in analytics.py).
Private Const INCOMING_FIRST_ROW As Long = 11
Private Const INCOMING_LAST_ROW As Long = 18
Private Const WG_FIRST_ROW As Long = 21
Private Const WG_LAST_ROW As Long = 23
Private Const OUT_FIRST_ROW As Long = 26
Private Const OUT_LAST_ROW As Long = 30

' Chart placement constants on the Activity Charts sheet.
Private Const CHART_W As Long = 600
Private Const CHART_H As Long = 400
Private Const CHART_GAP As Long = 20
Private Const FIRST_TOP As Single = 2520
Private Const FIRST_LEFT As Single = 10
Private Const COL2_LEFT As Single = 630

Public Sub PlotKPISummaries()
    PlotKPIs False
End Sub

Public Sub PlotKPISummariesSilent()
    PlotKPIs True
End Sub

Private Sub PlotKPIs(ByVal silent As Boolean)
    Dim wsData As Worksheet
    Dim wsOut As Worksheet
    On Error Resume Next
    Set wsData = ThisWorkbook.Worksheets("Overview")
    Set wsOut = ThisWorkbook.Worksheets("Activity Charts")
    On Error GoTo 0

    If wsData Is Nothing Then
        If Not silent Then MsgBox "Sheet 'Overview' not found.", vbCritical
        Exit Sub
    End If
    If wsOut Is Nothing Then
        If Not silent Then MsgBox "Sheet 'Activity Charts' not found. " & _
            "Run PlotActivityCharts first.", vbCritical
        Exit Sub
    End If

    ' Delete any prior copies so re-runs don't pile up duplicates.
    Dim co As ChartObject
    For Each co In wsOut.ChartObjects
        Select Case co.Name
            Case "KPIIncoming", "KPIWagonGroups", "KPIOutbound"
                co.Delete
        End Select
    Next co

    Application.ScreenUpdating = False

    ' Row 1: Incoming + WG side by side.
    BuildKPIBarChart wsOut, wsData, "KPIIncoming", _
                     "Mean Incoming Trains KPIs", _
                     INCOMING_FIRST_ROW, INCOMING_LAST_ROW, _
                     FIRST_LEFT, FIRST_TOP
    BuildKPIBarChart wsOut, wsData, "KPIWagonGroups", _
                     "Mean Wagon Groups KPIs", _
                     WG_FIRST_ROW, WG_LAST_ROW, _
                     COL2_LEFT, FIRST_TOP

    ' Row 2: Outbound (full width row -- only chart on this row).
    BuildKPIBarChart wsOut, wsData, "KPIOutbound", _
                     "Mean Outbound Trains KPIs", _
                     OUT_FIRST_ROW, OUT_LAST_ROW, _
                     FIRST_LEFT, FIRST_TOP + CHART_H + CHART_GAP

    Application.ScreenUpdating = True

    If Not silent Then
        wsOut.Activate
        MsgBox "KPI summary charts added to 'Activity Charts' (below the " & _
               "drive scatter).", vbInformation
    End If
End Sub

' Build one horizontal bar chart at the given position. Categories
' (KPI names) come from Overview!A<firstRow>:A<lastRow>, values from
' Overview!B<firstRow>:B<lastRow> (the AVERAGE-of-source-column formula
' Overview emits). xlBarClustered puts categories on the Y axis and
' values on the X axis, which is the natural orientation for KPI labels
' that can be long.
Private Sub BuildKPIBarChart(wsOut As Worksheet, wsData As Worksheet, _
                             ByVal chartName As String, ByVal chartTitle As String, _
                             ByVal firstRow As Long, ByVal lastRow As Long, _
                             ByVal chartLeft As Single, ByVal chartTop As Single)
    Dim co As ChartObject
    Set co = wsOut.ChartObjects.Add(Left:=chartLeft, Top:=chartTop, _
                                    Width:=CHART_W, Height:=CHART_H)
    co.Name = chartName

    With co.Chart
        ' Set chart type BEFORE adding series (see Module_PlotDriveActivities
        ' comment header: ChartObjects.Add creates the chart at
        ' xlColumnClustered, so MarkerStyle / category orientation depends
        ' on the type being set first).
        .ChartType = xlBarClustered

        Do While .SeriesCollection.Count > 0
            .SeriesCollection(1).Delete
        Loop

        Dim s As Series: Set s = .SeriesCollection.NewSeries
        s.Name = "Mean"
        ' XValues for a bar chart = the category labels (KPI names on Y).
        s.XValues = wsData.Range(wsData.Cells(firstRow, 1), wsData.Cells(lastRow, 1))
        s.Values = wsData.Range(wsData.Cells(firstRow, 2), wsData.Cells(lastRow, 2))
        s.Format.Fill.ForeColor.RGB = RGB(46, 134, 222)

        ' Per-bar data labels showing the mean as hh:mm:ss.
        s.ApplyDataLabels Type:=xlDataLabelsShowValue
        On Error Resume Next
        s.DataLabels.NumberFormat = "[h]:mm:ss"
        s.DataLabels.Font.Size = 8
        On Error GoTo 0

        .HasTitle = True
        .ChartTitle.Text = chartTitle
        .HasLegend = False

        On Error Resume Next
        .Axes(xlValue).HasTitle = True
        .Axes(xlValue).AxisTitle.Text = "Mean duration (hh:mm:ss)"
        .Axes(xlValue).TickLabels.NumberFormat = "[h]:mm:ss"
        .Axes(xlCategory).TickLabels.Font.Size = 9
        .Axes(xlCategory).ReversePlotOrder = True   ' top-down order
        On Error GoTo 0
    End With
End Sub
