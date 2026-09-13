# Inventor Scripts environment manifest

## Required

- Windows 11 x64
- Git and Git Bash; native Windows entry point is `scripts\check.cmd`
- Autodesk Inventor 2027 installed at `C:\Program Files\Autodesk\Inventor 2027`
- Inventor API v31 interop assembly under Inventor 2027 `Bin\Public Assemblies`
- .NET SDK 10.0.401, pinned by `global.json`
- Network access to NuGet during dependency restore
- `gitleaks` 8.30.1
- `git-cliff` 2.14.1
- Local .NET tool restore for Stryker.NET 5.0.0

## Interactive development

- Visual Studio Community 2026 version 18.0 or later
- .NET desktop development workload for WPF and C#
- Autodesk Inventor 2027 Developer Tools from the installed SDK folder

The command-line SDK is enough for workspace format, build, test, and mutation checks. Visual Studio and Autodesk Developer Tools are required before interactive add-in debugging in Inventor.

## Repository policy

- CLI tools are preferred over MCP servers when the CLI covers the need.
- Search uses `rg`; edits are patch-based and reviewable.
- Installs and CI tool downloads are version-pinned and package lockfiles are committed.
- No Autodesk binaries, customer CAD, production exports, user classifications, private URLs, or credentials enter Git.
- Independent automations live under `projects/`; only proven cross-project components live under `shared/`.

## Agent routing

- Primary orchestration: GPT-6 Astra at medium reasoning.
- Substantive coding and adversarial review: GPT-5.6 Sol at medium or high reasoning.
- Bounded low-stakes edits, exploration, and CI monitoring: GPT-5.6 Terra at low or medium reasoning.
- Delegate only independent bounded work when it improves elapsed time or verification enough to justify the added token use.
