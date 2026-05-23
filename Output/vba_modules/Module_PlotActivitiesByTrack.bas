Attribute VB_Name = "Module_PlotActivitiesByTrack"
Option Explicit

' ---------------------------------------------------------------------------
' PlotActivitiesByTrack
'
' Reads the "Activities (raw)" sheet, groups rows by activity-type prefix,
' and creates one XY scatter chart per prefix on a fresh "Activity Scatter
' Charts" sheet.
'
'   X axis: StartedAt   (real Excel date serial, plots on a time axis)
'   Y axis: Duration (min)
'   Series: one per track. The track id is the last underscore-segment of
'           the ActivityId, e.g.
'             Act_ARRD_12001_050000_703            -> track "703"
'             Act_OBTP_OBT010125102335-Graz_..._615 -> track "615"
'
' Prefixes covered (in this order, top-left to bottom-right, 2 charts/row):
'   Act_ARRD_   ArrivalDrive
'   Act_ITP_    IncomingTrainPreparation
'   Act_PO_     PushOff (parent)
'   Act_POD_    PushOffDrive
'   Act_SEC_    Securing
'   Act_COP_    Coupling
'   Act_OBTP_   OutboundTrainPreparation
'   Act_DEPD_   DepartureDrive
'
' Note: "Act_PO_" and "Act_POD_" stay distinct because the prefix match
' includes the trailing underscore -- Left("Act_POD_...", 7) = "Act_POD"
' which does NOT equal "Act_PO_".
'
' NOTE on encoding: keep this .bas file PURE ASCII. VBA imports .bas as
' Windows-1252, not UTF-8 -- non-ASCII chars (em-dashes, box-drawing,
' ellipsis, arrows) get mangled into garbage like "aEUR" in the imported
' code.
' ---------------------------------------------------------------------------

' silent:=True suppresses the final MsgBox and the sheet activation. The
' post-processor (run_analytics_post.vbs) passes True so the macro can run
' under COM automation without a modal dialog hanging the hidden Excel
' instance. Manual runs via Alt+F8 leave it False, so you still get the
' "Created N charts" confirmation.
Public Sub PlotActivitiesByTrack(Optional ByVal silent As Boolean = False)
    Dim wsData As Worksheet
    On Error Resume Next
    Set wsData = ThisWorkbook.Worksheets("Activities (raw)")
    On Error GoTo 0
    If wsData Is Nothing Then
        MsgBox "Sheet 'Activities (raw)' not found.", vbCritical, _
               "PlotActivitiesByTrack"
        Exit Sub
    End If

    Dim colId As Long, colStart As Long, colDurMin As Long
    colId     = FindHeaderColumn(wsData, "ActivityId")
    colStart  = FindHeaderColumn(wsData, "StartedAt")
    colDurMin = FindHeaderColumn(wsData, "Duration (min)")
    If colId = 0 Or colStart = 0 Or colDurMin = 0 Then
        MsgBox "One or more required columns are missing on 'Activities (raw)':" _
            & vbCrLf & "  ActivityId, StartedAt, Duration (min)", _
            vbCritical, "PlotActivitiesByTrack"
        Exit Sub
    End If

    Dim lastRow As Long
    lastRow = wsData.Cells(wsData.Rows.Count, colId).End(xlUp).Row
    If lastRow < 2 Then Exit Sub

    ' Bulk-read the three relevant columns into in-memory arrays. Touching
    ' Cells(r,c).Value in a row loop on a thousand-row sheet is ~100x slower.
    Dim ids() As Variant, starts() As Variant, durs() As Variant
    ids    = wsData.Range(wsData.Cells(2, colId),     wsData.Cells(lastRow, colId)).Value2
    starts = wsData.Range(wsData.Cells(2, colStart),  wsData.Cells(lastRow, colStart)).Value2
    durs   = wsData.Range(wsData.Cells(2, colDurMin), wsData.Cells(lastRow, colDurMin)).Value2

    ' Recreate the output sheet so re-runs don't pile up old charts.
    Dim wsOut As Worksheet
    Application.DisplayAlerts = False
    On Error Resume Next
    ThisWorkbook.Worksheets("Activity Scatter Charts").Delete
    On Error GoTo 0
    Application.DisplayAlerts = True
    Set wsOut = ThisWorkbook.Worksheets.Add(After:=wsData)
    wsOut.Name = "Activity Scatter Charts"

    Dim prefixes As Variant
    prefixes = Array( _
        Array("Act_ARRD_", "ArrivalDrive"), _
        Array("Act_ITP_",  "IncomingTrainPreparation"), _
        Array("Act_PO_",   "PushOff (parent)"), _
        Array("Act_POD_",  "PushOffDrive"), _
        Array("Act_SEC_",  "Securing"), _
        Array("Act_COP_",  "Coupling"), _
        Array("Act_OBTP_", "OutboundTrainPreparation"), _
        Array("Act_DEPD_", "DepartureDrive") _
    )

    Const CHART_W As Long = 600
    Const CHART_H As Long = 460          ' was 360; taller leaves room for
                                          ' the vertical date labels.
    Const CHART_GAP As Long = 20
    Const COLS_PER_ROW As Long = 2

    Application.ScreenUpdating = False

    Dim i As Long
    For i = 0 To UBound(prefixes)
        Dim prefix As String, friendly As String
        prefix = prefixes(i)(0)
        friendly = prefixes(i)(1)

        Dim rowIdx As Long, colIdx As Long
        rowIdx = i \ COLS_PER_ROW
        colIdx = i Mod COLS_PER_ROW

        Dim chartLeft As Single, chartTop As Single
        chartLeft = 10 + colIdx * (CHART_W + CHART_GAP)
        chartTop = 10 + rowIdx * (CHART_H + CHART_GAP)

        BuildChartForPrefix wsOut, prefix, friendly, _
            ids, starts, durs, _
            chartLeft, chartTop, CHART_W, CHART_H
    Next i

    Application.ScreenUpdating = True
    If Not silent Then
        wsOut.Activate
        wsOut.Cells(1, 1).Select
        MsgBox "Created " & (UBound(prefixes) + 1) & _
               " scatter charts on '" & wsOut.Name & "'.", _
               vbInformation, "PlotActivitiesByTrack"
    End If
End Sub

' --- internals ------------------------------------------------------------

Private Sub BuildChartForPrefix(wsOut As Worksheet, prefix As String, _
    friendly As String, _
    ids() As Variant, starts() As Variant, durs() As Variant, _
    chartLeft As Single, chartTop As Single, _
    chartW As Long, chartH As Long)

    ' Group row indices by track id.
    Dim tracks As Object
    Set tracks = CreateObject("Scripting.Dictionary")

    Dim n As Long, r As Long, actId As String, track As String
    n = UBound(ids, 1)
    For r = 1 To n
        actId = CStr(ids(r, 1))
        If Len(actId) >= Len(prefix) Then
            If Left$(actId, Len(prefix)) = prefix Then
                track = ExtractTrack(actId)
                If Not tracks.Exists(track) Then
                    tracks.Add track, New Collection
                End If
                tracks(track).Add r
            End If
        End If
    Next r

    If tracks.Count = 0 Then Exit Sub

    Dim cht As ChartObject
    Set cht = wsOut.ChartObjects.Add(Left:=chartLeft, Top:=chartTop, _
                                      Width:=chartW, Height:=chartH)
    With cht.Chart
        .ChartType = xlXYScatter
        .HasTitle = True
        .ChartTitle.Text = friendly & " - Duration (min) vs StartedAt, by track"
        .HasLegend = True
        .Legend.Position = xlLegendPositionRight
        ' Strip the default empty series that .Add gives us.
        Do While .SeriesCollection.Count > 0
            .SeriesCollection(1).Delete
        Loop
    End With

    ' Add one series per track. Sort track keys for a sensible legend order.
    Dim trackKeys As Variant
    trackKeys = tracks.Keys
    SortStringArray trackKeys

    Dim seriesIdx As Long
    seriesIdx = 0
    Dim k As Variant
    For Each k In trackKeys
        Dim coll As Collection
        Set coll = tracks(CStr(k))
        Dim m As Long: m = coll.Count
        Dim xArr() As Variant, yArr() As Variant
        ReDim xArr(1 To m), yArr(1 To m)
        Dim j As Long, rr As Long
        For j = 1 To m
            rr = coll(j)
            xArr(j) = starts(rr, 1)
            yArr(j) = durs(rr, 1)
        Next j
        Dim s As Series
        Set s = cht.Chart.SeriesCollection.NewSeries
        s.XValues = xArr
        s.Values = yArr
        s.Name = "Track " & CStr(k)
        s.MarkerStyle = xlMarkerStyleCircle
        ' Was 6; 25% smaller -> ~4.5, rounded to 5. Adjust if too small.
        s.MarkerSize = 5
        ' NOTE: do NOT name this `rgb` -- VBA is case-insensitive, so a local
        ' `rgb` shadows the built-in RGB() function and any later RGB(...)
        ' call in this Sub becomes "Expected array".
        Dim clr As Long
        clr = PaletteColor(seriesIdx)
        ' Legacy MarkerBackground/Foreground are the reliable cross-version
        ' way to colour the marker FACE on XY scatter. Format.Fill on Series
        ' often only reaches the marker BORDER, leaving the inside default-blue.
        s.MarkerBackgroundColor = clr
        s.MarkerForegroundColor = clr
        ' Belt-and-braces: ensure no connecting line ever appears between
        ' points (xlXYScatter implies markers-only, but explicit is safer).
        s.Format.Line.Visible = msoFalse
        seriesIdx = seriesIdx + 1
    Next k

    ' --- axes ---
    ' For XY Scatter, xlCategory IS the X axis (a value axis even though the
    ' constant says category). Set the date format on its tick labels so the
    ' numeric serials render as dates, rotate them vertical so successive
    ' timestamps don't overlap, and turn on major gridlines for day boundaries.
    On Error Resume Next
    With cht.Chart.Axes(xlCategory)
        .HasTitle = True
        .AxisTitle.Text = "Start time"
        .TickLabels.NumberFormat = "yyyy-mm-dd hh:mm"
        .TickLabels.Orientation = 90       ' 90 = vertical (reading bottom-up)
        .HasMajorGridlines = True
        .MajorGridlines.Format.Line.Visible = msoTrue
        .MajorGridlines.Format.Line.ForeColor.RGB = RGB(200, 200, 200)
    End With
    With cht.Chart.Axes(xlValue)
        .HasTitle = True
        .AxisTitle.Text = "Duration (min)"
        .HasMajorGridlines = True
        .MajorGridlines.Format.Line.ForeColor.RGB = RGB(220, 220, 220)
    End With
    On Error GoTo 0

    ' --- shrink plot area so vertical date labels fit ---
    ' Excel's auto-layout often clips vertical tick labels at the bottom of
    ' the chart frame. Manually carve out room: pin the plot area top below
    ' the title and stop its bottom edge well above the chart's bottom edge,
    ' leaving the lower band of the chart for the rotated labels.
    On Error Resume Next
    With cht.Chart.PlotArea
        .Top = 30
        .Height = chartH * 0.62           ' leave ~38% of chart height for
                                           ' title + rotated x labels + axis
                                           ' title underneath.
    End With
    On Error GoTo 0
End Sub

Private Function FindHeaderColumn(ws As Worksheet, header As String) As Long
    Dim lastCol As Long
    lastCol = ws.Cells(1, ws.Columns.Count).End(xlToLeft).Column
    Dim c As Long
    For c = 1 To lastCol
        If Trim(CStr(ws.Cells(1, c).Value)) = header Then
            FindHeaderColumn = c
            Exit Function
        End If
    Next c
    FindHeaderColumn = 0
End Function

Private Function ExtractTrack(actId As String) As String
    Dim parts() As String
    parts = Split(actId, "_")
    ExtractTrack = parts(UBound(parts))
End Function

Private Function PaletteColor(idx As Long) As Long
    ' Tableau-10 palette + 6 extras -- distinguishable up to ~16 tracks.
    Dim p As Variant
    p = Array( _
        RGB(31, 119, 180),  RGB(255, 127, 14),  RGB(44, 160, 44), _
        RGB(214, 39, 40),   RGB(148, 103, 189), RGB(140, 86, 75), _
        RGB(227, 119, 194), RGB(127, 127, 127), RGB(188, 189, 34), _
        RGB(23, 190, 207),  RGB(174, 199, 232), RGB(255, 187, 120), _
        RGB(152, 223, 138), RGB(255, 152, 150), RGB(197, 176, 213), _
        RGB(196, 156, 148))
    PaletteColor = p(idx Mod (UBound(p) + 1))
End Function

Private Sub SortStringArray(arr As Variant)
    ' In-place insertion sort. ~30 tracks max in practice, so O(n^2) is fine.
    Dim i As Long, j As Long, tmp As Variant
    For i = LBound(arr) + 1 To UBound(arr)
        tmp = arr(i)
        j = i - 1
        Do While j >= LBound(arr)
            If CStr(arr(j)) <= CStr(tmp) Then Exit Do
            arr(j + 1) = arr(j)
            j = j - 1
        Loop
        arr(j + 1) = tmp
    Next i
End Sub
