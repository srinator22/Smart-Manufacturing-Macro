# Phase 2 Inventor 2027 test plan

Use sanitized CAD outside this repository. Do not use production or customer files.

## Fixture

Create and save a three-level assembly:

```text
Machine.iam
  Frame.iam
    Bracket.iam
      Plate.ipt (2 occurrences)
    Rail.ipt
  Cover.ipt
  Suppressed.iam
    Hidden.ipt
```

Use a new empty output directory. Keep every source document saved before testing.

## Hierarchy and selection

1. Open `Machine.iam`, start Smart Export, and choose Parts Recursive.
2. Confirm Frame, Bracket, Plate, Rail, and Cover appear at their real depths.
3. Confirm Plate shows quantity 2 and Hidden does not appear.
4. Clear Bracket and confirm its eligible descendants clear and ancestors become indeterminate.
5. Select Bracket and confirm its eligible descendants select and ancestors recompute.
6. Run Collapse All and Expand All and confirm every hierarchy level follows.

## Scope

Switch through all four scopes and confirm:

- Top Level Only enables Cover only.
- Parts Recursive enables Plate, Rail, and Cover.
- Assemblies Only enables Machine, Frame, and Bracket.
- Assemblies And Parts enables all saved part and assembly documents except the suppressed branch.

Each scope change intentionally resets eligible nodes to selected.

## Unique STEP export

1. Choose Assemblies And Parts and select one repeated Plate occurrence plus Frame.
2. Export at Low precision.
3. Confirm exactly `Plate.step` and `Frame.step` are created.
4. Repeat into another empty directory at Highest precision.
5. Reopen or import the outputs and compare fidelity, elapsed time, and file size.
6. Confirm no source document is saved, renamed, moved, or closed if it was already open.

## Evidence

Record the Inventor 2027 display version, add-in informational version, fixture structure,
scope results, selected source files, produced files, failures, timings, sizes, and screenshots.
Until these observations are recorded, live hierarchy and `.iam` translation remain unverified.
