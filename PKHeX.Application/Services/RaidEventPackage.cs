using System.Buffers.Binary;
using System.Text.RegularExpressions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Game family whose News/BCAT raid tables a package targets.</summary>
public enum RaidEventGame { SwordShield, ScarletViolet }

/// <summary>Published raid-package layout, identified from the file names alone.</summary>
public enum RaidEventPackageLayout
{
    /// <summary>Sword/Shield before the Isle of Armor update: four tables.</summary>
    SwordShieldBase,
    /// <summary>Sword/Shield Isle of Armor update: adds <c>normal_encount_rigel1</c>.</summary>
    SwordShieldIsleOfArmor,
    /// <summary>Sword/Shield Crown Tundra update: adds <c>normal_encount_rigel2</c>.</summary>
    SwordShieldCrownTundra,
    /// <summary>Scarlet/Violet tables without a version suffix (game 1.0 to 1.2).</summary>
    ScarletVioletUnversioned,
    /// <summary>Scarlet/Violet tables suffixed <c>_1_3_0</c>.</summary>
    ScarletViolet130,
    /// <summary>Scarlet/Violet tables suffixed <c>_2_0_0</c> (The Teal Mask update).</summary>
    ScarletViolet200,
    /// <summary>Scarlet/Violet tables suffixed <c>_3_0_0</c> (The Indigo Disk update).</summary>
    ScarletViolet300,
}

/// <summary>Why a package or an import plan was rejected. Nothing is written for any of these.</summary>
public enum RaidEventPackageError
{
    None,
    FolderNotFound,
    Empty,
    UnknownFile,
    DuplicateFile,
    MixedGames,
    MixedVersions,
    UnsupportedVersion,
    MissingFile,
    WrongLength,
    ReadFailed,
    WrongGame,
    MissingDestination,
    DestinationMismatch,
    PackageNewerThanSave,
}

/// <summary>Non-blocking differences between a valid package and the target save; each must be acknowledged.</summary>
public enum RaidEventImportWarning
{
    /// <summary>The package was published for an earlier game update than the save's data layout.</summary>
    OlderPackageFormat,
    /// <summary>The save has DLC-area tables that the package does not contain; they keep their current data.</summary>
    DlcTablesNotInPackage,
}

/// <summary>One raw table from a package and the save block it replaces.</summary>
public sealed record RaidEventPackageFile(string FileName, uint BlockKey, string TableName, byte[] Data);

/// <summary>A complete, internally consistent raid package. Validation against a save is a separate step.</summary>
public sealed class RaidEventPackage
{
    internal RaidEventPackage(RaidEventPackageLayout layout, string? folder, IReadOnlyList<RaidEventPackageFile> files)
    { Layout = layout; Folder = folder; Files = files; }
    public RaidEventPackageLayout Layout { get; }
    public string? Folder { get; }
    public IReadOnlyList<RaidEventPackageFile> Files { get; }
    public RaidEventGame Game => RaidEventPackageReader.GetGame(Layout);
    /// <summary>The Scarlet/Violet <c>event_raid_identifier</c> value, or null for Sword/Shield packages.</summary>
    public uint? EventIdentifier => Files.FirstOrDefault(f => f.BlockKey == RaidEventPackageReader.KSvIdentifier) is { } id
        ? BinaryPrimitives.ReadUInt32LittleEndian(id.Data) : null;
}

/// <summary>Result of reading/identifying a package; <see cref="Package"/> is set only when <see cref="Error"/> is None.</summary>
public sealed record RaidEventPackageResult(RaidEventPackage? Package, RaidEventPackageError Error, string? Detail = null)
{
    public bool IsValid => Error == RaidEventPackageError.None && Package is not null;
    internal static RaidEventPackageResult Fail(RaidEventPackageError error, string? detail = null) => new(null, error, detail);
}

/// <summary>
/// Identifies Events Gallery raid packages (Sword/Shield Wild Area Events and Scarlet/Violet Raid Events).
/// The block keys mirror the private BCAT/News constants in Core's SaveBlockAccessor8SWSH/9SV.
/// </summary>
public static class RaidEventPackageReader
{
    internal const uint KSvIdentifier = 0x37B99B4D;
    private const long MaxFileLength = 1 << 20;

    private sealed record Table(string Name, uint Key, int Length);

    private static readonly Table[] SwordShieldTables =
    [
        new("drop_rewards", 0x680EEB85, 17004),
        new("dai_encount", 0xAD3920F5, 74780),
        new("normal_encount", 0xAD9DFA6A, 9172),
        new("bonus_rewards", 0xEFCAE04E, 71364),
        new("normal_encount_rigel1", 0x0E615A8C, 9172),
        new("normal_encount_rigel2", 0x11615F45, 9172),
    ];

    private static readonly Table[] ScarletVioletTables =
    [
        new("event_raid_identifier", KSvIdentifier, 4),
        new("fixed_reward_item_array", 0x7D6C2B82, 27456),
        new("lottery_reward_item_array", 0xA52B4811, 53464),
        new("raid_enemy_array", 0x0520A1B0, 30000),
        new("raid_priority_array", 0x095451E4, 88),
    ];

    /// <summary>Sword/Shield DLC-area tables, absent from older package layouts.</summary>
    public static IReadOnlyList<(string TableName, uint BlockKey)> SwordShieldDlcTables { get; } =
        SwordShieldTables.Skip(4).Select(t => (t.Name, t.Key)).ToArray();

    private static readonly string[] IgnoredExtensions = [".json", ".txt", ".md", ".meta"];
    private static readonly string[] IgnoredNames = ["Thumbs.db", "desktop.ini"];
    private static readonly Regex SvName = new(
        @"^(event_raid_identifier|fixed_reward_item_array|lottery_reward_item_array|raid_enemy_array|raid_priority_array)(?:_(\d+_\d+_\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RaidEventGame GetGame(RaidEventPackageLayout layout) => layout switch
    {
        RaidEventPackageLayout.SwordShieldBase or RaidEventPackageLayout.SwordShieldIsleOfArmor
            or RaidEventPackageLayout.SwordShieldCrownTundra => RaidEventGame.SwordShield,
        _ => RaidEventGame.ScarletViolet,
    };

    /// <summary>
    /// Reads the top-level files of a package folder. Documentation sidecars (.json/.txt/.md/.meta) are ignored;
    /// a folder containing only a <c>Files</c> subfolder (the Scarlet/Violet gallery layout) is followed once.
    /// </summary>
    public static RaidEventPackageResult ReadFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return RaidEventPackageResult.Fail(RaidEventPackageError.FolderNotFound, folder);
            var candidates = GetCandidateFiles(folder);
            if (candidates.Count == 0 && Directory.GetDirectories(folder) is [var only] &&
                string.Equals(Path.GetFileName(only), "Files", StringComparison.OrdinalIgnoreCase))
            {
                folder = only;
                candidates = GetCandidateFiles(folder);
            }
            var files = new List<KeyValuePair<string, byte[]>>(candidates.Count);
            foreach (var info in candidates)
            {
                if (info.Length > MaxFileLength) return RaidEventPackageResult.Fail(RaidEventPackageError.WrongLength, info.Name);
                files.Add(new(info.Name, File.ReadAllBytes(info.FullName)));
            }
            var result = Identify(files);
            return result.IsValid ? result with { Package = new RaidEventPackage(result.Package!.Layout, folder, result.Package.Files) } : result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return RaidEventPackageResult.Fail(RaidEventPackageError.ReadFailed, ex.Message);
        }
    }

    private static List<FileInfo> GetCandidateFiles(string folder) => new DirectoryInfo(folder).GetFiles()
        .Where(f => !f.Name.StartsWith('.') &&
                    !IgnoredNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase) &&
                    !IgnoredExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
        .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();

    /// <summary>Identifies a package from file names and contents without touching any save.</summary>
    public static RaidEventPackageResult Identify(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        var list = files.ToList();
        if (list.Count == 0) return RaidEventPackageResult.Fail(RaidEventPackageError.Empty);

        var swsh = new Dictionary<Table, KeyValuePair<string, byte[]>>();
        var sv = new Dictionary<Table, KeyValuePair<string, byte[]>>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in list)
        {
            if (SwordShieldTables.FirstOrDefault(t => string.Equals(t.Name, file.Key, StringComparison.OrdinalIgnoreCase)) is { } swTable)
            {
                if (!swsh.TryAdd(swTable, file)) return RaidEventPackageResult.Fail(RaidEventPackageError.DuplicateFile, file.Key);
                continue;
            }
            var match = SvName.Match(file.Key);
            if (!match.Success) return RaidEventPackageResult.Fail(RaidEventPackageError.UnknownFile, file.Key);
            var svTable = ScarletVioletTables.First(t => string.Equals(t.Name, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            if (!sv.TryAdd(svTable, file))
            {
                var existingVersion = SvName.Match(sv[svTable].Key).Groups[2].Value;
                return RaidEventPackageResult.Fail(existingVersion == match.Groups[2].Value
                    ? RaidEventPackageError.DuplicateFile : RaidEventPackageError.MixedVersions, file.Key);
            }
            versions.Add(match.Groups[2].Value);
        }

        if (swsh.Count != 0 && sv.Count != 0) return RaidEventPackageResult.Fail(RaidEventPackageError.MixedGames);
        return swsh.Count != 0 ? IdentifySwordShield(swsh) : IdentifyScarletViolet(sv, versions);
    }

    private static RaidEventPackageResult IdentifySwordShield(Dictionary<Table, KeyValuePair<string, byte[]>> files)
    {
        foreach (var table in SwordShieldTables.Take(4))
            if (!files.ContainsKey(table)) return RaidEventPackageResult.Fail(RaidEventPackageError.MissingFile, table.Name);
        var hasRigel1 = files.ContainsKey(SwordShieldTables[4]);
        var hasRigel2 = files.ContainsKey(SwordShieldTables[5]);
        if (hasRigel2 && !hasRigel1) return RaidEventPackageResult.Fail(RaidEventPackageError.MissingFile, SwordShieldTables[4].Name);
        var layout = hasRigel2 ? RaidEventPackageLayout.SwordShieldCrownTundra
            : hasRigel1 ? RaidEventPackageLayout.SwordShieldIsleOfArmor : RaidEventPackageLayout.SwordShieldBase;
        return Build(layout, SwordShieldTables, files);
    }

    private static RaidEventPackageResult IdentifyScarletViolet(Dictionary<Table, KeyValuePair<string, byte[]>> files, HashSet<string> versions)
    {
        if (versions.Count != 1) return RaidEventPackageResult.Fail(RaidEventPackageError.MixedVersions);
        RaidEventPackageLayout? layout = versions.Single() switch
        {
            "" => RaidEventPackageLayout.ScarletVioletUnversioned,
            "1_3_0" => RaidEventPackageLayout.ScarletViolet130,
            "2_0_0" => RaidEventPackageLayout.ScarletViolet200,
            "3_0_0" => RaidEventPackageLayout.ScarletViolet300,
            _ => null,
        };
        if (layout is null) return RaidEventPackageResult.Fail(RaidEventPackageError.UnsupportedVersion, versions.Single());
        foreach (var table in ScarletVioletTables)
            if (!files.ContainsKey(table)) return RaidEventPackageResult.Fail(RaidEventPackageError.MissingFile, table.Name);
        return Build(layout.Value, ScarletVioletTables, files);
    }

    private static RaidEventPackageResult Build(RaidEventPackageLayout layout, Table[] order, Dictionary<Table, KeyValuePair<string, byte[]>> files)
    {
        var result = new List<RaidEventPackageFile>(files.Count);
        foreach (var table in order)
        {
            if (!files.TryGetValue(table, out var file)) continue;
            if (file.Value.Length != table.Length) return RaidEventPackageResult.Fail(RaidEventPackageError.WrongLength, file.Key);
            result.Add(new RaidEventPackageFile(file.Key, table.Key, table.Name, file.Value.ToArray()));
        }
        return new RaidEventPackageResult(new RaidEventPackage(layout, null, result), RaidEventPackageError.None);
    }
}

/// <summary>How one destination table is affected by an import plan.</summary>
public enum RaidEventBlockChange { Replace, Identical, NotInPackage }

/// <summary>One affected (or deliberately untouched) destination table.</summary>
public sealed record RaidEventBlockPlan(string TableName, uint BlockKey, string? FileName, int Length, RaidEventBlockChange Change);

/// <summary>A package validated against one save; created only by <see cref="RaidEventImportSession.Create"/>.</summary>
public sealed record RaidEventImportPlanResult(RaidEventImportSession? Session, RaidEventPackageError Error, string? Detail = null)
{
    public bool IsValid => Error == RaidEventPackageError.None && Session is not null;
}

/// <summary>Outcome of applying a staged import.</summary>
public enum RaidEventCommitResult { Applied, NoChanges, Conflict }

/// <summary>
/// Stages a validated package for one save. Nothing is written until <see cref="TryCommit"/>, which rechecks every
/// destination block against the bytes captured at staging and then writes all of them or none of them.
/// Only the package's own News tables change; current den/raid spawn state is never touched.
/// </summary>
public sealed class RaidEventImportSession
{
    private readonly ISCBlockArray _source;
    private readonly SaveFile _save;
    private readonly Dictionary<uint, (SCTypeCode Type, byte[] Data)> _staged;

    private RaidEventImportSession(SaveFile save, RaidEventPackage package, IReadOnlyList<RaidEventBlockPlan> blocks,
        IReadOnlyList<RaidEventImportWarning> warnings, Dictionary<uint, (SCTypeCode, byte[])> staged, uint? currentIdentifier)
    {
        _save = save; _source = (ISCBlockArray)save; Package = package; Blocks = blocks; Warnings = warnings;
        _staged = staged; CurrentEventIdentifier = currentIdentifier;
    }

    public RaidEventPackage Package { get; }
    public IReadOnlyList<RaidEventBlockPlan> Blocks { get; }
    public IReadOnlyList<RaidEventImportWarning> Warnings { get; }
    /// <summary>The identifier currently stored in a Scarlet/Violet save, for comparison with the package.</summary>
    public uint? CurrentEventIdentifier { get; }
    public bool HasChanges => Blocks.Any(b => b.Change == RaidEventBlockChange.Replace);

    /// <summary>Returns the target game of a save, or null when it cannot receive raid packages.</summary>
    public static RaidEventGame? GetTargetGame(SaveFile save) => save switch
    {
        SAV8SWSH => RaidEventGame.SwordShield,
        SAV9SV => RaidEventGame.ScarletViolet,
        _ => null,
    };

    /// <summary>Validates every destination block before anything can be applied.</summary>
    public static RaidEventImportPlanResult Create(SaveFile save, RaidEventPackage package)
    {
        var target = GetTargetGame(save);
        if (target is null || target != package.Game) return new(null, RaidEventPackageError.WrongGame);
        var blocks = ((ISCBlockArray)save).AllBlocks.GroupBy(b => b.Key).ToDictionary(g => g.Key, g => g.ToList());

        var plans = new List<RaidEventBlockPlan>();
        var staged = new Dictionary<uint, (SCTypeCode, byte[])>();
        foreach (var file in package.Files)
        {
            if (!blocks.TryGetValue(file.BlockKey, out var matches))
                return new(null, package.Game == RaidEventGame.SwordShield && RaidEventPackageReader.SwordShieldDlcTables.Any(t => t.BlockKey == file.BlockKey)
                    ? RaidEventPackageError.PackageNewerThanSave : RaidEventPackageError.MissingDestination, file.TableName);
            if (matches is not [{ Type: SCTypeCode.Object } live] || live.Data.Length != file.Data.Length)
                return new(null, RaidEventPackageError.DestinationMismatch, file.TableName);
            var change = live.Data.SequenceEqual(file.Data) ? RaidEventBlockChange.Identical : RaidEventBlockChange.Replace;
            plans.Add(new(file.TableName, file.BlockKey, file.FileName, file.Data.Length, change));
            staged[file.BlockKey] = (live.Type, live.Data.ToArray());
        }

        var warnings = new List<RaidEventImportWarning>();
        if (package.Game == RaidEventGame.SwordShield)
        {
            foreach (var (name, key) in RaidEventPackageReader.SwordShieldDlcTables)
            {
                if (staged.ContainsKey(key) || !blocks.TryGetValue(key, out var present) || present.Count == 0) continue;
                plans.Add(new(name, key, null, present[0].Data.Length, RaidEventBlockChange.NotInPackage));
                if (!warnings.Contains(RaidEventImportWarning.DlcTablesNotInPackage)) warnings.Add(RaidEventImportWarning.DlcTablesNotInPackage);
            }
        }
        else
        {
            var revision = ((SAV9SV)save).SaveRevision;
            var packageRevision = package.Layout switch
            {
                RaidEventPackageLayout.ScarletViolet300 => 2,
                RaidEventPackageLayout.ScarletViolet200 => 1,
                _ => 0,
            };
            if (packageRevision > revision) return new(null, RaidEventPackageError.PackageNewerThanSave);
            if (packageRevision < revision) warnings.Add(RaidEventImportWarning.OlderPackageFormat);
        }

        uint? currentIdentifier = blocks.TryGetValue(RaidEventPackageReader.KSvIdentifier, out var id) && id is [{ Data.Length: 4 } idBlock]
            ? BinaryPrimitives.ReadUInt32LittleEndian(idBlock.Data) : null;
        return new(new RaidEventImportSession(save, package, plans, warnings, staged, currentIdentifier), RaidEventPackageError.None);
    }

    /// <summary>Writes every replaced table atomically, or nothing if any destination changed since staging.</summary>
    public RaidEventCommitResult TryCommit()
    {
        var live = _source.AllBlocks.GroupBy(b => b.Key).ToDictionary(g => g.Key, g => g.ToList());
        var writes = new List<(SCBlock Block, byte[] Data)>();
        foreach (var file in Package.Files)
        {
            var (type, original) = _staged[file.BlockKey];
            if (!live.TryGetValue(file.BlockKey, out var matches) || matches is not [var block] ||
                block.Type != type || !block.Data.SequenceEqual(original))
                return RaidEventCommitResult.Conflict;
            if (!block.Data.SequenceEqual(file.Data)) writes.Add((block, file.Data));
        }
        if (writes.Count == 0) return RaidEventCommitResult.NoChanges;
        foreach (var (block, data) in writes) data.CopyTo(block.Data);
        _save.State.Edited = true;
        return RaidEventCommitResult.Applied;
    }
}
