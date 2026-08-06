using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PSuite.Models
{
    public enum TweakRisk
    {
        Safe,
        Advanced,
        Experimental,
        Blocked
    }

    public class TweakItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uE9A1";
        public TweakRisk Risk { get; set; } = TweakRisk.Safe;
        public bool RequiresRestart { get; set; }

        private bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set
            {
                if (_isApplied != value)
                {
                    _isApplied = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActionButtonText));
                }
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(IsActionEnabled));
                }
            }
        }

        public string ActionButtonText => IsBusy
            ? "Выполняется..."
            : (IsApplied ? "Откатить" : "Применить");

        public bool IsBlocked => Risk == TweakRisk.Blocked;
        public bool IsActionEnabled => !IsBlocked && !IsBusy;

        public string RiskLabel => Risk switch
        {
            TweakRisk.Safe => "Безопасно",
            TweakRisk.Advanced => "Продвинутый",
            TweakRisk.Experimental => "Экспериментальный",
            TweakRisk.Blocked => "Заблокировано",
            _ => Risk.ToString()
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}