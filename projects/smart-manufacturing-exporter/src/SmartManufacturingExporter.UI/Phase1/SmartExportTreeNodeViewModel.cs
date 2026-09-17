// Purpose: Represent one COM-free assembly hierarchy node with deterministic tri-state selection.
// Inputs: An immutable export hierarchy node and the source paths eligible in the active scope.
// Outputs: Recursive selection and expansion state for the Smart Export WPF tree.
// Dependencies: Core Phase 1 models only.
// Assumptions: Selection mutations occur synchronously on the Inventor UI thread. Occurrences of the
//   same source document (matched case-insensitively, since export deduplicates that way) share one
//   selection state: checking or unchecking any occurrence checks or unchecks every occurrence of that
//   document, because export is planned per source document, not per occurrence.
//   A selection write runs in two strict phases so no aggregate is ever computed against stale
//   own-state (see IsSelected):
//     1. Apply own-state only, for this node's subtree and every peer of every document whose
//        own-state actually changed - no aggregate recomputation happens here.
//     2. Settle every affected aggregate exactly once, deepest first, from ONE merged set: this
//        node's own subtree (post-order, so children are final before their parents), every touched
//        peer, and the full ancestor chain of this node and of every touched peer. A touched peer can
//        itself be the ANCESTOR of another touched peer (two occurrences of the same repeated
//        sub-assembly, each containing an occurrence of the same repeated part) or an ancestor can be
//        shared by two different branches (two occurrences of the same document under different parent
//        assemblies); either shape means a node's correct aggregate depends on another node that is
//        only settled elsewhere in this same batch. Settling peers and ancestors in two separate,
//        differently-ordered passes - a peer pass in touch order, then an ancestor bubble - recomputes
//        a node against whichever of those dependents has not been settled yet, publishing a transient
//        wrong tri-state and notifying twice for one user action. A single set, sorted deepest first
//        and settled once per node, is what guarantees every node's dependents are already final by the
//        time that node itself is recomputed. A node whose aggregate changes then notifies exactly
//        once, with its final value; a node whose aggregate does not change never notifies at all.
// Validation source: SmartExportViewModelTests.

using System.Collections.ObjectModel;
using System.ComponentModel;
using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UI.Phase1;

public sealed class SmartExportTreeNodeViewModel : INotifyPropertyChanged
{
    private readonly SmartExportTreeNodeViewModel? parent;
    private readonly int depth;
    private readonly Action? selectionChanged;
    private readonly Dictionary<string, List<SmartExportTreeNodeViewModel>> peersBySourcePath;
    private readonly AggregationCounter aggregationCounter;
    private bool isExpanded;
    private bool ownSelection;
    private bool? selection;

    public SmartExportTreeNodeViewModel(
        ExportHierarchyNode node,
        ISet<string> exportablePaths)
        : this(node, exportablePaths, null, null, new(StringComparer.OrdinalIgnoreCase), new AggregationCounter())
    {
    }

    internal SmartExportTreeNodeViewModel(
        ExportHierarchyNode node,
        ISet<string> exportablePaths,
        Action selectionChanged)
        : this(node, exportablePaths, null, selectionChanged, new(StringComparer.OrdinalIgnoreCase), new AggregationCounter())
    {
    }

    private SmartExportTreeNodeViewModel(
        ExportHierarchyNode node,
        ISet<string> exportablePaths,
        SmartExportTreeNodeViewModel? parent,
        Action? selectionChanged,
        Dictionary<string, List<SmartExportTreeNodeViewModel>> peersBySourcePath,
        AggregationCounter aggregationCounter)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(exportablePaths);

        this.parent = parent;
        depth = parent is null ? 0 : parent.depth + 1;
        this.selectionChanged = selectionChanged;
        this.peersBySourcePath = peersBySourcePath;
        this.aggregationCounter = aggregationCounter;
        NodeId = node.NodeId;
        DisplayName = node.DisplayName;
        SourcePath = node.SourcePath;
        DocumentKind = node.DocumentKind;
        Quantity = node.Quantity;
        IsExportable = !string.IsNullOrWhiteSpace(SourcePath) && exportablePaths.Contains(SourcePath);
        ownSelection = IsExportable;
        Children = new(node.Children.Select(
            child => new SmartExportTreeNodeViewModel(child, exportablePaths, this, selectionChanged, peersBySourcePath, aggregationCounter)));
        CanSelect = IsExportable || Children.Any(child => child.CanSelect);
        selection = CalculateSelection();

        if (IsExportable)
        {
            if (!peersBySourcePath.TryGetValue(SourcePath!, out List<SmartExportTreeNodeViewModel>? peers))
            {
                peers = [];
                peersBySourcePath.Add(SourcePath!, peers);
            }

            peers.Add(this);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string NodeId { get; }

    public string DisplayName { get; }

    public string? SourcePath { get; }

    public ComponentDocumentKind DocumentKind { get; }

    public int Quantity { get; }

    public bool IsExportable { get; }

    public bool IsDocumentSelected => IsExportable && ownSelection;

    public bool CanSelect { get; }

    public ObservableCollection<SmartExportTreeNodeViewModel> Children { get; }

    // Exposed for tests only (SmartManufacturingExporter.UnitTests, via InternalsVisibleTo) to bound
    // selection aggregation work per tree and guard against quadratic peer bubbling regressions.
    internal long AggregationCount => aggregationCounter.Count;

    public bool? IsSelected
    {
        get => selection;
        set
        {
            if (!CanSelect)
            {
                return;
            }

            bool target = value ?? false;
            HashSet<SmartExportTreeNodeViewModel> touchedPeers = [];

            // Phase 1: write own-state only - this node, its selectable descendants, and every peer of
            // every document whose own-state actually changed. No aggregate recomputation.
            ApplyOwnSelection(target, touchedPeers);

            // Phase 2a: settle this node's own subtree, post-order, so its children are final before it.
            RefreshSubtreeBottomUp();

            // Phase 2b: settle every touched peer together with the full ancestor chain of this node
            // and of every touched peer, as ONE merged set, deepest first. A touched peer can itself be
            // the ancestor of another touched peer (two occurrences of a repeated sub-assembly, each
            // containing an occurrence of the same repeated part), so peers and ancestors cannot be two
            // separate passes - see SettlePeersAndAncestors.
            SettlePeersAndAncestors(touchedPeers);

            selectionChanged?.Invoke();
        }
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
        }
    }

    public IEnumerable<SmartExportTreeNodeViewModel> DescendantsAndSelf()
    {
        yield return this;
        foreach (SmartExportTreeNodeViewModel child in Children)
        {
            foreach (SmartExportTreeNodeViewModel descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    public void SetExpandedRecursively(bool value)
    {
        IsExpanded = value;
        foreach (SmartExportTreeNodeViewModel child in Children)
        {
            child.SetExpandedRecursively(value);
        }
    }

    // Phase 1: recursion that writes own-state only, for this node, its selectable descendants, and
    // every peer of every document whose own-state actually flips. Never calls back into
    // ApplySelection/IsSelected on a peer, and computes no aggregates - that is Phase 2's job.
    private void ApplyOwnSelection(bool value, HashSet<SmartExportTreeNodeViewModel> touchedPeers)
    {
        if (IsExportable && ownSelection != value)
        {
            ownSelection = value;
            OnPropertyChanged(nameof(IsDocumentSelected));

            // Export deduplicates by source document (case-insensitively), so every other occurrence
            // of this same document must share the new state. Peers' own state is written directly
            // (never through ApplyOwnSelection/IsSelected) to avoid recursing back into this node and
            // to keep this batch emitting exactly one selectionChanged callback.
            if (peersBySourcePath.TryGetValue(SourcePath!, out List<SmartExportTreeNodeViewModel>? peers))
            {
                foreach (SmartExportTreeNodeViewModel peer in peers)
                {
                    if (ReferenceEquals(peer, this))
                    {
                        continue;
                    }

                    peer.ownSelection = value;
                    peer.OnPropertyChanged(nameof(IsDocumentSelected));
                    touchedPeers.Add(peer);
                }
            }
        }

        foreach (SmartExportTreeNodeViewModel child in Children)
        {
            if (child.CanSelect)
            {
                child.ApplyOwnSelection(value, touchedPeers);
            }
        }
    }

    // Phase 2 (subtree): post-order so every child's aggregate is final before its parent recomputes.
    // Never touches anything outside this subtree - no bubbling to parents.
    private void RefreshSubtreeBottomUp()
    {
        foreach (SmartExportTreeNodeViewModel child in Children)
        {
            if (child.CanSelect)
            {
                child.RefreshSubtreeBottomUp();
            }
        }

        RefreshSelfOnly();
    }

    // Phase 2 (peer): recompute this node's own aggregate and notify if changed. Never touches parents.
    private void RefreshSelfOnly()
    {
        bool? next = CalculateSelection();
        if (selection != next)
        {
            selection = next;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    // Phase 2b: settle every touched peer AND every affected ancestor from one merged set, deepest
    // first - never as two separate passes. A touched peer can be the ancestor of another touched peer
    // (two occurrences of the same repeated sub-assembly, each holding an occurrence of the same
    // repeated part): settling peers in touch order and ancestors in a later, separate bubble
    // recomputes that ancestor-peer against its own still-stale descendant peer first (publishing a
    // transient wrong tri-state and notifying twice), then again once the descendant is finally
    // settled. A shared ancestor reached from two different branches has the same problem for the same
    // reason. Merging everything into one set and settling it deepest-first guarantees every node's
    // dependents - descendant peers and child aggregates alike - are already final by the time that
    // node itself is recomputed, so a node whose aggregate changes notifies exactly once with its final
    // value and a node whose aggregate is unchanged never notifies. This also covers
    // a non-exportable intermediate node sitting between two peers (e.g. an unsupported-kind container
    // between an exportable assembly and its exportable part): it is never itself in touchedPeers, only
    // reachable through the ancestor chains, so sorting touchedPeers alone would miss it.
    private void SettlePeersAndAncestors(HashSet<SmartExportTreeNodeViewModel> touchedPeers)
    {
        HashSet<SmartExportTreeNodeViewModel> settle = [];
        CollectAncestorChain(parent, settle);
        foreach (SmartExportTreeNodeViewModel peer in touchedPeers)
        {
            CollectAncestorChain(peer.parent, settle);
        }

        // The set is ancestor-closed at this point (every member arrived via a chain walk). Only now
        // can the peers themselves be added - see CollectAncestorChain's early-exit note for why
        // pre-seeding with a peer before this point would be wrong.
        foreach (SmartExportTreeNodeViewModel peer in touchedPeers)
        {
            settle.Add(peer);
        }

        foreach (SmartExportTreeNodeViewModel node in settle.OrderByDescending(node => node.depth))
        {
            node.RefreshSelfOnly();
        }
    }

    // Walks from start to the root, adding every node to the shared settle set. Stops as soon as a node
    // is already present (HashSet.Add returns false) - that node's own ancestors were already added by
    // an earlier chain, so there is nothing further up left to add. This early exit is only correct
    // while the set is ancestor-closed, i.e. while every member got there exclusively through a chain
    // walk: if a peer were added directly before its own ancestor chain is walked, a later chain walk
    // that reaches that peer would stop there and never add its ancestors. That is why
    // SettlePeersAndAncestors walks every ancestor chain first and adds the peers themselves last.
    private static void CollectAncestorChain(SmartExportTreeNodeViewModel? start, HashSet<SmartExportTreeNodeViewModel> ancestors)
    {
        SmartExportTreeNodeViewModel? node = start;
        while (node is not null && ancestors.Add(node))
        {
            node = node.parent;
        }
    }

    private bool? CalculateSelection()
    {
        // Counts element VISITS, not call entries: a per-entry-only counter cannot see this method's
        // real cost, since the quadratic regression this guards against is O(N) calls each doing O(N)
        // internal work (walking every child), not O(N^2) calls. Children.Count (not the built list
        // length or the selectable-child count) keeps the metric a pure function of tree shape so it
        // cannot drift with selection state.
        aggregationCounter.Count += 1 + Children.Count;

        List<bool?> selectableStates = [];
        if (IsExportable)
        {
            selectableStates.Add(ownSelection);
        }

        selectableStates.AddRange(Children.Where(child => child.CanSelect).Select(child => child.IsSelected));
        if (selectableStates.Count == 0)
        {
            return false;
        }

        if (selectableStates.All(state => state == true))
        {
            return true;
        }

        if (selectableStates.All(state => state == false))
        {
            return false;
        }

        return null;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // Shared per-tree mutation counter (never static - a static counter would leak across xunit's
    // parallel test classes and flake). One instance is created at root construction and threaded
    // through the private recursive constructor so every node in one tree shares it.
    private sealed class AggregationCounter
    {
        public long Count;
    }
}
