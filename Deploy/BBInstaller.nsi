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

; UAC up-front. The script writes HKLM keys (line 88-90 in this file) which
; require admin regardless of install location, and Windows installer-detection
; heuristics (filename contains "install"/"patch") would prompt anyway. Setting
; this explicitly elevates via the embedded manifest so the prompt is
; predictable instead of triggered by EXE-name pattern matching.
RequestExecutionLevel admin

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

Var STEAMDIR ; Steam install of StarDrive (AppID 220660), if any
Var PREVDIR  ; previous Jupiter install dir
Function .onInit
        ; ----- Existing Jupiter install? Re-use that path. -----
        ; Read prior install path from the Jupiter-line registry key only. We deliberately
        ; do NOT fall back to the Mars-line key (Software\StarDrive) — the Mars-patch
        ; installer keeps reading Software\StarDrive, so we want them partitioned.
        ; See migration-plan-phase5.md §5.1.A step 3 for the rationale.
        ReadRegStr $PREVDIR HKLM ${REGPATH} InstallPath
        IfFileExists "$PREVDIR\${LAUNCHER}" UseJupiterPath 0
        Goto NoJupiter
    UseJupiterPath:
        StrCpy $INSTDIR $PREVDIR
        Goto Done

    NoJupiter:
        ; Patch installers are incremental deltas — they ship only files that changed
        ; since the major's Release.txt baseline, so overlaying one on a vanilla/Steam
        ; folder leaves the install in a broken half-Jupiter half-vanilla state. Refuse
        ; to run if no existing Jupiter major install was found.
        !ifdef IS_PATCH
            MessageBox MB_OK|MB_ICONSTOP \
                "This is a patch installer for an existing BlackBox Jupiter install, but no Jupiter install was found.$\r$\n$\r$\nPlease install the BlackBox Jupiter major release first, then run this patch.$\r$\n$\r$\nDownload from: https://github.com/TeamStarDrive/StarDrive/releases"
            Abort
        !endif

        ; ----- No Jupiter install. Probe for a Steam install of StarDrive (AppID 220660). -----
        ; Steam writes a per-app uninstall registry entry whose InstallLocation
        ; points at the actual game folder regardless of which library drive
        ; it's on. Far simpler than parsing libraryfolders.vdf.
        ;
        ; Steam's per-app Uninstall entry lives in the 64-bit registry view
        ; (Steam writes there explicitly via KEY_WOW64_64KEY) — confirmed
        ; locally: the key is at HKLM\SOFTWARE\Microsoft\...\Steam App 220660
        ; in the 64-bit view, missing under WOW6432Node. NSIS is 32-bit so
        ; HKLM\SOFTWARE\... reads are redirected to WOW6432Node by default.
        ; SetRegView 64 around the read accesses the 64-bit view directly;
        ; SetRegView 32 restores the redirector for the Section's HKLM writes
        ; below, which need to land in the 32-bit view to match Mars-line
        ; convention and the Jupiter REGPATH lookup at the top of .onInit.
        ;
        ; Caveats the user must accept (in the MessageBox below):
        ;   - Disable Steam auto-update for StarDrive — Steam's depot manifest
        ;     is vanilla StarDrive 15b (the original 2013 publisher build, NOT
        ;     BlackBox/Mars), so any Steam sync overwrites Jupiter back to
        ;     stock 15b without warning.
        ;   - Never run "Verify Integrity of Game Files" — same depot:
        ;     restores vanilla 15b. BlackBox isn't on Steam at all.
        ;   - Steam still reports the title as "StarDrive" in profile/playtime
        ;     (Steam's DisplayName for AppID 220660 is just "StarDrive");
        ;     achievements/cloud-save aren't wired (Steamworks DLLs not bundled
        ;     in 1.60 — see Phase 4 §4.9). Playtime tracking still works because
        ;     it's process-lifetime monitoring on Steam's side.
        SetRegView 64
        ReadRegStr $STEAMDIR HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 220660" "InstallLocation"
        SetRegView 32
        StrCmp $STEAMDIR "" SetDefaultPath
        IfFileExists "$STEAMDIR\${LAUNCHER}" 0 SetDefaultPath
        MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 \
            "Steam install of StarDrive detected at:$\r$\n$STEAMDIR$\r$\n$\r$\nInstall Jupiter 1.60 there so Steam keeps tracking your playtime?$\r$\n$\r$\nIMPORTANT — Steam's StarDrive depot is vanilla 15b (the original 2013 build, NOT BlackBox). To avoid Steam wiping Jupiter back to stock 15b:$\r$\n  - Disable auto-updates for StarDrive in Steam (Properties -> Updates)$\r$\n  - Never run 'Verify Integrity of Game Files' (it restores vanilla 15b)$\r$\n  - Steam will still report this title as 'StarDrive' in your profile / playtime — Steam doesn't know about BlackBox/Jupiter." \
            IDYES UseSteamPath IDNO SetDefaultPath
    UseSteamPath:
        StrCpy $INSTDIR $STEAMDIR
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
