using System;
using HwMonLinux.Core;

namespace HwMonLinux.App.ViewModels;

public sealed class ProviderDiagnosticViewModel : ViewModelBase
{
    private string _status = "Unknown";
    private string? _message;
    private int _readingCount;
    private TimeSpan? _duration;

    public ProviderDiagnosticViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public int ReadingCount
    {
        get => _readingCount;
        private set => SetProperty(ref _readingCount, value);
    }

    public TimeSpan? Duration
    {
        get => _duration;
        private set => SetProperty(ref _duration, value);
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public void Update(ProviderDiagnostic diagnostic)
    {
        ReadingCount = diagnostic.ReadingCount;
        Status = diagnostic.Status.ToString();
        Message = diagnostic.Message;
        Duration = diagnostic.Duration;
    }
}
