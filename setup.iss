; Script Inno Setup — installeur CopyClaude.
; Compilé par la CI avec : iscc /DMyAppVersion=x.y.z setup.iss
; Attend les binaires publiés dans .\publish (dotnet publish -o publish).

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{B7E1F3C2-9A4D-4E5B-8C6F-2D1A7E9B0C4D}
AppName=CopyClaude
AppVersion={#MyAppVersion}
AppPublisher=come
DefaultDirName={localappdata}\Programs\CopyClaude
DisableProgramGroupPage=yes
DisableDirPage=yes
; Installation par utilisateur, pas besoin d'admin.
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=CopyClaude-Setup-{#MyAppVersion}
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\CopyClaude.exe
Compression=lzma2
SolidCompression=yes
; Ferme l'app si elle tourne pendant la mise à jour.
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\CopyClaude"; Filename: "{app}\CopyClaude.exe"

[Run]
Filename: "{app}\CopyClaude.exe"; Description: "{cm:LaunchProgram,CopyClaude}"; Flags: nowait postinstall skipifsilent
