; Inno Setup script for the Windows installer of 바꿔 Bakkwo.
; Built by .github/workflows/release.yml:  iscc /DAppVersion=0.6.0 build\windows\bakkwo.iss
; Expects the published app in publish\Bakkwo-win-x64\.
; Installs per user by default (no admin prompt); offers all users when run as admin.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8C1F4E52-6B2D-4F3A-9C1E-5B7A2D9E4F10}
AppName=바꿔 Bakkwo
AppVersion={#AppVersion}
AppVerName=바꿔 Bakkwo {#AppVersion}
AppPublisher=Khamis Abdulkarim Ali
AppPublisherURL=https://github.com/
DefaultDirName={autopf}\Bakkwo
DefaultGroupName=Bakkwo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\publish
OutputBaseFilename=Bakkwo-{#AppVersion}-win-x64-Setup
SetupIconFile=..\..\src\Bakkwo.App\Assets\Bakkwo.ico
UninstallDisplayIcon={app}\Bakkwo.exe
UninstallDisplayName=바꿔 Bakkwo
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
LicenseFile=..\..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.ContextMenu=Add "Convert with 바꿔" to the right-click menu
korean.ContextMenu=마우스 오른쪽 메뉴에 "바꿔로 변환" 추가
english.ConvertWith=Convert with 바꿔
korean.ConvertWith=바꿔로 변환

[Tasks]
Name: "contextmenu"; Description: "{cm:ContextMenu}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\publish\Bakkwo-win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\바꿔 Bakkwo"; Filename: "{app}\Bakkwo.exe"
Name: "{autodesktop}\바꿔 Bakkwo"; Filename: "{app}\Bakkwo.exe"; Tasks: desktopicon

[Registry]
; "Open with" — Bakkwo is listed, never made the default app.
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "바꿔 Bakkwo"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Bakkwo.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Bakkwo.File"; ValueType: string; ValueData: "바꿔 Bakkwo"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Bakkwo.File\DefaultIcon"; ValueType: string; ValueData: """{app}\Bakkwo.exe"",0"
Root: HKA; Subkey: "Software\Classes\Bakkwo.File\shell\open\command"; ValueType: string; ValueData: """{app}\Bakkwo.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.webp\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.gif\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tif\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tiff\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tga"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tga\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".docm"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.docm\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".dotx"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.dotx\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".doc"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.doc\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".dot"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.dot\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".rtf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.rtf\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".odt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.odt\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".html"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.html\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".htm"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.htm\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".json"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.json\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".yaml"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.yaml\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".yml"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.yml\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".csv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.csv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tsv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tsv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".xml"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.xml\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mp4"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".m4v"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mov"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mkv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".webm"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".avi"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".wmv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".flv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.flv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mpg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mpg\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mpeg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mpeg\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".ts"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.ts\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mts"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mts\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".m2ts"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.m2ts\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".3gp"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.3gp\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".ogv"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.ogv\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".vob"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.vob\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".mp3"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".m4a"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".aac"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".wav"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".flac"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".ogg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".opus"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".aiff"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".aif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".wma"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".amr"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.amr\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".srt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.srt\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".vtt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.vtt\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".ass"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.ass\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".ssa"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.ssa\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".zip"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.zip\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tar"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tar\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".tgz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.tgz\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Bakkwo.exe\SupportedTypes"; ValueType: string; ValueName: ".gz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.gz\OpenWithProgids"; ValueType: string; ValueName: "Bakkwo.File"; ValueData: ""; Flags: uninsdeletevalue

; Right-click "Convert with 바꿔" on any file or folder. Several selected files start Bakkwo once each;
; the first window gathers them into one ring.
Root: HKA; Subkey: "Software\Classes\*\shell\Bakkwo"; ValueType: string; ValueData: "{cm:ConvertWith}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\*\shell\Bakkwo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Bakkwo.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\Bakkwo\command"; ValueType: string; ValueData: """{app}\Bakkwo.exe"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Bakkwo"; ValueType: string; ValueData: "{cm:ConvertWith}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\Bakkwo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Bakkwo.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Bakkwo\command"; ValueType: string; ValueData: """{app}\Bakkwo.exe"" ""%1"""; Tasks: contextmenu

[Run]
Filename: "{app}\Bakkwo.exe"; Description: "{cm:LaunchProgram,바꿔 Bakkwo}"; Flags: nowait postinstall skipifsilent
