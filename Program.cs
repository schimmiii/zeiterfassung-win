using System.Windows.Forms;

namespace Zeiterfassung;

internal static class Program
{
    // Verhindert doppelte Instanzen (zwei Tray-Icons, doppelte Timer).
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        _mutex = new Mutex(true, "Zeiterfassung.SingleInstance", out var isNew);
        if (!isNew) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayAppContext());

        GC.KeepAlive(_mutex);
    }
}
