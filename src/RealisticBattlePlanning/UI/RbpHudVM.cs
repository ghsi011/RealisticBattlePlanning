using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// One pill in the signal-palette legend (B9 legibility): "1 · charge",
    /// lit once the signal has been raised (signals latch for the battle).
    /// </summary>
    public sealed class SignalPillVM : ViewModel
    {
        private string _label;
        private bool _isFired;

        public SignalPillVM(string label, bool isFired)
        {
            _label = label;
            _isFired = isFired;
        }

        [DataSourceProperty]
        public string Label
        {
            get => _label;
            set { if (value != _label) { _label = value; OnPropertyChangedWithValue(value, nameof(Label)); } }
        }

        [DataSourceProperty]
        public bool IsFired
        {
            get => _isFired;
            set { if (value != _isFired) { _isFired = value; OnPropertyChangedWithValue(value, nameof(IsFired)); } }
        }
    }

    /// <summary>
    /// The always-on mission HUD (spec A1.1 + B9 + B5 discoverability): a
    /// "Battle Plan" entry button during deployment, the signal-palette key
    /// legend once the battle runs, and a Resume chip while any formation is
    /// player-overridden. Pure presentation — RbpHudView polls the mission
    /// state into it a few times a second; clicks call back into the view.
    /// </summary>
    public sealed class RbpHudVM : ViewModel
    {
        private readonly System.Action _onOpenPlanner;
        private readonly System.Action _onResumeAll;

        private bool _showEntry;
        private string _entryText = "";
        private bool _showPalette;
        private bool _showResume;
        private string _resumeText = "";
        private MBBindingList<SignalPillVM> _pills = new();

        public RbpHudVM(System.Action onOpenPlanner, System.Action onResumeAll)
        {
            _onOpenPlanner = onOpenPlanner;
            _onResumeAll = onResumeAll;
        }

        public void ExecuteOpenPlanner() => _onOpenPlanner?.Invoke();

        public void ExecuteResumeAll() => _onResumeAll?.Invoke();

        [DataSourceProperty]
        public bool ShowEntry
        {
            get => _showEntry;
            set { if (value != _showEntry) { _showEntry = value; OnPropertyChangedWithValue(value, nameof(ShowEntry)); } }
        }

        [DataSourceProperty]
        public string EntryText
        {
            get => _entryText;
            set { if (value != _entryText) { _entryText = value; OnPropertyChangedWithValue(value, nameof(EntryText)); } }
        }

        [DataSourceProperty]
        public bool ShowPalette
        {
            get => _showPalette;
            set { if (value != _showPalette) { _showPalette = value; OnPropertyChangedWithValue(value, nameof(ShowPalette)); } }
        }

        [DataSourceProperty]
        public bool ShowResume
        {
            get => _showResume;
            set { if (value != _showResume) { _showResume = value; OnPropertyChangedWithValue(value, nameof(ShowResume)); } }
        }

        [DataSourceProperty]
        public string ResumeText
        {
            get => _resumeText;
            set { if (value != _resumeText) { _resumeText = value; OnPropertyChangedWithValue(value, nameof(ResumeText)); } }
        }

        [DataSourceProperty]
        public MBBindingList<SignalPillVM> Pills
        {
            get => _pills;
            set { if (value != _pills) { _pills = value; OnPropertyChangedWithValue(value, nameof(Pills)); } }
        }
    }
}
