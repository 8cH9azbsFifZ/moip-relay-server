; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "RSS Morse"
#define MyAppVerName "RSS Morse 1.4"
#define MyAppPublisher "Robert B. Denny"
#define MyAppURL "https://sourceforge.net/projects/morse-rss-news/"
#define MyAppExeName "RSSMorse.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{CAAD9283-ECBB-4809-B176-5EAAB4745144}
AppName={#MyAppName}
AppVerName={#MyAppVerName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=D:\dev\misc\MorseTools\locrss\Setup
OutputBaseFilename=RSSMorse14Setup
SetupIconFile=D:\dev\misc\MorseTools\locrss\Resources\AppIcon.ico
WizardImageFile=SetupImage.bmp
Compression=lzma
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\RSSMorse.exe"; DestDir: "{app}";
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\RSSMorse.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\DC3.MorseEncode.dll"; DestDir: "{app}";
Source: "D:\dev\misc\MorseTools\locrss\doc\*"; DestDir: "{app}\doc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "D:\dev\misc\MorseTools\locrss\Setup\DirectX Assemblies\*"; DestDir: "{app}";

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\RSS Morse Help"; Filename: "{app}\doc\index.html"; IconFilename: "{app}\doc\Help.ico"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

