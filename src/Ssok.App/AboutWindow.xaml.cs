using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ssok.Core;

namespace Ssok.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"버전 {typeof(AboutWindow).Assembly.GetName().Version?.ToString(3)}";
        ShowText(Notices.Privacy);
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (Body is null) return; // InitializeComponent 도중
        if (sender == TermsTab) ShowText(Notices.Terms);
        else if (sender == NoticesTab) ShowText(Notices.ThirdParty);
        else ShowText(Notices.Privacy);
    }

    /// <summary>
    /// 안내문을 문단으로 나눠 보여준다. 빈 줄 다음 첫 줄은 소제목(굵게)으로,
    /// "·"로 시작하는 줄은 들여쓴 항목으로 보여준다.
    /// </summary>
    private void ShowText(string text)
    {
        Body.Children.Clear();
        bool headingNext = true;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { headingNext = true; continue; }

            var tb = new TextBlock { Text = line, Style = (Style)FindResource("Doc") };
            if (headingNext && !line.StartsWith('·') && line.Length < 60 && !line.StartsWith("Permission") && !line.StartsWith("THE SOFTWARE"))
            {
                tb.FontWeight = FontWeights.Bold;
                tb.FontSize = 15;
                tb.Foreground = (System.Windows.Media.Brush)FindResource("Ink");
                tb.Margin = new Thickness(0, Body.Children.Count == 0 ? 0 : 16, 0, 4);
            }
            else if (line.StartsWith('·'))
            {
                tb.Margin = new Thickness(10, 2, 0, 2);
            }
            else
            {
                tb.Margin = new Thickness(0, 2, 0, 2);
            }
            Body.Children.Add(tb);
            headingNext = false;
        }
        Scroller.ScrollToTop();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Error("open-logs", ex); }
    }
}
