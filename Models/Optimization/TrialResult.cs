using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LlamaServerLauncher.Models.Optimization;

public sealed class TrialResult : INotifyPropertyChanged
{
    public int Stage { get; init; }

    public int Number { get; init; }

    public double Value { get; init; }
    public double PpValue { get; init; }
    public string Command { get; init; } = "";

    public string StageLabel => $"{Stage}";
    public string ValueText => Value.ToString("F2", CultureInfo.InvariantCulture);

    private bool _isBest;
    public bool IsBest
    {
        get => _isBest;
        set
        {
            if (_isBest != value)
            {
                _isBest = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBest)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
