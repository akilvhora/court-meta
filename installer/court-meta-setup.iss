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

; License-verifier helper. Used by the wizard's License page to validate the
; JWT before the user advances. Lives next to the service exe in {app} so it
; ships with whatever public keys are embedded in this build.
Source: "..\tools\VerifyLicense\publish\cm-license-verify.exe"; DestDir: "{app}"; Flags: ignoreversion

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
var
  // License wizard page state
  LicensePage:        TWizardPage;
  LicenseEdit:        TNewMemo;
  LicenseStatusLabel: TNewStaticText;
  LicenseSkipCheck:   TNewCheckBox;
  LicenseAccepted:    Boolean;     // true once a JWT validates

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

(*
  License wizard page — paste a JWT or skip for the free tier.

  Lives between Select Destination and Ready to Install. The page calls
  cm-license-verify.exe (extracted to {tmp} on demand) to validate the JWT
  before the user advances; valid keys get their summary line displayed.
*)
procedure ExtractVerifierToTmp(out VerifierPath: string);
var
  ResourceName: string;
begin
  // The verifier ships in {app} after install but we need it during the
  // wizard. Extract from the SetupLdr's compressed resources to {tmp}.
  ResourceName := 'cm-license-verify.exe';
  VerifierPath := ExpandConstant('{tmp}\' + ResourceName);
  if not FileExists(VerifierPath) then
    ExtractTemporaryFile(ResourceName);
end;

{
  Run cm-license-verify with the pasted JWT. Returns the first line of stdout
  (status=... key=value tokens) and the process exit code.
}
function RunVerifier(const Jwt: string; out OutputLine: string): Integer;
var
  VerifierPath: string;
  StdoutPath:   string;
  ExitCode:     Integer;
  Lines:        TArrayOfString;
begin
  ExtractVerifierToTmp(VerifierPath);
  StdoutPath := ExpandConstant('{tmp}\license-verify.out');

  // Inno's Exec doesn't capture stdout; shell out via cmd.exe with redirection.
  // The JWT is passed as the program argument, so it has to be quoted to
  // survive cmd's parsing.
  Exec(ExpandConstant('{cmd}'),
       '/C "' + AddQuotes(VerifierPath) + ' ' + AddQuotes(Jwt) + ' > ' + AddQuotes(StdoutPath) + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  OutputLine := '';
  if FileExists(StdoutPath) then
  begin
    if LoadStringsFromFile(StdoutPath, Lines) and (GetArrayLength(Lines) > 0) then
      OutputLine := Lines[0];
    DeleteFile(StdoutPath);
  end;
  Result := ExitCode;
end;

procedure UpdateLicenseStatus();
var
  Jwt:    string;
  Status: string;
  Code:   Integer;
begin
  Jwt := Trim(LicenseEdit.Text);

  if LicenseSkipCheck.Checked then
  begin
    LicenseStatusLabel.Caption := 'Free tier (no license key)';
    LicenseAccepted := True;
    Exit;
  end;

  if Jwt = '' then
  begin
    LicenseStatusLabel.Caption := 'Paste a license key or check "I don''t have a key".';
    LicenseAccepted := False;
    Exit;
  end;

  Code := RunVerifier(Jwt, Status);
  // Status is the verifier's first stdout line: "status=valid customer=... ..."
  if Code = 0 then
  begin
    LicenseStatusLabel.Caption := 'Valid: ' + Status;
    LicenseAccepted := True;
  end
  else if Code = 2 then
  begin
    LicenseStatusLabel.Caption := 'Expired: ' + Status;
    LicenseAccepted := False;
  end
  else
  begin
    LicenseStatusLabel.Caption := 'Invalid: ' + Status;
    LicenseAccepted := False;
  end;
end;

procedure LicenseSkipChanged(Sender: TObject);
begin
  LicenseEdit.Enabled := not LicenseSkipCheck.Checked;
  UpdateLicenseStatus();
end;

procedure LicenseEditChanged(Sender: TObject);
begin
  // Live-validate as the user pastes / types, but don't shell out on every
  // keystroke; only re-run when length looks JWT-shaped (3 dot segments).
  if (StringChangeEx(LicenseEdit.Text, '.', '.', True) >= 2) then
    UpdateLicenseStatus()
  else
  begin
    LicenseStatusLabel.Caption := 'Paste a license key or check "I don''t have a key".';
    LicenseAccepted := False;
  end;
end;

procedure InitializeWizard();
begin
  LicensePage := CreateCustomPage(wpSelectDir,
    'License key',
    'Paid features need a license key. You can paste one now or skip and add it later.');

  LicenseEdit := TNewMemo.Create(LicensePage);
  LicenseEdit.Parent := LicensePage.Surface;
  LicenseEdit.Left := 0;
  LicenseEdit.Top := 0;
  LicenseEdit.Width := LicensePage.SurfaceWidth;
  LicenseEdit.Height := ScaleY(80);
  LicenseEdit.ScrollBars := ssVertical;
  LicenseEdit.WordWrap := True;
  LicenseEdit.OnChange := @LicenseEditChanged;

  LicenseSkipCheck := TNewCheckBox.Create(LicensePage);
  LicenseSkipCheck.Parent := LicensePage.Surface;
  LicenseSkipCheck.Left := 0;
  LicenseSkipCheck.Top := LicenseEdit.Top + LicenseEdit.Height + ScaleY(8);
  LicenseSkipCheck.Width := LicensePage.SurfaceWidth;
  LicenseSkipCheck.Caption := 'I don''t have a key — install the free version';
  LicenseSkipCheck.OnClick := @LicenseSkipChanged;

  LicenseStatusLabel := TNewStaticText.Create(LicensePage);
  LicenseStatusLabel.Parent := LicensePage.Surface;
  LicenseStatusLabel.Left := 0;
  LicenseStatusLabel.Top := LicenseSkipCheck.Top + LicenseSkipCheck.Height + ScaleY(12);
  LicenseStatusLabel.Width := LicensePage.SurfaceWidth;
  LicenseStatusLabel.AutoSize := False;
  LicenseStatusLabel.Caption := 'Paste a license key or check "I don''t have a key".';

  LicenseAccepted := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = LicensePage.ID) and not LicenseAccepted then
  begin
    MsgBox('Paste a valid license key, or check "I don''t have a key" to install the free version.',
           mbInformation, MB_OK);
    Result := False;
  end;
end;

{
  After Setup confirms the license is acceptable, persist it to ProgramData.
  This runs after files have been laid down so cm-license-verify.exe is
  already where it belongs.
}
procedure CurStepChanged(CurStep: TSetupStep);
var
  LicenseDir:  string;
  LicensePath: string;
begin
  if CurStep <> ssPostInstall then Exit;
  if LicenseSkipCheck.Checked then Exit;
  if Trim(LicenseEdit.Text) = '' then Exit;

  LicenseDir  := ExpandConstant('{commonappdata}\CourtMeta');
  LicensePath := LicenseDir + '\license.jwt';
  if not DirExists(LicenseDir) then
    CreateDir(LicenseDir);
  if not SaveStringToFile(LicensePath, Trim(LicenseEdit.Text), False) then
    MsgBox('Could not write license file to ' + LicensePath +
           '. The service will start in free mode.', mbInformation, MB_OK);
end;
