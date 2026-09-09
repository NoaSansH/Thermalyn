# Contributing

## Getting it running

```powershell
dotnet run --project .\Thermalyn\Thermalyn.csproj
```

You need the .NET 8 SDK. Sensor readings that require the PawnIO driver are absent until it is
installed; everything else works without it, so a driverless machine is fine for interface work.

Building the installers additionally needs Inno Setup 6. `tools/build-release.ps1` fetches the
pinned PawnIO installer itself and verifies its hash; the repository does not carry that binary,
for the reason set out in [`NOTICE.md`](NOTICE.md).

## Before opening a pull request

```powershell
dotnet build .\Thermalyn\Thermalyn.csproj
dotnet run --project .\tests\Thermalyn.Tests -- --settings --repository-contracts
```

That second command covers sensor selection, the settings schema, key and placeholder parity
between the two string tables, absolute user paths in tracked files, and wording written straight
into the code instead of the string tables.

## House rules

Sensors are matched by exact name. If the name isn't recognised, return nothing. There is no
nearest-match fallback and there shouldn't be one — see [`docs/sensors.md`](docs/sensors.md).

User-visible strings go in `Themes/Strings.en.xaml` and `Strings.fr.xaml`, both files, same keys.
A string written straight into the code shows up untranslated in the other language. The checks
reject it.

Readings run on worker threads, the UI updates on the dispatcher. `LocalizationService` snapshots
its table for that reason: a `ResourceDictionary` is not safe to read concurrently.

Comments should say why something is done. Restating the line below them is noise.

Keep an eye on the refresh cost. The app idles around 0.3% CPU. Anything you add to that path runs
once a second on other people's machines, so if it's expensive and slow-moving give it its own
cadence.

## Reporting a wrong reading

Sensor problems are hardware-specific and cannot be guessed at from a description. Run the probe on
the machine and attach its output:

```powershell
dotnet run --project .\tests\Thermalyn.Tests -- --probe
```

It lists every sensor the library exposes with its exact name, and what Thermalyn selects from
them. Say which value looked wrong and what you expected instead.
