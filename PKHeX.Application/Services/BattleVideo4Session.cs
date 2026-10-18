using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Why a battle video file could not be staged into a slot. Nothing changes for any of these.</summary>
public enum BattleVideo4ImportError { None, SlotUnavailable, InvalidSize, InvalidData }

/// <summary>Outcome of applying staged battle video changes.</summary>
public enum BattleVideo4CommitResult { Applied, NoChanges, Conflict }

/// <summary>One of the four Platinum/HeartGold/SoulSilver battle video slots.</summary>
public sealed class BattleVideo4Slot
{
    private readonly byte[]? _original;
    private readonly byte[]? _staged;

    internal BattleVideo4Slot(int index, byte[]? original)
    {
        Index = index;
        _original = original;
        _staged = original?.ToArray();
    }

    public int Index { get; }
    /// <summary>False when the game has never initialized this slot; such slots cannot receive an import.</summary>
    public bool IsAvailable => _staged is not null;
    public bool IsChanged => _staged is not null && !_staged.AsSpan().SequenceEqual(_original);
    /// <summary>A view over the staged bytes; reading teams/names does not change them.</summary>
    public BattleVideo4? Video => _staged is null ? null : new BattleVideo4(_staged);

    internal byte[]? Original => _original;
    internal byte[]? Staged => _staged;
    internal void Reset() => _original?.CopyTo(_staged!, 0);
    internal void AcceptStaged() => _staged?.CopyTo(_original!, 0);
}

/// <summary>
/// Stages Gen 4 battle video imports per slot. The save is untouched until <see cref="TryCommit"/>, which
/// rechecks every changed slot against the bytes read when the session opened and writes all or none.
/// Unlike copying a whole cloned save back, unrelated live edits made meanwhile are preserved.
/// </summary>
public sealed class BattleVideo4Session
{
    public const int SlotCount = 4;
    private readonly SAV4 _save;

    public BattleVideo4Session(SAV4 save)
    {
        _save = save;
        Slots = Enumerable.Range(0, SlotCount)
            .Select(i => new BattleVideo4Slot(i, IsSupported(save) ? save.GetBattleVideo(i)?.Data.ToArray() : null))
            .ToArray();
    }

    /// <summary>Diamond/Pearl saves have no battle video blocks.</summary>
    public static bool IsSupported(SaveFile save) => save is SAV4Pt or SAV4HGSS;

    public IReadOnlyList<BattleVideo4Slot> Slots { get; }
    public bool HasChanges => Slots.Any(s => s.IsChanged);

    /// <summary>
    /// Replaces a slot's video payload with a .bv4 file (encrypted or decrypted). The slot keeps its own
    /// block metadata (key, magic, revision, size and ID), then checksums are refreshed and the data is stored encrypted.
    /// </summary>
    public BattleVideo4ImportError Import(int index, ReadOnlySpan<byte> file)
    {
        var slot = Slots[index];
        if (slot.Staged is not { } staged) return BattleVideo4ImportError.SlotUnavailable;
        if (!BattleVideo4.IsValid(file)) return BattleVideo4ImportError.InvalidSize;
        var data = file.ToArray();
        var state = BattleVideo4.DetectEncryption(data);
        if (state == BattleVideo4DecryptionState.Invalid) return BattleVideo4ImportError.InvalidData;

        var target = new BattleVideo4(staged);
        // Unlike upstream's WinForms import, the slot's Key is also kept: the game (and Core's GetBattleVideo)
        // only treats the slot as present while it matches the key stored in the General block.
        var imported = new BattleVideo4(data)
        {
            IsDecrypted = state == BattleVideo4DecryptionState.Decrypted,
            Key = target.Key,
            Magic = target.Magic,
            Revision = target.Revision,
            BlockSize = target.BlockSize,
            BlockID = target.BlockID,
        };
        imported.RefreshChecksums();
        imported.Encrypt();
        imported.Data.CopyTo(staged);
        return BattleVideo4ImportError.None;
    }

    /// <summary>Returns a copy of the staged slot, optionally with its team/trainer region decrypted.</summary>
    public byte[]? Export(int index, bool decrypted)
    {
        if (Slots[index].Staged is not { } staged) return null;
        var copy = staged.ToArray();
        if (decrypted) new BattleVideo4(copy) { IsDecrypted = false }.Decrypt();
        return copy;
    }

    public void Reset(int index) => Slots[index].Reset();
    public void ResetAll() { foreach (var slot in Slots) slot.Reset(); }

    public BattleVideo4CommitResult TryCommit()
    {
        var writes = new List<(BattleVideo4 Live, byte[] Data)>();
        foreach (var slot in Slots)
        {
            if (!slot.IsChanged) continue;
            var live = _save.GetBattleVideo(slot.Index);
            if (live is null || !live.Data.SequenceEqual(slot.Original)) return BattleVideo4CommitResult.Conflict;
            writes.Add((live, slot.Staged!));
        }
        if (writes.Count == 0) return BattleVideo4CommitResult.NoChanges;
        foreach (var (live, data) in writes) data.CopyTo(live.Data);
        foreach (var slot in Slots) slot.AcceptStaged();
        _save.State.Edited = true;
        return BattleVideo4CommitResult.Applied;
    }
}
