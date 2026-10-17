using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.Models;

/// <summary>A stable selector identity; language changes update text without clearing selection.</summary>
public sealed class BoxWallpaperChoice(string text, int value) : ObservableObject
{
    private string _text = text;
    public int Value { get; } = value;
    public string Text { get => _text; internal set => SetProperty(ref _text, value); }
}

/// <summary>Maps each game's stored wallpaper IDs to the shared, localized Core name catalog.</summary>
public static class BoxWallpaperChoices
{
    // These are catalog positions, not the game's stored wallpaper IDs. Keep the order
    // aligned with upstream SAV_BoxLayout; Presentation must not depend on its Drawing UI.
    private enum Name
    {
        Forest, City, Desert, Savanna, Crag, Volcano, Snow, Cave, Beach, Seafloor, River, Sky,
        PokemonCenter, Machine, Checks, Simple, Special1, Special2, Special3, Special4,
        Special5, Special6, Special7, Special8,
        PolkaDot, Machine3, Plain, Friends, Stars, Tiles, Flower, Carpet, Ruin, MyWallpaper,
        Space, Backyard, Nostalgic, Torchic, Trio, Pikapika, Legend, TeamGalactic,
        Distortion, Contest, Croagunk, Heart, Soul, BigBrother, Pokeathlon, SpikyPika, KimonoGirl, Revival,
        Reshiram, Zekrom, Monochrome, TeamPlasma, Munna, Zoroark, Subway, Musical, Movie, PWT, Kyurem1, Kyurem2,
    }

    public static IReadOnlyList<ComboItem> Create(SaveFile save)
    {
        if (save is not IBoxDetailWallpaper)
            return [];

        var catalog = GameInfo.Strings.wallpapernames;
        var result = new List<ComboItem>();
        void Add(Name name, bool platinum = false)
        {
            var text = catalog[(int)name];
            if (platinum)
                text = LocalizedStrings.Instance.Format("BoxLayoutEditor_PlatinumWallpaper", text);
            result.Add(new ComboItem(text, result.Count));
        }
        void Prefix(int count)
        {
            for (int i = 0; i < count; i++) Add((Name)i);
        }
        void Extra(params Name[] names)
        {
            foreach (var name in names) Add(name);
        }
        void Placeholders(int count)
        {
            for (int i = 1; i <= count; i++)
                result.Add(new ComboItem(LocalizedStrings.Instance.Format("BoxLayoutEditor_WallpaperNumber", i), i - 1));
        }

        switch (save)
        {
            case SAV3RS:
                Prefix(12); Extra(Name.PolkaDot, Name.PokemonCenter, Name.Machine3, Name.Plain); break;
            case SAV3E:
                Prefix(12); Extra(Name.PolkaDot, Name.PokemonCenter, Name.Machine3, Name.Simple, Name.Friends); break;
            case SAV3FRLG:
                Prefix(12); Extra(Name.Stars, Name.PokemonCenter, Name.Tiles, Name.Simple); break;
            case SAV3RSBox:
                Prefix(12); Extra(Name.PolkaDot, Name.PokemonCenter, Name.Machine3, Name.Plain, Name.Flower, Name.Tiles, Name.Carpet, Name.Ruin, Name.MyWallpaper); break;
            case SAV4DP:
                Prefix(16); Extra(Name.Space, Name.Backyard, Name.Nostalgic, Name.Torchic, Name.Trio, Name.Pikapika, Name.Legend, Name.TeamGalactic); break;
            case SAV4Pt:
                Prefix(16); Extra(Name.Distortion, Name.Contest, Name.Nostalgic, Name.Croagunk, Name.Trio, Name.Pikapika, Name.Legend, Name.TeamGalactic); break;
            case SAV4HGSS:
                Prefix(16); Extra(Name.Heart, Name.Soul, Name.BigBrother, Name.Pokeathlon, Name.Trio, Name.SpikyPika, Name.KimonoGirl, Name.Revival); break;
            case SAV5BW:
                Prefix(16); Extra(Name.Reshiram, Name.Zekrom, Name.Monochrome, Name.TeamPlasma, Name.Munna, Name.Zoroark, Name.Subway, Name.Musical); break;
            case SAV5B2W2:
                Prefix(16); Extra(Name.Monochrome, Name.TeamPlasma, Name.Movie, Name.PWT, Name.Kyurem1, Name.Kyurem2, Name.Reshiram, Name.Zekrom); break;
            case SAV8BS:
                Prefix(16); Extra(Name.Space, Name.Backyard, Name.Nostalgic, Name.Torchic, Name.Trio, Name.Pikapika, Name.Legend, Name.TeamGalactic);
                foreach (var name in new[] { Name.Distortion, Name.Contest, Name.Nostalgic, Name.Croagunk, Name.Trio, Name.Pikapika, Name.Legend, Name.TeamGalactic }) Add(name, platinum: true);
                break;
            default:
                switch (save.Generation)
                {
                    case 6: Prefix(24); break;
                    case 7: Prefix(16); break;
                    case 8: Placeholders(19); break;
                    case 9: Placeholders(20); break;
                }
                break;
        }
        return result;
    }
}
