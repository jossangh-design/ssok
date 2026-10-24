using System.Windows;
using System.Windows.Threading;
using Ssok.Core;

namespace Ssok.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 빌드 확인용: Ssok.exe --self-test <시험 파일 폴더> <결과 파일>
        if (e.Args.Length >= 3 && e.Args[0] == "--self-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = SelfTest.Run(e.Args[1], e.Args[2]);
            // 윈도우 PDF 렌더러(WinRT) 개체가 프로그램 마무리 중에 풀리면서 비정상 종료(-1073741819)가 날 수 있어,
            // 결과 파일을 다 쓴 뒤 마무리 단계를 건너뛰고 바로 끝내 시험 결과 코드를 그대로 돌려준다.
            EndNow(code);
            return;
        }

        AppLog.Prune();
        AppLog.Info("start v" + typeof(App).Assembly.GetName().Version);
        ImageWork.CleanTempRoot();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        // 저장할 것은 모두 저장한 뒤다. PDF 렌더러 정리 중 비정상 종료 창이 뜨지 않도록 바로 끝낸다.
        EndNow(e.ApplicationExitCode);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    private static void EndNow(int code)
    {
        try { TerminateProcess(GetCurrentProcess(), unchecked((uint)code)); }
        catch { Environment.Exit(code); }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("ui", e.Exception);
        MessageBox.Show("예상하지 못한 문제가 생겼어요. 원본 파일은 그대로 있어요.\n쏙을 다시 실행해 주세요.",
            "쏙", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
