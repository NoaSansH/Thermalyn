using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Thermalyn.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _gpuHeading = "";

    // Differs for an integrated and a discrete adapter.
    public string GpuHeading
    {
        get => _gpuHeading;
        set
        {
            if (_gpuHeading == value) return;
            _gpuHeading = value;
            Notify();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
