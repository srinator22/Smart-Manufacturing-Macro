// Purpose: Represent one COM-free assembly hierarchy node with deterministic tri-state selection.
// Inputs: An immutable export hierarchy node and the source paths eligible in the active scope.
// Outputs: Recursive selection and expansion state for the Smart Export WPF tree.
// Dependencies: Core Phase 1 models only.
// Assumptions: Selection mutations occur synchronously on the Inventor UI thread.
// Validation source: SmartExportViewModelTests.

using System.Collections.ObjectModel;
using System.ComponentModel;
using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UI.Phase1;

public sealed class SmartExportTreeNodeViewModel : INotifyPropertyChanged
{
    private readonly SmartExportTreeNodeViewModel? parent;
    private bool isExpanded;
    private bool ownSelection;
    private bool? selection;
    private bool isApplyingSelection;

    public SmartExportTreeNodeViewModel(
        ExportHierarchyNode node,
        ISet<string> exportablePaths,
        SmartExportTreeNodeViewModel? parent = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(exportablePaths);

        this.parent = parent;
        NodeId = node.NodeId;
        DisplayName = node.DisplayName;
        SourcePath = node.SourcePath;
        DocumentKind = node.DocumentKind;
        Quantity = node.Quantity;
        IsExportable = !string.IsNullOrWhiteSpace(SourcePath) && exportablePaths.Contains(SourcePath);
        ownSelection = IsExportable;
        Children = new(node.Children.Select(child => new SmartExportTreeNodeViewModel(child, exportablePaths, this)));
        CanSelect = IsExportable || Children.Any(child => child.CanSelect);
        selection = CalculateSelection();
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

    public bool? IsSelected
    {
        get => selection;
        set
        {
            if (!CanSelect)
            {
                return;
            }

            ApplySelection(value ?? false);
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

    private void ApplySelection(bool value)
    {
        isApplyingSelection = true;
        if (IsExportable && ownSelection != value)
        {
            ownSelection = value;
            OnPropertyChanged(nameof(IsDocumentSelected));
        }

        foreach (SmartExportTreeNodeViewModel child in Children)
        {
            if (child.CanSelect)
            {
                child.ApplySelection(value);
            }
        }

        isApplyingSelection = false;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        bool? next = CalculateSelection();
        if (selection != next)
        {
            selection = next;
            OnPropertyChanged(nameof(IsSelected));
        }

        if (!isApplyingSelection)
        {
            parent?.RefreshSelection();
        }
    }

    private bool? CalculateSelection()
    {
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
}
