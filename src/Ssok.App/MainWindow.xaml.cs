using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ssok.Core;

namespace Ssok.App;

public partial class MainWindow : Window
{
    private const int MaxFilesFromFolder = 2000;

    private readonly ObservableCollection<FileItem> _items = new();
    private readonly UserSettings _settings;
    private bool _busy;
    private bool _loading = true;
    private string? _lastResultPath;
    private string? _lastSaveDir;
    private string _saveNote = "";

    /// <summary>줄이는 중일 때만 있다. '멈추기'를 누르면 취소를 알린다.</summary>
    private CancellationTokenSource? _cts;
    private bool _closeAfterStop;

    /// <summary>줄인 결과를 저장 단추를 누를 때까지 잠시 두는 곳. 쏙을 닫으면 지운다.</summary>
    private string _holdDir = NewHoldDir();

    private static string NewHoldDir()
        => Path.Combine(ImageWork.TempRoot, $"hold-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}");

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _items;
        _items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (FileItem i in e.NewItems) i.PropertyChanged += OnItemChanged;
            if (e.OldItems is not null) foreach (FileItem i in e.OldItems) i.PropertyChanged -= OnItemChanged;
            RefreshState();
        };

        _settings = UserSettings.Load();
        ApplySettings();
        _loading = false;

        VersionText.Text = $"쏙 버전 {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        UpdateSection.Visibility = Updater.Enabled ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await CheckForUpdateAsync(force: false);

        // 작은 화면(노트북 150% 배율 등)에서는 창이 화면 밖으로 나가지 않게 줄인다.
        var work = SystemParameters.WorkArea;
        if (Height > work.Height - 16) Height = Math.Max(MinHeight, work.Height - 16);
        if (Width > work.Width - 16) Width = Math.Max(MinWidth, work.Width - 16);
        RefreshState();
    }

    // ── 설정 ──────────────────────────────────────────────────

    private void ApplySettings()
    {
        (_settings.Combined ? CombinedMode : PerFileMode).IsChecked = true;
        TargetBox.Text = _settings.TargetMb.ToString("0.##", CultureInfo.InvariantCulture);
        SyncPresetToTarget();
        PhotoFormatBox.SelectedIndex = (int)_settings.PhotoFormat;
        RemoveLocationBox.IsChecked = _settings.RemoveLocation;
        PdfGrayBox.IsChecked = _settings.PdfGray;
        CheckUpdatesBox.IsChecked = _settings.CheckUpdates;
        UpdateModeHint();
    }

    private void SaveSettings()
    {
        if (_loading) return;
        _settings.Combined = CombinedMode.IsChecked == true;
        if (TryGetTargetMb(out var mb)) _settings.TargetMb = mb;
        _settings.PhotoFormat = (PhotoFormat)Math.Max(0, PhotoFormatBox.SelectedIndex);
        _settings.RemoveLocation = RemoveLocationBox.IsChecked == true;
        _settings.PdfGray = PdfGrayBox.IsChecked == true;
        _settings.CheckUpdates = CheckUpdatesBox.IsChecked == true;
        _settings.Save();
    }

    private bool TryGetTargetMb(out double mb)
    {
        mb = 0;
        var text = TargetBox?.Text?.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mb) && mb >= 0.01 && mb <= 2000;
    }

    private bool TryGetTargetBytes(out long bytes)
    {
        bytes = 0;
        if (!TryGetTargetMb(out var mb)) return false;
        // 제출 시스템에 걸리지 않게 조금 더 작게 맞춘다 (화면에는 적은 값 그대로).
        bytes = TargetSize.SafeBytesFromMb(mb);
        return bytes >= TargetSize.MinBytes;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateModeHint();
        SaveSettings();
        RefreshState();
    }

    private void UpdateModeHint()
    {
        if (ModeHint is null) return;
        ModeHint.Text = CombinedMode.IsChecked == true
            ? "넣은 파일을 모두 더한 용량이 이 용량 이하가 되도록 줄여요."
            : "파일 하나하나를 이 용량 이하로 줄여요.";
    }

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mb } && TargetBox is not null && TargetBox.Text != mb)
            TargetBox.Text = mb;
    }

    private void OnTargetChanged(object sender, TextChangedEventArgs e)
    {
        if (TargetError is null) return; // InitializeComponent 도중
        TargetError.Visibility = TryGetTargetBytes(out _) ? Visibility.Collapsed : Visibility.Visible;
        SyncPresetToTarget();
        SaveSettings();
        RefreshState();
    }

    /// <summary>직접 입력한 값이 칩과 같으면 그 칩을 켜고, 아니면 모두 끈다.</summary>
    private void SyncPresetToTarget()
    {
        if (Presets is null) return;
        TryGetTargetMb(out var mb);
        foreach (var chip in Presets.Children.OfType<RadioButton>())
        {
            bool match = chip.Tag is string t && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                         && Math.Abs(v - mb) < 0.0001;
            if (chip.IsChecked != match) chip.IsChecked = match;
        }
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e) => SaveSettings();

    // ── 파일 넣기 ──────────────────────────────────────────────

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data.GetDataPresent(DataFormats.FileDrop))
            DropOutline.Stroke = (System.Windows.Media.Brush)FindResource("Accent");
    }

    private void OnDragLeave(object sender, DragEventArgs e)
        => DropOutline.Stroke = (System.Windows.Media.Brush)FindResource("DropLine");

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        if (_busy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        AddPaths(paths);
    }

    private void OnPickFiles(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dlg = new OpenFileDialog
        {
            Title = "줄일 파일 고르기",
            Multiselect = true,
            Filter = "줄일 수 있는 파일|*.pdf;*.hwp;*.hwpx;*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.bmp"
                   + "|문서 (pdf·hwp·hwpx)|*.pdf;*.hwp;*.hwpx"
                   + "|사진 (jpg·png·heic·webp·bmp)|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.bmp"
                   + "|모든 파일|*.*",
        };
        if (dlg.ShowDialog(this) == true) AddPaths(dlg.FileNames);
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dlg = new OpenFolderDialog { Title = "줄일 파일이 든 폴더 고르기", Multiselect = true };
        if (dlg.ShowDialog(this) == true) AddPaths(dlg.FolderNames);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        // 파일 이름은 따지지 않는다. 쏙으로 줄인 파일(_쏙)도 그대로 넣고 다시 줄일 수 있다.
        int added = 0, duplicate = 0, failed = 0;
        foreach (var p in ExpandFolders(paths))
        {
            if (_items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) { duplicate++; continue; }
            try { _items.Add(new FileItem(p)); added++; }
            catch (Exception ex) { failed++; AppLog.Error("add", ex); }
        }

        // 넣지 않은 파일이 있으면 왜 안 들어갔는지 알려 준다.
        var notes = new List<string>();
        if (duplicate > 0) notes.Add($"이미 목록에 있는 파일 {duplicate}개는 다시 넣지 않았어요.");
        if (failed > 0) notes.Add($"열 수 없는 파일 {failed}개는 넣지 못했어요.");
        var note = string.Join(" ", notes);

        if (added == 0 && note.Length > 0 && _items.Count == 0)
            MessageBox.Show(this, note, "쏙", MessageBoxButton.OK, MessageBoxImage.Information);
        ShowAddNote(note);
    }

    private void ShowAddNote(string note)
    {
        if (AddNote is null) return;
        AddNote.Text = note;
        AddNote.ToolTip = note.Length > 0 ? note : null;
        AddNote.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>폴더를 넣으면 그 안(하위 폴더 포함)의 지원 파일을 모두 넣는다.</summary>
    private static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        int count = 0;
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                List<string> files;
                try
                {
                    var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                    files = Directory.EnumerateFiles(p, "*", opts)
                        .Where(f => FileKinds.Detect(f) != FileKind.Unsupported)
                        .Take(MaxFilesFromFolder)
                        .ToList();
                }
                catch { continue; }
                foreach (var f in files)
                {
                    if (++count > MaxFilesFromFolder) yield break;
                    yield return f;
                }
            }
            else if (File.Exists(p))
            {
                if (++count > MaxFilesFromFolder) yield break;
                yield return p;
            }
        }
    }

    /// <summary>줄 끝 ✕(목록에서 빼기): 이 파일만 목록에서 뺀다. 원본 파일은 지우지 않는다.</summary>
    private void OnRemoveOne(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: FileItem item }) return;
        if (item.CanSave && !item.Saved &&
            MessageBox.Show(this,
                $"'{item.Name}'의 줄인 결과를 아직 다운로드하지 않았습니다.\n목록에서 빼면 줄인 파일은 사라집니다. 뺄까요?",
                "쏙", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        // 이 파일의 결과를 잠시 둔 칸(보관 폴더 안)만 지운다. 원본은 건드리지 않는다.
        if (item.ResultPath is { } result && Path.GetDirectoryName(result) is { } dir
            && IsInside(dir, _holdDir))
            TryDelete(dir);

        if (_lastResultPath is not null && item.ResultPath is not null
            && string.Equals(_lastResultPath, item.ResultPath, StringComparison.OrdinalIgnoreCase))
            _lastResultPath = null;

        _items.Remove(item);
        ShowAddNote("");
        RefreshState();
    }

    private static bool IsInside(string path, string root)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(top, StringComparison.OrdinalIgnoreCase) && full.Length > top.Length;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_busy || !ConfirmDiscard("목록을 비울까요?")) return;
        _items.Clear();
        _lastResultPath = null;
        _saveNote = "";
        ShowAddNote("");
        DeleteHoldDir();
        _holdDir = NewHoldDir();
        RefreshState();
    }

    /// <summary>저장하지 않은 결과가 있으면 한 번 더 묻는다.</summary>
    private bool ConfirmDiscard(string question)
    {
        int unsaved = Savable().Count(i => !i.Saved);
        if (unsaved == 0) return true;
        return MessageBox.Show(this,
            $"아직 저장하지 않은 결과가 {unsaved}개 있습니다.\n저장하지 않으면 줄인 파일은 사라집니다. {question}",
            "쏙", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void DeleteHoldDir()
    {
        try { if (Directory.Exists(_holdDir)) Directory.Delete(_holdDir, recursive: true); }
        catch (Exception ex) { AppLog.Error("hold-clean", ex); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_cts is not null)
        {
            // 줄이는 중이면 멈춘 뒤에 닫는다 (작업 스레드가 파일을 쓰는 도중에 끝나지 않도록).
            if (MessageBox.Show(this, "줄이는 중입니다. 멈추고 닫을까요?", "쏙",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                _closeAfterStop = true;
                _cts.Cancel();
                RefreshState();
            }
            e.Cancel = true;
        }
        else if (_busy)
        {
            MessageBox.Show(this, "저장하는 중입니다. 끝난 뒤에 닫아 주세요.", "쏙", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
        }
        else if (_closeAfterStop)
        {
            // 멈춘 뒤 닫기: 이미 한 번 물었으므로 다시 묻지 않는다.
        }
        else if (!ConfirmDiscard("그래도 닫을까요?"))
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        DeleteHoldDir();
        base.OnClosed(e);
    }

    // ── 실행 ───────────────────────────────────────────────────

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        // 줄이는 중이면 이 단추는 '멈추기'다.
        if (_cts is not null)
        {
            _cts.Cancel();
            RefreshState();
            return;
        }
        if (_busy || !TryGetTargetBytes(out var target)) return;

        // 남은 일이 없으면 '다시 줄이기': 결과를 지우고 원본에서 새 설정으로 다시 줄인다.
        bool hasWork = _items.Any(i => FileKinds.IsReadyNow(i.Kind) && i.State is ItemState.Waiting or ItemState.Failed);
        if (!hasWork)
        {
            if (!HasFinished() || !ConfirmDiscard("지우고 원본에서 다시 줄일까요?")) return;
            ResetAll();
        }

        bool combined = CombinedMode.IsChecked == true;
        // "모두 합쳐서"는 ZIP 하나로 받아도 목표 안에 들도록 ZIP으로 묶을 때 늘어나는 양을 미리 뺀다.
        if (combined) target -= ZipReserve();
        var globalFormat = (PhotoFormat)Math.Max(0, PhotoFormatBox.SelectedIndex);
        bool removeLocation = RemoveLocationBox.IsChecked == true;
        bool pdfGray = PdfGrayBox.IsChecked == true;

        var work = _items.Where(i => FileKinds.IsReadyNow(i.Kind) && i.State is ItemState.Waiting or ItemState.Failed).ToList();
        if (work.Count == 0) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        var cancel = _cts.Token;
        RefreshState();
        try
        {
            // "모두 합쳐서": 이미 끝난 파일의 결과 크기를 빼고 남은 예산을 남은 파일에 나눈다.
            long fixedBytes = _items.Except(work).Sum(i => i.ResultBytes ?? i.OriginalBytes);
            long budget = target - fixedBytes;

            for (int n = 0; n < work.Count; n++)
            {
                if (cancel.IsCancellationRequested) break;
                var item = work[n];
                long fileTarget = target;
                if (combined)
                {
                    var remaining = work.Skip(n).Select(i => i.OriginalBytes).ToList();
                    fileTarget = TargetPlanner.Split(Math.Max(0, budget), remaining)[0];
                }

                if (!await ShrinkItemAsync(item, fileTarget, globalFormat, removeLocation, pdfGray, cancel, keepOnCancel: false))
                    break; // 전체 멈추기
                budget -= item.ResultBytes ?? item.OriginalBytes;
            }

            // 화질을 지키느라 목표에 못 미친 파일이 있으면, 알아보기 어려워도 목표에 맞출지 한 번만 묻는다
            // (목적 1순위는 용량 맞추기). 고르면 이번 작업의 나머지(합계 다시 맞추기 포함)도 같은 방식으로 한다.
            bool force = false;
            var blocked = work.Where(i => i.CanForce).ToList();
            if (blocked.Count > 0 && !cancel.IsCancellationRequested && AskForce(blocked.Count))
            {
                force = true;
                // 합쳐서 모드: 나머지 파일이 쓰고 남은 용량을 못 맞춘 파일끼리 원래 크기에 비례해 나눈다.
                long room = target - _items.Except(blocked).Sum(i => i.ResultBytes ?? i.OriginalBytes);
                var shares = TargetPlanner.Split(Math.Max(0, room), blocked.Select(i => i.OriginalBytes).ToList());
                for (int k = 0; k < blocked.Count; k++)
                {
                    if (cancel.IsCancellationRequested) break;
                    long goal = combined ? shares[k] : target;
                    if (!await ShrinkItemAsync(blocked[k], goal, globalFormat, removeLocation, pdfGray, cancel, keepOnCancel: true, force: true))
                        break;
                }
            }

            // "모두 합쳐서" 다시 맞추기: 더 줄일 수 없는 파일(목표 미달)이 있어 합계가 넘치면,
            // 아직 여유가 있는 파일(목표에 맞춘 파일·그대로 둔 파일)을 그만큼 더 줄인다.
            if (combined)
            {
                for (int round = 0; round < 4 && !cancel.IsCancellationRequested; round++)
                {
                    long total = _items.Sum(i => i.ResultBytes ?? i.OriginalBytes);
                    if (total <= target) break;
                    var flexible = work.Where(i => i.State == ItemState.Done || (i.State == ItemState.Skipped && i.CanSave)).ToList();
                    if (flexible.Count == 0) break;
                    long room = target - _items.Except(flexible).Sum(i => i.ResultBytes ?? i.OriginalBytes);
                    if (room < TargetSize.MinBytes * flexible.Count) break; // 가장 작게 줄여도 못 맞춤
                    var shares = TargetPlanner.Split(room, flexible.Select(i => i.ResultBytes ?? i.OriginalBytes).ToList());
                    bool changed = false;
                    for (int k = 0; k < flexible.Count; k++)
                    {
                        if (cancel.IsCancellationRequested) break;
                        var item = flexible[k];
                        if ((item.ResultBytes ?? item.OriginalBytes) <= shares[k]) continue;
                        changed = true;
                        if (!await ShrinkItemAsync(item, shares[k], globalFormat, removeLocation, pdfGray, cancel, keepOnCancel: true, force: force))
                            break;
                    }
                    if (!changed) break;
                }
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _busy = false;
            _saveNote = "";
            RefreshState();
            if (_closeAfterStop) Close();
        }
    }

    /// <summary>
    /// 파일 하나를 fileTarget 이하로 줄여 결과를 목록에 적는다. 전체 '멈추기'를 눌렀으면 false.
    /// keepOnCancel: 다시 줄이다가 멈추면 앞서 만든 결과를 그대로 둔다 (합계 다시 맞추기용).
    /// force: 알아보기 어려울 만큼 줄여서라도 목표에 맞춘다 (사용자가 창에서 고른 경우).
    /// </summary>
    private async Task<bool> ShrinkItemAsync(FileItem item, long fileTarget, PhotoFormat globalFormat, bool removeLocation,
                                             bool pdfGray, CancellationToken cancel, bool keepOnCancel, bool force = false)
    {
        var before = (item.ResultBytes, item.ResultPath, item.PreviewBefore, item.PreviewAfter, item.Saved,
                      item.SaveSource, item.SaveName, item.IsSelected, item.Message, item.State);

        item.State = ItemState.Working;
        item.Message = "";

        var options = new WorkOptions(fileTarget, item.EffectiveFormat(globalFormat), removeLocation, pdfGray, Force: force);
        // 파일마다 칸을 따로 두어, 이름이 같은 파일도 저장할 때 원래 이름을 쓸 수 있게 한다.
        var holdFor = Path.Combine(_holdDir, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(holdFor);
        // ✕(이 파일만 멈추기)와 '멈추기'(전체) 어느 쪽이든 이 파일을 멈춘다.
        using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        item.Cancel = itemCts;
        item.Progress = 0;
        item.Message = "준비 중";
        // Progress<T>는 만든 스레드(화면)로 알림을 옮겨 준다.
        var report = new Progress<(double Fraction, string Note)>(p =>
        {
            if (item.State != ItemState.Working) return;
            item.Progress = Math.Clamp(p.Fraction, 0, 1) * 100;
            item.Message = p.Note;
        });
        IProgress<(double, string)> reporter = report;
        var outcome = await ImageWork.ShrinkAsync(item.Path, item.Kind, options, holdFor, itemCts.Token,
            (f, note) => reporter.Report((f, note)));
        item.Cancel = null;
        item.Progress = 0;
        if (outcome.Result == WorkResult.Canceled)
        {
            TryDelete(holdFor);
            if (keepOnCancel)
            {
                (item.ResultBytes, item.ResultPath, item.PreviewBefore, item.PreviewAfter, item.Saved,
                 item.SaveSource, item.SaveName, item.IsSelected, item.Message, item.State) = before;
            }
            else
            {
                item.ResultBytes = null;
                item.CanForce = false;
                item.State = ItemState.Waiting;
                item.Message = outcome.Message;
            }
            return !cancel.IsCancellationRequested; // 이 파일만 멈췄으면 다음 파일로
        }

        // 앞서 만든 결과가 있으면 지운다 (보관 폴더 안의 것만).
        if (before.ResultPath is { } oldResult && Path.GetDirectoryName(oldResult) is { } oldDir
            && IsInside(oldDir, _holdDir) && !string.Equals(oldDir, holdFor, StringComparison.OrdinalIgnoreCase))
            TryDelete(oldDir);

        item.ResultBytes = outcome.ResultBytes;
        item.ResultPath = outcome.ResultPath;
        item.CanForce = outcome.CanForce;
        item.PreviewBefore = outcome.PreviewBefore;
        item.PreviewAfter = outcome.PreviewAfter;
        item.Saved = false;
        if (outcome.ResultPath is not null)
        {
            item.SaveSource = outcome.ResultPath;
            item.SaveName = Path.GetFileName(outcome.ResultPath);
            item.IsSelected = true; // 다 줄이면 바로 받을 수 있게 체크해 둔다
        }
        else if (outcome.Result == WorkResult.AlreadySmall)
        {
            // 이미 목표보다 작은 파일도 함께 저장해야 제출할 파일이 한곳에 모인다.
            item.SaveSource = item.Path;
            item.SaveName = Path.GetFileNameWithoutExtension(item.Path) + SafeOutput.Suffix + Path.GetExtension(item.Path);
            item.IsSelected = true;
        }
        else
        {
            item.SaveSource = item.SaveName = null;
        }
        item.Message = outcome.Message;
        item.State = outcome.Result switch
        {
            WorkResult.Done => ItemState.Done,
            WorkResult.Partial => ItemState.Partial,
            WorkResult.AlreadySmall => ItemState.Skipped,
            _ => ItemState.Failed,
        };
        return true;
    }

    /// <summary>알아보기 어려울 만큼 줄여서라도 목표에 맞출지 묻는다. 기본은 '예' (용량 맞추기가 먼저).</summary>
    private bool AskForce(int count)
        => MessageBox.Show(this,
               $"목표 용량에 맞추려면 글자나 사진을 알아보기 어려울 만큼 줄여야 하는 파일이 {count}개 있습니다.\n\n" +
               "그래도 목표에 맞춰 줄일까요? 원본은 그대로 남습니다.",
               "쏙", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;

    /// <summary>목록의 파일을 모두 ZIP 하나로 받을 때 늘어나는 양. 파일이 하나면 ZIP으로 묶지 않으므로 0.</summary>
    private long ZipReserve()
        => _items.Count > 1 ? TargetPlanner.ZipOverhead(_items.Select(PlannedSaveName)) : 0;

    /// <summary>저장할 이름. 아직 줄이기 전이면 원래이름_쏙.확장자로 어림한다.</summary>
    private static string PlannedSaveName(FileItem item)
        => item.SaveName ?? Path.GetFileNameWithoutExtension(item.Path) + SafeOutput.Suffix + Path.GetExtension(item.Path);

    private bool HasFinished()
        => _items.Any(i => FileKinds.IsReadyNow(i.Kind) && i.State is not (ItemState.Waiting or ItemState.Working));

    /// <summary>모든 파일을 처음(대기) 상태로 돌리고, 잠시 보관한 결과를 지운다.</summary>
    private void ResetAll()
    {
        foreach (var item in _items) item.ResetResult();
        DeleteHoldDir();
        _holdDir = NewHoldDir();
        _saveNote = "";
        RefreshState();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy || !HasFinished()) return;
        if (!ConfirmDiscard("초기화할까요?")) return;
        ResetAll();
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    // ── 다운로드 (웹하드처럼: 체크해서 받기 / 줄마다 받기) ─────────

    private List<FileItem> Savable()
        => _items.Where(i => i.SaveSource is not null && i.SaveName is not null && File.Exists(i.SaveSource)).ToList();

    private List<FileItem> Selected() => Savable().Where(i => i.IsSelected).ToList();

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItem.IsSelected) or nameof(FileItem.Saved)) RefreshState();
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        var savable = Savable();
        bool selectAll = savable.Any(i => !i.IsSelected);
        foreach (var item in savable) item.IsSelected = selectAll;
        RefreshState();
    }

    /// <summary>줄의 빈 곳을 눌러도 체크가 바뀐다 (단추·체크박스·목록 상자는 스스로 처리).</summary>
    private void OnRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: FileItem item } || !item.CanSave) return;
        item.IsSelected = !item.IsSelected;
    }

    /// <summary>줄 끝 ✕: 이 파일만 멈추고 다음 파일로 넘어간다.</summary>
    private void OnCancelOne(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileItem item } && item.Cancel is { } c)
        {
            try { c.Cancel(); } catch (ObjectDisposedException) { }
            item.Message = "멈추는 중…";
        }
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: FileItem item } || !item.CanCompare) return;
        new CompareWindow(item) { Owner = this }.ShowDialog();
    }

    private async void OnDownloadOne(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: FileItem item } || !item.CanSave) return;
        await DownloadSingle(item);
    }

    private async void OnDownloadSelected(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var list = Selected();
        if (list.Count == 1) await DownloadSingle(list[0]);
        else if (list.Count > 1) await DownloadZip(list);
    }

    private string? SuggestedDir()
    {
        if (_lastSaveDir is not null && Directory.Exists(_lastSaveDir)) return _lastSaveDir;
        var first = _items.FirstOrDefault()?.Path;
        return first is null ? null : Path.GetDirectoryName(first);
    }

    /// <summary>넣은 원본 파일과 같은 경로로는 절대 저장하지 않는다.</summary>
    private bool IsOriginal(string path)
        => _items.Any(i => string.Equals(Path.GetFullPath(i.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    private async Task DownloadSingle(FileItem item)
    {
        if (item.SaveSource is null || item.SaveName is null || !File.Exists(item.SaveSource)) return;
        var ext = Path.GetExtension(item.SaveName);
        var dlg = new SaveFileDialog
        {
            Title = "다운로드할 곳 고르기",
            FileName = item.SaveName,
            Filter = $"{ext.TrimStart('.').ToUpperInvariant()} 파일|*{ext}|모든 파일|*.*",
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (SuggestedDir() is { } start) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(this) != true) return;
        var target = dlg.FileName;
        if (IsOriginal(target))
        {
            MessageBox.Show(this, "원본 파일 위에는 저장할 수 없습니다. 다른 이름이나 다른 폴더를 골라 주세요.",
                "쏙", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var source = item.SaveSource;
        await RunSave(() =>
        {
            // 사용자가 '바꾸기'를 눌러 확인한 경우에만 같은 이름을 덮어쓴다.
            SafeOutput.WriteAtomic(target, fs =>
            {
                using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                src.CopyTo(fs);
            }, overwrite: true);
            return target;
        }, new[] { item }, Path.GetDirectoryName(target)!, $"다운로드했어요: {Path.GetFileName(target)}");
    }

    private async Task DownloadZip(List<FileItem> list)
    {
        var dlg = new SaveFileDialog
        {
            Title = "ZIP 파일로 다운로드",
            Filter = "ZIP 압축 파일|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = ZipName(list),
        };
        if (SuggestedDir() is { } start) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(this) != true) return;
        var zipPath = dlg.FileName;
        if (IsOriginal(zipPath))
        {
            MessageBox.Show(this, "원본 파일 위에는 저장할 수 없습니다. 다른 이름이나 다른 폴더를 골라 주세요.",
                "쏙", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entries = list.Select(i => (Source: i.SaveSource!, Name: i.SaveName!)).ToList();
        await RunSave(() =>
        {
            SafeOutput.WriteAtomic(zipPath, fs =>
            {
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (source, saveName) in entries)
                {
                    var stem = Path.GetFileNameWithoutExtension(saveName);
                    var ext = Path.GetExtension(saveName);
                    var name = stem + ext;
                    for (int n = 2; !used.Add(name); n++) name = $"{stem}({n}){ext}";
                    // 사진·PDF·한글 문서는 이미 압축된 파일이라 더 줄지 않는다. 압축 없이 묶어야
                    // ZIP 크기를 정확히 알 수 있다 ("모두 합쳐서" 목표 계산).
                    zip.CreateEntryFromFile(source, name, CompressionLevel.NoCompression);
                }
            }, overwrite: true);
            return zipPath;
        }, list, Path.GetDirectoryName(zipPath)!, $"{list.Count}개를 '{Path.GetFileName(zipPath)}'로 묶어 다운로드했어요.");
    }

    /// <summary>ZIP 이름: 쏙_첫파일이름 외 N개_날짜.zip (웹하드의 묶음 받기처럼)</summary>
    internal static string ZipName(IReadOnlyList<FileItem> list)
    {
        var first = Path.GetFileNameWithoutExtension(list[0].Path);
        if (first.Length > 30) first = first[..30].TrimEnd();
        var label = list.Count > 1 ? $"{first} 외 {list.Count - 1}개" : first;
        var name = $"쏙_{label}_{DateTime.Now:yyyyMMdd}.zip";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>저장 작업을 돌리고, 끝나면 저장한 파일을 탐색기에서 보여준다.</summary>
    private async Task RunSave(Func<string?> save, IEnumerable<FileItem> items, string destDir, string doneNote)
    {
        _busy = true;
        RefreshState();
        try
        {
            var shown = await Task.Run(save);
            foreach (var item in items) item.Saved = true;
            _lastSaveDir = destDir;
            _lastResultPath = shown;
            _saveNote = doneNote;
            AppLog.Info("saved");
            ShowInExplorer(shown);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("save", ex);
            MessageBox.Show(this, "이 폴더에는 저장할 권한이 없습니다. 바탕화면이나 문서 폴더를 골라 주세요.",
                "쏙", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070))
        {
            AppLog.Error("save", ex);
            MessageBox.Show(this, "디스크 공간이 부족합니다.", "쏙", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex)
        {
            AppLog.Error("save", ex);
            MessageBox.Show(this, "저장하지 못했습니다. 같은 이름의 파일이 다른 프로그램에서 열려 있으면 닫고 다시 해 주세요.",
                "쏙", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Error("save", ex);
            MessageBox.Show(this, "저장하지 못했습니다. 다른 폴더를 골라 다시 해 주세요.",
                "쏙", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            RefreshState();
        }
    }

    private void ShowInExplorer(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Error("open-folder", ex); }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => ShowInExplorer(_lastResultPath);

    // ── 업데이트 ───────────────────────────────────────────────

    private Updater.Release? _newRelease;
    private bool _updating;
    private Updater.VerifiedSetup? _pendingSetup;

    private void OnUpdateOptionChanged(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (_loading) return;
        if (CheckUpdatesBox.IsChecked == true) _ = CheckForUpdateAsync(force: true);
        else UpdateButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>하루 한 번 새 버전을 확인한다. 끄면 확인하지 않는다.</summary>
    private async Task CheckForUpdateAsync(bool force)
    {
        if (!Updater.Enabled || !_settings.CheckUpdates) return;
        if (!force && _settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20)
            && _settings.KnownNewVersion is null) return;

        var release = await Updater.CheckAsync();
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.KnownNewVersion = release?.Version.ToString(3);
        _settings.Save();
        if (release is null || !_settings.CheckUpdates) return;

        _newRelease = release;
        UpdateButton.Content = $"새 버전 {release.Version.ToString(3)} 받기";
        UpdateButton.ToolTip = string.IsNullOrWhiteSpace(release.Notes) ? null : release.Notes;
        UpdateButton.Visibility = Visibility.Visible;
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_newRelease is not { } release || _updating) return;

        // 무설치판은 내려받기 페이지만 열어 준다.
        if (!Updater.IsInstalled)
        {
            Updater.OpenPage(release.PageUrl);
            return;
        }
        if (_busy)
        {
            MessageBox.Show(this, "지금 하는 일이 끝난 뒤에 업데이트해 주세요.", "쏙", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"쏙 {release.Version.ToString(3)}으로 업데이트할까요?\n쏙이 잠시 닫혔다가 다시 켜집니다.",
                "쏙", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
            return;
        if (!ConfirmDiscard("그래도 업데이트할까요?")) return;

        _updating = true;
        UpdateButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => UpdateButton.Content = $"내려받는 중 {Math.Round(p * 100)}%");
            // 잠금은 쏙이 닫힐 때까지 쥐고 있는다 (설치 프로그램이 파일을 다 읽기 전에 바꿔치기되지 않게).
            _pendingSetup = await Updater.DownloadAsync(release, progress);
            UpdateButton.Content = "설치하는 중…";
            Updater.RunInstaller(_pendingSetup);
            AppLog.Info("update-start v" + release.Version.ToString(3));
            _closeAfterStop = true; // 이미 물어봤으므로 닫을 때 다시 묻지 않는다
            Close();
        }
        catch (Exception ex)
        {
            AppLog.Error("update", ex);
            _pendingSetup?.Dispose();
            _pendingSetup = null;
            _updating = false;
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"새 버전 {release.Version.ToString(3)} 받기";
            if (MessageBox.Show(this, "업데이트 파일을 받지 못했습니다. 내려받기 페이지를 열까요?", "쏙",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                Updater.OpenPage(release.PageUrl);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void RefreshState()
    {
        if (StartButton is null || ListState is null) return;

        bool targetOk = TryGetTargetBytes(out var target);
        bool hasWork = _items.Any(i => FileKinds.IsReadyNow(i.Kind) && i.State is ItemState.Waiting or ItemState.Failed);

        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListState.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        bool finished = HasFinished();
        if (_cts is not null)
        {
            bool stopping = _cts.IsCancellationRequested;
            StartButton.Style = (Style)FindResource("StopButton");
            StartButton.Content = stopping ? "멈추는 중…" : "■  멈추기";
            StartButton.IsEnabled = !stopping;
            StartButton.ToolTip = "지금 줄이는 파일에서 멈춰요. 이미 끝난 파일은 그대로 남아요.";
        }
        else
        {
            StartButton.Style = (Style)FindResource("PrimaryButton");
            bool redo = !hasWork && finished;
            StartButton.Content = redo ? "다시 줄이기" : "쏙 줄이기";
            StartButton.IsEnabled = !_busy && targetOk && (hasWork || redo);
            StartButton.ToolTip = redo ? "결과를 지우고 원본에서 지금 설정으로 다시 줄여요" : null;
        }
        ResetButton.Visibility = finished && !_busy ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.IsEnabled = !_busy;
        AddFilesButton.IsEnabled = AddFolderButton.IsEnabled = ClearButton.IsEnabled = !_busy;
        OpenFolderButton.Visibility = !_busy && _lastResultPath is not null && File.Exists(_lastResultPath)
            ? Visibility.Visible : Visibility.Collapsed;

        var savable = Savable();
        var selected = savable.Where(i => i.IsSelected).ToList();
        int unsaved = savable.Count(i => !i.Saved);

        // 전체 선택: 모두 체크 ☑ / 일부 ▣ / 없음 ☐
        SelectAllBox.IsEnabled = !_busy && savable.Count > 0;
        SelectAllBox.IsChecked = savable.Count > 0 && selected.Count == savable.Count ? true
                               : selected.Count == 0 ? false : null;

        DownloadButton.IsEnabled = !_busy && selected.Count > 0;
        DownloadText.Text = selected.Count switch
        {
            0 => "다운로드",
            1 => "1개 다운로드",
            _ => $"{selected.Count}개 ZIP 다운로드",
        };
        // 목록은 줄이는 중에도 켜 둔다 (✕ 단추). 체크·다운로드는 각 처리기가 _busy를 확인한다.

        if (savable.Count == 0)
        {
            SaveHint.Text = _items.Count > 0 && !_busy ? "'쏙 줄이기'를 누르면 여기서 다운로드할 수 있어요." : "";
            SaveHint.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        }
        else if (unsaved > 0)
        {
            SaveHint.Text = selected.Count == 0
                ? "다운로드할 파일을 체크하거나, 줄 끝의 ⬇ 단추를 누르세요."
                : $"아직 다운로드 전인 파일이 {unsaved}개 있어요.";
            SaveHint.Foreground = (System.Windows.Media.Brush)FindResource("AccentText");
        }
        else
        {
            SaveHint.Text = _saveNote;
            SaveHint.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        }

        int done = _items.Count(i => i.State is ItemState.Done or ItemState.Partial);
        ListTitle.Text = $"파일 {_items.Count}개";

        long before = _items.Sum(i => i.OriginalBytes);
        long after = _items.Sum(i => i.ResultBytes ?? i.OriginalBytes);
        var summary = done == 0
            ? $"모두 {TargetSize.Format(before)}"
            : $"{done}개 완료 · 모두 {TargetSize.Format(before)} → {TargetSize.Format(after)}";
        if (CombinedMode.IsChecked == true && targetOk && TryGetTargetMb(out var mb))
        {
            // 목표는 사용자가 적은 값 그대로 보여 주고, 비교는 여유를 둔 값(ZIP으로 묶을 때 늘어나는 양 포함)으로 한다.
            var shown = mb < 1 ? $"{mb * 1000:0.##}KB" : $"{mb.ToString("0.##", CultureInfo.InvariantCulture)}MB";
            summary += after <= target - ZipReserve() || done == 0
                ? $" · 목표 합계 {shown}"
                : $" · 목표 합계 {shown}보다 큽니다. 목표를 조금 올려 보세요.";
        }
        Summary.Text = summary;
    }
}
