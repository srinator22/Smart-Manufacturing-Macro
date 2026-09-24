# Task: Ribbon icons restyled to Inventor's theme, crisp at every display scale
Mode: autopilot
Branch: task/ribbon-icon-restyle
Date: 2026-09-23

## Goal
The WMP Custom Tools ribbon icons look low quality next to Inventor's own: saturated cobalt (#1F4E9C) silhouettes on the dark ribbon, downsampled from 1280 px art, stretched from 32 px at 125% display scaling, and converted through AxHost.GetIPictureDispFromPicture, which yields a PICTYPE_BITMAP picture that Inventor re-converts and loses transparency on. Replace them with line-art icons in Inventor's palette (light grey bodies, sky-blue and soft-gold accents), drawn from vector masters at each scale-specific size, in a dark-theme and a light-theme variant, selected at activation from Inventor's active theme and the DPI of Inventor's window, and handed to Inventor as PICTYPE_ICON pictures.

## Non-goals
- Live icon swap when the user changes Inventor's theme while it runs; icons follow the theme on the next start.
- Archiving the superseded 1280 px logos, the old generate-icons.ps1 scripts, and the old <name>-16/32.png files (kernel rule 11: archive only in a maintenance pass; recorded in BACKLOG.md).
- Release packaging guard for interop-less builds (separate task; see Retro).

## Budget
- Wall-clock: one session
- Subagents / workflow runs: 0
- Retries per failing step: 2
- Escalate to human when: a live Inventor check contradicts the design, or CI cannot render the masters identically

## Acceptance criteria
- [x] Every ribbon icon has a dark and a light variant at small 16/20/24/32 and large 32/40/48/64 px, rendered from committed XAML masters -> shared/WmpIconRenderer `--check` in scripts/check.sh
- [x] Committed PNGs match a fresh render of the masters (generated artifacts are never hand-edited) -> shared/WmpIconRenderer `--check`
- [x] Theme name maps to a variant: names containing "Light" map to light, everything else (dark, unknown, empty) to dark -> shared/WmpRibbon.UnitTests/RibbonIconSelectorTests.cs
- [x] DPI maps to the smallest supplied size at or above the scaled target, capped at the largest (96 -> 16/32, 120 -> 20/40, 144 -> 24/48, 192 and above -> 32/64, below 96 -> 16/32) -> RibbonIconSelectorTests.cs
- [x] Resource names are `<assembly-prefix>.Ribbon.<icon>-<theme>-<size>.png` and each add-in embeds every variant it requests -> RibbonIconSelectorTests.cs and each project's ArchitectureTests
- [x] Analyze Naming and Apply Naming use different icons -> FileNamingManager.ArchitectureTests
- [x] All three add-ins convert ribbon icons through WmpRibbon.RibbonPicture (PICTYPE_ICON); the per-project AxHost converters are no longer used -> judgment: grep in review, plus live check
- [x] Live: on Inventor 2027 dark theme at 125% the four icons render in the new style without dark fringes -> judgment: user screenshot 2026-09-23 (before the resize; enlarged icons not re-screenshotted)
- [x] ./scripts/check.sh green in CI for the pushed SHA -> PR #19 CI success for e01d7b59c83638bef4c0c06557a6de30bba4afff; local check: OK on the advisor's machine (pwsh 7: packaging x3, icons 56/56, release-test, mutation all green); merged as b26f3157c74b57a678ccfa42c3b50919fe51924d; main CI success for that SHA

## Plan
1. Live spike: how Inventor sizes a 40 px icon at 125% (done, see log).
2. Shared RibbonPicture (PICTYPE_ICON) in shared/WmpRibbon.
3. Pure RibbonIconSelector (theme, DPI, resource name) in shared/WmpRibbon, compiled without the interop, with unit tests in shared/WmpRibbon.UnitTests.
4. RibbonIcons loader (Inventor theme + window DPI -> two PICTYPE_ICON pictures) in shared/WmpRibbon under INVENTOR_INTEROP.
5. XAML masters (16-unit and 32-unit grids, palette tokens) per icon under projects/<p>/assets/icons/.
6. shared/WmpIconRenderer console tool: render masters to projects/<p>/assets/ribbon/*.png; `--check` re-renders and compares; wired into scripts/check.sh.
7. Wire the three add-ins and window icons to the new assets; update architecture tests to the new asset contract.
8. Build, install, live check with the user; check.sh; push; PR; watch CI.

## Progress log
- 2026-09-23 spike: 40 px (PICTYPE_ICON) and 32 px (AxHost) test patterns both rendered at the same on-screen size (27-28 px in a downscaled screenshot), so Inventor resizes to its own target and a DPI-matched source avoids the upscale. The AxHost path rendered white stripes dark, reproducing the documented transparency loss.
- 2026-09-23 implemented: RibbonPicture (PICTYPE_ICON), RibbonIconSelector (31 unit tests), RibbonIcons loader, 8 XAML masters, WmpIconRenderer (56 PNGs, --check wired into check.sh as step 9a), three add-ins and windows rewired, architecture tests replaced with the variant contract (failed first on the missing UI resource, then passed).
- 2026-09-23 local evidence: Debug and Release builds 0 warnings with the interop; build, WmpRibbon.UnitTests and icon --check also pass with -p:InventorInteropPath pointing at a missing file (CI shape); dotnet test 744 passed 0 failed; gitleaks dir no leaks. Not run locally: project check.sh packaging tests (need pwsh 7) and mutation; both run in CI.
- 2026-09-23 docs: docs/rules/ribbon-icons.md (agent guide for new tools and icons), linked from the AGENTS.md rules index; WmpRibbon and WmpIconRenderer READMEs; BACKLOG rows for the release packaging guard (P0), archiving superseded icon sources, and live theme refresh.
- 2026-09-23 live (dark theme, 125%): user screenshot showed all four commands loaded with the new icons, no dark fringes; user judged them "a little small". Measured content spans 20.5 to 25 of 32 units against Inventor's near-full-square icons.
- 2026-09-23 resize: all eight masters redrawn to span 27 to 30.5 of 32 (14 to 16 of 16); WmpIconRenderer now rejects a master whose longer side is under 28/32 or shorter side under 26/32 (verified: the previous smart-export master fails, all new masters pass). Enlarged build installed; the user asked to commit without a second screenshot, so the enlarged icons are not yet re-checked live.
- 2026-09-24 gate: check.sh locally green through the three projects' packaging; it stops at test-check-live-evidence.sh because the only local PowerShell is 5.1 (ConvertFrom-Json -NoEnumerate is PowerShell 7 only). Remaining steps run individually: SME and WMP project checks OK, icons --check OK, test-build-release.sh OK, test-release.sh fails only because PowerShell 5.1 Compress-Archive writes backslash zip entries (confirmed with unzip -l), gitleaks git and dir no leaks, Release build 0 warnings. Mutation not run locally. CI (pwsh 7) is the authority for those steps.
- 2026-09-24 review: FAIL on stale asset READMEs, plus two non-blocking weaknesses; fixed in a50184a (READMEs, null-safe ReadThemeName, AnalyzeAndApplyButtonsAreWiredToDistinctIcons verified to fail on miswiring); delta review PASS. 743 tests pass.

## Review verdict
Reviewer (fresh context), first pass: FAIL - AGENTS.md Standards (README present-tense): the three projects' assets/README.md still called <name>-16.png/-32.png the live ribbon and window icons. Non-blocking: the distinct-icons assertion only checked names; ReadThemeName would throw on a null theme.

Reviewer, delta pass on a50184a: PASS - the asset READMEs are present-tense and match the csproj and XAML wiring; ReadThemeName falls back to dark on null; the new test pins each button's icon pair and that the analyze and apply PNGs differ, and its regexes cannot match across calls; BACKLOG's "nothing references them" holds. Pending: CI green for the pushed SHA.

## Retro

Defects escaped past merge (route b): none from this task. Defects reported by the human (route c): the first live screenshot showed the new icons rendering correctly but "a little small"; the masters were redrawn to fill the grid and WmpIconRenderer now rejects a master whose content spans less than 28/32 (longer side) or 26/32 (shorter side), so the check exists (kernel rule 3). The enlarged icons were installed but not re-screenshotted; that live confirmation stays open in BACKLOG.

Found during this task, belonging to the release-and-updater task: the published v0.6.0 assets were built on a GitHub-hosted runner without the Inventor interop, so every add-in DLL compiled without StandardAddInServer and loads as Unloaded. Recorded as the P0 BACKLOG row "Release packaging guard" and being fixed as task release-interop-guard, whose retro will amend the release-and-updater record and propose the lesson.

No new lesson proposed here: the icon size rule is mechanized in the renderer check.

Delivery evidence: PR #19 CI success for e01d7b59c83638bef4c0c06557a6de30bba4afff; local scripts/check.sh check: OK on 2026-09-24 (31 + 6 + 10 + 12 + 55 + 287 + 342 tests, packaging OK x3, icons OK 56 PNGs match 4 masters, live-evidence OK, build-release-test OK, release-test OK, no leaks, mutation naming 93.31 / 94.89 / 97.67 %, exporter 80.00 / 85.11 %, tools manager 92.57 / 92.47 / 79.31 %); merged as b26f3157c74b57a678ccfa42c3b50919fe51924d; main CI success for that SHA.

Residual gaps carried in BACKLOG: release packaging guard (P0, in progress); archiving the superseded icon sources at the next maintenance pass; live theme refresh; live re-check of the enlarged icons.
