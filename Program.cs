using System.Windows.Forms;
using BdoClient.Storage;

namespace BdoClient;

static class Program
{
    [STAThread]
    static void Main()
    {
        var appPaths = new AppPaths();
        appPaths.EnsureDirectories();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
