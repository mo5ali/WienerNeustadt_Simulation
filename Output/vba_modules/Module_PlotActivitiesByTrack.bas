Attribute VB_Name = "Module_PlotActivitiesByTrack"
Option Explicit

' ---------------------------------------------------------------------------
' Activity scatter charts from the "Activities (raw)" sheet.
'
' Two entry points (both runnable via Alt+F8):
'   PlotActivitiesByTrack        - one chart per activity type, points
'                                  coloured by destination track.
'   PlotActivitiesByLengthGroup  - one chart per activity type, points
'                                  coloured by entity-length bucket (5 equal
'                                  width bins over each chart's own range).
'
' Each chart:
'   X axis: StartedAt   (real Excel date serial -> time axis)
'   Y axis: Duration (min)
'
' Which colouring is meaningful depends on what drives the duration:
'   - ArrivalDrive duration is driven by per-track distance -> colour by TRACK
'     makes same-track points cluster at the same Y.
'   - ITP / Securing / Coupling / OBTP durations are driven by entity LENGTH
'     -> colour by LENGTH bucket shows the length->duration relationship.
'   - PushOffDrive / DepartureDrive are fixed -> flat regardless of colour.
'
' Track id = the last underscore-segment of the ActivityId, e.g.
'   Act_ARRD_12001_050000_703              -> "703"
'   Act_OBTP_OBT010125102335-Graz_..._615  -> "615"
'
' NOTE: keep this file PURE ASCII. VBA imports .bas as Windows-1252, so
' non-ASCII characters get mangled into garbage on import.
' ---------------------------------------------------------------------------

Private Const N_LENGTH_BINS As Long = 5

' IMPORTANT: these public entry points MUST be parameterless. A Sub with any
' argument -- even an Optional one -- is hidden from the Alt+F8 "Macros"
' dialog. Keeping them argument-free is what makes them runnable manually.

Public Sub PlotActivitiesByTrack()
    PlotActivities "track", False
End Sub

Public Sub PlotActivitiesByLengthGroup()
    PlotActivities "length", False
End Sub

' Silent driver used by run_analytics_post.vbs: builds BOTH chart sheets with
' no end-of-run MsgBox (a modal dialog would block the hidden Excel COM
' instance forever and hang the sim run). Also parameterless so the .vbs can
' invoke it by bare name and so it still shows in Alt+F8 if you want it.
Public Sub PlotAllSilent()
    PlotActivities "track", True
    PlotActivities "length", True
End Sub

Private Sub PlotActivities(ByVal groupMode As String, ByVal silent As Boolean)
    Dim wsData As Worksheet
    On Error Resume Next
    Set wsData = ThisWorkbook.Worksheets("Activities (raw)")
    On Error GoTo 0
    If wsData Is Nothing Then
        If Not silent Then MsgBox "Sheet 'Activities (raw)' not found.", vbCritical
        Exit Sub
    End If

    Dim colId As Long, colStart As Long, colDurMin As Long, colLen As Long
    colId     = FindHeaderColumn(wsData, "ActivityId")
    colStart  = FindHeaderColumn(wsData, "StartedAt")
    colDurMin = FindHeaderColumn(wsData, "Duration (min)")
    colLen    = FindHeaderColumn(wsData, "Length (m)")
    If colId = 0 Or colStart = 0 Or colDurMin = 0 Then
        If Not silent Then MsgBox _
            "Missing required column(s): ActivityId, StartedAt, Duration (min).", _
            vbCritical
        Exit Sub
    End If
    If groupMode = "length" And colLen = 0 Then
        If Not silent Then MsgBox _
            "No 'Length (m)' column found - re-run analytics.py to add it.", _
            vbCritical
        Exit Sub
    End If

    Dim lastRow As Long
    lastRow = wsData.Cells(wsData.Rows.Count, colId).End(xlUp).Row
    If lastRow < 2 Then Exit Sub

    Dim ids() As Variant, starts() As Variant, durs() As Variant, lens() As Variant
    ids    = wsData.Range(wsData.Cells(2, colId),     wsData.Cells(lastRow, colId)).Value2
    starts = wsData.Range(wsData.Cells(2, colStart),  wsData.Cells(lastRow, colStart)).Value2
    durs   = wsData.Range(wsData.Cells(2, colDurMin), wsData.Cells(lastRow, colDurMin)).Value2
    If colLen > 0 Then
        lens = wsData.Range(wsData.Cells(2, colLen), wsData.Cells(lastRow, colLen)).Value2
    Else
        lens = durs   ' unused in track mode; keep shapes aligned
    End If

    Dim sheetName As String
    If groupMode = "length" Then
        sheetName = "Activity Charts (by length)"
    Else
        sheetName = "Activity Charts (by track)"
    End If

    Dim wsOut As Worksheet
    Application.DisplayAlerts = False
    On Error Resume Next
    ThisWorkbook.Worksheets(sheetName).Delete
    On Error GoTo 0
    Application.DisplayAlerts = True
    Set wsOut = ThisWorkbook.Worksheets.Add(After:=wsData)
    wsOut.Name = sheetName

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
    Const CHART_H As Long = 460
    Const CHART_GAP As Long = 20
    Const COLS_PER_ROW As Long = 2

    Application.ScreenUpdating = False
    Dim i As Long
    For i = 0 To UBound(prefixes)
        Dim rIdx As Long, cIdx As Long
        rIdx = i \ COLS_PER_ROW
        cIdx = i Mod COLS_PER_ROW
        Dim cLeft As Single, cTop As Single
        cLeft = 10 + cIdx * (CHART_W + CHART_GAP)
        cTop = 10 + rIdx * (CHART_H + CHART_GAP)
        BuildChart wsOut, CStr(prefixes(i)(0)), CStr(prefixes(i)(1)), groupMode, _
                   ids, starts, durs, lens, cLeft, cTop, CHART_W, CHART_H
    Next i
    Application.ScreenUpdating = True

    If Not silent Then
        wsOut.Activate
        wsOut.Cells(1, 1).Select
        MsgBox "Created " & (UBound(prefixes) + 1) & " charts on '" & _
               sheetName & "'.", vbInformation
    End If
End Sub

Private Sub BuildChart(wsOut As Worksheet, prefix As String, friendly As String, _
    groupMode As String, _
    ids() As Variant, starts() As Variant, durs() As Variant, lens() As Variant, _
    chartLeft As Single, chartTop As Single, chartW As Long, chartH As Long)

    Dim n As Long: n = UBound(ids, 1)

    ' Pass 1: collect matching rows; in length mode find min/max length.
    Dim matched As Collection: Set matched = New Collection
    Dim minLen As Double, maxLen As Double, haveLen As Boolean
    minLen = 1E+30: maxLen = -1E+30: haveLen = False
    Dim r As Long, actId As String
    For r = 1 To n
        actId = CStr(ids(r, 1))
        If Len(actId) >= Len(prefix) Then
            If Left$(actId, Len(prefix)) = prefix Then
                matched.Add r
                If groupMode = "length" Then
                    If IsNumeric(lens(r, 1)) Then
                        Dim lv As Double: lv = CDbl(lens(r, 1))
                        If lv < minLen Then minLen = lv
                        If lv > maxLen Then maxLen = lv
                        haveLen = True
                    End If
                End If
            End If
        End If
    Next r
    If matched.Count = 0 Then Exit Sub

    ' Group rows into a dict keyed by an ordering index, with a label per key.
    Dim grpRows As Object: Set grpRows = CreateObject("Scripting.Dictionary")
    Dim grpLabel As Object: Set grpLabel = CreateObject("Scripting.Dictionary")

    Dim idx As Variant, key As String, lbl As String, ord As Long
    For Each idx In matched
        r = CLng(idx)
        actId = CStr(ids(r, 1))
        If groupMode = "length" Then
            ord = LengthBinIndex(lens(r, 1), minLen, maxLen)
            key = Format(ord, "00")           ' numeric-sortable key
            lbl = LengthBinLabel(ord, minLen, maxLen)
        Else
            key = ExtractTrack(actId)
            lbl = "Track " & key
        End If
        If Not grpRows.Exists(key) Then
            grpRows.Add key, New Collection
            grpLabel.Add key, lbl
        End If
        grpRows(key).Add r
    Next idx

    Dim cht As ChartObject
    Set cht = wsOut.ChartObjects.Add(Left:=chartLeft, Top:=chartTop, _
                                      Width:=chartW, Height:=chartH)
    Dim modeLbl As String
    If groupMode = "length" Then modeLbl = "by length" Else modeLbl = "by track"
    With cht.Chart
        .ChartType = xlXYScatter
        .HasTitle = True
        .ChartTitle.Text = friendly & " - Duration (min) vs StartedAt, " & modeLbl
        .HasLegend = True
        .Legend.Position = xlLegendPositionRight
        Do While .SeriesCollection.Count > 0
            .SeriesCollection(1).Delete
        Loop
    End With

    Dim keys As Variant: keys = grpRows.Keys
    SortStringArray keys

    Dim seriesIdx As Long: seriesIdx = 0
    Dim k As Variant
    For Each k In keys
        Dim coll As Collection: Set coll = grpRows(CStr(k))
        Dim m As Long: m = coll.Count
        Dim xArr() As Variant, yArr() As Variant
        ReDim xArr(1 To m), yArr(1 To m)
        Dim j As Long, rr As Long
        For j = 1 To m
            rr = coll(j)
            xArr(j) = starts(rr, 1)
            yArr(j) = durs(rr, 1)
        Next j
        Dim s As Series: Set s = cht.Chart.SeriesCollection.NewSeries
        s.XValues = xArr
        s.Values = yArr
        s.Name = grpLabel(CStr(k))
        s.MarkerStyle = xlMarkerStyleCircle
        s.MarkerSize = 5
        Dim clr As Long: clr = PaletteColor(seriesIdx)
        ' Legacy MarkerBackground/Foreground reliably colour the marker FACE
        ' on XY scatter across Excel versions (Format.Fill often only reaches
        ' the border).
        s.MarkerBackgroundColor = clr
        s.MarkerForegroundColor = clr
        s.Format.Line.Visible = msoFalse
        seriesIdx = seriesIdx + 1
    Next k

    On Error Resume Next
    With cht.Chart.Axes(xlCategory)
        .HasTitle = True
        .AxisTitle.Text = "Start time"
        .TickLabels.NumberFormat = "yyyy-mm-dd hh:mm"
        .TickLabels.Orientation = 90
        .HasMajorGridlines = True
        .MajorGridlines.Format.Line.ForeColor.RGB = RGB(200, 200, 200)
    End With
    With cht.Chart.Axes(xlValue)
        .HasTitle = True
        .AxisTitle.Text = "Duration (min)"
        .HasMajorGridlines = True
        .MajorGridlines.Format.Line.ForeColor.RGB = RGB(220, 220, 220)
    End With
    With cht.Chart.PlotArea
        .Top = 30
        .Height = chartH * 0.62
    End With
    On Error GoTo 0
End Sub

Private Function LengthBinIndex(lenVal As Variant, minLen As Double, maxLen As Double) As Long
    If Not IsNumeric(lenVal) Then
        LengthBinIndex = N_LENGTH_BINS    ' "unknown" bucket at the end
        Exit Function
    End If
    If maxLen <= minLen Then
        LengthBinIndex = 0
        Exit Function
    End If
    Dim w As Double: w = (maxLen - minLen) / N_LENGTH_BINS
    Dim b As Long: b = Int((CDbl(lenVal) - minLen) / w)
    If b < 0 Then b = 0
    If b >= N_LENGTH_BINS Then b = N_LENGTH_BINS - 1
    LengthBinIndex = b
End Function

Private Function LengthBinLabel(binIdx As Long, minLen As Double, maxLen As Double) As String
    If binIdx >= N_LENGTH_BINS Then
        LengthBinLabel = "(no length)"
        Exit Function
    End If
    If maxLen <= minLen Then
        LengthBinLabel = Format(minLen, "0") & " m"
        Exit Function
    End If
    Dim w As Double: w = (maxLen - minLen) / N_LENGTH_BINS
    Dim lo As Double, hi As Double
    lo = minLen + binIdx * w
    hi = lo + w
    LengthBinLabel = Format(lo, "0") & "-" & Format(hi, "0") & " m"
End Function

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
