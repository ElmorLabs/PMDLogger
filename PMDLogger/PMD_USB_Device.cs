using EVC2Lib;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PMDLogger
{
    public class PMD_USB_Device : IPMD_Device
    {
        public static List<PMD_USB_Device> GetAllDevices(int speed)
        {

            List<PMD_USB_Device> device_list = new List<PMD_USB_Device>();

            // Open registry to find matching CH340 USB-Serial ports
            RegistryKey masterRegKey = null;

            try
            {
                masterRegKey = Registry.LocalMachine.OpenSubKey(CH340_REGKEY);
            }
            catch
            {
                return device_list;
            }

            foreach (string subKey in masterRegKey.GetSubKeyNames())
            {
                // Name must contain either VCP or Serial to be valid. Process any entries NOT matching
                // Compare to subKey (name of RegKey entry)
                try
                {
                    RegistryKey subRegKey = masterRegKey.OpenSubKey($"{subKey}\\Device Parameters");
                    if (subRegKey == null) continue;

                    if (subRegKey.GetValueKind("PortName") != RegistryValueKind.String) continue;

                    string value = (string)subRegKey.GetValue("PortName");

                    if (value != null)
                    {
                        PMD_USB_Device pmd_usb_device = new PMD_USB_Device(value, speed);
                        device_list.Add(pmd_usb_device);
                    }
                }
                catch
                {
                    continue;
                }
            }
            masterRegKey.Close();

            return device_list;

        }

        public static byte[] KTH_SendCmd(SerialPort serial_port, byte[] tx_buffer, int rx_len, bool delay)
        {

            if (serial_port == null)
            {
                return null;
            }

            byte[] rx_buffer = new byte[rx_len];
            try
            {
                serial_port.Write(tx_buffer, 0, tx_buffer.Length);
                if (delay) Thread.Sleep(1);
                serial_port.Read(rx_buffer, 0, rx_buffer.Length);
            }
            catch (Exception ex)
            {
                return null;
            }

            return rx_buffer;
        }

        // UART interface commands
        private enum UART_CMD : byte
        {
            UART_CMD_WELCOME,
            UART_CMD_READ_ID,
            UART_CMD_READ_SENSORS,
            UART_CMD_READ_SENSOR_VALUES,
            UART_CMD_READ_CONFIG,
            UART_CMD_WRITE_CONFIG,
            UART_CMD_READ_ADC,
            UART_CMD_WRITE_CONFIG_CONT_TX,
            UART_CMD_WRITE_CONFIG_UART,
            UART_CMD_RESET = 0xF0,
            UART_CMD_BOOTLOADER = 0xF1,
            UART_CMD_NOP = 0xFF
        };

        private const string CH340_REGKEY = "SYSTEM\\CurrentControlSet\\Enum\\USB\\VID_1A86&PID_7523";
        private const byte PMD_USB_VID = 0xEE;
        private const byte PMD_USB_PID = 0x0A;

        static int DeviceCounter = 0;
        
        public int Id { get; private set; }
        public string Name { get; private set; }
        public List<Sensor> Sensors { get; private set; }

        public event NewDataEventHandler DataUpdated;

        private SerialPort serial_port;
        private int device_speed;

        public PMD_USB_Device(string port, int speed)
        {
            device_speed = speed;

            Id = DeviceCounter++;
            Name = $"PMD-USB-{Id}";
            int i = 0;
            Sensors = new List<Sensor>() {
                new Sensor(i++, "Total Power", "POWER", "W"), new Sensor(i++, "GPU Power", "GPU", "W"), new Sensor(i++, "CPU Power", "CPU", "W"),
                new Sensor(i++, "PCIE1 Voltage", "PCIE1_V", "V"), new Sensor(i++, "PCIE1 Current", "PCIE1_I", "A"), new Sensor(i++, "PCIE1 Power", "PCIE1_P", "W"),
                new Sensor(i++, "PCIE2 Voltage", "PCIE2_V", "V"), new Sensor(i++, "PCIE2 Current", "PCIE2_I", "A"), new Sensor(i++, "PCIE2 Power", "PCIE2_P", "W"),
                new Sensor(i++, "EPS1 Voltage", "EPS1_V", "V"), new Sensor(i++, "EPS1 Current", "EPS1_I", "A"), new Sensor(i++, "EPS1 Power", "EPS1_P", "W"),
                new Sensor(i++, "EPS2 Voltage", "EPS2_V", "V"), new Sensor(i++, "EPS2 Current", "EPS2_I", "A"), new Sensor(i++, "EPS2 Power", "EPS2_P", "W")
            };

            serial_port = new SerialPort(port);

            serial_port.BaudRate = 115200;
            serial_port.Parity = Parity.None;
            serial_port.StopBits = StopBits.One;
            serial_port.DataBits = 8;

            serial_port.Handshake = Handshake.None;
            serial_port.ReadTimeout = 100;
            serial_port.WriteTimeout = 100;

            // Check ID
            serial_port.Open();

            byte[] rx_buffer = KTH_SendCmd(serial_port, new byte[] { (byte)UART_CMD.UART_CMD_READ_ID }, 3, true);

            if (rx_buffer == null || rx_buffer[0] != PMD_USB_VID || rx_buffer[1] != PMD_USB_PID)
            {
                try
                {
                    serial_port.Close();
                    serial_port.Dispose();
                    serial_port = null;
                }
                catch (Exception ex) { }

                throw new Exception("Couldn't identify device");

            }

            serial_port.Close();

        }

        volatile bool run_task = false;
        Thread monitoring_thread = null;

        public bool StartMonitoring()
        {
            try
            {
                serial_port.Open();
            }
            catch
            {
                return false;
            }

            run_task = true;
            monitoring_thread = new Thread(new ThreadStart(update_task));
            monitoring_thread.IsBackground = true;
            monitoring_thread.Start();

            return true;
        }

        public bool StopMonitoring()
        {
            run_task = false;
            if (monitoring_thread != null)
            {
                monitoring_thread.Join(500);
                monitoring_thread = null;
            }

            try
            {
                serial_port.Close();
            }
            catch { }

            return true;
        }


        private void update_task()
        {

            byte[] rx_buffer;

            while (run_task)
            {
                rx_buffer = null;
                // Get sensor values
                rx_buffer = KTH_SendCmd(serial_port, new byte[] { (byte)UART_CMD.UART_CMD_READ_SENSOR_VALUES }, 4 * 2 * 2, true);

                if (rx_buffer != null)
                {
                    List<SensorData> sensor_data_list = new List<SensorData>();

                    double gpu_power = 0;
                    double cpu_power = 0;
                    double total_power = 0;

                    sensor_data_list.Add(new SensorData(0, total_power));
                    sensor_data_list.Add(new SensorData(1, gpu_power));
                    sensor_data_list.Add(new SensorData(2, cpu_power));

                    for (int i = 0; i < 4; i++)
                    {
                        double voltage = ((Int16)(rx_buffer[i * 4 + 1] << 8 | rx_buffer[i * 4 + 0])) / 100.0;
                        double current = ((Int16)(rx_buffer[i * 4 + 2 + 1] << 8 | rx_buffer[i * 4 + 2 + 0])) / 10.0;
                        double power = voltage * current;

                        sensor_data_list.Add(new SensorData(3 + i * 3, voltage));
                        sensor_data_list.Add(new SensorData(4 + i * 3, current));
                        sensor_data_list.Add(new SensorData(5 + i * 3, power));

                        if(i == 0 || i == 1)
                        {
                            gpu_power += power;
                        } else
                        {
                            cpu_power += power;
                        }
                    }

                    total_power = gpu_power + cpu_power;

                    sensor_data_list[0].Value = total_power;
                    sensor_data_list[1].Value = gpu_power;
                    sensor_data_list[2].Value = cpu_power;

                    DataUpdated?.Invoke(sensor_data_list);
                    
                }

            }
        }
    }
}