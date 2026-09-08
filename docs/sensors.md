# How Thermalyn picks a sensor

LibreHardwareMonitor hands you everything the machine publishes. On a laptop that is around sixty
readings, and several of them look like the one you want without being it. This page lists which
name is taken for each measurement and why the others were rejected.

`SensorSelector` matches verified sensor names exactly. Unknown readings remain unavailable until
their meaning and unit have been confirmed.

## Names the library actually publishes

Taken from LibreHardwareMonitor 0.9.7-pre729.

| Hardware | Power sensors created |
| --- | --- |
| AMD processor (`Amd17Cpu`) | `Package` |
| Intel processor (`IntelCpu`) | `CPU Package`, `CPU Cores`, `CPU Graphics`, `CPU Memory`, `CPU Platform` |
| NVIDIA (`NvidiaGpu`) | `GPU Package`, `12VHPWR Connector`, `12VHPWR Pin 1`–`6` |
| AMD graphics (`AmdGpu`) | `GPU Package`, `GPU PPT`, `GPU Core`, `GPU SoC` |
| Intel integrated (`IntelIntegratedGpu`) | `GPU Power` |

NVIDIA temperature, load and clock are all named `GPU Core`, from three different subsystems.

## Processor

Temperature is `Core (Tctl/Tdie)` on AMD and `CPU Package` on Intel. Both are the control
temperature the chip regulates itself against, which is what every other monitor reports.

Power is `Package` on AMD and `CPU Package` on Intel. `CPU Cores` is excluded because it omits
uncore and memory-controller consumption.

Clock is `Cores (Average Effective)` when available. This is the *effective* frequency, which
accounts for sleep residency and therefore represents work actually delivered. It reads lower than
Task Manager, which shows the instantaneous requested boost frequency. The difference is
intentional.

## Graphics

For NVIDIA, power is `GPU Package`, which the library fills from `nvmlDeviceGetPowerUsage`
divided by a thousand: total board power in watts.

That sensor exists only when NVML initialises. `nvml.dll` ships with the NVIDIA driver, in
`System32` on current versions, with a fallback to the legacy NVSMI folder. When it fails to load,
`NvidiaGpu` creates no power sensor. Percentage rails typed as `Load` are excluded from power
measurements.

For a discrete Radeon, power is `GPU Package` or `GPU PPT`, both fed from the driver's board total.

### Why an APU reports no graphics power

On an AMD APU, `GPU Core` power is a shared SMU rail filed under the graphics node, so it is not
used as graphics power.

Measured on a Ryzen 7 PRO 8840HS with the graphics core idle at 3% and all sixteen processor
threads loaded to 100%:

| | idle | processor at 100% |
| --- | --- | --- |
| `Package` (processor) | 6.1 W | 12.2 W |
| Sum of the per-core rails | 5.0 W | 13.6 W |
| **`GPU Core` (graphics node)** | **5 W** | **9–11 W** |
| `GPU SoC` | 1 W | 0–1 W |

The rail increases under processor load while graphics load remains idle. An APU without a
dedicated graphics rail therefore reports no graphics power.

### Graphics load

`GPU Core` from the driver when present. Otherwise the busiest Direct3D engine, which is what Task
Manager shows. Memory occupancy, the bus interface and NVIDIA's power-limit percentage are
excluded, all three being typed as `Load` by the library as well.

### Which adapter is shown

With both integrated and discrete adapters, Thermalyn selects the adapter with the highest measured
activity, prioritising load and then power.

## Memory

The library publishes several roots of type `Memory`: one per DIMM, plus `Total Memory` and
`Virtual Memory`. Both of the latter carry a `Load` sensor named exactly `Memory`, so taking the
first root that turns up gives whichever the library enumerated first.

Measured on a Ryzen 7 PRO 8840HS with 27.7 GB usable:

| Root | `Memory` load | `Memory Used` / `Memory Available` |
| --- | --- | --- |
| `Total Memory` | 82.3 % | 22.8 / 4.9 GB |
| **`Virtual Memory`** | **97.6 %** | **40.7 / 1.0 GB** |
| `Samsung - M425R2GA3PB0 (#0)` | none | — |

`Virtual Memory` is system commit usage, not page-file occupancy or physical RAM. The physical root
is selected by name, `Generic Memory` on older versions of the library, then any remaining root
that carries a load sensor and is not virtual. The percentage on screen is recomputed from
`GlobalMemoryStatusEx`, so the headline and the used/total line beside it are one measurement
rather than two that can disagree.

Each DIMM is a separate root and publishes its temperature as `DIMM #0`, `DIMM #1`, and so on.
Only these indexed names are accepted because SPD warning limits are also typed as temperatures.
The summary shows the hottest module and the detail page lists every module.

## Storage

Temperature is `Temperature`, `Drive Temperature` or `Composite`. `Warning Temperature` is a
manufacturer threshold, not a reading, and is never accepted.

Activity is `Total Activity`, not `Used Space`. The two are both percentages and only one of them
is about what the drive is doing right now.

## Per-process graphics usage

From the `GPU Engine` performance category. Windows publishes one instance per process, per adapter
and per engine, which is several hundred on a normal desktop.

Creating a `PerformanceCounter` per instance and calling `NextValue()` on each cost around 800 ms
per refresh, which was the entire processor footprint of the application. One `ReadCategory()`
returns every instance in one to two milliseconds. Its raw value is busy time accumulated in
100-nanosecond units, so the percentage is a delta over elapsed time, computed the same way as
processor usage.

## Refresh cadence

Not everything is read at the same rate, because not everything moves at the same rate.

| | Cadence |
| --- | --- |
| Processor, graphics, memory | Every refresh |
| Drives, board, power supply, cooling | Every tenth refresh |
| Process rankings | Every third refresh |
| Hardware inventory through WMI | Every two minutes |

A SMART pass costs about 30 ms, more than the processor and graphics groups together, for
temperatures that move over minutes. Groups that are not refreshed keep their last published
values, so the display stays correct.

## Adding a sensor

1. Run the probe on the target machine to see the real names:
   `dotnet run --project tests/Thermalyn.Tests -- --probe`
2. Add the exact name to the appropriate list in `SensorSelector`.
3. Add a test that pins the choice, including the readings that must *not* be selected.

Never add a fallback that picks a sensor by position, by maximum, or by partial name match.
