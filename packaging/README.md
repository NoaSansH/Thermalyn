# Package manifests

Manifests for the three Windows package managers. They point at the published GitHub release and
carry the checksum of the file they install, so a tampered download fails before anything runs.

Every version bump means: new URL, new checksum, new `version` field. The checksums are the ones in
`SHA256SUMS.txt` attached to the release.

## winget

Validated with `winget validate --manifest packaging/winget`.

Submitting means opening a pull request against
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) with the three files copied to
`manifests/n/NoaSansH/Thermalyn/<version>/`. The easiest route is
[`wingetcreate`](https://github.com/microsoft/winget-create), which forks, commits and opens the
pull request for you:

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --token <github-token> packaging/winget
```

Review is automated first, then human. An unsigned installer is accepted but flagged, and the
reviewers may ask about the kernel driver: it is [PawnIO](https://pawnio.eu), signed, open source,
and the same driver LibreHardwareMonitor uses.

Once merged: `winget install NoaSansH.Thermalyn`.

## Scoop

The manifest installs the portable build, so nothing is written outside the Scoop directory and no
driver is set up. Usable immediately, without any submission:

```powershell
scoop install https://raw.githubusercontent.com/NoaSansH/Thermalyn/main/packaging/scoop/thermalyn.json
```

To get it into the `extras` bucket, open a pull request against
[ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras) with the same file. `checkver` and
`autoupdate` are already configured, so their bot picks up new releases on its own.

## Chocolatey

```powershell
cd packaging/chocolatey
choco pack
choco push thermalyn.1.0.0.nupkg --source https://push.chocolatey.org/ --api-key <key>
```

An account on [chocolatey.org](https://chocolatey.org) is needed for the key. The first submission
from a new maintainer goes through moderation, which takes longer than the later ones.

Test locally before pushing:

```powershell
choco install thermalyn --source . --force
choco uninstall thermalyn
```
