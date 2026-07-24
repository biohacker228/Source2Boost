using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Source2Boost.App.Mvvm;
using Source2Boost.Core;

namespace Source2Boost.App.ViewModels;

/// <summary>Одна строка списка «Откат»: применённый твик + кнопка-команда «Откатить».</summary>
public sealed class RestoreRowViewModel : ObservableObject
{
    public ITweak Tweak { get; }
    public string Title { get; }
    public string ChipText { get; }
    public Brush ChipBackground { get; }
    public string ChipForegroundKey { get; }
    public bool RequiresRestart { get; }
    public string RevertLabel { get; }
    public string RebootLabel { get; }

    /// <summary>Команда отката ЭТОГО твика (AsyncRelayCommand — блокирует повторный клик).</summary>
    public ICommand RevertCommand { get; }

    public RestoreRowViewModel(ITweak tw, string lang, Func<ITweak, Task> revert)
    {
        Tweak = tw;
        Title = tw.Title.For(lang);
        RequiresRestart = tw.RequiresRestart;
        RevertLabel = Loc.T("restore.revert");
        RebootLabel = Loc.T("restore.reboot");
        (ChipText, ChipBackground, ChipForegroundKey) = RiskChip.For(tw.Risk);
        RevertCommand = new AsyncRelayCommand(() => revert(tw));
    }
}
