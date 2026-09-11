# Changelog

## 1.3.1 — 2026-09-11

- Settings now states that Thermalyn is free software under the GNU General Public License v3.0 and
  carries no warranty, with a link to the source code for the running version. The notice is
  translated into the four interface languages.
- Every source file carries an SPDX copyright and licence notice, so the terms travel with a file
  that is read or copied outside the repository.

## 1.3.0 — 2026-09-11

- Battery charge, health, charge or discharge rate, voltage, capacities and the remaining-time
  estimate are read on machines that have one. The rate is published under a different sensor name
  depending on the direction, and the time estimate exists only while draining, so both are read
  from the current state rather than assumed. A machine without a battery shows nothing extra.
- Scrolling with a precision touchpad now follows the distance travelled. Windows sends a stream of
  small movements, and each one used to scroll a full three lines, which made a two-finger swipe
  jump. A mouse wheel is unchanged: one notch still scrolls exactly what it did before.
- The maximize and restore button works again, and holding the pointer over it opens the Windows 11
  snap layouts flyout. Double-clicking an empty part of the title bar toggles the window the same way.
- The click that maximizes or restores the window no longer falls through to whatever the pointer
  lands on once the window has been resized, so a graphics card no longer gets selected by accident.
- A maximized window now stays inside the work area instead of extending underneath the taskbar.
- The title bar borders and glyphs are no longer clipped at any window size, and the restore glyph
  sits fully inside its box.
- The detailed view scrolls all the way down to its last row. The layout checks assert it for every
  window size, language and theme rather than only rendering the page.
- The WinGet installer manifest declares the .NET 8 desktop runtime as a dependency, so installing
  through WinGet pulls it in instead of leaving the application unable to start.

## 1.2.0 — 2026-09-10

- The interface is now also available in Spanish and German. On first launch Thermalyn follows the
  Windows language for any of the four; it falls back to English for the rest.
- The language setting moved from a row of buttons to a list, and remembers the choice by language
  code rather than by position.
- The build now checks every string table against English: a missing key, an unknown one, a
  mismatched `{0}` placeholder or a language the application offers without a table stops the build.
- Window layouts are rendered in all four languages at every supported size, so a long translation
  that overflows a narrow window is caught before release.

## 1.1.1 — 2026-09-09

- Copy diagnostics is now always available beside Settings and confirms when the text reaches the
  clipboard.
- Sensor startup no longer waits for the slow memory SPD scan; RAM usage still comes from Windows.
- Removing the optional PawnIO driver during uninstall now uses its supported silent command.

## 1.1.0 — 2026-09-09

- A new Mini view keeps processor, graphics and memory readable in a small window.
- Minimum, average and maximum values are calculated for the current session without writing any
  additional history to disk. They can be reset at any time.
- A concise diagnostic snapshot can be copied to the clipboard without creating a file.
- The selected Mini view is remembered through the existing settings file; no database, account,
  telemetry or background process was added.

## 1.0.5 — 2026-09-09

- Thermalyn checks the latest stable GitHub Release when it starts and quietly retries after the
  network returns, with throttling to avoid repeated requests.
- A discreet title-bar button shows a badge when an update exists. Its panel presents the new
  version, release notes, download progress and clear errors.
- Settings now includes a manual **Check for updates** action and the result of the latest check.
- Downloads are verified against the GitHub Release asset size and `SHA256SUMS.txt`, then checked
  again under a file lock immediately before the installer starts.
- **Restart and install** remains an explicit user action. The compact Inno Setup update replaces
  the application and starts the new version when it finishes.

## 1.0.4 — 2026-09-09

- Memory temperature no longer borrows the drive thresholds. A DIMM at 60 °C was shown as warm;
  DDR5 throttles around 85 °C, so memory has its own band.
- Settings and threshold history are written aside and moved into place. A crash during the write
  used to leave a truncated file, which read back as no settings at all.
- On a machine without a WDDM 2.0 driver, the per-process graphics counters are looked up once
  instead of on every refresh.
- The Settings page shows which version is running.

## 1.0.3 — 2026-09-08

- The installer puts a copy of the GNU General Public License beside the program, and the licence
  is published with the release. The setup wizard shows the licence itself rather than a summary;
  the driver and administrator-rights explanation moved to its own information page.

## 1.0.2 — 2026-09-08

- Memory occupancy reported the page file instead of the installed memory. The library publishes
  both under a sensor of the same name, and the commit charge sits near its limit permanently, so
  the figure read around 97 % and never moved.
- Memory module temperatures are read and shown.
- The percentage and the used/total figure beside it are now one measurement.

## 1.0.1 — 2026-09-08

- The maximise tooltip was in French in the English interface. All three window buttons now read
  their tooltip from the string tables.
- Declining the elevation prompt closed the application with a Windows error dialog instead of
  exiting quietly.
- The threshold history showed dates as `dd/MM`, which reads as month and day in English.
- Long French labels were cut off at the minimum window width.
- Settings can put the graphics card before the processor in every view.
- Removed a hidden gauge control that was updated on every refresh without ever being shown.

## 1.0.0 — 2026-09-07

First release. Processor, graphics, memory and drive sensors, Compact, Balanced and Detailed
views, English and French interface, per-component thresholds with an exceedance history,
portable executable and a bilingual installer.
