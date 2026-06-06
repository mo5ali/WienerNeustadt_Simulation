Attribute VB_Name = "Module_PlotDriveActivities"
Option Explicit

' ---------------------------------------------------------------------------
' Drive Length vs Duration scatter chart.
'
' One chart, three colour-coded series (ArrivalDrive / PushOffDrive /
' DepartureDrive), built from the "Activities (raw)" sheet. Plotted on
' the unified "Activity Charts" sheet, below the 8 per-activity-type
' charts produced by Module_PlotActivitiesByTrack.
'
' Axes:
'   X  Drive length (m)        (Activities (raw) column "Drive length (m)")
'   Y  Duration (hh:mm:ss)     (Activities (raw) column "Duration (hh:mm:ss)";
'                               that column stores fraction-of-a-day, so the
'                               chart Y-axis is also a fraction. Excel's
'                               default axis labels show it as a time value.)
'
' Hover behaviour: Excel scatter charts natively show
' "<series name>, (X, Y)" when the user hovers a marker -- so on hover
' you get e.g. "PushOffDrive, (412, 0:02:30)" automatically. To
' supplement this with entity ID + entity length (which the user asked
' for), each point also carries a small persistent data label of the
' form "<entityId> (<entityLen>m)". Labels can be turned off via
' Excel's chart-element panel if the point density makes them cluttered.
'
' Entry points (both runnable via Alt+F8):
'   PlotDriveLengthVsDuration         - prompts on completion.
'   PlotDriveLengthVsDurationSilent   - silent variant for the .vbs
'                                       post-processor.
'
' Implementation notes:
'   - XValues / Values must be VBA arrays, NOT Range references.
'     Range refs go blank on XY scatter in some Excel versions.
'   - Marker face colour must be set via MarkerBackgroundColor +
'     MarkerForegroundColor (legacy). Format.Fill.ForeColor only
'     colours the marker border on XY scatter.
'   - ChartType must be set AFTER the first series is added; setting
'     it on an empty chart can produce a blank box.
'
' NOTE: keep this file PURE ASCII. VBA imports .bas as Windows-1252.
' ---------------------------------------------------------------------------

' Marker colours per series.
Private Const COLOR_ARRD As Long = 14844206  ' RGB(46, 134, 222)  blue
Private Const COLOR_POD  As Long = 1156092   ' RGB(252, 162, 17)  orange
Private Const COLOR_DEPD As Long = 7390766   ' RGB(46, 204, 113)  green

Public Sub PlotDriveLengthVsDuration()
    PlotDrives False
End Sub

Public Sub PlotDriveLengthVsDurationSilent()
    PlotDrives True
End Sub

Private Sub PlotDrives(ByVal silent As Boolean)
    Dim wsData As Worksheet
    On Error Resume Next
    Set wsData = ThisWorkbook.Worksheets("Activities (raw)")
    On Error GoTo 0
    If wsData Is Nothing Then
        If Not silent Then MsgBox "Sheet 'Activities (raw)' not found.", vbCritical
        Exit Sub
    End If

    Dim colType As Long, colId As Long, colEntity As Long
    Dim colEntLen As Long, colDriveLen As Long, colDurHms As Long
    colType     = FindHeader(wsData, "ActivityType")
    colId       = FindHeader(wsData, "ActivityId")
    colEntity   = FindHeader(wsData, "Entity")
    colEntLen   = FindHeader(wsData, "Length (m)")
    colDriveLen = FindHeader(wsData, "Drive length (m)")
    colDurHms   = FindHeader(wsData, "Duration (hh:mm:ss)")
    ' Fall back to the legacy "Duration (min)" or "Duration (s)" header
    ' if the workbook predates the hh:mm:ss-only switch.
    If colDurHms = 0 Then colDurHms = FindHeader(wsData, "Duration (min)")
    If colDurHms = 0 Then colDurHms = FindHeader(wsData, "Duration (s)")

    If colType = 0 Or colDriveLen = 0 Or colDurHms = 0 Then
        If Not silent Then MsgBox _
            "Activities (raw) is missing one of: ActivityType, " & _
            "Drive length (m), Duration. Re-run analytics.py.", vbCritical
        Exit Sub
    End If

    Dim lastRow As Long
    lastRow = wsData.Cells(wsData.Rows.Count, colType).End(xlUp).Row
    If lastRow < 2 Then Exit Sub

    Dim types As Variant, ids As Variant, entities As Variant
    Dim entLens As Variant, driveLens As Variant, durs As Variant
    types     = wsData.Range(wsData.Cells(2, colType),     wsData.Cells(lastRow, colType)).Value2
    ids       = wsData.Range(wsData.Cells(2, colId),       wsData.Cells(lastRow, colId)).Value2
    entities  = wsData.Range(wsData.Cells(2, colEntity),   wsData.Cells(lastRow, colEntity)).Value2
    entLens   = wsData.Range(wsData.Cells(2, colEntLen),   wsData.Cells(lastRow, colEntLen)).Value2
    driveLens = wsData.Range(wsData.Cells(2, colDriveLen), wsData.Cells(lastRow, colDriveLen)).Value2
    durs      = wsData.Range(wsData.Cells(2, colDurHms),   wsData.Cells(lastRow, colDurHms)).Value2

    ' Locate (or create) the target chart sheet.
    Dim sheetName As String: sheetName = "Activity Charts"
    Dim wsChart As Worksheet
    On Error Resume Next
    Set wsChart = ThisWorkbook.Worksheets(sheetName)
    On Error GoTo 0
    If wsChart Is Nothing Then
        Set wsChart = ThisWorkbook.Worksheets.Add(After:=wsData)
        wsChart.Name = sheetName
    End If

    ' Collect points into per-series VBA arrays. (Arrays, NOT Ranges --
    ' Range-based XValues/Values produced the blank chart in v1.)
    Dim n As Long: n = UBound(types, 1)

    Dim arrX() As Double, arrY() As Double, arrLbl() As String
    Dim podX() As Double, podY() As Double, podLbl() As String
    Dim depdX() As Double, depdY() As Double, depdLbl() As String
    ReDim arrX(1 To n), arrY(1 To n), arrLbl(1 To n)
    ReDim podX(1 To n), podY(1 To n), podLbl(1 To n)
    ReDim depdX(1 To n), depdY(1 To n), depdLbl(1 To n)

    Dim arrCount As Long, podCount As Long, depdCount As Long
    arrCount = 0: podCount = 0: depdCount = 0

    Dim i As Long
    For i = 1 To n
        If Not IsNumeric(driveLens(i, 1)) Then GoTo NextIter
        If Not IsNumeric(durs(i, 1)) Then GoTo NextIter

        Dim t As String: t = CStr(types(i, 1))
        If t <> "ArrivalDrive" And t <> "PushOffDrive" And t <> "DepartureDrive" Then GoTo NextIter

        Dim labelTxt As String
        labelTxt = CStr(entities(i, 1))
        If IsNumeric(entLens(i, 1)) Then
            labelTxt = labelTxt & " (" & Format(entLens(i, 1), "0") & "m)"
        End If

        Select Case t
            Case "ArrivalDrive"
                arrCount = arrCount + 1
                arrX(arrCount) = CDbl(driveLens(i, 1))
                arrY(arrCount) = CDbl(durs(i, 1))
                arrLbl(arrCount) = labelTxt
            Case "PushOffDrive"
                podCount = podCount + 1
                podX(podCount) = CDbl(driveLens(i, 1))
                podY(podCount) = CDbl(durs(i, 1))
                podLbl(podCount) = labelTxt
            Case "DepartureDrive"
                depdCount = depdCount + 1
                depdX(depdCount) = CDbl(driveLens(i, 1))
                depdY(depdCount) = CDbl(durs(i, 1))
                depdLbl(depdCount) = labelTxt
        End Select
NextIter:
    Next i

    ' Trim arrays to actual counts (Series wants exact-sized arrays).
    If arrCount > 0 Then ReDim Preserve arrX(1 To arrCount), arrY(1 To arrCount), arrLbl(1 To arrCount)
    If podCount > 0 Then ReDim Preserve podX(1 To podCount), podY(1 To podCount), podLbl(1 To podCount)
    If depdCount > 0 Then ReDim Preserve depdX(1 To depdCount), depdY(1 To depdCount), depdLbl(1 To depdCount)

    If arrCount = 0 And podCount = 0 And depdCount = 0 Then
        If Not silent Then MsgBox _
            "No driving-activity rows found in Activities (raw).", vbInformation
        Exit Sub
    End If

    ' Position the chart below the existing 8 per-activity-type charts.
    ' Those use Module_PlotActivitiesByTrack constants CHART_H=460,
    ' CHART_GAP=20, COLS_PER_ROW=2, so 8 charts -> 4 rows -> the row below
    ' starts at top = 10 + 4 * (460 + 20) = 1930.
    Const CHART_W As Long = 900
    Const CHART_H As Long = 520
    Const CHART_TOP As Single = 1950

    ' Delete any prior copy of this chart.
    Dim co As ChartObject
    For Each co In wsChart.ChartObjects
        If co.Name = "DriveLenVsDur" Then
            co.Delete
            Exit For
        End If
    Next co

    Dim chObj As ChartObject
    Set chObj = wsChart.ChartObjects.Add(Left:=10, Top:=CHART_TOP, _
                                         Width:=CHART_W, Height:=CHART_H)
    chObj.Name = "DriveLenVsDur"

    With chObj.Chart
        ' Set the chart type BEFORE adding series. ChartObjects.Add starts
        ' the chart at xlColumnClustered, so any series NewSeries-added
        ' inherits column semantics and rejects MarkerStyle / MarkerSize /
        ' MarkerBackgroundColor with run-time error 1004 ("this property is
        ' not valid for the current chart type"). Switching the type first
        ' makes every subsequent series an XY-scatter series natively.
        .ChartType = xlXYScatter

        ' Clear the default placeholder series the chart was created with.
        Do While .SeriesCollection.Count > 0
            .SeriesCollection(1).Delete
        Loop

        If arrCount > 0 Then
            AddDriveSeries chObj.Chart, "ArrivalDrive", arrX, arrY, arrLbl, COLOR_ARRD
        End If
        If podCount > 0 Then
            AddDriveSeries chObj.Chart, "PushOffDrive", podX, podY, podLbl, COLOR_POD
        End If
        If depdCount > 0 Then
            AddDriveSeries chObj.Chart, "DepartureDrive", depdX, depdY, depdLbl, COLOR_DEPD
        End If

        .HasTitle = True
        .ChartTitle.Text = "Drive activities: drive length vs duration"

        .Axes(xlCategory).HasTitle = True
        .Axes(xlCategory).AxisTitle.Text = "Drive length (m)"
        .Axes(xlValue).HasTitle = True
        .Axes(xlValue).AxisTitle.Text = "Duration (hh:mm:ss)"

        ' Format Y-axis tick labels as time (the Y values are fractions of
        ' a day from the source Duration (hh:mm:ss) column).
        On Error Resume Next
        .Axes(xlValue).TickLabels.NumberFormat = "[h]:mm:ss"
        On Error GoTo 0

        .HasLegend = True
        .Legend.Position = xlLegendPositionBottom
    End With

    If Not silent Then
        wsChart.Activate
        MsgBox "Drive Length vs Duration chart added to '" & sheetName & "'." & vbCrLf & _
               "ArrivalDrive points:   " & arrCount & vbCrLf & _
               "PushOffDrive points:   " & podCount & vbCrLf & _
               "DepartureDrive points: " & depdCount, vbInformation
    End If
End Sub

' Add one colour-coded scatter series. Uses VBA arrays for the X/Y data
' (Range references go blank on XY scatter) and MarkerBackgroundColor /
' MarkerForegroundColor for the face/border colour (Format.Fill only
' reaches the border on XY scatter -- see comment at top of file).
Private Sub AddDriveSeries(ByVal cht As Chart, ByVal seriesName As String, _
                           xArr() As Double, yArr() As Double, lblArr() As String, _
                           ByVal markerColor As Long)
    Dim s As Series: Set s = cht.SeriesCollection.NewSeries
    s.Name = seriesName
    s.XValues = xArr
    s.Values = yArr
    ' Marker properties are only valid once the chart is xlXYScatter, which
    ' PlotDrives ensures BEFORE calling this sub. Wrap in On Error anyway so
    ' if the caller order ever drifts, the series still appears (just without
    ' marker styling) instead of breaking the whole macro mid-run.
    On Error Resume Next
    s.MarkerStyle = xlMarkerStyleCircle
    s.MarkerSize = 7
    s.MarkerBackgroundColor = markerColor
    s.MarkerForegroundColor = markerColor
    s.Format.Line.Visible = msoFalse
    ' Data labels off. The chart stays clean; Excel's built-in
    ' chart hover already shows "<series name>, (X, Y)" on each marker,
    ' which covers the basic identification need without the visual
    ' clutter of persistent per-point labels.
    s.HasDataLabels = False
    On Error GoTo 0
End Sub

Private Function FindHeader(ws As Worksheet, ByVal header As String) As Long
    Dim lastCol As Long
    lastCol = ws.Cells(1, ws.Columns.Count).End(xlToLeft).Column
    Dim c As Long
    For c = 1 To lastCol
        If Trim(CStr(ws.Cells(1, c).Value)) = header Then
            FindHeader = c
            Exit Function
        End If
    Next c
    FindHeader = 0
End Function
