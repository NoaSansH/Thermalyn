# Third-party components

Thermalyn itself is under the GNU General Public License v3.0, in [`LICENSE`](LICENSE). The
components below keep their own terms.

## LibreHardwareMonitor

Mozilla Public License 2.0. Consumed as a NuGet package, unmodified.

- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

MPL-2.0 is a file-level copyleft: it covers the library's own files, which are not modified here.
Section 3.3 of the MPL expressly allows the combined work to be distributed under a Secondary
License, which the GPL is, so combining it with GPL-3.0 code is permitted.

## PawnIO

GNU General Public License version 2, by namazso.

- Source: https://github.com/namazso/PawnIO
- Home page and downloads: https://pawnio.eu

Thermalyn does not link against PawnIO and does not include it. It reads sensors through
LibreHardwareMonitor, which communicates with the driver through its device IO control interface.
The PawnIO licence grants an explicit exception for programs that talk to it that way, so the
question of GPLv2 and GPLv3 compatibility never arises: the two programs are separate works that
exchange messages, not one combined work.

The repository does not contain the driver. `tools/fetch-pawnio.ps1` downloads a pinned version at
build time and verifies its SHA-256, so a change upstream fails the build instead of quietly
shipping a different binary.

`Thermalyn-Setup.exe` and `Thermalyn-Setup-Offline.exe` both embed that installer, unmodified, and
run it when the driver is absent. Those two release assets therefore redistribute a GPLv2 work.
Their complete corresponding source is the upstream repository linked above, where the author
offers it. The two installers differ only in whether the .NET runtime is included, not in how they
handle the driver.

`Thermalyn-Portable.exe` carries no driver at all: it points at the official download page when a
reading needs one.

## HidSharp

Apache License 2.0, by James F. Bellinger. Pulled in by LibreHardwareMonitor and shipped inside the
portable executable, unmodified.

- Source: https://github.com/IntergatedCircuits/HidSharp

Apache-2.0 is compatible with GPL version 3, and only with version 3. That compatibility is the
reason Thermalyn is licensed under GPL-3.0 rather than GPL-2.0.

## BlackSharp.Core, DiskInfoToolkit and RAMSPDToolkit

Mozilla Public License 2.0. Companion libraries of LibreHardwareMonitor, shipped unmodified inside
the portable executable. Their sources are linked from the LibreHardwareMonitor repository above,
and section 3.3 of the MPL covers them the same way.

## Microsoft .NET Desktop Runtime 8

MIT licence. Downloaded by the online installer, bundled by the offline one, in both cases as the
unmodified redistributable published by Microsoft.

- Source and terms: https://github.com/dotnet/runtime

## Where to get the source

Thermalyn is distributed under the GPL, which requires the source of any binary to be available.
Every published executable is built by GitHub Actions from the tag it is attached to, so the
complete corresponding source of a release is the repository at that same tag:

    https://github.com/NoaSansH/Thermalyn/tree/vX.Y.Z

Nothing else is needed to reproduce it: the build is one script, `tools/build-release.ps1`.
