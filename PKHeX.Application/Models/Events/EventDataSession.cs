using PKHeX.Core;

namespace PKHeX.Application.Models.Events;

public enum EventDataKind { Flag, SystemFlag, Work }
public sealed record EventDataOption(string Name, long Value);
public sealed record EventDataDifference(EventDataKind Kind, int Index, string Name, long Before, long After);

/// <summary>A typed field whose writes target a disposable snapshot until Commit.</summary>
public sealed class EventDataField
{
    private readonly Func<long> _read;
    private readonly Action<long> _stage;
    private readonly Action<long> _commit;
    public EventDataKind Kind { get; }
    public int Index { get; }
    public string Name { get; private set; }
    public string Category { get; private set; }
    public long Minimum { get; }
    public long Maximum { get; }
    public IReadOnlyList<EventDataOption> Options { get; private set; }
    public long OriginalValue { get; private set; }
    public long Value => _read();
    public bool IsChanged => Value != OriginalValue;
    public bool IsBoolean => Kind != EventDataKind.Work;
    public bool SupportsFloatInterpretation { get; }

    internal EventDataField(EventDataKind kind, int index, string name, string category, long min, long max,
        Func<long> read, Action<long> stage, Action<long> commit, IReadOnlyList<EventDataOption>? options = null,
        bool supportsFloat = false)
    {
        Kind = kind;
        Index = index;
        Name = name;
        Category = category;
        Minimum = min;
        Maximum = max;
        _read = read;
        _stage = stage;
        _commit = commit;
        Options = options ?? [];
        SupportsFloatInterpretation = supportsFloat;
        OriginalValue = read();
    }

    public bool TrySetValue(long value)
    {
        if (value < Minimum || value > Maximum) return false;
        _stage(value);
        return true;
    }
    public void Reset() => _stage(OriginalValue);
    internal void AcceptChanges() => OriginalValue = Value;
    internal void RefreshLabels(EventDataField other)
    {
        Name = other.Name;
        Category = other.Category;
        Options = other.Options;
    }
    internal void Commit()
    {
        _commit(Value);
        OriginalValue = Value;
    }
}

/// <summary>Game-aware Gen 4/Gen 7/LGPE/BDSP event metadata and transactional storage.</summary>
public sealed class EventDataSession
{
    private readonly SaveFile _source;
    private readonly SaveFile _snapshot;
    private readonly List<EventDataField> _fields = [];
    public IReadOnlyList<EventDataField> Fields => _fields;

    private EventDataSession(SaveFile source)
    {
        _source = source;
        var edited = source.State.Edited;
        try { _snapshot = source.Clone(); }
        finally { source.State.Edited = edited; }
        switch (_snapshot)
        {
            case SAV4 four: LoadFour(four, (SAV4)source); break;
            case SAV7 seven: LoadSeven(seven, (SAV7)source); break;
            case SAV7b letsGo: LoadLetsGo(letsGo, (SAV7b)source); break;
            case SAV8BS bdsp: LoadBdsp(bdsp, (SAV8BS)source); break;
        }
    }

    public static EventDataSession? Create(SaveFile source) => source is SAV4 or SAV7 or SAV7b or SAV8BS
        ? new EventDataSession(source) : null;

    public void Reset()
    {
        foreach (var field in Fields) field.Reset();
    }
    public void RefreshLabels()
    {
        var labels = Create(_snapshot)!;
        foreach (var pair in Fields.Zip(labels.Fields))
            pair.First.RefreshLabels(pair.Second);
    }
    public int Commit()
    {
        var changed = Fields.Where(f => f.IsChanged).ToArray();
        foreach (var field in changed) field.Commit();
        if (changed.Length != 0)
        {
            if (_source is SAV7 seven && _snapshot is SAV7 snapshot)
            {
                seven.EventWork.UpdateQrConstants();
                snapshot.EventWork.UpdateQrConstants();
            }
            foreach (var field in Fields) field.AcceptChanges();
            _source.State.Edited = true;
        }
        return changed.Length;
    }

    public static bool TryCompare(SaveFile before, SaveFile after, out IReadOnlyList<EventDataDifference> differences)
    {
        differences = [];
        if (before.GetType() != after.GetType() || before.Version != after.Version) return false;
        var left = Create(before);
        var right = Create(after);
        if (left is null || right is null || left.Fields.Count != right.Fields.Count) return false;
        differences = left.Fields.Zip(right.Fields).Where(pair => pair.First.Value != pair.Second.Value)
            .Select(pair => new EventDataDifference(pair.First.Kind, pair.First.Index, pair.First.Name,
                pair.First.Value, pair.Second.Value)).ToArray();
        return true;
    }

    private static IReadOnlyList<EventDataOption> Options(NamedEventWork? label) => label?.PredefinedValues
        .Where(option => !option.IsCustom).Select(option => new EventDataOption(option.Name, option.Value)).ToArray() ?? [];

    private void LoadFour(SAV4 snapshot, SAV4 source)
    {
        var game = snapshot switch
        {
            SAV4DP => "dp",
            SAV4Pt => "pt",
            SAV4HGSS => "hgss",
            _ => throw new ArgumentException(nameof(snapshot)),
        };
        var labels = new EventLabelCollection(game, snapshot.EventFlagCount, snapshot.EventWorkCount);
        var flags = labels.Flag.ToDictionary(f => f.Index);
        var values = labels.Work.ToDictionary(f => f.Index);
        foreach (int index in Enumerable.Range(0, snapshot.EventFlagCount))
        {
            flags.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Flag, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, 1,
                () => snapshot.GetEventFlag(index) ? 1 : 0, v => snapshot.SetEventFlag(index, v != 0),
                v => source.SetEventFlag(index, v != 0)));
        }
        foreach (int index in Enumerable.Range(0, snapshot.EventWorkCount))
        {
            values.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Work, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, ushort.MaxValue,
                () => snapshot.GetWork(index), v => snapshot.SetWork(index, (ushort)v),
                v => source.SetWork(index, (ushort)v), Options(label)));
        }
    }

    private void LoadSeven(SAV7 snapshot, SAV7 source)
    {
        var work = snapshot.EventWork;
        var labels = new EventLabelCollection(snapshot is SAV7USUM ? "usum" : "sm", work.EventFlagCount, work.EventWorkCount);
        var flags = labels.Flag.GroupBy(f => f.Index).ToDictionary(g => g.Key, g => g.First());
        var values = labels.Work.GroupBy(f => f.Index).ToDictionary(g => g.Key, g => g.First());
        foreach (int index in Enumerable.Range(0, work.EventFlagCount))
        {
            flags.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Flag, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, 1,
                () => work.GetEventFlag(index) ? 1 : 0, v => work.SetEventFlag(index, v != 0),
                v => source.EventWork.SetEventFlag(index, v != 0)));
        }
        foreach (int index in Enumerable.Range(0, work.EventWorkCount))
        {
            values.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Work, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, ushort.MaxValue,
                () => work.GetWork(index), v => work.SetWork(index, (ushort)v),
                v => source.EventWork.SetWork(index, (ushort)v), Options(label)));
        }
    }

    private void LoadLetsGo(SAV7b snapshot, SAV7b source)
    {
        var work = snapshot.Blocks.EventWork;
        var labels = new SplitEventEditor<int>(work,
            GameLanguage.GetStrings("gg", GameInfo.CurrentLanguage, "const"),
            GameLanguage.GetStrings("gg", GameInfo.CurrentLanguage, "flags"));
        var flags = labels.Flag.SelectMany(g => g.Vars).GroupBy(f => f.RawIndex).ToDictionary(g => g.Key, g => g.First());
        var values = labels.Work.SelectMany(g => g.Vars).GroupBy(f => f.RawIndex).ToDictionary(g => g.Key, g => (EventWork<int>)g.First());
        foreach (int index in Enumerable.Range(0, work.CountFlag))
        {
            flags.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Flag, index, label?.Name ?? string.Empty,
                LetsGoCategory(work, index, flag: true), 0, 1,
                () => work.GetFlag(index) ? 1 : 0, v => work.SetFlag(index, v != 0),
                v => source.Blocks.EventWork.SetFlag(index, v != 0)));
        }
        foreach (int index in Enumerable.Range(0, work.CountWork))
        {
            values.TryGetValue(index, out var label);
            var category = LetsGoCategory(work, index, flag: false);
            var options = label?.Options.Where(o => !o.Custom).Select(o => new EventDataOption(o.Text, o.Value)).ToArray();
            _fields.Add(new(EventDataKind.Work, index, label?.Name ?? string.Empty, category, int.MinValue, int.MaxValue,
                () => work.GetWork(index), v => work.SetWork(index, (int)v),
                v => source.Blocks.EventWork.SetWork(index, (int)v), options));
        }
    }

    private static string LetsGoCategory(IEventVar<int> work, int index, bool flag)
    {
        try
        {
            var type = flag ? work.GetFlagType(index, out _) : work.GetWorkType(index, out _);
            return type == EventVarType.Vanish ? flag ? "Vanish" : "Scene" : type.ToString();
        }
        catch (ArgumentOutOfRangeException)
        {
            // The table includes unclassified trailing work values. Keep them accessible.
            return "None";
        }
    }

    private void LoadBdsp(SAV8BS snapshot, SAV8BS source)
    {
        var work = snapshot.FlagWork;
        var labels = new EventLabelCollectionSystem("bdsp", work.CountFlag, work.CountSystem, work.CountWork);
        var flags = labels.Flag.GroupBy(f => f.Index).ToDictionary(g => g.Key, g => g.First());
        var systems = labels.System.GroupBy(f => f.Index).ToDictionary(g => g.Key, g => g.First());
        var values = labels.Work.GroupBy(f => f.Index).ToDictionary(g => g.Key, g => g.First());
        foreach (int index in Enumerable.Range(0, work.CountFlag))
        {
            flags.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Flag, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, 1,
                () => work.GetFlag(index) ? 1 : 0, v => work.SetFlag(index, v != 0),
                v => source.FlagWork.SetFlag(index, v != 0)));
        }
        foreach (int index in Enumerable.Range(0, work.CountSystem))
        {
            systems.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.SystemFlag, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", 0, 1,
                () => work.GetSystemFlag(index) ? 1 : 0, v => work.SetSystemFlag(index, v != 0),
                v => source.FlagWork.SetSystemFlag(index, v != 0)));
        }
        foreach (int index in Enumerable.Range(0, work.CountWork))
        {
            values.TryGetValue(index, out var label);
            _fields.Add(new(EventDataKind.Work, index, label?.Name ?? string.Empty, label?.Type.ToString() ?? "None", int.MinValue, int.MaxValue,
                () => work.GetWork(index), v => work.SetWork(index, (int)v),
                v => source.FlagWork.SetWork(index, (int)v), Options(label), supportsFloat: true));
        }
    }
}
