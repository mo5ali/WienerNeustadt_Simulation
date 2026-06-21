' run_analytics_post.vbs
'
' Post-processor for SimulationAnalytics.xlsx -> SimulationAnalytics.xlsm.
' Runs after analytics.py has written the .xlsx. Drives a hidden Excel
' instance via COM automation to:
'   1. Open the .xlsx,
'   2. Save it as macro-enabled .xlsm (XlFileFormat 52),
'   3. Import every Module_*.bas from Output/vba_modules/ (replacing same-named),
'   4. Pre-build the chart sheets (PlotAllSilent),
'   5. Save the .xlsm and delete the now-redundant .xlsx.
'
' Requirements:
'   - Windows + Microsoft Excel installed.
'   - Trust Center -> Macro Settings -> "Trust access to the VBA project
'     object model" must be CHECKED (one-time setting).
'
' Invoke directly:   cscript //Nologo Output\run_analytics_post.vbs
' Or let analytics.py call it automatically (it does on Windows).

Option Explicit

Const XL_OPEN_XML_MACRO_ENABLED = 52
Const MSO_AUTOMATION_SECURITY_DISABLE = 3

Dim fso, scriptDir, repoRoot, xlsxPath, xlsmPath, vbaDir
Set fso = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
repoRoot  = fso.GetParentFolderName(scriptDir)
' Workbook lives in the Output\ folder (= scriptDir) so it is tracked in
' git and visible in the repo, matching OUT_PATH in analytics.py. (Was
' previously the git-ignored bin\Debug\net8.0\OutputFiles build tree.)
xlsxPath  = fso.BuildPath(scriptDir, "SimulationAnalytics.xlsx")
xlsmPath  = fso.BuildPath(scriptDir, "SimulationAnalytics.xlsm")
vbaDir    = fso.BuildPath(scriptDir, "vba_modules")

If Not fso.FileExists(xlsxPath) Then
    WScript.Echo "[vbs] Source .xlsx not found: " & xlsxPath
    WScript.Echo "[vbs] Run analytics.py first."
    WScript.Quit 1
End If
If Not fso.FolderExists(vbaDir) Then
    WScript.Echo "[vbs] vba_modules folder not found: " & vbaDir
    WScript.Quit 1
End If

Dim xl
On Error Resume Next
Set xl = CreateObject("Excel.Application")
If Err.Number <> 0 Then
    WScript.Echo "[vbs] Could not start Excel COM (is Excel installed?): " & Err.Description
    WScript.Quit 1
End If
On Error GoTo 0

xl.Visible = False
xl.DisplayAlerts = False
xl.AutomationSecurity = MSO_AUTOMATION_SECURITY_DISABLE
' Speed up chart building via COM: no redraw, no recalc, no events.
xl.ScreenUpdating = False
xl.EnableEvents = False
On Error Resume Next
xl.Calculation = -4135   ' xlCalculationManual
On Error GoTo 0

' Overwrite any previous .xlsm (must not be open in Excel).
If fso.FileExists(xlsmPath) Then
    On Error Resume Next
    fso.DeleteFile xlsmPath, True
    If Err.Number <> 0 Then
        WScript.Echo "[vbs] Could not overwrite existing .xlsm (close it in Excel first): " & Err.Description
        xl.Quit
        WScript.Quit 1
    End If
    On Error GoTo 0
End If

Dim wb
On Error Resume Next
Set wb = xl.Workbooks.Open(xlsxPath)
If Err.Number <> 0 Then
    WScript.Echo "[vbs] Open failed: " & Err.Description
    xl.Quit
    WScript.Quit 1
End If
On Error GoTo 0

On Error Resume Next
wb.SaveAs xlsmPath, XL_OPEN_XML_MACRO_ENABLED
If Err.Number <> 0 Then
    WScript.Echo "[vbs] SaveAs .xlsm failed: " & Err.Description
    wb.Close False
    xl.Quit
    WScript.Quit 1
End If
On Error GoTo 0

' Import each Module_*.bas (remove same-named module first).
Dim folder, file, modName, importedCount
Set folder = fso.GetFolder(vbaDir)
importedCount = 0
For Each file In folder.Files
    If LCase(fso.GetExtensionName(file.Name)) = "bas" Then
        modName = fso.GetBaseName(file.Name)
        On Error Resume Next
        wb.VBProject.VBComponents.Remove wb.VBProject.VBComponents(modName)
        Err.Clear
        wb.VBProject.VBComponents.Import file.Path
        If Err.Number <> 0 Then
            WScript.Echo "[vbs] Import failed for " & file.Name & ": " & Err.Description
            WScript.Echo "[vbs] Likely cause: 'Trust access to the VBA project object model' is OFF"
            WScript.Echo "[vbs]   Fix: Excel -> File -> Options -> Trust Center -> Trust Center Settings"
            WScript.Echo "[vbs]        -> Macro Settings -> check 'Trust access to the VBA project object model'"
            wb.Close True
            xl.Quit
            WScript.Quit 2
        End If
        On Error GoTo 0
        importedCount = importedCount + 1
    End If
Next

' Commit the imported project before running a macro out of it.
wb.Save

' Pre-build BOTH chart sheets via the parameterless silent driver.
Dim nPlotted
nPlotted = 0
If RunMacro(xl, wb, "PlotAllSilent") Then nPlotted = 2

wb.Save
wb.Close False
xl.Quit
Set wb = Nothing
Set xl = Nothing

' Delete the intermediate .xlsx so only the .xlsm remains.
On Error Resume Next
fso.DeleteFile xlsxPath, True
On Error GoTo 0

Dim plotMsg
If nPlotted > 0 Then
    plotMsg = " Pre-built " & nPlotted & " chart sheet(s)."
Else
    plotMsg = " (charts not pre-built -- run the Plot* macros manually via Alt+F8)."
End If
WScript.Echo "[vbs] Wrote " & xlsmPath & " with " & importedCount & _
             " macro module(s) imported." & plotMsg
WScript.Quit 0

' --- helper ----------------------------------------------------------------
' Runs a macro silently. Tries the workbook-qualified name first, then the
' bare name. Returns True on success.
Function RunMacro(xlApp, book, macroName)
    RunMacro = False
    On Error Resume Next
    xlApp.Run "'" & book.Name & "'!" & macroName
    If Err.Number = 0 Then
        RunMacro = True
    Else
        Err.Clear
        xlApp.Run macroName
        If Err.Number = 0 Then
            RunMacro = True
        Else
            WScript.Echo "[vbs] Warning: " & macroName & " did not run: " & Err.Description
            Err.Clear
        End If
    End If
    On Error GoTo 0
End Function
