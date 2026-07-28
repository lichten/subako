; Subako の Inno Setup スクリプト (docs/release-plan.md §4-2)。
; 前提: dotnet publish -c Release -p:PublishProfile=win-x64 済みであること。
; ビルド: iscc /DMyAppVersion=X.Y.Z installer\subako.iss
; (GitHub Actions の release ワークフローが自動実行する。ローカルでは Inno Setup 6 が必要)

#define MyAppName "Subako"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppExeName "Subako.exe"
#define PublishDir "..\viewer\TweetViewer\bin\publish\win-x64"

[Setup]
; AppId は Subako 固有の識別子。変更するとアップグレードが別アプリ扱いになるため固定
AppId={{FF228F35-75A2-49F4-B9E0-BDF7DC2B1806}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Lichten
AppPublisherURL=https://github.com/lichten/TestTwitterAPIIO
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=out
OutputBaseFilename=subako-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
