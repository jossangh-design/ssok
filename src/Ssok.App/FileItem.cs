using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Ssok.Core;

namespace Ssok.App;

internal enum ItemState { Waiting, Working, Done, Partial, Skipped, Failed }

internal sealed class FileItem : INotifyPropertyChanged
{
    private static readonly Brush PdfBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)));
    private static readonly Brush HangulBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x63, 0xD6)));
    private static readonly Brush PhotoBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x4F, 0xD0)));
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x7A)));
    private static readonly Brush OkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x6B)));
    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB4, 0x6A, 0x00)));
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));

    private static readonly Brush OkSoft = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEE)));
    private static readonly Brush WarnSoft = Freeze(new SolidColorBrush(Color.FromRgb(0xFD, 0xF1, 0xDC)));
    private static readonly Brush ErrorSoft = Freeze(new SolidColorBrush(Color.FromRgb(0xFC, 0xE8, 0xE8)));
    private static readonly Brush MutedSoft = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF0)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public FileItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Kind = FileKinds.Detect(path);
        OriginalBytes = new FileInfo(path).Length;
        if (!FileKinds.IsReadyNow(Kind))
        {
            State = ItemState.Skipped;
            Message = "지원하지 않는 파일입니다. (pdf·hwp·hwpx·jpg·png·heic·webp·bmp)";
        }
    }

    public string Path { get; }
    public string Name { get; }
    public FileKind Kind { get; }
    public string KindText => FileKinds.DisplayName(Kind);
    public bool IsPhoto => FileKinds.IsPhoto(Kind);

    public string Badge => FileKinds.Group(Kind) switch
    {
        FileGroup.Pdf => "PDF",
        FileGroup.Hangul => "한글",
        FileGroup.Photo => "사진",
        _ => "?",
    };

    public Brush BadgeBrush => FileKinds.Group(Kind) switch
    {
        FileGroup.Pdf => PdfBrush,
        FileGroup.Hangul => HangulBrush,
        FileGroup.Photo => PhotoBrush,
        _ => MutedBrush,
    };

    public long OriginalBytes { get; }
    public string OriginalText => TargetSize.Format(OriginalBytes);

    private long? _resultBytes;
    public long? ResultBytes
    {
        get => _resultBytes;
        set { _resultBytes = value; Raise(); RaiseResult(); }
    }

    public string SizeText => _resultBytes is { } b && HasResult
        ? $"{OriginalText} → {TargetSize.Format(b)}"
        : OriginalText;

    /// <summary>줄인 결과가 있으면 "→ 8.69MB" 부분을 따로 굵게 보여준다.</summary>
    public bool HasResult => _state is ItemState.Done or ItemState.Partial && _resultBytes is not null;
    public string ResultText => _resultBytes is { } b ? TargetSize.Format(b) : "";

    /// <summary>원본보다 몇 % 줄었는지 (92 → "92%↓").</summary>
    public int ReducedPercent => _resultBytes is { } b && OriginalBytes > 0
        ? (int)Math.Round(100.0 * (OriginalBytes - b) / OriginalBytes) : 0;

    // ── 진행 표시 ──
    private double _progress;
    /// <summary>0~100</summary>
    public double Progress
    {
        get => _progress;
        set { _progress = value; Raise(); Raise(nameof(ProgressText)); }
    }
    public string ProgressText => $"{Math.Round(_progress)}%";
    public bool IsWorking => _state == ItemState.Working;

    /// <summary>이 파일만 멈추기 (줄 끝 ✕).</summary>
    public CancellationTokenSource? Cancel { get; set; }

    // ── 비교 ──
    /// <summary>한글 문서는 속 그림으로 비교. 없으면 원본 파일과 결과 파일로 비교한다.</summary>
    public string? PreviewBefore { get; set; }
    public string? PreviewAfter { get; set; }
    public bool CanCompare => HasResult && ResultPath is not null
        && (FileKinds.IsPhoto(Kind) || Kind == FileKind.Pdf || PreviewBefore is not null);

    public string? ResultPath { get; set; }

    private string? _saveSource;
    /// <summary>다운로드할 때 내보낼 파일 (줄인 결과, 또는 이미 작아서 그대로 쓸 원본).</summary>
    public string? SaveSource
    {
        get => _saveSource;
        set
        {
            _saveSource = value;
            if (value is null) IsSelected = false;
            Raise(); Raise(nameof(CanSave)); Raise(nameof(StateText));
        }
    }

    /// <summary>내보낼 때 쓸 이름 (예: 사진_쏙.jpg).</summary>
    public string? SaveName { get; set; }

    /// <summary>다운로드할 결과가 있는지 (체크박스·다운로드 단추를 켤지).</summary>
    public bool CanSave => _saveSource is not null;

    private bool _isSelected;
    /// <summary>목록에서 체크했는지.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; Raise(); }
    }

    private bool _saved;
    /// <summary>한 번이라도 다운로드했는지.</summary>
    public bool Saved
    {
        get => _saved;
        set { _saved = value; Raise(); Raise(nameof(DownloadTip)); }
    }

    /// <summary>줄인 결과를 버리고 처음(대기) 상태로 돌린다. 지원하지 않는 파일은 그대로 둔다.</summary>
    public void ResetResult()
    {
        if (!FileKinds.IsReadyNow(Kind)) return;
        ResultPath = null;
        SaveSource = null;
        SaveName = null;
        Saved = false;
        IsSelected = false;
        Message = "";
        PreviewBefore = PreviewAfter = null;
        ResultBytes = null;
        Progress = 0;
        State = ItemState.Waiting;
    }

    public string DownloadTip => _saved ? "다운로드했어요. 한 번 더 받기" : "이 파일 다운로드";

    /// <summary>사진 저장 형식: 0 = 왼쪽 설정 따름, 1 = 원래대로, 2 = JPG, 3 = PNG</summary>
    private int _formatChoice;
    public int FormatChoice
    {
        get => _formatChoice;
        set { _formatChoice = value; Raise(); }
    }

    public PhotoFormat EffectiveFormat(PhotoFormat global) => _formatChoice switch
    {
        1 => PhotoFormat.Original,
        2 => PhotoFormat.Jpg,
        3 => PhotoFormat.Png,
        _ => global,
    };

    private ItemState _state = ItemState.Waiting;
    public ItemState State
    {
        get => _state;
        set
        {
            _state = value;
            Raise(); Raise(nameof(StateText)); Raise(nameof(StateBrush)); Raise(nameof(CanEditFormat));
            Raise(nameof(IsWorking)); RaiseResult();
        }
    }

    public bool CanEditFormat => IsPhoto && _state is ItemState.Waiting or ItemState.Failed;

    /// <summary>상태 알약: "완료 92%↓", "목표 미달 52%↓", "그대로", "실패" …</summary>
    public string StateText => _state switch
    {
        ItemState.Waiting => "대기",
        ItemState.Working => "줄이는 중",
        ItemState.Done => ReducedPercent > 0 ? $"완료 {ReducedPercent}%↓" : "완료",
        ItemState.Partial => ReducedPercent > 0 ? $"목표 미달 {ReducedPercent}%↓" : "목표 미달",
        ItemState.Skipped => CanSave ? "그대로" : "건너뜀",
        _ => "실패",
    };

    public Brush StateSoftBrush => _state switch
    {
        ItemState.Done => OkSoft,
        ItemState.Partial => WarnSoft,
        ItemState.Failed => ErrorSoft,
        _ => MutedSoft,
    };

    private void RaiseResult()
    {
        Raise(nameof(SizeText)); Raise(nameof(HasResult)); Raise(nameof(ResultText));
        Raise(nameof(ReducedPercent)); Raise(nameof(StateText)); Raise(nameof(StateSoftBrush)); Raise(nameof(CanCompare));
    }

    public Brush StateBrush => _state switch
    {
        ItemState.Done => OkBrush,
        ItemState.Partial => WarnBrush,
        ItemState.Failed => ErrorBrush,
        _ => MutedBrush,
    };

    private string _message = "";
    public string Message
    {
        get => _message;
        set { _message = value; Raise(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
