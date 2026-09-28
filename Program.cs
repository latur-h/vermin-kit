namespace Vermintide_2;

static class Program
{
    const string MutexName = @"Local\Vermintide2";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(false, MutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
                return;

            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplication());
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }
}
