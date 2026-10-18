using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Application;

/// <summary>Registers the Application layer (framework-free app services + ports with app-side impls).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<LanguageService>();
        services.AddSingleton<UndoRedoService>();
        // Keyboard shortcut overrides. The host registers an IShortcutPlatform; without one the
        // framework-free Windows/Linux conventions apply.
        services.AddSingleton(sp => new KeyboardShortcutService(
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetService<IShortcutPlatform>() ?? StandardShortcutPlatform.Instance));
        // Workflow use cases (PKHeX.Application.UseCases) are stateless and constructed at call sites.
        return services;
    }
}
