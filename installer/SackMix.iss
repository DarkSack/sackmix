; Wizard de instalación de SackMix (Inno Setup 6). Lo compila publish.ps1:
;   ISCC.exe installer\SackMix.iss   ->  dist\SackMix-Setup-<versión>.exe
; Por usuario y sin admin. Instalar encima actualiza y conserva los ajustes (%LOCALAPPDATA%\SackMix).

#define AppExe "SackMix.exe"
#define SourceExe AddBackslash(SourcePath) + "..\dist\" + AppExe
#define FullVersion GetVersionNumbersString(SourceExe)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
AppId=SackMix
AppName=SackMix
AppVersion={#AppVersion}
AppPublisher=Sackito
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\SackMix
DefaultGroupName=SackMix
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Condiciones de uso y privacidad: hay que aceptarlas para seguir.
LicenseFile=LICENCIA.txt
WizardStyle=modern
; Solo pregunta el idioma si Windows no está en español ni en inglés.
ShowLanguageDialog=auto
SetupIconFile=..\src\SackMix\Assets\sackmix.ico
UninstallDisplayName=SackMix
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=SackMix-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Cierra SackMix (aunque esté en la bandeja) antes de reemplazar el exe; al final se vuelve a abrir desde [Run].
CloseApplications=force
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Iniciar SackMix con Windows (en la bandeja)"; GroupDescription: "Opciones:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SackMix"; Filename: "{app}\{#AppExe}"; Comment: "Mezclador de audio por programa"
Name: "{autodesktop}\SackMix"; Filename: "{app}\{#AppExe}"; Comment: "Mezclador de audio por programa"; Tasks: desktopicon

[Registry]
; --autostart: arranca oculto en la bandeja (App.xaml.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SackMix"; ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,SackMix}"; Flags: nowait postinstall skipifsilent

[Code]
// Al desinstalar Windows no cierra la app como al instalar, y con el exe abierto no se puede borrar.
function InitializeUninstall(): Boolean;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
  Result := True;
end;
