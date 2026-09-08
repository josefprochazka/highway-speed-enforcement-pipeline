using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HighwaySpeed.Client.Infrastructure;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class. Hand-written on
/// purpose - it is a dozen lines, has no magic, and is exactly what a source
/// generator or MVVM framework would produce.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets <paramref name="field"/> and raises the change event only if the value actually changed.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
