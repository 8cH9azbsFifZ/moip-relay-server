; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "Morse Code Tools"
#define MyAppVerName "Morse Code Tools 2.0"
#define MyAppPublisher "Robert B. Denny"
#define MyAppURL "https://sourceforge.net/projects/morse-rss-news/"
#define MyAppExeName1 "MorseNews.exe"
#define MyAppExeName2 "MorseKeyer.exe"

[CustomMessages]
dotnetmissing=Morse Tools needs Microsoft .NET Framework 3.5. Would you like to download and install it now?
get35=Be sure to download and install .NET Framework 3.5, not 4.0 (ignore the "newer version" icon, the two versions are separate).
[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{735ACD67-720C-4031-B534-BC81C87CB826}
AppName={#MyAppName}
AppVerName={#MyAppVerName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=D:\dev\misc\MorseTools\mastersetup
OutputBaseFilename=MorseTools20Setup
SetupIconFile=D:\dev\misc\MorseTools\locrss\Resources\AppIcon.ico
WizardImageFile=SetupImage.bmp
Compression=lzma
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop icons"; GroupDescription: "{cm:AdditionalIcons}";

[Files]
; MorseNews and common assemblies
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\MorseNews.exe"; DestDir: "{app}";
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\MorseNews.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\dev\misc\MorseTools\locrss\bin\Release\*.dll"; DestDir: "{app}";
Source: "D:\dev\misc\MorseTools\locrss\doc\*"; DestDir: "{app}\doc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "D:\dev\misc\MorseTools\locrss\Setup\DirectX Assemblies\*"; DestDir: "{app}";
; Additional files for MorseKeyer
Source: "D:\dev\misc\MorseTools\keyer\bin\Release\MorseKeyer.exe"; DestDir: "{app}";
Source: "D:\dev\misc\MorseTools\keyer\bin\Release\MorseKeyer.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\dev\misc\MorseTools\keyer\bin\Release\DC3.Morse.IambicKeyer.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\dev\misc\MorseTools\keyer\doc\*"; DestDir: "{app}\doc"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Morse News"; Filename: "{app}\MorseNews.exe"
Name: "{group}\Morse News Help"; Filename: "{app}\doc\news.html"; IconFilename: "{app}\doc\Help.ico"
Name: "{group}\Morse Keyer"; Filename: "{app}\MorseKeyer.exe"
Name: "{group}\Morse Keyer Help"; Filename: "{app}\doc\keyer.html"; IconFilename: "{app}\doc\Help.ico"
Name: "{commondesktop}\Morse News"; Filename: "{app}\MorseNews.exe"; Tasks: desktopicon
Name: "{commondesktop}\Morse Keyer"; Filename: "{app}\MorseKeyer.exe"; Tasks: desktopicon

[InstallDelete]
Type: files; name: "{app}\RSSMorse.exe"
Type: files; Name: "{app}\doc\rssmorse.html"
Type: files; Name: "{group}\RSS Morse.lnk"
Type: files; Name: "{commondesktop}\RSS Morse.lnk"
Type: files; Name: "{group}\RSS Morse Help.lnk"

[Code]
function InitializeSetup(): Boolean;
var
    ErrorCode: Integer;
    netFrameWorkInstalled : Boolean;
    isInstalled: Cardinal;
begin
  result := true;
 
    // Check for the .Net 3.5 framework
  isInstalled := 0;
  netFrameworkInstalled := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5', 'Install', isInstalled);
  if ((netFrameworkInstalled)  and (isInstalled <> 1)) then netFrameworkInstalled := false;
 
  if netFrameworkInstalled = false then
  begin
    if (MsgBox(ExpandConstant('{cm:dotnetmissing}'),
        mbConfirmation, MB_YESNO) = idYes) then
    begin
      MsgBox(ExpandConstant('{cm:get35}'),
            mbInformation, MB_OK);
      ShellExec('open',
          'http://www.microsoft.com/downloads/details.aspx?FamilyID=333325fd-ae52-4e35-b531-508d977d32a6&DisplayLang=en',
          '','',SW_SHOWNORMAL,ewNoWait,ErrorCode);
    end;
    result := false;
  end;
 
end;