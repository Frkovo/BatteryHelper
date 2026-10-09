# Third-party components

BatteryHelper's original application, service, tests and build scripts are licensed under the MIT License in `LICENSE`. Third-party source and derived files retain their original licenses; the MIT License does not replace those licenses.

- LibreHardwareMonitor: MPL-2.0, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
  - Pinned source: `76150732c2df9241415eec216001bec4557c9033`.
  - Modifications: `patches/IntelMsr.cs` validates module availability and ioctl completion; `patches/IntelCpu.cs` and `patches/IntelIntegratedGpu.cs` clear failed/stale/reset power reads and reestablish counter baselines. Build targets are restricted to .NET 10 x64.
  - `src/BatteryHelper.Sensors/Interop/IntelGcl.cs` is adapted from this source; changes are its namespace, nullable device-enumeration argument and secure System32 DLL probing.
  - The source release includes the upstream source archive and the modification sources. The installer includes the MPL license.
  - The repository includes the license in `licenses/LibreHardwareMonitor-MPL-2.0.txt` and preserves upstream dependency notices in `licenses/LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt`.
- PawnIO and its modules: licenses and source links are supplied by the upstream project and installer.
  - https://github.com/namazso/PawnIO
  - https://github.com/namazso/PawnIO.Modules
  - https://github.com/namazso/PawnIO.Setup
  - The signed setup bundled with the pinned LibreHardwareMonitor source is used without modification.
- .NET / Windows SDK projection: Microsoft and .NET Foundation, applicable MIT and Microsoft component licenses.
- Other LibreHardwareMonitor dependencies retain their upstream licenses. See `licenses/LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt`.

BatteryHelper does not redistribute Intel graphics drivers. IGCL is loaded from the system's installed Intel driver.
