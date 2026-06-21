Attribute VB_Name = "Module_PlotActivitiesByTrack"
Option Explicit

' ---------------------------------------------------------------------------
' Activity scatter charts from the "Activities (raw)" sheet.
'
' All charts land on a single sheet "Activity Charts" -- one chart per
' activity type, plus the Drive Length vs Duration scatter contributed by
' Module_PlotDriveActivities.
'
' Each chart:
'   X axis: StartedAt   (real Excel date serial -> time axis)
'   Y axis: Duration (hh:mm:ss)
'
' Marker colour-coding per activity type, chosen so the dominant duration
' driver is visible at a glance:
'
'   ActivityType                 Group by    Why
'   ---------------------------  ----------  ----------------------------
'   ArrivalDrive                 Track       distance-driven (per-track)
'   PushOffDrive                 Track       distance-driven
'   DepartureDrive               Track       distance-driven
'   IncomingTrainPreparation     Joints      separation-joint-count-driven
'                                            (NEW, supersedes length grouping)
'   PushOff (parent)             Length      length-driven envelope
'   Securing                     Length      length-driven
'   Coupling                     Length      length-driven
'   OutboundTrainPreparation     Length      length-driven
'
' Track id = the last underscore-segment of the ActivityId, e.g.
'   Act_ARRD_12001_050000_703              -> "703"
'   Act_OBTP_OBT010125102335-Graz_..._615  -> "615"
'
' Entry points (Alt+F8):
'   PlotActivityCharts        - prompts on completion.
'   PlotActivityChartsSilent  - silent variant.
'   PlotAllSilent             - silent driver used by run_analytics_post.vbs;
'                               builds these charts AND the drive scatter.
'
' NOTE: keep this file PURE ASCII. VBA imports .bas as Windows-1252, so
' non-ASCII characters get mangled into garbage on import.
' ---------------------------------------------------------------------------

Private Const N_LENGTH_BINS As Long = 5
Private Const CHARTS_SHEET As String = "Activity Charts"

Public Sub PlotActivityCharts()
    BuildAllCharts False
End Sub

Public Sub PlotActivityChartsSilent()
    BuildAllCharts True
End Sub

' Silent driver invoked by run_analytics_post.vbs. Builds the per-activity
' charts plus the Drive Length vs Duration scatter from
' Module_PlotDriveActivities, all on the same "Activity Charts" sheet.
' Wrapped in On Error so a missing Module_PlotDriveActivities doesn't
' break the whole post-processor.
Public Sub PlotAllSilent()
    BuildAllCharts True
    On Error Resume Next
    Application.Run "Module_PlotDriveActivities.PlotDriveLengthVsDurationSilent"
    Application.Run "Module_PlotKPIs.PlotKPISummariesSilent"
    On Error GoTo 0
End Sub

Private Sub BuildAllCharts(ByVal silent As Boolean)
    Dim wsData As Worksheet
    On Error Resume Next
    Set wsData = ThisWorkbook.Worksheets("Activities (raw)")
    On Error GoTo 0
    If wsData Is Nothing Then
        If Not silent Then MsgBox "Sheet 'Activities (raw)' not found.", vbCritical
        Exit Sub
    End If

    Dim colId As Long, colStart As Long, colDur As Long
    Dim colLen As Long, colJoints As Long
    colId    = FindHeaderColumn(wsData, "ActivityId")
    colStart = FindHeaderColumn(wsData, "StartedAt")
    ' Duration column is "Duration (hh:mm:ss)" (fraction-of-day) on the
    ' current workbook. Falls back to older "Duration (min)" / "Duration (s)"
    ' headers if someone opens an older workbook.
    colDur = FindHeaderColumn(wsData, "Duration (hh:mm:ss)")
    If colDur = 0 Then colDur = FindHeaderColumn(wsData, "Duration (min)")
    If colDur = 0 Then colDur = FindHeaderColumn(wsData, "Duration (s)")
    colLen    = FindHeaderColumn(wsData, "Length (m)")
    colJoints = FindHeaderColumn(wsData, "Number of separation joints")
    If colId = 0 Or colStart = 0 Or colDur = 0 Then
        If Not silent Then MsgBox _
            "Missing required column(s): ActivityId, StartedAt, Duration.", _
            vbCritical
        Exit Sub
    End If

    Dim lastRow As Long
    lastRow = wsData.Cells(wsData.Rows.Count, colId).End(xlUp).Row
    If lastRow < 2 Then Exit Sub

    Dim ids() As Variant, starts() As Variant, durs() As Variant
    Dim lens() As Variant, joints() As Variant
    ids    = wsData.Range(wsData.Cells(2, colId),    wsData.Cells(lastRow, colId)).Value2
    starts = wsData.Range(wsData.Cells(2, colStart), wsData.Cells(lastRow, colStart)).Value2
    durs   = wsData.Range(wsData.Cells(2, colDur),   wsData.Cells(lastRow, colDur)).Value2
    If colLen > 0 Then
        lens = wsData.Range(wsData.Cells(2, colLen), wsData.Cells(lastRow, colLen)).Value2
    Else
        lens = durs   ' unused outside "length" mode; keep shapes aligned
    End If
    If colJoints > 0 Then
        joints = wsData.Range(wsData.Cells(2, colJoints), wsData.Cells(lastRow, colJoints)).Value2
    Else
        joints = durs   ' unused outside "joints" mode; keep shapes aligned
    End If

    ' Replace any prior copy of the unified charts sheet, plus tidy up
    ' the legacy split sheets so old workbooks converge to the new layout.
    Application.DisplayAlerts = False
    On Error Resume Next
    ThisWorkbook.Worksheets(CHARTS_SHEET).Delete
    ThisWorkbook.Worksheets("Activity Charts (by track)").Delete
    ThisWorkbook.Worksheets("Activity Charts (by length)").Delete
    On Error GoTo 0
    Application.DisplayAlerts = True

    Dim wsOut As Worksheet
    Set wsOut = ThisWorkbook.Worksheets.Add(After:=wsData)
    wsOut.Name = CHARTS_SHEET

    ' Per-activity-type prefix, friendly name, and group mode.
    Dim prefixes As Variant
    prefixes = Array( _
        Array("Act_ARRD_", "ArrivalDrive",             "track"), _
        Array("Act_ITP_",  "IncomingTrainPreparation", "joints"), _
        Array("Act_PO_",   "PushOff (parent)",         "length"), _
        Array("Act_POD_",  "PushOffDrive",             "track"), _
        Array("Act_SEC_",  "Securing",                 "length"), _
        Array("Act_COP_",  "Coupling",                 "length"), _
        Array("Act_OBTP_", "OutboundTrainPreparation", "length"), _
        Array("Act_DEPD_", "DepartureDrive",           "track") _
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
        BuildChart wsOut, _
                   CStr(prefixes(i)(0)), CStr(prefixes(i)(1)), CStr(prefixes(i)(2)), _
                   ids, starts, durs, lens, joints, _
                   cLeft, cTop, CHART_W, CHART_H
    Next i
    Application.ScreenUpdating = True

    If Not silent Then
        wsOut.Activate
        wsOut.Cells(1, 1).Select
        MsgBox "Created " & (UBound(prefixes) + 1) & " charts on '" & _
               CHARTS_SHEET & "'.", vbInformation
    End If
End Sub

Private Sub BuildChart(wsOut As Worksheet, prefix As String, friendly As String, _
    groupMode As String, _
    ids() As Variant, starts() As Variant, durs() As Variant, _
    lens() As Variant, joints() As Variant, _
    chartLeft As Single, chartTop As Single, chartW As Long, chartH As Long)

    Dim n As Long: n = UBound(ids, 1)

    ' Pass 1: collect matching rows; in length mode also find min/max.
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
        ElseIf groupMode = "joints" Then
            ' Group directly by joint count. ITP rows carry an integer in
            ' the joints column; non-ITP rows have blank cells and end up
            ' in the "(no joints)" bucket (which shouldn't happen since we
            ' only use this mode for ITP, but stays defensive).
            If IsNumeric(joints(r, 1)) Then
                ord = CLng(joints(r, 1))
                key = Format(ord, "00")
                lbl = ord & " joint"
                If ord <> 1 Then lbl = lbl & "s"
            Else
                key = "99"
                lbl = "(no joints)"
            End If
        Else
            ' Track mode (default).
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
    Select Case groupMode
        Case "length": modeLbl = "by length"
        Case "joints": modeLbl = "by separation joints"
        Case Else:     modeLbl = "by track"
    End Select
    With cht.Chart
        .ChartType = xlXYScatter
        .HasTitle = True
        .ChartTitle.Text = friendly & " - Duration (hh:mm:ss) vs StartedAt, " & modeLbl
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
        .AxisTitle.Text = "Duration (hh:mm:ss)"
        .TickLabels.NumberFormat = "[h]:mm:ss"
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
