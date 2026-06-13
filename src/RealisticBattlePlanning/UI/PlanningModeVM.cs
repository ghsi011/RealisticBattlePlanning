using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Gauntlet datasource for the Planning Mode panel. First slice: read-only
    /// title + plan summary text. The editor's mutation surface is Core's
    /// PlanDraft, which this VM will wrap as the editing widgets land.
    /// </summary>
    public sealed class PlanningModeVM : ViewModel
    {
        private string _titleText;
        private string _summaryText;

        public PlanningModeVM(string title, string summary)
        {
            _titleText = title;
            _summaryText = summary;
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set
            {
                if (value != _titleText)
                {
                    _titleText = value;
                    OnPropertyChangedWithValue(value, "TitleText");
                }
            }
        }

        [DataSourceProperty]
        public string SummaryText
        {
            get => _summaryText;
            set
            {
                if (value != _summaryText)
                {
                    _summaryText = value;
                    OnPropertyChangedWithValue(value, "SummaryText");
                }
            }
        }
    }
}
