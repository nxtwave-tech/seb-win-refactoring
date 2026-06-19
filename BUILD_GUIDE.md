# Topin Secure Browser - Build Guide

How to build the TSB application, MSI installers, and EXE bundle. For code signing, see [SIGNING.md](SIGNING.md).

## Prerequisites

- Windows 10/11 x64
- Visual Studio 2022 (or Build Tools 2022) with the .NET Framework 4.8 targeting pack
- WiX Toolset v3.14
- PowerShell 5.1+

The `scripts/setup-environment.ps1` helper can install MSBuild/WiX/.NET if they are missing.

## Build (recommended: build script)

The build is driven by `scripts/build-application.ps1`, which restores packages, builds the
solution, then builds the MSI installers and the EXE bundle for each platform.

```powershell
# Both platforms, unsigned
.\scripts\build-application.ps1 -Configuration Release -Platforms x64,x86 -SkipTests -Local
```

To produce signed output, add the signing parameters described in [SIGNING.md](SIGNING.md).

> Notes
> - The build runs serially (MSBuild `/m` is intentionally disabled): some projects copy
>   files into other projects' output folders via `robocopy` in their post-build events,
>   which deadlocks/locks under a parallel build.
> - `$(SolutionDir)` is passed with a doubled trailing backslash so the WiX `heat` harvest
>   commands in `Setup/Setup.wixproj` receive a valid path.

## Output locations

| Artifact | Path |
|---|---|
| Standalone app | `SafeExamBrowser.Runtime\bin\<platform>\Release\SafeExamBrowser.exe` |
| MSI installer | `Setup\bin\<platform>\Release\TSB.msi` |
| EXE bundle | `SetupBundle\bin\<platform>\Release\TSB.exe` |

`<platform>` is `x64` or `x86`. The `TSB.exe` bundle is the primary deliverable: it chains
both the x64 and x86 MSIs and bootstraps the .NET Framework 4.8 and WebView2 prerequisites.

## Collect artifacts (optional)

```powershell
.\scripts\collect-artifacts.ps1 -Configuration Release -Platforms x64,x86 -Local
```

This gathers everything into `artifacts\deploy\` (`TSB.exe`, `TSB.msi`, `TSB-x86.exe`,
`TSB-x86.msi`, the standalone app, and `build-info.json`).

## Silent installation

```powershell
msiexec /i TSB.msi /quiet
```
