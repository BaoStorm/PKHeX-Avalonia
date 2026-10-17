using System.Buffers.Binary;
using System.Reflection;
using PKHeX.Core;

namespace PKHeX.Testing;

/// <summary>Synthetic CMPR texture, not a game-produced RSBOX fixture.</summary>
public static class RsBoxWallpaperFixture
{
    public static SAV3RSBox Create(bool enabled = true, bool payload = true)
    {
        var save = new SAV3RSBox();
        if (payload)
        {
            var texture = save.MyWallpaper;
            for (var y = 0; y < SAV3RSBox.WP_HEIGHT / 8; y++)
            for (var x = 0; x < SAV3RSBox.WP_WIDTH / 8; x++)
            for (var sub = 0; sub < 4; sub++)
            {
                var offset = ((y * (SAV3RSBox.WP_WIDTH / 8) + x) * 4 + sub) * 8;
                ushort color = sub switch { 0 => 0xF800, 1 => 0x001F, 2 => 0x07E0, _ => 0xFFFF };
                if (x == SAV3RSBox.WP_WIDTH / 8 - 1 && y == SAV3RSBox.WP_HEIGHT / 8 - 1 && sub == 3)
                    color = 0xFFE0;
                BinaryPrimitives.WriteUInt16BigEndian(texture[offset..], color);
            }
            // Known transparent block: endpoint0 <= endpoint1 and every selector is 3.
            BinaryPrimitives.WriteUInt16BigEndian(texture[24..], 0);
            BinaryPrimitives.WriteUInt16BigEndian(texture[26..], 0xFFFF);
            BinaryPrimitives.WriteUInt32BigEndian(texture[28..], uint.MaxValue);
        }
        save.MyWallpaperEnabled = enabled;
        save.State.Edited = false;
        return save;
    }

    // RSBOX keeps uncommitted boxes, wallpaper and flags outside SaveFile.Data. Read the
    // complete private backing memory only in tests so a partial-buffer assertion cannot
    // miss a source mutation. Calling Clone/Write here would serialize or normalize state.
    public static byte[] BoxBuffer(SAV3RSBox save) => ((Memory<byte>)typeof(SAV3RSBox)
        .GetField("Boxes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(save)!).ToArray();
}
