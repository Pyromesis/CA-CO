using CaCo.App.Pages;
using CaCo.App.ViewModels;
using CaCo.Application.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CaCo.App.Services;

/// <summary>Registro de los servicios propios de la UI en el contenedor DI.</summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>Registra navegación, diálogos, picker, ventana y ViewModels.</summary>
    public static IServiceCollection AddCacoUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFileLauncherService, FileLauncherService>();
        services.AddSingleton<IVoiceDictationService, VoiceDictationService>();
        services.AddSingleton<IVoiceMessageSessionFactory, VoiceMessageSessionFactory>();
        services.AddSingleton<ILanguageFeatureInstaller, LanguageFeatureInstaller>();
        services.AddSingleton<IImageOcrService, ImageOcrService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<INavigationService>(sp =>
        {
            var navigation = new NavigationService();
            navigation.Register<HomeViewModel, HomePage>();
            navigation.Register<DocumentsViewModel, DocumentsPage>();
            navigation.Register<DocumentDetailViewModel, DocumentDetailPage>();
            navigation.Register<ReaderViewModel, ReaderPage>();
            navigation.Register<NotebooksViewModel, NotebooksPage>();
            navigation.Register<FavoritesViewModel, FavoritesPage>();
            navigation.Register<RecentsViewModel, RecentsPage>();
            navigation.Register<TrashViewModel, TrashPage>();
            navigation.Register<SettingsViewModel, SettingsPage>();
            return navigation;
        });

        services.AddSingleton<MainWindow>();

        services.AddTransient<HomeViewModel>();
        services.AddTransient<DocumentsViewModel>();
        services.AddTransient<DocumentDetailViewModel>();
        services.AddTransient<ReaderViewModel>();
        services.AddTransient<NotebooksViewModel>();
        services.AddTransient<FavoritesViewModel>();
        services.AddTransient<RecentsViewModel>();
        services.AddTransient<TrashViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
