; MicroApp installer (NSIS)
;
; Build:
;   makensis -DSRC=../../bin/Release -DVERSION=4.2.2 MicroApp.nsi
;
; SRC is the folder holding the built app (MicroApp.exe and its DLLs). The
; installer asks, on its last page, whether MicroApp should start with Windows;
; ticking it drops a shortcut in the all-users Startup folder.
;
; Silent install:  MicroApp-<version>-setup.exe /S [/STARTUP] [/D=C:\path]
; Silent uninstall: "%ProgramFiles%\MicroApp\Uninstall.exe" /S

Unicode true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef VERSION
  !define VERSION "4.2.2"
!endif
!ifndef SRC
  !define SRC "..\..\bin\Release"
!endif

!define APPNAME    "MicroApp"
!define PUBLISHER  "Samsur Rahman Mahi"
!define HOMEPAGE   "https://github.com/Mahi-BD/MicroApp"
!define UNINSTKEY  "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"

; Two flavours from one script:
;   default   - all users, Program Files, needs admin
;   -DPERUSER - just me, %LOCALAPPDATA%\Programs, no admin needed
!ifdef PERUSER
  !define REGROOT "HKCU"
  !define CTX "current"
  !define FLAVOUR "-peruser"
!else
  !define REGROOT "HKLM"
  !define CTX "all"
  !define FLAVOUR ""
!endif

Name "${APPNAME} ${VERSION}"
OutFile "MicroApp-${VERSION}${FLAVOUR}-setup.exe"
!ifdef PERUSER
  InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
  RequestExecutionLevel user
!else
  InstallDir "$PROGRAMFILES64\${APPNAME}"
  RequestExecutionLevel admin
!endif
InstallDirRegKey ${REGROOT} "Software\${APPNAME}" "InstallDir"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName"     "${APPNAME}"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "FileVersion"     "${VERSION}.0"
VIAddVersionKey "FileDescription" "${APPNAME} setup"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "BSD 3-Clause"

;--------------------------------- interface
!define MUI_ICON   "..\..\Resources\AppIcon.ico"
!define MUI_UNICON "..\..\Resources\AppIcon.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_HEADERIMAGE_RIGHT

!define MUI_WELCOMEPAGE_TITLE "${APPNAME} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Types the clipboard as real keystrokes, reads text off the screen with OCR, captures screenshots and records GIFs.$\r$\n$\r$\nMicroApp lives in the notification area; right-click its icon for everything.$\r$\n$\r$\nClick Next to continue."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; the last page carries the two questions: start now, and start with Windows
!define MUI_FINISHPAGE_RUN "$INSTDIR\MicroApp.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run MicroApp now"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Run MicroApp when Windows starts"
!define MUI_FINISHPAGE_SHOWREADME_CHECKED
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION EnableStartup
!define MUI_FINISHPAGE_LINK "MicroApp on GitHub"
!define MUI_FINISHPAGE_LINK_LOCATION "${HOMEPAGE}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

;--------------------------------- install
Section "MicroApp (required)" SecApp
  SectionIn RO
  SetShellVarContext ${CTX}
  SetOutPath "$INSTDIR"

  ; stop a running copy so the files are not locked
  nsExec::Exec 'taskkill /IM MicroApp.exe /F'
  Pop $0
  Sleep 500

  File "${SRC}\MicroApp.exe"
  File "${SRC}\MicroApp.exe.config"
  File "${SRC}\AutoItX3.Assembly.dll"
  File "${SRC}\AutoItX3.dll"
  File "${SRC}\AutoItX3_x64.dll"
  File "${SRC}\Gma.System.MouseKeyHook.dll"
  File "${SRC}\AutoIt_License.html"
  File "..\..\LICENSE"
  File "..\..\NOTICE.md"
  File "..\..\README.md"
  File "..\..\HELP.md"
  File "..\..\CHANGELOG.md"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\MicroApp.exe"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\Help.lnk" "$INSTDIR\HELP.md"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr ${REGROOT} "Software\${APPNAME}" "InstallDir" "$INSTDIR"
  WriteRegStr ${REGROOT} "Software\${APPNAME}" "Version" "${VERSION}"

  WriteRegStr ${REGROOT} "${UNINSTKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "DisplayIcon"     "$INSTDIR\MicroApp.exe"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "URLInfoAbout"    "${HOMEPAGE}"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr ${REGROOT} "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD ${REGROOT} "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD ${REGROOT} "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD ${REGROOT} "${UNINSTKEY}" "EstimatedSize" "$0"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Desktop shortcut" SecDesktop
  SetShellVarContext ${CTX}
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\MicroApp.exe"
SectionEnd

LangString DESC_SecApp     ${LANG_ENGLISH} "The MicroApp program files and Start Menu shortcuts."
LangString DESC_SecDesktop ${LANG_ENGLISH} "Put a MicroApp shortcut on the desktop."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp}     $(DESC_SecApp)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

;--------------------------------- start with Windows
; Called when the finish-page checkbox is ticked. A Startup-folder shortcut is
; used rather than an HKCU Run value: the installer runs elevated, so HKCU would
; land in the administrator's hive instead of the user's.
Function EnableStartup
  SetShellVarContext ${CTX}
  CreateShortCut "$SMSTARTUP\${APPNAME}.lnk" "$INSTDIR\MicroApp.exe"
  WriteRegDWORD ${REGROOT} "Software\${APPNAME}" "RunAtStartup" 1
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "MicroApp needs 64-bit Windows 10 or 11."
    Abort
  ${EndIf}
  ; silent installs opt in with /STARTUP
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/STARTUP" $R1
  ${IfNot} ${Errors}
    StrCpy $R9 "1"
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  ${If} $R9 == "1"
    Call EnableStartup
  ${EndIf}
FunctionEnd

;--------------------------------- uninstall
Section "Uninstall"
  SetShellVarContext ${CTX}

  nsExec::Exec 'taskkill /IM MicroApp.exe /F'
  Pop $0
  Sleep 500

  Delete "$INSTDIR\MicroApp.exe"
  Delete "$INSTDIR\MicroApp.exe.config"
  Delete "$INSTDIR\AutoItX3.Assembly.dll"
  Delete "$INSTDIR\AutoItX3.dll"
  Delete "$INSTDIR\AutoItX3_x64.dll"
  Delete "$INSTDIR\Gma.System.MouseKeyHook.dll"
  Delete "$INSTDIR\AutoIt_License.html"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\NOTICE.md"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\HELP.md"
  Delete "$INSTDIR\CHANGELOG.md"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Help.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMSTARTUP\${APPNAME}.lnk"

  DeleteRegKey ${REGROOT} "${UNINSTKEY}"
  DeleteRegKey ${REGROOT} "Software\${APPNAME}"
SectionEnd
