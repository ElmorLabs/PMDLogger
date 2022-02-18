using PMDLogger.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PMDLogger {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayLogger()); ;
        }
    }
}

// https://stackoverflow.com/questions/995195/how-can-i-make-a-net-windows-forms-application-that-only-runs-in-the-system-tra
public class TrayLogger : ApplicationContext {

    private NotifyIcon trayIcon;

    MenuItem pmd_status_menu_item = new MenuItem();

    public TrayLogger() {
        // Initialize Tray Icon
        trayIcon = new NotifyIcon() {
            Icon = Resources.elmorlabs,
            ContextMenu = new ContextMenu(new MenuItem[] {
                pmd_status_menu_item,
                new MenuItem("Exit", Exit)
            }),
            Visible = true
        };

        // Check if EVC2 devices are available

    }

    void Exit(object sender, EventArgs e) {
        // Hide tray icon, otherwise it will remain shown until user mouses over it
        trayIcon.Visible = false;

        Application.Exit();
    }
}