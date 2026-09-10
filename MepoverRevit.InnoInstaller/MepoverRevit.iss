; MepoverRevit installer (Inno Setup)
;
; Build once per machine:
;   1. Install Inno Setup 6 (free): https://jrsoftware.org/isdl.php
;   2. Build the Revit version(s) you want to package (Release config) via the .sln.
;      The IfcExport plugin is excluded from Release builds (still in Debug), so it
;      never ships in this installer.
;   3. Compile from a command prompt:
;        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" MepoverRevit.iss
;      Output MSI-equivalent (setup.exe) lands in .\Output\
;
; Each Revit version is packaged only if its bin\Release output exists, so this
; compiles fine even if a coworker has only built a subset of versions.
;
; Override the build configuration without editing this file:
;   ISCC.exe MepoverRevit.iss /DConfig=Debug

#ifndef Config
  #define Config "Release"
#endif

#define AppName "MepoverRevit"
#define AppVersion "0.1.0"
#define Manufacturer "MEPover"
#define AppId "{{d027893e-c704-41cb-aedf-c95befa90190}}"
#define SolutionDir "..\"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Manufacturer}
DefaultDirName={userappdata}\MepoverRevit
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=MepoverRevit-Setup-{#AppVersion}
LicenseFile=License.rtf
WizardImageFile=images\MEPover background image.png
WizardSmallImageFile=images\MEPover banner.png
UninstallDisplayName={#AppName} ({#AppVersion})
VersionInfoVersion={#AppVersion}

[Files]
; ---------- Revit 2021 ----------
#if DirExists(SolutionDir + "MepoverRevit.2021\bin\" + Config + "\2021")
Source: "{#SolutionDir}MepoverRevit.2021\bin\{#Config}\2021\MepoverRevit.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2021"; Flags: ignoreversion
Source: "{#SolutionDir}MepoverRevit.2021\bin\{#Config}\2021\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2021\Mepover"; Excludes: "*.addin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

; ---------- Revit 2022 ----------
#if DirExists(SolutionDir + "MepoverRevit.2022\bin\" + Config + "\2022")
Source: "{#SolutionDir}MepoverRevit.2022\bin\{#Config}\2022\MepoverRevit.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Flags: ignoreversion
Source: "{#SolutionDir}MepoverRevit.2022\bin\{#Config}\2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022\Mepover"; Excludes: "*.addin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

; ---------- Revit 2023 ----------
#if DirExists(SolutionDir + "MepoverRevit.2023\bin\" + Config + "\2023")
Source: "{#SolutionDir}MepoverRevit.2023\bin\{#Config}\2023\MepoverRevit.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Flags: ignoreversion
Source: "{#SolutionDir}MepoverRevit.2023\bin\{#Config}\2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023\Mepover"; Excludes: "*.addin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

; ---------- Revit 2024 ----------
#if DirExists(SolutionDir + "MepoverRevit.2024\bin\" + Config + "\2024")
Source: "{#SolutionDir}MepoverRevit.2024\bin\{#Config}\2024\MepoverRevit.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Flags: ignoreversion
Source: "{#SolutionDir}MepoverRevit.2024\bin\{#Config}\2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024\Mepover"; Excludes: "*.addin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

; ---------- Revit 2025 (net8.0-windows, x64) ----------
#if DirExists(SolutionDir + "MepoverRevit.2025\bin\" + Config + "\net8.0-windows")
Source: "{#SolutionDir}MepoverRevit.2025\bin\{#Config}\net8.0-windows\MepoverRevit.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Flags: ignoreversion
Source: "{#SolutionDir}MepoverRevit.2025\bin\{#Config}\net8.0-windows\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025\Mepover"; Excludes: "*.addin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2021\Mepover"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\MepoverRevit.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2022\Mepover"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\MepoverRevit.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2023\Mepover"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\MepoverRevit.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2024\Mepover"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\MepoverRevit.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\Mepover"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\MepoverRevit.addin"
