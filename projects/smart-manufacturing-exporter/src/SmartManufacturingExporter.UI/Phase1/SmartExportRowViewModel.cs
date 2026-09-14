using System.ComponentModel;
using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UI.Phase1;

public sealed class SmartExportRowViewModel : INotifyPropertyChanged
{
    private bool isSelected = true;

    public SmartExportRowViewModel(ExportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SourcePath = candidate.SourcePath;
        DisplayName = candidate.DisplayName;
        Quantity = candidate.Quantity;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourcePath { get; }

    public string DisplayName { get; }

    public int Quantity { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
