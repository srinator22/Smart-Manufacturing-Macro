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
