using Microsoft.AspNetCore.Components;
using MigrateSQLServerDatabases.Localization;

namespace MigrateSQLServerDatabases.Components;

/// <summary>
/// Base for components that show translated text; re-renders when the language changes.
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject] protected Localizer L { get; set; } = default!;

    private bool subscribed;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!subscribed)
        {
            L.Changed += OnLanguageChanged;
            subscribed = true;
        }

        return base.SetParametersAsync(parameters);
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        if (subscribed)
            L.Changed -= OnLanguageChanged;
    }
}
