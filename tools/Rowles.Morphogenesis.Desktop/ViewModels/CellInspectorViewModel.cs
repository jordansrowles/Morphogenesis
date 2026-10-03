using CommunityToolkit.Mvvm.ComponentModel;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class CellInspectorViewModel(ILaboratoryApiClient apiClient, Guid sessionId) : ObservableObject
{
    [ObservableProperty]
    private CellInspectionDto? _inspection;

    [ObservableProperty]
    private string _status = "Select a cell to inspect.";

    public async Task InspectAsync(int cellId, long displayedFrameMcs, CancellationToken cancellationToken = default)
    {
        if (cellId <= 0)
        {
            Inspection = null;
            Status = "Select a cell to inspect.";
            return;
        }

        try
        {
            CellInspectionDto inspection = await apiClient.InspectCellAsync(sessionId, cellId, cancellationToken);
            Inspection = inspection;
            UpdateDisplayedFrameMcs(displayedFrameMcs);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            Inspection = null;
            Status = exception.Message;
        }
    }

    public void UpdateDisplayedFrameMcs(long displayedFrameMcs)
    {
        if (Inspection is null)
            return;
        Status = Inspection.Mcs > displayedFrameMcs
            ? $"Inspection at MCS {Inspection.Mcs}; displayed frame is MCS {displayedFrameMcs}. The inspection is newer."
            : $"Inspection at MCS {Inspection.Mcs}; displayed frame is MCS {displayedFrameMcs}.";
    }
}
