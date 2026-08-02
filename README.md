# Power Plan Manager / 电源计划管理器

A local-only Windows tray application for quickly switching and scheduling Windows power plans.

一个仅在本机运行的 Windows 托盘应用，用于快速切换电源计划，并按每日时间段自动切换。

## Features / 功能

- Four configurable slots: Power saver, Balanced, High performance, and Ultimate performance.
- Daily time ranges, including ranges across midnight, with overlap validation.
- Manual choices override automation until the next schedule boundary.
- Native `PowrProf.dll` integration; no `powercfg.exe`, service, registry workaround, or elevation.
- WinUI 3 tray flyout and context menu, single-instance activation, sign-in startup, and Explorer restart recovery.
- Chinese Simplified and English UI in one MSIX package.
- Local, versioned, atomic JSON settings with corrupt-file recovery.

## Requirements / 环境

- Windows 10 build 19041 or later, or Windows 11.
- .NET 10 SDK and Windows SDK 10.0.26100 or later.
- For final `.msixupload` creation: Visual Studio with **Windows application development**, MSIX tooling, and the Windows App SDK C# components.

The application targets Windows App SDK 2.3.1 and builds for x86, x64, and ARM64.

## Build and test / 构建与测试

```powershell
dotnet restore PowerManager.slnx
dotnet test tests\PowerManager.Core.Tests\PowerManager.Core.Tests.csproj -c Release
dotnet build src\PowerManager.App\PowerManager.App.csproj -c Debug -p:Platform=x64
```

Run the unpackaged development build:

```powershell
src\PowerManager.App\bin\x64\Debug\net10.0-windows10.0.19041.0\PowerManager.App.exe
```

Create unsigned local-validation MSIX packages for all architectures:

```powershell
.\scripts\Build-StorePackages.ps1
```

After associating the project with the reserved Partner Center identity and installing Visual Studio packaging prerequisites, create Store upload containers:

```powershell
.\scripts\Build-StorePackages.ps1 -StoreUpload
```

Generated packages are written under `src\PowerManager.App\AppPackages`. Certificates, `.pfx` files, and `Package.StoreAssociation.xml` must never be committed.

## Safety model / 安全模型

- Switching and template duplication run with the current user's rights.
- A missing or policy-blocked plan produces an actionable error; the app does not fall back silently.
- Creating plans always requires explicit confirmation in the UI.
- Explicitly exiting the tray process stops automation; no background service or scheduled task remains.
- Some OEM and Modern Standby devices expose only a subset of Windows plans. Users can bind any available OEM plan to a slot.

## Store preparation

Localized listing drafts, privacy statements, certification notes, and the release checklist are in [`docs/store`](docs/store). Before submission, replace the placeholder package identity in `Package.appxmanifest` by associating the project with the reserved Microsoft Store product.

## Project layout

- `src/PowerManager.Core`: models, schedule engine, settings store, and service contracts.
- `src/PowerManager.App`: WinUI 3 UI, PowrProf interop, tray integration, startup task, resources, and MSIX manifest.
- `tests/PowerManager.Core.Tests`: deterministic tests that never change the host's power plan.

## Privacy

The app does not collect, transmit, or share personal information. Settings remain in the app's local data directory. See the localized policies in [`docs/store`](docs/store).
