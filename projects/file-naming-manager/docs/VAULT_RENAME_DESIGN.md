# Renaming files that live in Autodesk Vault

## Why this document exists

The File Naming Manager renames Inventor files. Most of the files it will meet are
managed by Autodesk Vault (vault `WMPPD`, project type Vault, Inventor's
unique-filenames mode on). A rename that only touches the local working folder
turns a controlled file into a new, uncontrolled file: the old name stays in
Vault with its history, the new name arrives as a first version with none, and
every parent that referenced the old name has to be reconciled by hand. That is
the failure the user asked this tool to avoid. This page records how the working
folder exposes Vault state, what the tool does in v1, and the exact procedure a
later task must implement and verify for Vault-managed files.

## How Vault state is visible in the working folder

- Every Vault-managed file has a tracker beside it: `_V\<filename>.v`, a four-line
  text file holding the checksum, the version's modification time, its check-in
  time, and the checksum again. The Inventor Vault add-in maintains these
  trackers and uses them to decide whether a local file is a clean copy of a
  vault version. A file without a tracker is unmanaged: typically a new file the
  user saved but has not checked in.
- The 2026-09-22 incident notes in the user's Vault backup folder show the tracker
  format verified with a hex dump, and show that the tracker can claim a file is
  clean when its bytes are not. The tool therefore treats the tracker as a
  presence signal only, never as proof of content integrity.
- Read-only queries against the vault are possible without stored credentials.
  `Autodesk.Connectivity.WebServicesTools.AutodeskAccount.Login(IntPtr.Zero)` uses
  the Autodesk ID already held by Autodesk Identity Manager on the machine, and
  `AutodeskAuthCredentials(ServerIdentities, vaultName, readOnly: true, account)`
  opens a read-only session. `DocumentService.GetLatestFilePathsByNames` and
  `FindFilesByPathsAndChecksums` resolve a local file to its vault master. v1 does
  not call the SDK; the tracker heuristic is sufficient to keep managed files out
  of the local rename path.

## The three cases

| Case | How detected | What v1 does |
| --- | --- | --- |
| Unmanaged file (no tracker) | `_V\<name>.v` absent | Renamed in-session through `Document.SaveAs(newPath, false)`, leaf documents first, then sub-assemblies, then the root; parents saved afterwards so the reference change persists; original moved to `_renamed-originals\<timestamp>\` with a manifest, never deleted. |
| Managed file | Tracker present; the tracker says nothing about checkout state and v1 cannot tell | Not renamed by v1 regardless of modifiability. Listed as Vault-managed with its planned name and written to the exported Vault rename plan, with its companion drawings. |
| Parent that must be saved is not modifiable | Inventor reports `IsModifiable` false, typically a managed file that is not checked out | The plan is blocked and the document is named, because saving a parent that is not checked out is impossible and attempting it is how references get lost. This applies only to parents of files the tool would rename locally. |

Mixed assemblies are the normal case: a checked-out, managed main assembly whose
new children are unmanaged. Renaming those children in-session updates the
parent's references in memory, the parent is saved, and the user checks the
parent and the new children in as usual. Vault sees the parent's new reference
to a new file, which is the ordinary new-file flow, so nothing breaks.

## Procedure for Vault-managed files (next task, not implemented in v1)

Vault Explorer's Rename command preserves history because a rename in Vault is a
check-in of the same master under a new name, followed by reference updates in
the parents. The Vault Client 2027 SDK (`Autodesk.Connectivity.WebServices.dll`
32.0.71.0, in `C:\Program Files\Autodesk\Vault Client 2027\Explorer`) exposes
every step. Method names below were enumerated from that assembly on 2026-09-23.

1. Resolve each planned file to its vault master with
   `DocumentService.GetLatestFilePathsByNames` (or `FindFilesByPathsAndChecksums`
   when the local checksum is available).
2. Ask Vault whether the rename is allowed before doing anything:
   `GetFileRenameRestrictionsByMasterIds(masterIds, newFileNames)`. Any
   restriction blocks the plan with Vault's own reason.
3. With the assembly open in Inventor and every affected document checked out to
   the user, rename leaf-first through `Document.SaveAs(newPath, false)` so
   Inventor rewrites the parents' internal references, then save the parents.
   This is the same in-session step v1 already performs for unmanaged files.
4. For each renamed file, in the same leaf-first order: `CheckoutFile` is already
   held by the user; upload the renamed local bytes with the SDK's
   `FileManagerUtil.UploadFile`, then `CheckinUploadedFile(masterId, comment,
   keepCheckedOut: false, lastWrite, associations, bom, copyBom: true,
   newFileName: <new name>, fileClassification, hidden, uploadTicket)`. The
   `newFileName` argument is what makes this a rename of the existing master
   rather than a new file.
5. For each parent, remap its `FileAssocParam[]` to the renamed children and check
   it in the same way, or call
   `UpdateFileAssociationReferences(fileIds, fileVaultPaths, associations)` where
   the parent's bytes have not changed.
6. Refresh the working folder so the `_V` trackers match the new versions, and
   move the old-named local files to `_renamed-originals` with the manifest.
7. Verify by re-downloading each new version and comparing SHA-256 against the
   uploaded bytes, exactly as the 2026-09-22 restore did.

Preconditions the next task must enforce mechanically:

- A non-production vault, or explicit human approval per run against `WMPPD`.
  The production server rejected 15.8 MB commits with error 109 on 2026-09-22 and
  had already stored truncated versions of two assemblies; renaming through
  check-in on a server in that state would create more damaged versions.
- Every affected file checked out to the current user on this machine, verified
  through `File.CheckedOut`, `CkOutUserId` and `CkOutMach`, not inferred.
- Upload success is not commit success. A `CheckinUploadedFile` fault leaves the
  checkout in place; the tool must undo only checkouts it created, as the restore
  did, and must never retry a failing commit more than once.
- `GetLatestDuplicateFilePaths()` before and after, because the project runs in
  unique-filenames mode and a rename that collides with a file elsewhere in the
  vault would be resolved by Inventor to the wrong file.

## What the exported Vault rename plan contains

For each managed file with a planned name: current vault-relative path, planned
file name, the series and number allocated, and the list of parent files whose
references Vault must update, ordered leaf-first so that executing the list top
to bottom in Vault Explorer's Rename command never leaves a parent pointing at a
missing child. Numbers allocated to managed files are reserved in the same
allocator as unmanaged ones, so a later in-session rename cannot reuse them.
Each entry also lists its companion drawings (same stem, same folder) with
their planned names, informationally, since v1 renames no drawing on this list.
