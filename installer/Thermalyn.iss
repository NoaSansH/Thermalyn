; Thermalyn installer. Compile directly with ISCC.exe or tools\build-release.ps1.
; Default: lightweight online setup around the framework-dependent single-file build.
; Compile with /DOfflineBuild=1 for a setup embedding the self-contained build instead.

#define AppName "Thermalyn"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Thermalyn Project"
#define AppExe "Thermalyn.exe"
#ifdef OfflineBuild
  #define AppPayload "..\artifacts\Thermalyn-win-x64\Thermalyn.exe"
  #define SetupFileName "Thermalyn-Setup-Offline"
#else
  #define AppPayload "..\artifacts\Thermalyn-installed\Thermalyn.exe"
  #define SetupFileName "Thermalyn-Setup"
#endif

[Setup]
AppId={{8F2C1D74-4E63-4A18-9E2B-5C7A0D3F1B96}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\artifacts
OutputBaseFilename={#SetupFileName}
SetupIconFile=Thermalyn-setup.ico
AllowNoIcons=yes
; Inno skips these pages by default as soon as a previous installation is detected.
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=no
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
ChangesAssociations=yes
; Thermalyn reads low-level sensors, so it installs for the whole machine.
PrivilegesRequired=admin
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "LICENSE-en.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "LICENSE-fr.txt"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
french.DesktopIcon=Créer un raccourci sur le Bureau
english.ShortcutsGroup=Shortcuts:
french.ShortcutsGroup=Raccourcis :
english.InstallingDriver=Installing the PawnIO sensor driver...
french.InstallingDriver=Installation du pilote de capteurs PawnIO...
english.LaunchApp=Launch %1
french.LaunchApp=Lancer %1
english.RuntimeTitle=Required component
french.RuntimeTitle=Composant requis
english.RuntimeSubtitle=Downloading Microsoft .NET Desktop Runtime 8
french.RuntimeSubtitle=Téléchargement de Microsoft .NET Desktop Runtime 8
english.RuntimeConsent=Thermalyn requires Microsoft .NET Desktop Runtime 8 (x64).%n%nDownload it from Microsoft and install it automatically now?
french.RuntimeConsent=Thermalyn nécessite Microsoft .NET Desktop Runtime 8 (x64).%n%nLe télécharger depuis Microsoft et l'installer automatiquement maintenant ?
english.RuntimeDownloadFailed=Microsoft .NET Desktop Runtime 8 could not be downloaded. Check the Internet connection and try again.
french.RuntimeDownloadFailed=Le téléchargement de Microsoft .NET Desktop Runtime 8 a échoué. Vérifiez la connexion Internet et réessayez.
english.RuntimeInstallFailed=Microsoft .NET Desktop Runtime 8 could not be installed (code %1). Thermalyn has not been installed.
french.RuntimeInstallFailed=Microsoft .NET Desktop Runtime 8 n'a pas pu être installé (code %1). Thermalyn n'a pas été installé.
english.StartupRegistrationFailed=Thermalyn could not be registered in Windows Startup Apps (code %1).
french.StartupRegistrationFailed=Thermalyn n'a pas pu être inscrit dans les applications de démarrage Windows (code %1).
english.RemoveDriver=Also remove the PawnIO sensor driver?%n%nAnswer No if another monitoring tool uses it.
french.RemoveDriver=Retirer aussi le pilote de capteurs PawnIO ?%n%nRépondez Non si un autre outil de surveillance l'utilise.
english.AlreadyInstalled=Thermalyn %1 is already installed on this computer.%n%nThis wizard will reinstall it over the existing copy: program files are replaced, your settings and threshold history are kept.%n%nTo remove it completely, use Add or Remove Programs.
french.AlreadyInstalled=Thermalyn %1 est déjà installé sur cet ordinateur.%n%nCet assistant va le réinstaller par-dessus : les fichiers du programme sont remplacés, vos réglages et votre historique de dépassements sont conservés.%n%nPour le retirer complètement, passez par Ajout/Suppression de programmes.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:ShortcutsGroup}"

[Files]
Source: "{#AppPayload}"; DestDir: "{app}"; Flags: ignoreversion
; Sensor prerequisite: without it, processor temperature and power draw stay unreachable.
Source: "..\Thermalyn\Assets\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: PawnIoMissing

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:LaunchApp,{#AppName}}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:LaunchApp,{#AppName}}"; Tasks: desktopicon

[Run]
; -install -silent are the switches documented by the PawnIO author; without them its own
; wizard pops up in the middle of ours.
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "{cm:InstallingDriver}"; Flags: waituntilterminated runhidden; Check: PawnIoMissing
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; The scheduled task is machine-wide, so the uninstaller's elevated token removes it directly.
; Everything living in the signed-in user's profile is removed from [Code] instead, because an
; elevated reg.exe writes to the administrator's hive rather than the user's.
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /f /tn ""{#AppName}"""; Flags: runhidden; RunOnceId: "ThermalynLegacyTask"

[Code]
const
  AppUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F2C1D74-4E63-4A18-9E2B-5C7A0D3F1B96}_is1';
  PawnIoKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
#ifndef OfflineBuild
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-8-x64.exe';

var
  DownloadPage: TDownloadWizardPage;
  RuntimeDownloaded: Boolean;

function DesktopRuntime8Installed: Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(Base + '8.*', FindRec) then
  begin
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         FileExists(Base + FindRec.Name + '\WindowsBase.dll') then
        Result := True;
    until Result or not FindNext(FindRec);
    FindClose(FindRec);
  end;
end;
#endif

function InstalledVersion: String;
begin
  Result := '';
  RegQueryStringValue(HKLM, AppUninstallKey, 'DisplayVersion', Result);
end;

procedure InitializeWizard;
var
  Previous: String;
begin
#ifndef OfflineBuild
  DownloadPage := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeSubtitle'), nil);
#endif
  Previous := InstalledVersion;
  if Previous <> '' then
    WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('AlreadyInstalled'), [Previous]);
end;

#ifndef OfflineBuild
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or RuntimeDownloaded or DesktopRuntime8Installed then
    Exit;

  if MsgBox(CustomMessage('RuntimeConsent'), mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := False;
    Exit;
  end;

  DownloadPage.Clear;
  DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      RuntimeDownloaded := True;
    except
      MsgBox(CustomMessage('RuntimeDownloadFailed'), mbError, MB_OK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if DesktopRuntime8Installed then
    Exit;

  if not RuntimeDownloaded then
  begin
    Result := CustomMessage('RuntimeDownloadFailed');
    Exit;
  end;

  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeSubtitle');
  if not Exec(ExpandConstant('{tmp}\' + RuntimeFile), '/install /quiet /norestart', '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  if ResultCode = 3010 then
    NeedsRestart := True;

  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(ResultCode)])
  else if not DesktopRuntime8Installed then
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(ResultCode)]);
end;
#endif

function PawnIoMissing: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
        and not RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  RegisterParams: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  { Registration is mandatory and independent of the optional first application launch. }
  if not Exec(ExpandConstant('{app}\{#AppExe}'), '--register-startup', ExpandConstant('{app}'),
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  if ResultCode <> 0 then
    RaiseException(FmtMessage(CustomMessage('StartupRegistrationFailed'), [IntToStr(ResultCode)]));

  { Administrative installs can have a different HKCU from the person who started Setup. Write
    the standard Run entry once more with the original pre-UAC identity so Startup Apps sees it. }
  RegisterParams := 'add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"' +
    ' /v Thermalyn /t REG_SZ /d "\"' + ExpandConstant('{app}\{#AppExe}') +
    '\" --startup" /f';
  if not ExecAsOriginalUser(ExpandConstant('{sys}\reg.exe'), RegisterParams, '',
                            SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  if ResultCode <> 0 then
    RaiseException(FmtMessage(CustomMessage('StartupRegistrationFailed'), [IntToStr(ResultCode)]));
end;

// Per-profile cleanup. ExecAsOriginalUser is unavailable during uninstall, and {localappdata}
// resolves to whoever elevated the uninstaller, so ProfileList is walked with the elevated token.

procedure RemoveProfileFiles;
var
  Profiles: TArrayOfString;
  Index: Integer;
  Root: string;
begin
  if not RegGetSubkeyNames(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList', Profiles) then
  begin
    Log('Cleanup: profile list unreadable');
    exit;
  end;

  for Index := 0 to GetArrayLength(Profiles) - 1 do
  begin
    if not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\' + Profiles[Index],
                               'ProfileImagePath', Root) then continue;
    if Root = '' then continue;

    // Settings, threshold history and diagnostics.
    DelTree(Root + '\AppData\Local\{#AppName}', True, True, True);
    // Extraction folder of the single-file bundle, recreated at every launch.
    DelTree(Root + '\AppData\Local\Temp\.net\{#AppName}', True, True, True);
    DeleteFile(Root + '\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\{#AppName}.lnk');
  end;
end;

procedure RemoveUserData;
begin
  RemoveProfileFiles;
  // Only reachable for the hive currently loaded, which is the account running the uninstaller.
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', '{#AppName}');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder', '{#AppName}.lnk');
end;

procedure RemovePawnIo;
var
  Command, Executable, Arguments: string;
  Quote, ResultCode: Integer;
begin
  if not RegQueryStringValue(HKLM, PawnIoKey, 'UninstallString', Command) then exit;
  if Command = '' then exit;
  // Suppressible, and defaults to keeping the driver: other tools may use it.
  if SuppressibleMsgBox(CustomMessage('RemoveDriver'), mbConfirmation, MB_YESNO, IDNO) <> IDYES then exit;

  Command := Trim(Command);
  if Copy(Command, 1, 1) = '"' then
  begin
    Quote := Pos('"', Copy(Command, 2, Length(Command)));
    Executable := Copy(Command, 2, Quote - 1);
    Arguments := Trim(Copy(Command, Quote + 2, Length(Command)));
  end
  else
  begin
    Executable := Command;
    Arguments := '';
  end;
  Exec(Executable, Trim(Arguments + ' /S'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    // A running instance holds its own executable open, which would leave the folder behind.
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if CurUninstallStep = usPostUninstall then
  begin
    RemoveUserData;
    // The application folder can hold files written after setup ran, which Inno does not track.
    DelTree(ExpandConstant('{app}'), True, True, True);
    RemovePawnIo;
  end;
end;
