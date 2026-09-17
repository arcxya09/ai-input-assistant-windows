#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
[Setup]
AppId={{89D271AA-5ED4-44F1-A53F-8E42E65BA680}
AppName=AI Input Assistant
AppVersion={#AppVersion}
AppPublisher=arcxya09
DefaultDirName={localappdata}\Programs\AiInputAssistant
DefaultGroupName=AI Input Assistant
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=AiInputAssistant-{#AppVersion}-Setup-x64
SetupIconFile=..\src\AiInput.App\Assets\App.ico
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\AiInputAssistant.exe
[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\AI Input Assistant"; Filename: "{app}\AiInputAssistant.exe"
Name: "{autodesktop}\AI Input Assistant"; Filename: "{app}\AiInputAssistant.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Run]
Filename: "{app}\AiInputAssistant.exe"; Description: "Launch AI Input Assistant"; Flags: nowait postinstall skipifsilent
