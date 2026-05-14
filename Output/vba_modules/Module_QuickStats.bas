Attribute VB_Name = "Module_QuickStats"
Option Explicit

' Pops a message box with summary statistics for the current selection.
' Skips empty / non-numeric cells.
Sub QuickStats()
    Dim sel As Range
    Set sel = Selection
    If sel Is Nothing Then Exit Sub

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

    If n = 0 Then
        MsgBox "No numeric cells in selection.", vbInformation, "QuickStats"
        Exit Sub
    End If
    ReDim Preserve vals(1 To n)

    Dim sum As Double, sumSq As Double
    Dim minV As Double: minV = vals(1)
    Dim maxV As Double: maxV = vals(1)
    Dim i As Long
    For i = 1 To n
        sum = sum + vals(i)
        If vals(i) < minV Then minV = vals(i)
        If vals(i) > maxV Then maxV = vals(i)
    Next i
    Dim mean As Double: mean = sum / n
    For i = 1 To n
        sumSq = sumSq + (vals(i) - mean) ^ 2
    Next i
    Dim stdev As Double: stdev = Sqr(sumSq / n) ' population

    ' Median (sort copy)
    Dim sorted() As Double
    sorted = vals
    QuickSortDouble sorted, 1, n
    Dim median As Double
    If (n Mod 2) = 1 Then
        median = sorted((n + 1) \ 2)
    Else
        median = (sorted(n \ 2) + sorted(n \ 2 + 1)) / 2
    End If

    Dim msg As String
    msg = "Count : " & n & vbCrLf & _
          "Mean  : " & Format(mean, "#,##0.00") & vbCrLf & _
          "Median: " & Format(median, "#,##0.00") & vbCrLf & _
          "Min   : " & Format(minV, "#,##0.00") & vbCrLf & _
          "Max   : " & Format(maxV, "#,##0.00") & vbCrLf & _
          "Stdev : " & Format(stdev, "#,##0.00") & " (population)"
    MsgBox msg, vbInformation, "QuickStats — " & sel.Address(False, False)
End Sub

Private Sub QuickSortDouble(arr() As Double, lo As Long, hi As Long)
    Dim i As Long, j As Long, p As Double, t As Double
    i = lo: j = hi
    p = arr((lo + hi) \ 2)
    Do
        Do While arr(i) < p: i = i + 1: Loop
        Do While arr(j) > p: j = j - 1: Loop
        If i <= j Then
            t = arr(i): arr(i) = arr(j): arr(j) = t
            i = i + 1: j = j - 1
        End If
    Loop While i <= j
    If lo < j Then QuickSortDouble arr, lo, j
    If i < hi Then QuickSortDouble arr, i, hi
End Sub
