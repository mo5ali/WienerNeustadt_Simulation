Attribute VB_Name = "Module_HighlightOutliers"
Option Explicit

' Highlights cells in the current selection that are more than 2 sigma above
' or below the mean. High-side fills red, low-side fills blue. Run again to
' refresh after sorting / filtering.
Sub HighlightOutliers()
    Dim sel As Range
    Set sel = Selection
    If sel Is Nothing Then Exit Sub

    Dim n As Long, sum As Double, sumSq As Double
    Dim c As Range
    For Each c In sel.Cells
        If IsNumeric(c.Value) And Not IsEmpty(c.Value) And Len(c.Value) > 0 Then
            n = n + 1
            sum = sum + CDbl(c.Value)
        End If
    Next c
    If n < 3 Then
        MsgBox "Need at least 3 numeric cells.", vbInformation, "HighlightOutliers"
        Exit Sub
    End If
    Dim mean As Double: mean = sum / n
    For Each c In sel.Cells
        If IsNumeric(c.Value) And Not IsEmpty(c.Value) And Len(c.Value) > 0 Then
            sumSq = sumSq + (CDbl(c.Value) - mean) ^ 2
        End If
    Next c
    Dim sigma As Double: sigma = Sqr(sumSq / n)
    Dim hiCut As Double: hiCut = mean + 2 * sigma
    Dim loCut As Double: loCut = mean - 2 * sigma

    Dim hiCount As Long, loCount As Long
    For Each c In sel.Cells
        c.Interior.Pattern = xlNone
        If IsNumeric(c.Value) And Not IsEmpty(c.Value) And Len(c.Value) > 0 Then
            If CDbl(c.Value) > hiCut Then
                c.Interior.Color = RGB(255, 199, 199)
                hiCount = hiCount + 1
            ElseIf CDbl(c.Value) < loCut Then
                c.Interior.Color = RGB(199, 215, 255)
                loCount = loCount + 1
            End If
        End If
    Next c
    MsgBox "Mean: " & Format(mean, "#,##0.00") & vbCrLf & _
           "Sigma: " & Format(sigma, "#,##0.00") & vbCrLf & _
           "Above mean+2sigma (" & Format(hiCut, "#,##0.00") & "): " & hiCount & vbCrLf & _
           "Below mean-2sigma (" & Format(loCut, "#,##0.00") & "): " & loCount, _
           vbInformation, "HighlightOutliers"
End Sub
