using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

public sealed record RsBoxWallpaper(PixelImage Image, bool IsEnabled);

/// <summary>Reads the complete stored RSBOX image without cloning or serializing the save.</summary>
public sealed class ReadRsBoxWallpaperUseCase
{
    public static bool IsAvailable(SaveFile? save) => save is SAV3RSBox box &&
        (box.MyWallpaperEnabled || box.MyWallpaper.IndexOfAnyExcept((byte)0) >= 0);

    public RsBoxWallpaper? Execute(SAV3RSBox save)
    {
        if (!IsAvailable(save))
            return null;
        // Despite its RGBA documentation, mirrored CMPR writes B, G, R, A. This is the
        // existing IImageCodec boundary's BGRA layout; do not swap red and blue again.
        var pixels = CMPR.Decompress(save.MyWallpaper, SAV3RSBox.WP_WIDTH, SAV3RSBox.WP_HEIGHT);
        return new(new PixelImage(SAV3RSBox.WP_WIDTH, SAV3RSBox.WP_HEIGHT, pixels), save.MyWallpaperEnabled);
    }
}

/// <summary>Completes a PNG export atomically; cancellation never replaces an existing file.</summary>
public sealed class ExportWallpaperPngUseCase
{
    public async Task ExecuteAsync(string path, ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".pkhex-wallpaper-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, png, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
