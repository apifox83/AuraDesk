!define APP_NAME "AuraDesk"
!define SERVICE_NAME "AuraDeskService"
!define VERSION "1.0.0"
!define INSTALL_DIR "$PROGRAMFILES64\AuraDesk"

Name "${APP_NAME} ${VERSION}"
OutFile "C:\Dev\AuraDesk\publish\AuraDeskSetup.exe"
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
  nsExec::Exec 'taskkill /F /IM AuraDeskService.exe'
  Sleep 1000
  ; Supprimer le service
  nsExec::Exec 'sc.exe delete "${SERVICE_NAME}"'
  Sleep 1000
  ; Supprimer l'ancien dossier si présent
  RMDir /r "$PROGRAMFILES64\AuraDesk"
SectionEnd

Section "Service AuraDesk" SEC_SERVICE
  SetOutPath "$INSTDIR"
  File "C:\Dev\AuraDesk\publish\service\AuraDeskService.exe"
  File "C:\Dev\AuraDesk\publish\service\appsettings.json"
  SetOutPath "$INSTDIR\wwwroot"
  File "C:\Dev\AuraDesk\publish\service\wwwroot\*.*"
  SetOutPath "$INSTDIR"

  ; Installer le service Windows
  nsExec::Exec 'sc.exe create "${SERVICE_NAME}" binPath= "$INSTDIR\AuraDeskService.exe" start= auto DisplayName= "AuraDesk Remote Service"'
  nsExec::Exec 'sc.exe description "${SERVICE_NAME}" "Service de controle distant AuraDesk - acces LAN securise"'
  nsExec::Exec 'sc.exe start "${SERVICE_NAME}"'

  ; Règles firewall
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraDesk"'
  nsExec::Exec 'netsh advfirewall firewall add rule name="AuraDesk UDP" dir=in action=allow protocol=UDP localport=47200'
  nsExec::Exec 'netsh advfirewall firewall add rule name="AuraDesk TCP" dir=in action=allow protocol=TCP localport=47201'

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
  nsExec::Exec 'taskkill /F /IM AuraDeskService.exe'
  Sleep 1000
  nsExec::Exec 'sc.exe delete "${SERVICE_NAME}"'
  Sleep 1000
  Delete "$INSTDIR\AuraDeskService.exe"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\wwwroot\*.*"
  RMDir "$INSTDIR\wwwroot"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraDesk UDP"'
  nsExec::Exec 'netsh advfirewall firewall delete rule name="AuraDesk TCP"'
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
SectionEnd



