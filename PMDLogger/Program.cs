using PMDLogger.Properties;
using System;
using System.Windows.Forms;
using EVC2Lib;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Collections.Generic;

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

        private NotifyIcon trayIcon;

        MenuItem pmd_status_menu_item = new MenuItem("PMD");

        List<PMDDevice_EVC2> pmd_devices;
        List<DataLogger> DataLoggerList = new List<DataLogger>();

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

            // Find PMDs
            pmd_devices = PMDDevice_EVC2.GetAllDevices(0);

            string time_str = DateTime.Now.ToString("_yyyyMMdd_HHmmss");

            // Check if any are found
            if (pmd_devices.Count > 0) {

                foreach (PMDDevice_EVC2 device in pmd_devices)
                {

                    // Init data logger
                    DataLogger data_logger = new DataLogger();
                    data_logger.SetFilePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\" + device.Name + time_str + ".csv", true);

                    foreach (Sensor sensor in device.Sensors)
                    {
                        sensor.Id = data_logger.AddLogItem(sensor.DescriptionLong, sensor.DescriptionShort, sensor.Unit);
                    }

                    // Start logger
                    data_logger.Start();
                    DataLoggerList.Add(data_logger);

                    // Register for event
                    device.DataUpdated += (List<SensorData> sensor_data_list) => {
                        foreach(SensorData sensor_data in sensor_data_list)
                        {
                            data_logger.UpdateValue(sensor_data.Id, sensor_data.Value);
                        }
                        data_logger.WriteEntry();
                        
                        // Print total power
                        if(device.Sensors.Count > 1 && sensor_data_list.Count > 1)
                        {
                            pmd_status_menu_item.Text = $"{device.Sensors[0].DescriptionLong} {sensor_data_list[0].Value.ToString("F0")}{device.Sensors[0].Unit}";
                        }

                    };

                    if (device.StartMonitoring())
                    {
                        // Update status
                        pmd_status_menu_item.Text = $"Logging on {device.Name}...";
                    }
                    else
                    {
                        pmd_status_menu_item.Text = $"Error starting {device.Name}...";
                    }
                }
               
            } else {
                // No devices found
                pmd_status_menu_item.Text = "No PMD devices found";
            }
        }

        void Exit(object sender, EventArgs e) {

            foreach(PMDDevice_EVC2 device in pmd_devices)
            {
                device.StopMonitoring();
            }

            foreach(DataLogger data_logger in DataLoggerList)
            {
                data_logger.Stop();
            }

            // Hide tray icon, otherwise it will remain shown until user mouses over it
            trayIcon.Visible = false;

            Application.Exit();
        }

    }
}