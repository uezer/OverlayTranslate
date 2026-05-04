using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.ComponentModel;

namespace OverlayTranslate.Localization;

public class LocExtension : MarkupExtension
{
    private readonly string _key;

    public LocExtension(string key) => _key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var proxy = new LocBindingProxy(_key);
        LocManager.RegisterProxy(proxy);

        var binding = new Binding("Value")
        {
            Source = proxy,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

internal class LocBindingProxy : INotifyPropertyChanged
{
    private readonly string _key;
    public string Value => LocManager.Get(_key);

    public LocBindingProxy(string key) => _key = key;

    internal void OnChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
