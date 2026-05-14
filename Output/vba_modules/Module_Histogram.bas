Attribute VB_Name = "Module_Histogram"
Option Explicit

' Builds a 10-bin histogram of the selected numeric column. Drops the
' bin/count table two columns to the right of the selection and inserts
' a column chart referencing it.
Sub Histogram()
    Const N_BINS As Long = 10
    Dim sel As Range
    Set sel = Selection
    If sel Is Nothing Then Exit Sub
    If sel.Columns.Count > 1 Then
        MsgBox "Select a single column.", vbExclamation, "Histogram"
        Exit Sub
    End If

    Dim vals() As Double
    Dim n As Long: n = 0
    ReDim vals(1 To sel.Cells.Count)
    Dim c As Range
    For Each c In sel.Cells
        If IsNumeric(c.Value) And Not IsEmpty(c.Value) And Len(c.Value) > 0 Then
            n = n + 1
            vals(n) = CDbl(c.Value)
        End If
    Next c
    If n < 5 Then
        MsgBox "Need at least 5 numeric cells.", vbInformation, "Histogram"
        Exit Sub
    End If
    ReDim Preserve vals(1 To n)

    Dim minV As Double: minV = vals(1)
    Dim maxV As Double: maxV = vals(1)
    Dim i As Long
    For i = 2 To n
        If vals(i) < minV Then minV = vals(i)
        If vals(i) > maxV Then maxV = vals(i)
    Next i
    If maxV = minV Then
        MsgBox "All values identical — no histogram to draw.", vbInformation, "Histogram"
        Exit Sub
    End If
    Dim binW As Double: binW = (maxV - minV) / N_BINS

    Dim counts(1 To N_BINS) As Long
    Dim idx As Long
    For i = 1 To n
        idx = Int((vals(i) - minV) / binW) + 1
        If idx > N_BINS Then idx = N_BINS
        If idx < 1 Then idx = 1
        counts(idx) = counts(idx) + 1
    Next i

    ' Drop the table two columns right of the selection.
    Dim ws As Worksheet: Set ws = sel.Worksheet
    Dim startCol As Long: startCol = sel.Column + 2
    Dim startRow As Long: startRow = sel.Row
    ws.Cells(startRow, startCol).Value = "Bin start"
    ws.Cells(startRow, startCol + 1).Value = "Count"
    Dim r As Long
    For r = 1 To N_BINS
        ws.Cells(startRow + r, startCol).Value = minV + (r - 1) * binW
        ws.Cells(startRow + r, startCol + 1).Value = counts(r)
    Next r
    ws.Cells(startRow, startCol).Resize(1, 2).Font.Bold = True

    ' Insert chart anchored at the table.
    Dim tableRange As Range
    Set tableRange = ws.Range(ws.Cells(startRow, startCol), _
                              ws.Cells(startRow + N_BINS, startCol + 1))
    Dim cht As ChartObject
    Set cht = ws.ChartObjects.Add(Left:=ws.Cells(startRow, startCol + 3).Left, _
                                   Top:=ws.Cells(startRow, startCol + 3).Top, _
                                   Width:=400, Height:=240)
    With cht.Chart
        .ChartType = xlColumnClustered
        .SetSourceData Source:=tableRange
        .HasTitle = True
        .ChartTitle.Text = "Histogram of " & sel.Address(False, False)
        .HasLegend = False
    End With
End Sub
