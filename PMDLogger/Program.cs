using PMDLogger.Properties;
using System;
using System.Windows.Forms;
using EVC2Lib;
using System.Threading;
using System.Reflection;
using System.IO;

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

    // https://stackoverflow.com/questions/995195/how-can-i-make-a-net-windows-forms-application-that-only-runs-in-the-system-tra
    public class TrayLogger : ApplicationContext {

        private const string PMD_LOG_FILE = "pmd.txt";

        private const byte PMD_BUS = 1;

        private const byte PMD_REG_DEVID = 0x00;
        private const byte PMD_REG_MON_START = 0x03;
        private const int PMD_MON_LENGTH = 2 * 2 * 4; // 2 bytes * 2 values * 4 ch

        private byte pmd_dev_addr = 0;

        private NotifyIcon trayIcon;

        MenuItem pmd_status_menu_item = new MenuItem("PMD");

        int evc2_device_index = -1;

        Thread logging_thread;
        volatile bool run_task = true;
        DataLogger data_logger;

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

            // Disable status item
            pmd_status_menu_item.Enabled = false;

            // Find EVC2 devices
            int num_devices = EVC2Manager.FindDevices();

            // Check if any are found
            if(num_devices > 0) {

                // TODO: Support more than one EVC2 device
                evc2_device_index = 0;

                // Try to find PMD device on I2C2
                byte[] rx_data;
                int i2c_error;
                EVC2_ERROR evc2_error;

                byte[] addr_array;
                int addr_length;
                evc2_error = EVC2Manager.I2cScan(evc2_device_index, PMD_BUS, out addr_array, out addr_length, out i2c_error);

                if(evc2_error == EVC2_ERROR.NO_ERROR) {
                    // Iterate devices and check device id

                    if(addr_array.Length > 0) {
                        foreach(byte addr in addr_array) {
                            evc2_error = EVC2Manager.I2cRx(0, PMD_BUS, addr, new byte[] { 0x00 }, out rx_data, 2, out i2c_error);
                            if(evc2_error == EVC2_ERROR.NO_ERROR) {
                                // Check if PMD device
                                if(rx_data[0] == 0xEE && rx_data[1] == 0x0A) {
                                    // TODO: Last found device is always used
                                    pmd_dev_addr = addr;
                                }
                            }
                        }
                        if(pmd_dev_addr != 0) {

                            // Init data logger
                            data_logger = new DataLogger(evc2_device_index);
                            data_logger.SetFilePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\" + PMD_LOG_FILE, false);
                            data_logger.AddLogItem(0, "CPU Power", "GPU", "W");
                            data_logger.AddLogItem(0, "GPU Power", "CPU", "W");
                            data_logger.Start();

                            // Start logging thread
                            logging_thread = new Thread(new ThreadStart(LoggingTask));
                            logging_thread.IsBackground = true;
                            logging_thread.Start();

                            // Update status
                            pmd_status_menu_item.Text = "Logging started...";
                        }
                    } else {
                        pmd_status_menu_item.Text = "No I2C devices found";
                    }
                } else {
                    if(evc2_error == EVC2_ERROR.I2C) {
                        pmd_status_menu_item.Text = $"I2C Scan Error {i2c_error.ToString()}";
                    } else {
                        pmd_status_menu_item.Text = $"EVC2 Error {evc2_error.ToString()}";
                    }
                }

            } else {
                // No devices found
                pmd_status_menu_item.Text = "No EVC2 devices found";
            }
        }

        private void LoggingTask() {
            while(run_task) {
                if(data_logger != null) {
                    // Read new values through EVC2
                    byte[] rx_data;
                    int evc2_error;
                    if(EVC2Manager.I2cRx(evc2_device_index, PMD_BUS, pmd_dev_addr, new byte[] { PMD_REG_MON_START }, out rx_data, PMD_MON_LENGTH, out evc2_error) == EVC2_ERROR.NO_ERROR) {
                        double pcie1_v = ((rx_data[1] << 8 | rx_data[0]) >> 4) * 0.007568f;
                        double pcie1_i = ((rx_data[3] << 8 | rx_data[2]) >> 4) * 0.0488;
                        double pcie1_p = pcie1_v * pcie1_i;
                        double pcie2_v = ((rx_data[5] << 8 | rx_data[4]) >> 4) * 0.007568f;
                        double pcie2_i = ((rx_data[7] << 8 | rx_data[6]) >> 4) * 0.0488;
                        double pcie2_p = pcie2_v * pcie2_i;
                        double eps1_v = ((rx_data[9] << 8 | rx_data[8]) >> 4) * 0.007568f;
                        double eps1_i = ((rx_data[11] << 8 | rx_data[10]) >> 4) * 0.0488;
                        double eps1_p = eps1_v * eps1_i;
                        double eps2_v = ((rx_data[13] << 8 | rx_data[12]) >> 4) * 0.007568f;
                        double eps2_i = ((rx_data[15] << 8 | rx_data[14]) >> 4) * 0.0488;
                        double eps2_p = eps2_v * eps2_i;

                        double gpu_power = pcie1_p + pcie2_p;
                        double cpu_power = eps1_p + eps2_p;
                        data_logger.UpdateValue(0, gpu_power);
                        data_logger.UpdateValue(1, cpu_power);
                        data_logger.WriteEntry();

                        pmd_status_menu_item.Text = $"GPU {gpu_power.ToString("F0")}W CPU {cpu_power.ToString("F0")}W";
                }
            }
            Thread.Sleep(100);
        }
    }

        void Exit(object sender, EventArgs e) {

            // Stop logging thread
            run_task = false;

            // Hide tray icon, otherwise it will remain shown until user mouses over it
            trayIcon.Visible = false;

            Application.Exit();
        }

    }
}