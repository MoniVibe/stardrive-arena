;!include FileFunc.nsh

; This script is intended to be run with WorkingDir=C:\Projects\BlackBox
; Written by RedFox

;--------------------------------
; Project related helper defines
!define PRODUCT_PUBLISHER   "Mod by The BlackBox Team"
!define LAUNCHER            "StarDrive.exe"
; Jupiter writes to its own registry key (was "Software\StarDrive" through Mars 1.51).
; This keeps Jupiter installs partitioned from Mars: a 1.51 user running the Jupiter
; installer doesn't have their Mars-line registry overwritten, and the Mars-patch
; installer (which still reads Software\StarDrive) continues to find the Mars install.
!define REGPATH             "Software\StarDrivePlus64"
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "upload/${INSTALLER_NAME}_${PRODUCT_VERSION}.exe"

;Include Modern UI
!include "MUI2.nsh"
!include "Sections.nsh"
!include "LogicLib.nsh"
!addplugindir Installer

!define MUI_ABORTWARNING
!define MUI_ICON "blackbox.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP           "top.bmp" ; "Installer\upper_header.bmp" ; optional
!define MUI_WELCOMEFINISHPAGE_BITMAP     "left.bmp" ; "Installer\leftside_image.bmp"
!define MUI_COMPONENTSPAGE_SMALLDESC

;Pages
!define MUI_WELCOMEPAGE_TITLE        "BlackBox Installation Wizard"
!define MUI_WELCOMEPAGE_TEXT         "The wizard will guide you through the installation of $\r$\n${PRODUCT_NAME} ${PRODUCT_VERSION} onto your computer.$\r$\n$\r$\nClick Next to Continue"
!define MUI_DIRECTORYPAGE_TEXT_TOP   "Please verify that the Destination Folder is a clean installation folder. This is a stand-alone BETA version of StarDrive Plus"
!define MUI_FINISHPAGE_NOAUTOCLOSE
!define MUI_FINISHPAGE_RUN              "$INSTDIR\${LAUNCHER}"
!define MUI_FINISHPAGE_RUN_TEXT         "Run BlackBox ${PRODUCT_VERSION}"
!define MUI_FINISHPAGE_RUN_PARAMETERS   ""
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_LINK             "Visit our Discord for Announcements and Help"
!define MUI_FINISHPAGE_LINK_LOCATION    "https://discord.gg/dfvnfH4"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE         "LICENSE" ; Deploy/LICENSE text file
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

;Languages
!insertmacro MUI_LANGUAGE "English"

; Installer file INFO
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductName" "StarDrive BlackBox"
VIAddVersionKey /LANG=${LANG_ENGLISH} "CompanyName" "Codegremlins"
VIAddVersionKey /LANG=${LANG_ENGLISH} "LegalCopyright" "Copyright ZeroSum Games and Codegremlins"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileDescription" "StarDrive BlackBox Installer"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileVersion" "${PRODUCT_VERSION}"

;Var STEAMDIR ; found steam dir
Var PREVDIR ; previous mod install dir
Function .onInit
        ; Read prior install path from the Jupiter-line registry key only. We deliberately
        ; do NOT fall back to the Mars-line key (Software\StarDrive) or the Steam install
        ; path: cross-major fresh installs land at C:\Games\StarDrivePlus64, and the user
        ; can override via the directory page if they want upgrade-in-place over Mars or
        ; under their Steam library. See migration-plan-phase5.md §5.1.A step 3 for the
        ; rationale (clean major break + maintainer has no SteamPipe push access).
        ReadRegStr $PREVDIR HKLM ${REGPATH} InstallPath
        IfFileExists "$PREVDIR\${LAUNCHER}" 0 SetDefaultPath
        StrCpy $INSTDIR $PREVDIR ;; existing Jupiter install detected — re-use that path
        Goto Done
    SetDefaultPath:
        StrCpy $INSTDIR "C:\Games\StarDrivePlus64"
    Done:
FunctionEnd

SectionGroup /e "BlackBox"

    Section -Prerequisites
        ;Registry entries to figure out patch versions
        WriteRegStr HKLM ${REGPATH} "Author"       "${PRODUCT_PUBLISHER}"
        WriteRegStr HKLM ${REGPATH} "Version"      "${PRODUCT_VERSION}"
        WriteRegStr HKLM ${REGPATH} "InstallPath"  $INSTDIR
        DetailPrint "*** Compiled by RedFox ***"
        DetailPrint "${PRODUCT_NAME} ${PRODUCT_VERSION}"
        DetailPrint "Initializing Installation"
        DetailPrint "*************************"

        ;-----------------------------------------------------------------
        ; .NET 8 Desktop Runtime check (Jupiter line is net8.0-windows;
        ; Mars 1.51 ran on net48 which ships with Windows, so this is new).
        ;
        ; Major release: bundles + runs the .NET 8 Desktop Runtime installer
        ; as a prerequisite (gated by BUNDLE_RUNTIME defined in
        ; BlackBox-Jupiter.nsi — patch installers omit it since patch users
        ; came from a major install that already provisioned the runtime).
        ;
        ; Microsoft's apphost shows a "must install runtime" dialog when the
        ; runtime is missing — but as of .NET 8.0.26 + .NET 9 SDK build
        ; tooling, that dialog's Download link comes out broken (URL gets
        ; truncated to just "&gui=true"). Rather than fight Microsoft's
        ; broken UX, we bundle the runtime installer ourselves.
        ;
        ; Detection: probe the standard install location. .NET runtimes
        ; live at C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\<ver>\.
        ; If a 8.x subdir exists, skip the prereq installer.
        ; The runtime installer is idempotent — if a current or newer
        ; version is already present, it exits immediately.
        ;-----------------------------------------------------------------
        !ifdef BUNDLE_RUNTIME
        DetailPrint "Checking for .NET 8 Desktop Runtime..."
        IfFileExists "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\8.*\*" RuntimePresent RuntimeMissing

      RuntimeMissing:
        DetailPrint ".NET 8 Desktop Runtime not found — running bundled installer"
        SetOutPath "$PLUGINSDIR"
        File "prereq\windowsdesktop-runtime-8.0.26-win-x64.exe"
        ; /install /quiet shows a brief progress UI; /norestart prevents auto-reboot
        ; after install. /passive would show a full progress dialog; /quiet is the
        ; standard MS-recommended silent flag. UAC elevation prompt fires
        ; automatically because the runtime installer is admin-required.
        ExecWait '"$PLUGINSDIR\windowsdesktop-runtime-8.0.26-win-x64.exe" /install /quiet /norestart' $0
        Delete "$PLUGINSDIR\windowsdesktop-runtime-8.0.26-win-x64.exe"
        ${If} $0 = 0
            DetailPrint ".NET 8 Desktop Runtime installed successfully"
        ${ElseIf} $0 = 1602
            ; 1602 = User cancelled (clicked No on UAC or installer's prompt).
            ; Continue install — user can manually install runtime later.
            DetailPrint "WARNING: .NET 8 Desktop Runtime install cancelled by user"
            DetailPrint "BlackBox will not launch until the runtime is installed."
            DetailPrint "Get it from: https://dotnet.microsoft.com/download/dotnet/8.0"
            MessageBox MB_OK|MB_ICONEXCLAMATION \
                ".NET 8 Desktop Runtime install was cancelled.$\r$\n$\r$\nBlackBox needs this runtime to launch. You can install it later from:$\r$\n  https://dotnet.microsoft.com/download/dotnet/8.0$\r$\n  (pick: Windows Desktop Runtime x64)$\r$\n$\r$\nContinuing the BlackBox install — the game will not launch until the runtime is installed."
        ${Else}
            DetailPrint "WARNING: .NET 8 Desktop Runtime installer exited with code $0"
            DetailPrint "BlackBox may not launch — see https://dotnet.microsoft.com/download/dotnet/8.0"
        ${EndIf}
        Goto RuntimeDone

      RuntimePresent:
        DetailPrint ".NET 8 Desktop Runtime detected — skipping prerequisite installer"

      RuntimeDone:
        !endif ; BUNDLE_RUNTIME
    SectionEnd

    Section "-BlackBox" SecMain
        SectionIn RO
        DetailPrint "Unpacking ${PRODUCT_NAME} files"
        SetOutPath "$INSTDIR"
        !include "GeneratedFilesList.nsh"
    SectionEnd

    Section "-Finish Install" SECFinish
    SectionEnd

SectionGroupEnd

;--------------------------------
;Descriptions
LangString DESC_SecMain ${LANG_ENGLISH} "This installs the main contents of ${PRODUCT_NAME} ${PRODUCT_VERSION} on your computer."
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DESC_SecMain)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
