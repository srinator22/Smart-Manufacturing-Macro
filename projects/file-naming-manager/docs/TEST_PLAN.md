# File Naming Manager - live test plan

Use sanitized CAD in a folder outside Git and outside the Vault workspace for
sections 1 to 3. Never run the automated smoke harness against
`...\Documents\Vault\`.

## 1. Automated smoke (temp folder, no Vault)

`.work\jobs\naming-live-smoke` starts a hidden Inventor through the
`Inventor.Application` ProgID only when no Inventor process exists, builds this
fixture in `%TEMP%`, runs the adapter, and quits Inventor:

```text
P901 Smoke\
  Assemblies\
    smoke rig.iam                 (root; references both parts and the sub-assembly)
    lifting frame.iam             (sub-assembly; references one part)
  Parts\
    high density mesh material.ipt
    901-0004 Existing Bracket.ipt (already numbered; consumes 0004)
    spacer plate.ipt
```

Expected after Apply with project 901 (numbers are allocated in snapshot order: the root is first, so the main assembly takes A001 as in P124; Inventor's `AllReferencedDocuments` then enumerates depth-first, so `spacer plate` under the sub-assembly is reached before `high density mesh material` and takes 0005; observed live on 2026-09-23):

```text
Parts\901-0005 spacer plate.ipt
Parts\901-0006 high density mesh material.ipt
Assemblies\901-A001 smoke rig (main assembly).iam
Assemblies\901-A002 lifting frame (sub-assembly).iam
_renamed-originals\<timestamp>\...  (the four original names under Parts and Assemblies, plus manifest.json)
```

The harness reopens `901-A001 smoke rig (main assembly).iam` and asserts
`HasReferencesMissing` is false and every referenced path is a new name. The
existing `901-0004` file is untouched. If the harness cannot run, the log says
so and live behaviour stays unclaimed.

Harness caveats observed on 2026-09-23: `Documents.CloseAll(true)` did not close
the fixture documents, so the reopen check attests to in-session state, not a
cold read from disk; a cold reopen needs a second Inventor process. The
harness also reads part and assembly templates from the active project's
template folder (read-only); it writes nothing outside `%TEMP%`.

## 2. Interactive - unmanaged new files inside a checked-out assembly

1. In a copy of a real project outside Vault, open the main assembly and add two
   new parts saved as `bracket left.ipt` and `bracket right.ipt`.
2. Ribbon `WMP Custom Tools` > `Analyze Naming`. Confirm the project number is
   prefilled from the root and that the two new files show state Unnumbered and
   action Rename, while numbered files show Canonical and no action.
3. Confirm the report lists every real finding present in the folder.
4. `Apply Naming`. Confirm the two files are renamed with the next two part
   numbers, the assembly still opens with no missing references, the originals
   are under `_renamed-originals\<timestamp>\Parts\`, and `manifest.json` lists
   both moves. Confirm Part Number iProperty equals the number token.

## 3. Interactive - blockers

1. With the root assembly unsaved, run Apply Naming: blocked, root named.
2. Make one part read-only on disk, run Apply: blocked, that document named.
3. Pre-create a file with the planned target name, run Apply: blocked with the
   target path named.
4. Add a `_V\<drawing name>.v` tracker beside a companion drawing that shares a
   part's base name, leaving the part itself unmanaged, run Apply: blocked,
   with the drawing named and Vault Explorer named as where to rename it.
5. Instead of a tracker, set the companion drawing's file to read-only on
   disk, run Apply: blocked, with the drawing named as read-only.
6. Open a second assembly in the same Inventor session that references one
   part of the active assembly, then run Apply Naming on the active assembly:
   blocked, with the part named and the second assembly named as the external
   reference to close or include first.
7. Copy one file to a second folder under the project root using the exact
   file name a proposal would produce for another file in scope, run Apply:
   blocked with the unique-filenames-mode message naming the proposed name
   and the path of the colliding file in the other folder.

## 4. Vault-managed files (manual, Vault Explorer)

Do this on a non-critical project first, and not while the server is rejecting
commits (see the 2026-09-22 incident notes).

1. Open the assembly from the Vault working folder with every affected file
   checked out. Run `Analyze Naming`. Managed files show Vault-managed with a
   planned name and no Rename action.
2. `Export Vault plan`. Open the exported plan: it lists each managed file
   leaf-first with its planned name and the parents Vault must update.
3. In Vault Explorer, select the first file in the list, Rename, enter the
   planned name exactly, and accept the reference update for the listed
   parents. Repeat down the list, and rename the listed companion drawings to
   the same stem.
4. Check in. Re-run `Analyze Naming`: every renamed file is Canonical and no
   duplicate numbers are reported.
5. Record the Vault server version, the number of files renamed, and any file
   Vault refused to rename with its restriction message.

## Evidence

Record the Inventor 2027 display version, add-in informational version, fixture
structure, the analysis report, the exact produced file names, the manifest, and
screenshots. Until recorded, live rename behaviour is unverified.
