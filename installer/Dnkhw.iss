; 电脑开荒的当前用户安装包。由 package.ps1 调用 ISCC 编译。
; 该脚本刻意采用每用户安装，不需要管理员权限，也不向 Program Files 写入文件。

#ifndef MyAppVersion
  #error MyAppVersion must be supplied by package.ps1
#endif
#ifndef MyAppSource
  #error MyAppSource must be supplied by package.ps1
#endif
#ifndef MyAppOutput
  #error MyAppOutput must be supplied by package.ps1
#endif
#ifndef MyAppBaseName
  #error MyAppBaseName must be supplied by package.ps1
#endif

#define MyAppName "电脑开荒"
#define MyAppExeName "Dnkhw.exe"

[Setup]
AppId={{A7FBC00A-5F0F-44BB-8AA2-0CEB5ED6295A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=尘风一枚
AppPublisherURL=https://www.cyhaoka.vip
AppSupportURL=https://www.cyhaoka.vip
AppUpdatesURL=https://app.cyhaoka.vip/#download
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#MyAppOutput}
OutputBaseFilename={#MyAppBaseName}
SetupIconFile=..\src\Dnkhw\Assets\Icons\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#MyAppSource}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
