!define APP_NAME "AuraCtrl"
!define SERVICE_NAME "AuraCtrlService"
!define VERSION "1.0.0"
!define INSTALL_DIR "$PROGRAMFILES64\AuraCtrl"

Name "${APP_NAME} ${VERSION}"
OutFile "C:\Dev\AuraCtrl\publish\AuraCtrlSetup.exe"
InstallDir "${INSTALL_DIR}"
RequestExecutionLevel admin
ShowInstDetails show

!include "MUI2.nsh"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "French"

Section "PreInstall" SEC_PRE
  ; Arrêter le service s'il tourne
  nsExec::Exec 'sc.exe stop "${SERVICE_NAME}"'
  Sleep 2000
  ; Tuer le processus si encore actif
  nsExec::Exec 'taskkill /F /IM AuraCtrlService.exe'
  Sleep 1000
  ; Supprimer le service
  nsExec::Exec 'sc.exe delete "${SERVICE_NAME}"'
  Sleep 1000
  ; Supprimer l'ancien dossier si présent
  RMDir /r "$PROGRAMFILES64\AuraCtrl"
SectionEnd

Section "Service AuraCtrl" SEC_SERVICE
  SetOutPath "$INSTDIR"
  File "C:\Dev\AuraCtrl\publish\service\AuraCtrlService.exe"
  File "C:\Dev\AuraCtrl\publish\service\appsettings.json"
  SetOutPath "$INSTDIR\wwwroot"
  File "C:\Dev\AuraCtrl\publish\service\wwwroot\*.*"
  SetOutPath "$INSTDIR"

  ; Installer le service Windows
  nsExec::Exec 'sc.exe create "${SERVICE_NAME}" binPath= "$INSTDIR\AuraCtrlService.exe" start= auto DisplayName= "AuraCtrl Remote Service"'
  nsExec::Exec 'sc.exe description "${SERVICE_NAME}" "Service de controle distant AuraCtrl - acces LAN securise"'
  nsExec::Exec 'sc.exe start "${SERVICE_NAME}"'

  ; Règles firewall
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraCtrl"'
  nsExec::Exec 'netsh advfirewall firewall add rule name="AuraCtrl UDP" dir=in action=allow protocol=UDP localport=47200'
  nsExec::Exec 'netsh advfirewall firewall add rule name="AuraCtrl TCP" dir=in action=allow protocol=TCP localport=47201'

  ; Désinstalleur
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "AuraForge"
SectionEnd

Section "Uninstall"
  nsExec::Exec 'sc.exe stop "${SERVICE_NAME}"'
  Sleep 2000
  nsExec::Exec 'taskkill /F /IM AuraCtrlService.exe'
  Sleep 1000
  nsExec::Exec 'sc.exe delete "${SERVICE_NAME}"'
  Sleep 1000
  Delete "$INSTDIR\AuraCtrlService.exe"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\wwwroot\*.*"
  RMDir "$INSTDIR\wwwroot"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraCtrl UDP"'
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraCtrl TCP"'
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
SectionEnd



