using System.ComponentModel;

namespace VehicleInspection.App.Localization;

public static class Loc
{
    public static event PropertyChangedEventHandler? LanguageChanged;

    public static void NotifyLanguageChanged()
    {
        LanguageChanged?.Invoke(null, new PropertyChangedEventArgs(string.Empty));
    }

    public static string Get(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
