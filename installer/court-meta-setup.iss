; Court Meta — Windows Installer
; Built by Jenkins; ISCC passes /dExtensionID=<id> and /dVersionStr=<ver> on the command line.

#ifndef ExtensionID
  #define ExtensionID "hkkdncijcdoeohbiemlocjkeccgmclpj"
#endif

#ifndef VersionStr
  #define VersionStr "1.0.0"
#endif

#define AppName    "Court Meta"
#define AppPublisher "Arvatech"
#define ServiceName "Court Meta API"
#define ServiceExe  "CourtMetaAPI.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#VersionStr}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputBaseFilename=court-meta-setup-{#VersionStr}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#ServiceExe}
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ── Files ────────────────────────────────────────────────────────────────────

[Files]
; Self-contained API exe (single file, no .NET runtime needed on client)
Source: "..\CourtMetaAPI\publish\{#ServiceExe}"; DestDir: "{app}"; Flags: ignoreversion

; wwwroot static files (served by the API on localhost:5000)
Source: "..\CourtMetaAPI\publish\wwwroot\*"; DestDir: "{app}\wwwroot"; \
        Flags: ignoreversion recursesubdirs createallsubdirs

; Packed Chrome extension (.crx) — installed into Chrome via registry below
Source: "..\CourtMetaAPI\publish\court-meta.crx"; DestDir: "{app}"; Flags: ignoreversion

; ── Windows Service ──────────────────────────────────────────────────────────

[Run]
; Stop old service instance if upgrading
Filename: "sc.exe"; Parameters: "stop ""{#ServiceName}"""; \
          Flags: runhidden; StatusMsg: "Stopping existing service..."; \
          Check: ServiceExists

; Register the service (auto-start, runs as LocalSystem)
Filename: "sc.exe"; \
  Parameters: "create ""{#ServiceName}"" binpath= ""{app}\{#ServiceExe}"" start= auto DisplayName= ""{#ServiceName}"""; \
  Flags: runhidden; StatusMsg: "Registering Windows service..."

; Set failure action: restart after 5 s (up to 3 times)
Filename: "sc.exe"; \
  Parameters: "failure ""{#ServiceName}"" reset= 86400 actions= restart/5000/restart/5000/restart/5000"; \
  Flags: runhidden

; Set description
Filename: "sc.exe"; \
  Parameters: "description ""{#ServiceName}"" ""Court Meta local API bridge for eCourts data access"""; \
  Flags: runhidden

; Start the service
Filename: "sc.exe"; Parameters: "start ""{#ServiceName}"""; \
          Flags: runhidden; StatusMsg: "Starting Court Meta API service..."

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""{#ServiceName}""";   Flags: runhidden
Filename: "sc.exe"; Parameters: "delete ""{#ServiceName}"""; Flags: runhidden

; ── Chrome External Extension registration ────────────────────────────────────
; Chrome auto-installs the .crx when it next launches.
; Covers 64-bit Chrome (Wow6432Node is NOT used for 64-bit Chrome on 64-bit Windows).

[Registry]
; 64-bit Chrome
Root: HKLM; Subkey: "SOFTWARE\Google\Chrome\Extensions\{#ExtensionID}"; \
      ValueType: string; ValueName: "path";    ValueData: "{app}\court-meta.crx"; \
      Flags: uninsdeletekey; Check: Not IsWin64Wow
Root: HKLM; Subkey: "SOFTWARE\Google\Chrome\Extensions\{#ExtensionID}"; \
      ValueType: string; ValueName: "version"; ValueData: "{#VersionStr}"; \
      Check: Not IsWin64Wow

; 32-bit Chrome on 64-bit Windows
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Google\Chrome\Extensions\{#ExtensionID}"; \
      ValueType: string; ValueName: "path";    ValueData: "{app}\court-meta.crx"; \
      Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Google\Chrome\Extensions\{#ExtensionID}"; \
      ValueType: string; ValueName: "version"; ValueData: "{#VersionStr}"

; ── Start Menu ───────────────────────────────────────────────────────────────

[Icons]
Name: "{group}\{#AppName} API (localhost:5000)"; \
      Filename: "{app}\{#ServiceExe}"; \
      Comment: "Court Meta local API service"

; ── Pascal helpers ───────────────────────────────────────────────────────────

[Code]
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('sc.exe', 'query "' + '{#ServiceName}' + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

function IsWin64Wow(): Boolean;
begin
  Result := Is64BitInstallMode;
end;
