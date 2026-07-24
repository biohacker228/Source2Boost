using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Source2Boost.App.Mvvm;

/// <summary>
/// Минимальная база MVVM: уведомление об изменении свойств (INotifyPropertyChanged).
/// Свой класс вместо тяжёлого пакета (CommunityToolkit.Mvvm) — приложение маленькое,
/// лишняя зависимость и вес установщика ни к чему.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Присвоить поле и, если значение изменилось, поднять PropertyChanged.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
