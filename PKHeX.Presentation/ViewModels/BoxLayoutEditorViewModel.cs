using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;

namespace PKHeX.Presentation.ViewModels;

public partial class BoxLayoutEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SaveFile _source;
    private readonly SaveFile _working;
    private readonly IBoxDetailNameRead? _nameReader;
    private readonly IBoxDetailName? _nameWriter;
    private readonly IBoxDetailWallpaper? _wallpaper;
    private bool _hasChanges;

    public Action? CloseRequested { get; set; }

    public BoxLayoutEditorViewModel(SaveFile sav)
    {
        _source = sav;
        _working = CloneForEditing(sav);
        _nameReader = _working as IBoxDetailNameRead;
        _nameWriter = _working as IBoxDetailName;
        _wallpaper = _working as IBoxDetailWallpaper;

        IsSupported = _nameReader is not null || _wallpaper is not null;
        CanEditNames = _nameWriter is not null;
        CanEditWallpaper = _wallpaper is not null;
        CanEditUnlocked = _working.BoxesUnlocked > 0;

        // Build list of possible unlocked box counts
        for (int i = 0; i <= _working.BoxCount; i++)
            UnlockedOptions.Add(i);
        
        _unlockedBoxes = Math.Min(_working.BoxCount, _working.BoxesUnlocked);

        WallpaperChoices = BoxWallpaperChoices.Create(_working);
        LoadBoxes();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((BoxLayoutEditorViewModel)recipient).RefreshLanguage());
    }

    public bool IsSupported { get; }
    public bool CanEditNames { get; }
    public bool CanEditWallpaper { get; }
    public bool CanEditUnlocked { get; }

    [ObservableProperty]
    private ObservableCollection<BoxLayoutItemViewModel> _boxes = [];

    [ObservableProperty]
    private ObservableCollection<int> _unlockedOptions = [];

    public IReadOnlyList<ComboItem> WallpaperChoices { get; private set; }

    [ObservableProperty]
    private int _unlockedBoxes;

    partial void OnUnlockedBoxesChanged(int value)
    {
        if (CanEditUnlocked && value >= 0 && value <= _working.BoxCount)
        {
            if (value == _working.BoxesUnlocked) return;
            _working.BoxesUnlocked = value;
            _hasChanges = true;
        }
    }

    private static SaveFile CloneForEditing(SaveFile source)
    {
        if (source is SAV3 gen3)
        {
            // Core serializes the live Gen 3 buffers before cloning. Copy those buffers
            // directly, including unexportable blank saves, to preserve the source bytes.
            SAV3 clone = gen3 switch
            {
                SAV3RS => new SAV3RS(gen3.Japanese),
                SAV3E => new SAV3E(gen3.Japanese),
                SAV3FRLG => new SAV3FRLG(gen3.Japanese),
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
            gen3.Small.CopyTo(clone.Small);
            gen3.Large.CopyTo(clone.Large);
            gen3.Storage.CopyTo(clone.Storage);
            clone.Version = gen3.Version;
            clone.Language = gen3.Language;
            return clone;
        }
        if (source is SAV3RSBox { State.Exportable: false } box)
        {
            // A blank RSBOX has no GCI header to retain its private encoding setting.
            // Ask its public codec which of the two Gen 3 alphabets it uses.
            ReadOnlySpan<byte> sample = [0x01, 0xFF];
            var japanese = box.GetString(sample) == StringConverter3.GetString(sample, true);
            var clone = new SAV3RSBox(japanese);
            clone.CopyChangesFrom(box);
            return clone;
        }
        var bytes = source.Data.ToArray();
        var edited = source.State.Edited;
        try { return source.Clone(); }
        finally { bytes.AsSpan().CopyTo(source.Data); source.State.Edited = edited; }
    }

    private void RefreshLanguage()
    {
        WallpaperChoices = BoxWallpaperChoices.Create(_working);
        OnPropertyChanged(nameof(WallpaperChoices));
        foreach (var box in Boxes) box.RefreshLanguage(WallpaperChoices);
    }

    private void LoadBoxes()
    {
        Boxes.Clear();
        for (int i = 0; i < _working.BoxCount; i++)
        {
            var name = _nameReader?.GetBoxName(i) ?? BoxDetailNameExtensions.GetDefaultBoxName(i);
            var wallpaper = _wallpaper?.GetBoxWallpaper(i) ?? 0;
            Boxes.Add(new BoxLayoutItemViewModel(i, _working.BoxCount, name, wallpaper, WallpaperChoices, OnBoxNameChanged, OnBoxWallpaperChanged));
        }
    }

    private void OnBoxNameChanged(int box, string name)
    {
        _nameWriter?.SetBoxName(box, name);
        _hasChanges = true;
    }

    private void OnBoxWallpaperChanged(int box, int wallpaper)
    {
        if (_wallpaper is not null && Boxes[box].WallpaperChoices.Any(choice => choice.Value == wallpaper))
        {
            _wallpaper.SetBoxWallpaper(box, wallpaper);
            // RSBOX stores a single wallpaper byte for each adjacent pair of boxes.
            foreach (var row in Boxes)
                row.RefreshWallpaper(_wallpaper.GetBoxWallpaper(row.Index));
            _hasChanges = true;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!IsSupported)
            return;

        if (_hasChanges)
        {
            _source.CopyChangesFrom(_working);
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void MoveUp(BoxLayoutItemViewModel? box)
    {
        if (box is null || box.Index <= 0)
            return;

        if (_working.SwapBox(box.Index, box.Index - 1))
        {
            _hasChanges = true;
            LoadBoxes();
        }
    }

    [RelayCommand]
    private void MoveDown(BoxLayoutItemViewModel? box)
    {
        if (box is null || box.Index >= _working.BoxCount - 1)
            return;

        if (_working.SwapBox(box.Index, box.Index + 1))
        {
            _hasChanges = true;
            LoadBoxes();
        }
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}

public partial class BoxLayoutItemViewModel : ViewModelBase
{
    private readonly Action<int, string> _onNameChanged;
    private readonly Action<int, int> _onWallpaperChanged;
    private readonly int _originalWallpaper;
    private IReadOnlyList<ComboItem> _knownWallpapers;

    public BoxLayoutItemViewModel(int index, int boxCount, string name, int wallpaper, IReadOnlyList<ComboItem> choices, Action<int, string> onNameChanged, Action<int, int> onWallpaperChanged)
    {
        Index = index;
        BoxCount = boxCount;
        _name = name;
        _wallpaper = wallpaper;
        _originalWallpaper = wallpaper;
        _onNameChanged = onNameChanged;
        _onWallpaperChanged = onWallpaperChanged;
        _knownWallpapers = choices;
        var rows = choices.Select(choice => new BoxWallpaperChoice(choice.Text, choice.Value)).ToList();
        if (!choices.Any(choice => choice.Value == wallpaper))
            rows.Add(new BoxWallpaperChoice(LocalizedStrings.Instance.Format("BoxLayoutEditor_UnknownWallpaper", wallpaper), wallpaper));
        WallpaperChoices = rows.ToArray();
        RefreshLanguage(choices);
    }

    public int Index { get; }
    public int BoxCount { get; }
    public string DisplayIndex => LocalizedStrings.Instance.Format("BoxLayoutEditor_BoxNumber", Index + 1);
    public bool CanMoveUp => Index > 0;
    public bool CanMoveDown => Index < BoxCount - 1;

    [ObservableProperty]
    private string _name;

    private int _wallpaper;
    public int Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (value == _wallpaper || (value != _originalWallpaper && !_knownWallpapers.Any(choice => choice.Value == value))) return;
            _wallpaper = value;
            _onWallpaperChanged(Index, value);
            OnPropertyChanged(); OnPropertyChanged(nameof(SelectedWallpaper));
        }
    }
    public IReadOnlyList<BoxWallpaperChoice> WallpaperChoices { get; }
    public BoxWallpaperChoice? SelectedWallpaper
    {
        get => WallpaperChoices.FirstOrDefault(choice => choice.Value == _wallpaper);
        // Selection can clear while Avalonia rebinds a row. It is not a user edit.
        set { if (value is not null && WallpaperChoices.Contains(value)) Wallpaper = value.Value; }
    }

    internal void RefreshWallpaper(int value)
    {
        if (_wallpaper == value) return;
        _wallpaper = value;
        RefreshLanguage(_knownWallpapers);
        OnPropertyChanged(nameof(Wallpaper));
    }

    internal void RefreshLanguage(IReadOnlyList<ComboItem> choices)
    {
        _knownWallpapers = choices;
        foreach (var choice in WallpaperChoices)
            choice.Text = choices.FirstOrDefault(known => known.Value == choice.Value)?.Text
                ?? LocalizedStrings.Instance.Format("BoxLayoutEditor_UnknownWallpaper", choice.Value);
        OnPropertyChanged(nameof(SelectedWallpaper));
        OnPropertyChanged(nameof(DisplayIndex));
    }

    partial void OnNameChanged(string value) => _onNameChanged(Index, value);
}
