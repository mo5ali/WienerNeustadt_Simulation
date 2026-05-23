' run_analytics_post.vbs
'
' Post-processor for SimulationAnalytics.xlsx → SimulationAnalytics.xlsm.
' Runs after analytics.py has written the .xlsx. Drives a hidden Excel
' instance via COM automation to:
'   1. Open the .xlsx,
'   2. Save it as macro-enabled .xlsm (XlFileFormat 52 = xlOpenXMLWorkbookMacroEnabled),
'   3. Import every Module_*.bas file from Output/vba_modules/ into the
'      workbook's VBA project (replacing any module with the same name),
'   4. Save the .xlsm and delete the now-redundant .xlsx.
'
' Requirements:
'   - Windows + Microsoft Excel installed.
'   - Trust Center → Macro Settings → "Trust access to the VBA project
'     object model" must be CHECKED (one-time setting). Without it, the
'     .Import line fails with "Programmatic access to Visual Basic
'     Project is not trusted."
'
' Invoke directly:   cscript //Nologo Output\run_analytics_post.vbs
' Or let analytics.py call it automatically (it does on Windows).

Option Explicit

Const XL_OPEN_XML_MACRO_ENABLED = 52
Const MSO_AUTOMATION_SECURITY_DISABLE = 3   ' don't run macros on open

Dim fso, scriptDir, repoRoot, xlsxPath, xlsmPath, vbaDir
Set fso = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
repoRoot  = fso.GetParentFolderName(scriptDir)
xlsxPath  = fso.BuildPath(repoRoot, "bin\Debug\net8.0\OutputFiles\SimulationAnalytics.xlsx")
xlsmPath  = fso.BuildPath(repoRoot, "bin\Debug\net8.0\OutputFiles\SimulationAnalytics.xlsm")
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

' If a previous .xlsm exists (and isn't locked by an open Excel window),
' delete it so SaveAs doesn't prompt for overwrite.
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

' Save as macro-enabled .xlsm.
On Error Resume Next
wb.SaveAs xlsmPath, XL_OPEN_XML_MACRO_ENABLED
If Err.Number <> 0 Then
    WScript.Echo "[vbs] SaveAs .xlsm failed: " & Err.Description
    wb.Close False
    xl.Quit
    WScript.Quit 1
End If
On Error GoTo 0

' Import each Module_*.bas. If a module with the same name already exists
' in the VBA project, remove it first so the new copy lands cleanly.
Dim folder, file, modName, importedCount
Set folder = fso.GetFolder(vbaDir)
importedCount = 0
For Each file In folder.Files
    If LCase(fso.GetExtensionName(file.Name)) = "bas" Then
        modName = fso.GetBaseName(file.Name)
        On Error Resume Next
        wb.VBProject.VBComponents.Remove wb.VBProject.VBComponents(modName)
        Err.Clear  ' remove() throws if module doesn't exist — that's fine
        wb.VBProject.VBComponents.Import file.Path
        If Err.Number <> 0 Then
            WScript.Echo "[vbs] Import failed for " & file.Name & ": " & Err.Description
            WScript.Echo "[vbs] Likely cause: 'Trust access to the VBA project object model' is OFF"
            WScript.Echo "[vbs]   Fix: Excel → File → Options → Trust Center → Trust Center Settings"
            WScript.Echo "[vbs]        → Macro Settings → check 'Trust access to the VBA project object model'"
            wb.Close True
            xl.Quit
            WScript.Quit 2
        End If
        On Error GoTo 0
        importedCount = importedCount + 1
    End If
Next

' Save first so the freshly-imported VBA project is committed before we try
' to run a macro out of it — running straight after Import can occasionally
' fail to resolve the new procedure.
wb.Save

' Pre-build the scatter charts so they're baked into the saved .xlsm and
' the user sees them immediately on open (no Alt+F8 needed). Passing True
' runs the macro in silent mode -- without it, the macro's end-of-run MsgBox
' would block this hidden Excel instance forever and hang the sim run.
'
' We try the workbook-qualified macro name first ("Book.xlsm!Macro") because
' a bare name sometimes won't resolve under COM automation, then fall back
' to the bare name.
Dim ranPlot
ranPlot = False
On Error Resume Next
xl.Run "'" & wb.Name & "'!PlotActivitiesByTrack", True
If Err.Number = 0 Then
    ranPlot = True
Else
    WScript.Echo "[vbs] qualified Run failed (" & Err.Description & "); trying bare name"
    Err.Clear
    xl.Run "PlotActivitiesByTrack", True
    If Err.Number = 0 Then
        ranPlot = True
    Else
        WScript.Echo "[vbs] Warning: PlotActivitiesByTrack did not run: " & Err.Description
        Err.Clear
    End If
End If
On Error GoTo 0

wb.Save
wb.Close False
xl.Quit
Set wb = Nothing
Set xl = Nothing

' Delete the intermediate .xlsx so the user sees only the .xlsm.
On Error Resume Next
fso.DeleteFile xlsxPath, True
On Error GoTo 0

Dim plotMsg
If ranPlot Then
    plotMsg = " Scatter charts pre-built on 'Activity Scatter Charts'."
Else
    plotMsg = " (charts not pre-built — run PlotActivitiesByTrack manually via Alt+F8)."
End If
WScript.Echo "[vbs] Wrote " & xlsmPath & " with " & importedCount & _
             " macro module(s) imported." & plotMsg
WScript.Quit 0
