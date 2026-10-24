using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ssok.Core;

namespace Ssok.App;

/// <summary>
/// 원본과 줄인 결과를 겹쳐 놓고, 마우스를 좌우로 움직여 비교하는 창 (쏙 2.0의 '비교').
/// 왼쪽은 원본, 오른쪽은 줄인 결과. 휠로 확대·축소, 더블클릭으로 원래 크기.
/// PDF는 1쪽, 사진은 사진 그대로, 한글 문서는 가장 큰 그림으로 비교한다.
/// </summary>
internal sealed class CompareWindow : Window
{
    private readonly FileItem _item;
    private readonly Grid _stage = new() { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xF7)) };
    private readonly Image _before = new() { Stretch = Stretch.Uniform };
    private readonly Image _after = new() { Stretch = Stretch.Uniform };
    private readonly Grid _afterHost = new();
    private readonly Rectangle _divider = new() { Width = 2, Fill = new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x6B)), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _move = new();
    private readonly TextBlock _hint = new() { Foreground = Brushes.Gray, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private double _split = 0.5;

    public CompareWindow(FileItem item)
    {
        _item = item;
        Title = "비교 · " + item.Name;
        Width = 1000; Height = 760; MinWidth = 600; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Malgun Gothic, Segoe UI");
        Background = (Brush)Application.Current.FindResource("Canvas");
        UseLayoutRounding = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ssok.ico")); } catch { }

        // 그림을 무대 전체를 채우는 칸에 넣고, 그 칸을 확대·이동한다 (마우스 좌표와 기준을 맞추려고).
        var transform = new TransformGroup { Children = { _scale, _move } };
        var beforeHost = new Grid { RenderTransform = transform };
        beforeHost.Children.Add(_before);
        var afterInner = new Grid { RenderTransform = transform };
        afterInner.Children.Add(_after);
        _afterHost.Children.Add(afterInner);
        _stage.Children.Add(beforeHost);
        _stage.Children.Add(_afterHost);
        _stage.Children.Add(_divider);
        _stage.Children.Add(Chip("원본", HorizontalAlignment.Left));
        _stage.Children.Add(Chip("줄인 결과", HorizontalAlignment.Right));
        _stage.Cursor = Cursors.SizeWE;
        _stage.MouseMove += (_, e) => { _split = Math.Clamp(e.GetPosition(_stage).X / Math.Max(1, _stage.ActualWidth), 0, 1); UpdateSplit(); };
        _stage.SizeChanged += (_, _) => UpdateSplit();
        _stage.MouseWheel += OnWheel;
        _stage.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ResetZoom(); };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var left = new TextBlock { FontSize = 14, Foreground = (Brush)Application.Current.FindResource("Ink"), VerticalAlignment = VerticalAlignment.Center };
        left.Inlines.Add(new System.Windows.Documents.Run(Label(true)) { FontWeight = FontWeights.Bold });
        left.Inlines.Add(new System.Windows.Documents.Run($"  {item.OriginalText}") { Foreground = Brushes.Gray });
        var right = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        right.Inlines.Add(new System.Windows.Documents.Run(Label(false)) { FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("AccentText") });
        right.Inlines.Add(new System.Windows.Documents.Run($"  {item.ResultText} · {item.ReducedPercent}%↓") { Foreground = Brushes.Gray });
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(left);

        var close = new Button { Content = "닫기", Width = 96, Height = 34, IsCancel = true, IsDefault = true, FontSize = 14,
                                 Style = (Style)Application.Current.FindResource("PrimaryButton") };
        close.Click += (_, _) => Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_hint);
        _hint.Text = "비교할 화면을 만드는 중…";

        var frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true,
                                 BorderBrush = (Brush)Application.Current.FindResource("Line"), Child = _stage };
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(frame);
        Content = root;

        Loaded += async (_, _) => await LoadImages();
    }

    private string Label(bool before) => _item.Kind == FileKind.Pdf
        ? (before ? "원본 1쪽" : "줄인 결과 1쪽")
        : FileKinds.IsPhoto(_item.Kind) ? (before ? "원본 사진" : "줄인 사진")
        : (before ? "원본 그림" : "줄인 그림");

    private static Border Chip(string text, HorizontalAlignment side) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1F, 0x23, 0x28)),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10),
        HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5 },
    };

    private async Task LoadImages()
    {
        try
        {
            var item = _item;
            var (a, b) = await ImageWork.RunSta(() => (Load(item, true), Load(item, false)));
            _before.Source = a;
            _after.Source = b;
            _hint.Text = "마우스를 좌우로 움직여 비교 · 휠로 확대 · 두 번 누르면 원래 크기"
                       + (item.Kind is FileKind.Hwp or FileKind.Hwpx ? " · 문서에서 가장 큰 그림으로 비교해요" : "");
            UpdateSplit();
        }
        catch (Exception ex)
        {
            AppLog.Error("compare", ex);
            _hint.Text = "비교 화면을 만들지 못했어요.";
        }
    }

    /// <summary>비교용 그림을 만든다 (STA 작업 스레드에서). 화면보다 너무 크지 않게 줄여 읽는다.</summary>
    private static BitmapSource Load(FileItem item, bool before)
    {
        const int width = 1600;
        if (item.Kind == FileKind.Pdf)
        {
            using var r = PdfRasterizer.Open(before ? item.Path : item.ResultPath!);
            var (w, _) = r.PageSize(0);
            return r.Render(0, dpi: width / Math.Max(1, w / 72.0));
        }
        string path = FileKinds.IsPhoto(item.Kind)
            ? (before ? item.Path : item.ResultPath!)
            : (before ? item.PreviewBefore! : item.PreviewAfter!);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = width;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void UpdateSplit()
    {
        double w = _stage.ActualWidth, h = _stage.ActualHeight;
        double x = w * _split;
        _afterHost.Clip = new RectangleGeometry(new Rect(x, 0, Math.Max(0, w - x), h));
        _divider.Margin = new Thickness(x - 1, 0, 0, 0);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(_stage);
        double old = _scale.ScaleX;
        double next = Math.Clamp(old * (e.Delta > 0 ? 1.2 : 1 / 1.2), 1, 8);
        if (next <= 1.0001) { ResetZoom(); return; }
        // 마우스가 가리키는 곳을 중심으로 확대한다.
        _move.X = p.X - (p.X - _move.X) * next / old;
        _move.Y = p.Y - (p.Y - _move.Y) * next / old;
        _scale.ScaleX = _scale.ScaleY = next;
        _hint.Text = $"확대 {next:0.0}배 · 휠로 조절 · 두 번 누르면 원래 크기";
        e.Handled = true;
    }

    private void ResetZoom()
    {
        _scale.ScaleX = _scale.ScaleY = 1;
        _move.X = _move.Y = 0;
        _hint.Text = "마우스를 좌우로 움직여 비교 · 휠로 확대 · 두 번 누르면 원래 크기";
    }
}
