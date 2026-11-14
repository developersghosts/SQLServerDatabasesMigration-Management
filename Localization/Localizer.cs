namespace MigrateSQLServerDatabases.Localization;

public enum AppLanguage
{
    Arabic,
    English
}

/// <summary>
/// Per-circuit UI language. Switching does not reload the page, so the entered
/// connection strings stay in memory.
/// </summary>
public sealed class Localizer
{
    public AppLanguage Language { get; private set; } = AppLanguage.Arabic;

    public bool IsArabic => Language == AppLanguage.Arabic;
    public string Code => IsArabic ? "ar" : "en";
    public string Dir => IsArabic ? "rtl" : "ltr";

    public event Action? Changed;

    public string this[string key] =>
        Texts.All.TryGetValue(key, out var text) || ManagerTexts.All.TryGetValue(key, out text)
            ? (IsArabic ? text.Ar : text.En)
            : key;

    public void SetLanguage(string? code)
    {
        var language = code == "en" ? AppLanguage.English : AppLanguage.Arabic;
        if (language == Language)
            return;

        Language = language;
        Changed?.Invoke();
    }

    public void Toggle() => SetLanguage(IsArabic ? "en" : "ar");
}
