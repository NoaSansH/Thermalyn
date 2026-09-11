// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Thermalyn.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _gpuHeading = "";

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
