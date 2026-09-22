# Pending lessons - awaiting human approval

Proposed lessons land here. Unapproved entries are never acted on;
approval means a human moves the entry into `INDEX.md` (kernel rule 9).
Confirmed-good calls belong here too, not only corrections. Never record
what is derivable from the repo itself.

Entry format (all four fields required):

- what happened:
- what check should have caught it:
- what was added:
- retire-when:

- what happened: The Smart Export WPF window threw `XamlParseException` from
  `Window.Show()` because `Run.Text` is registered `BindsTwoWayByDefault` and
  the item template bound it to read-only view-model properties. The add-in
  would have crashed the first time a user opened it. It passed 48 unit tests,
  a full green `scripts/check.sh`, an independent review, and three GitHub
  review rounds, because nothing in the gate ever instantiated the window.
  It was found only by hand-porting a local screenshot harness.
- what check should have caught it: a gate test that actually shows the window
  and fails on a thrown exception, on any `PresentationTraceSources`
  data-binding trace, and on the template failing to generate containers and
  text. View-model tests cannot see XAML, and human review did not either.
- what was added: `SmartExportWindowRenderTests`, which shows the real window
  on an STA thread and asserts realized `TreeViewItem` containers, tri-state
  checkboxes, and the rendered text; `<UseWPF>` on the test project. Proven to
  gate by commenting out the template and observing the failure.
- retire-when: no WPF or other markup-bound UI ships from this workspace, or
  an equivalent render check is enforced for every window by a shared rule
  rather than per-window tests.

- what happened: Quadratic selection-aggregation work recurred three times in
  one file. Each instance was caught pre-merge by a reviewer and repaired, and
  each repair passed the full gate, so by discovery route the system looked
  like it was working. It was not: no check measured aggregation work, so each
  fix was verified only by reading. The fourth pass added a work bound. The
  first attempt at that bound was itself wrong - it counted `CalculateSelection`
  call entries, which is ~2N whether or not the defect is present, because the
  cost is the per-call walk over children, not the number of calls. Counting
  element visits (`1 + Children.Count`) separated 40,800 visits from 403 at
  N=200.
- what check should have caught it: a deterministic work-bound assertion over a
  wide fixture, whose metric measures the dimension the cost actually lives in.
  Notification counts and call counts both pass a quadratic implementation.
- what was added: a per-tree aggregation counter exposed via
  `InternalsVisibleTo`, and
  `TogglingOneOccurrenceOfAWidelyRepeatedDocumentStaysLinearInTreeSize`
  asserting the delta stays under 6N.
- retire-when: the selection view models are deleted or replaced, or the
  workspace adopts a general performance-regression gate that covers them.
- what happened: The File Naming Manager needed five independent review passes
  and one GitHub review round before PASS. Every FAIL was a real defect, and
  most traced back to the advisor's own implementation spec rather than to
  worker mistakes: companion drawings were outside the Vault and modifiability
  guards, files outside the project scope were renameable, the root snapshot's
  external parents were never read, and the parent-modifiability blocker fired
  for rows that were never renamed. All of these live in the port contracts and
  the Plan rules, which were written before any code existed and were never
  reviewed on their own.
- what check should have caught it: an adversarial review of the spec and port
  contracts (TASK.md rules, NamingPorts.cs, the blocker list) before
  implementation, asking "which document can reach RenameDocument, SaveDocument
  or MoveToOriginals without every stated guard applying to it" - the same
  questions the post-implementation reviews eventually asked, answered five
  passes earlier.
- what was added: nothing mechanized yet. Recorded here as a process lesson;
  the candidate mechanization is a reviewer step in docs/procedures/ship.md
  that runs on the spec before the first worker is dispatched for any task
  that writes, moves or deletes user files.
- retire-when: two consecutive file-mutating tasks reach PASS in at most two
  review passes, or the pre-implementation review step is adopted in ship.md.

- what happened: A live Inventor run was the only check that found D7 (parents
  saved by pre-rename paths); unit tests over a fake gateway could not see it.
  Later the adapter changed again and the cited smoke log predated the shipped
  code until a reviewer refused it (B1). Separately, the T3 fix went further
  than the finding required (drawings dropped from the series maximum) and the
  tests written alongside it asserted the new behaviour rather than the task's
  allocation rule, so they passed while contradicting the spec.
- what check should have caught it: for the first, a gate check that the newest
  live evidence is newer than the last change to the adapter and Execute path;
  for the second, tests that quote the spec rule they pin (here TASK.md
  "highest observed in the project scope + 1") so a reviewer can see when an
  assertion encodes the implementation instead.
- what was added: the live smoke harness, committed at
  projects/file-naming-manager/tools/FileNamingManager.LiveSmoke, with a
  cold-reopen check in a second Inventor process; the evidence stamp it writes
  on pass at projects/file-naming-manager/tests/live-evidence/LIVE_EVIDENCE.json,
  hashing every InventorAdapter source, FileNamingWorkflow.cs and the harness
  itself; projects/file-naming-manager/scripts/check-live-evidence.sh, wired
  into that project's check.sh, which recomputes the hash and fails when the
  recorded live run no longer matches the tree; and TEST_PLAN section 1
  documenting the stamp and the refresh command. The allocator tests were
  rewritten to assert the rule.
- retire-when: the check has been in the gate for 45 days without a
  stale-evidence miss.

