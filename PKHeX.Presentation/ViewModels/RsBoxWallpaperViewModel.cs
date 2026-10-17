using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Abstractions;
using PKHeX.Application.UseCases;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Read-only image snapshot. No save reference is retained after construction.</summary>
public partial class RsBoxWallpaperViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public Action? CloseRequested { get; set; }
    public byte[]? PreviewPng { get; }
    public bool HasImage => PreviewPng is { Length: > 0 };
    public bool IsEnabledInSave { get; }
    public bool CanExport => !_disposed && HasImage;
    [ObservableProperty] private string _status = string.Empty;

    public RsBoxWallpaperViewModel(SAV3RSBox save, IImageCodec imageCodec, IDialogService dialogs)
    {
        _dialogs = dialogs;
        var wallpaper = new ReadRsBoxWallpaperUseCase().Execute(save);
        if (wallpaper is null)
        {
            Status = LocalizedStrings.Instance["RsBoxWallpaper_Unavailable"];
            return;
        }
        IsEnabledInSave = wallpaper.IsEnabled;
        try
        {
            PreviewPng = imageCodec.EncodePng(wallpaper.Image);
            if (!IsEnabledInSave)
                Status = LocalizedStrings.Instance["RsBoxWallpaper_Disabled"];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = LocalizedStrings.Instance.Format("RsBoxWallpaper_PreviewFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport), IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (!CanExport)
            return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var path = await _dialogs.SaveFileAsync(LocalizedStrings.Instance["RsBoxWallpaper_Export"], "rsbox-wallpaper.png", ["png"]);
            if (string.IsNullOrEmpty(path) || linked.IsCancellationRequested)
                return;
            await new ExportWallpaperPngUseCase().ExecuteAsync(path, PreviewPng!, linked.Token);
            if (!_disposed)
                Status = LocalizedStrings.Instance["RsBoxWallpaper_Exported"];
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (!_disposed)
                await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["RsBoxWallpaper_ExportFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private void Close()
    {
        Dispose();
        CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        ExportCommand.NotifyCanExecuteChanged();
    }
}
