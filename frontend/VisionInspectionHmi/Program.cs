using VisionInspectionHmi.Forms;

namespace VisionInspectionHmi;

internal static class Program
{
    /// <summary>
    /// アプリケーションのエントリポイント。
    /// WinForms の共通ダイアログ（OpenFileDialog 等）はシェルの COM/OLE を使用するため、
    /// UI スレッドは必ず STA（Single Thread Apartment）である必要がある。
    /// トップレベルステートメントでは STA が付与されず MTA となり、OpenFileDialog が
    /// 応答なしになるため、明示的に [STAThread] を指定する。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new MainForm());
    }
}
