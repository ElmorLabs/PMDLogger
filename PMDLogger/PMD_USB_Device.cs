using EVC2Lib;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        List<byte> rx_buffer = new List<byte>();

        private bool PMD_USB_SendCmd(byte cmd, int rx_len)
        {

            if (serial_port == null)
            {
                return false;
            }

            lock (rx_buffer) rx_buffer.Clear();
            serial_port.Write(new byte[] { cmd }, 0, 1);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (rx_buffer.Count < rx_len && sw.ElapsedMilliseconds < 100)
            {
                
            }

            return rx_buffer.Count == rx_len;
        }

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

            serial_port.RtsEnable = true;
            serial_port.DtrEnable = true;

            serial_port.DataReceived += Serial_port_DataReceived;

            // Check ID
            serial_port.Open();

            bool result = PMD_USB_SendCmd((byte)UART_CMD.UART_CMD_READ_ID, 3);

            if (!result || rx_buffer[0] != PMD_USB_VID || rx_buffer[1] != PMD_USB_PID)
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

        private void Serial_port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int bytes = serial_port.BytesToRead;
            byte[] data_buffer = new byte[bytes];
            serial_port.Read(data_buffer, 0, bytes);
            lock(rx_buffer) {
                rx_buffer.AddRange(data_buffer);
            }
        }

        volatile bool run_task = false;
        Thread monitoring_thread = null;

        public bool StartMonitoring()
        {
            try
            {
                serial_port.Open();

                if (device_speed == 3)
                {
                    // Change baud rate

                    int baud = 460800;
                    int parity = 2; // None
                    int datawidth = 0; // 8 bit
                    int stopbits = 0; // 1 bit

                    /*byte[] tx_buffer = new byte[] {
                        (byte)UART_CMD.UART_CMD_WRITE_CONFIG_UART,
                        (byte)(baud>>24), (byte)(baud>>16), (byte)(baud>>8), (byte)baud,
                        (byte)(parity>>24), (byte)(parity>>16), (byte)(parity>>8), (byte)parity,
                        (byte)(datawidth>>24), (byte)(datawidth>>16), (byte)(datawidth>>8), (byte)datawidth,
                        (byte)(stopbits>>24), (byte)(stopbits>>16), (byte)(stopbits>>8), (byte)stopbits,
                    };*/

                    byte[] tx_buffer = new byte[] {
                        (byte)UART_CMD.UART_CMD_WRITE_CONFIG_UART,
                        (byte)(baud>>0), (byte)(baud>>8), (byte)(baud>>16), (byte)(baud>>24),
                        (byte)(parity>>0), (byte)(parity>>8), (byte)(parity>>16), (byte)(parity>>24),
                        (byte)(datawidth>>0), (byte)(datawidth>>8), (byte)(datawidth>>16), (byte)(datawidth>>24),
                        (byte)(stopbits>>0), (byte)(stopbits>>8), (byte)(stopbits>>16), (byte)(stopbits>>24),
                    };

                    serial_port.Write(tx_buffer, 0, tx_buffer.Length);
                    Thread.Sleep(100);
                    serial_port.Close();
                    serial_port.BaudRate = baud;
                    
                    serial_port.Open();
                }
;
                if (device_speed >= 2)
                {
                    // Enable cont rx
                    lock(rx_buffer)
                    {
                        rx_buffer.Clear();
                    }
                    byte[] tx_buffer = new byte[] { (byte)UART_CMD.UART_CMD_WRITE_CONFIG_CONT_TX, 1, 0, 0xFF };
                    serial_port.Write(tx_buffer, 0, tx_buffer.Length);
                }
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
                if (device_speed >= 2)
                {
                    // Disable cont rx
                    byte[] tx_buffer = new byte[] { (byte)UART_CMD.UART_CMD_WRITE_CONFIG_CONT_TX, 0, 0, 0 };
                    serial_port.Write(tx_buffer, 0, tx_buffer.Length);
                }

                if (device_speed == 3)
                {
                    // Restore baud rate

                    int baud = 115200;
                    int parity = 2; // None
                    int datawidth = 0; // 8 bit
                    int stopbits = 0; // 1 bit

                    /*byte[] tx_buffer = new byte[] {
                        (byte)UART_CMD.UART_CMD_WRITE_CONFIG_UART,
                        (byte)(baud>>24), (byte)(baud>>16), (byte)(baud>>8), (byte)baud,
                        (byte)(parity>>24), (byte)(parity>>16), (byte)(parity>>8), (byte)parity,
                        (byte)(datawidth>>24), (byte)(datawidth>>16), (byte)(datawidth>>8), (byte)datawidth,
                        (byte)(stopbits>>24), (byte)(stopbits>>16), (byte)(stopbits>>8), (byte)stopbits,
                    };*/

                    byte[] tx_buffer = new byte[] {
                        (byte)UART_CMD.UART_CMD_WRITE_CONFIG_UART,
                        (byte)(baud>>0), (byte)(baud>>8), (byte)(baud>>16), (byte)(baud>>24),
                        (byte)(parity>>0), (byte)(parity>>8), (byte)(parity>>16), (byte)(parity>>24),
                        (byte)(datawidth>>0), (byte)(datawidth>>8), (byte)(datawidth>>16), (byte)(datawidth>>24),
                        (byte)(stopbits>>0), (byte)(stopbits>>8), (byte)(stopbits>>16), (byte)(stopbits>>24),
                    };

                    serial_port.Write(tx_buffer, 0, tx_buffer.Length);
                    Thread.Sleep(100);
                    serial_port.BaudRate = baud;
                }

                serial_port.Close();
            }
            catch {
                return false;
            }

            return true;
        }


        private void update_task()
        {

            //byte[] rx_buffer;

            while (run_task)
            {
                //rx_buffer = null;

                if (device_speed == 0)
                {

                    // Get sensor values
                    bool result = PMD_USB_SendCmd((byte)UART_CMD.UART_CMD_READ_SENSOR_VALUES, 4 * 2 * 2);

                    if (result)
                    {
                        List<SensorData> sensor_data_list = new List<SensorData>();

                        double gpu_power = 0;
                        double cpu_power = 0;
                        double total_power = 0;

                        sensor_data_list.Add(new SensorData(0, total_power));
                        sensor_data_list.Add(new SensorData(1, gpu_power));
                        sensor_data_list.Add(new SensorData(2, cpu_power));

                        lock (rx_buffer)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                double voltage = ((Int16)(rx_buffer[i * 4 + 1] << 8 | rx_buffer[i * 4 + 0])) / 100.0;
                                double current = ((Int16)(rx_buffer[i * 4 + 2 + 1] << 8 | rx_buffer[i * 4 + 2 + 0])) / 10.0;
                                double power = voltage * current;

                                sensor_data_list.Add(new SensorData(3 + i * 3, voltage));
                                sensor_data_list.Add(new SensorData(4 + i * 3, current));
                                sensor_data_list.Add(new SensorData(5 + i * 3, power));

                                if (i == 0 || i == 1)
                                {
                                    gpu_power += power;
                                }
                                else
                                {
                                    cpu_power += power;
                                }
                            }
                        }

                        total_power = gpu_power + cpu_power;

                        sensor_data_list[0].Value = total_power;
                        sensor_data_list[1].Value = gpu_power;
                        sensor_data_list[2].Value = cpu_power;

                        DataUpdated?.Invoke(sensor_data_list);

                    }
                } else if(device_speed == 1)
                {
                    // Get sensor values
                    bool result = PMD_USB_SendCmd((byte)UART_CMD.UART_CMD_READ_ADC, 4 * 2 * 2);

                    if (result)
                    {
                        List<SensorData> sensor_data_list = new List<SensorData>();

                        // Convert data
                        lock (rx_buffer)
                        {
                            double pcie1_v = ((Int16)(rx_buffer[1] << 8 | rx_buffer[0]) >> 4) * 0.007568f;
                            double pcie1_i = ((Int16)(rx_buffer[3] << 8 | rx_buffer[2]) >> 4) * 0.0488;
                            double pcie1_p = pcie1_v * pcie1_i;
                            double pcie2_v = ((Int16)(rx_buffer[5] << 8 | rx_buffer[4]) >> 4) * 0.007568f;
                            double pcie2_i = ((Int16)(rx_buffer[7] << 8 | rx_buffer[6]) >> 4) * 0.0488;
                            double pcie2_p = pcie2_v * pcie2_i;
                            double eps1_v = ((Int16)(rx_buffer[9] << 8 | rx_buffer[8]) >> 4) * 0.007568f;
                            double eps1_i = ((Int16)(rx_buffer[11] << 8 | rx_buffer[10]) >> 4) * 0.0488;
                            double eps1_p = eps1_v * eps1_i;
                            double eps2_v = ((Int16)(rx_buffer[13] << 8 | rx_buffer[12]) >> 4) * 0.007568f;
                            double eps2_i = ((Int16)(rx_buffer[15] << 8 | rx_buffer[14]) >> 4) * 0.0488;
                            double eps2_p = eps2_v * eps2_i;

                            double gpu_power = pcie1_p + pcie2_p;
                            double cpu_power = eps1_p + eps2_p;
                            double total_power = gpu_power + cpu_power;

                            // Build list
                            int i = 0;
                            sensor_data_list.Add(new SensorData(i++, total_power));
                            sensor_data_list.Add(new SensorData(i++, gpu_power));
                            sensor_data_list.Add(new SensorData(i++, cpu_power));
                            sensor_data_list.Add(new SensorData(i++, pcie1_v));
                            sensor_data_list.Add(new SensorData(i++, pcie1_i));
                            sensor_data_list.Add(new SensorData(i++, pcie1_p));
                            sensor_data_list.Add(new SensorData(i++, pcie2_v));
                            sensor_data_list.Add(new SensorData(i++, pcie2_i));
                            sensor_data_list.Add(new SensorData(i++, pcie2_p));
                            sensor_data_list.Add(new SensorData(i++, eps1_v));
                            sensor_data_list.Add(new SensorData(i++, eps1_i));
                            sensor_data_list.Add(new SensorData(i++, eps1_p));
                            sensor_data_list.Add(new SensorData(i++, eps2_v));
                            sensor_data_list.Add(new SensorData(i++, eps2_i));
                            sensor_data_list.Add(new SensorData(i++, eps2_p));
                        }

                        // Trigger event
                        DataUpdated?.Invoke(sensor_data_list);

                    }
                } else if (device_speed >= 2)
                {
                    int num_sets = rx_buffer.Count / (4 * 2 * 2);
                    for (int j = 0; j < num_sets; j++)
                    {
                        List<SensorData> sensor_data_list = new List<SensorData>();

                        // Convert data
                        double pcie1_v = ((Int16)(rx_buffer[1 + j * 16] << 8 | rx_buffer[0 + j * 16]) >> 4) * 0.007568f;
                        double pcie1_i = ((Int16)(rx_buffer[3 + j * 16] << 8 | rx_buffer[2 + j * 16]) >> 4) * 0.0488;
                        double pcie1_p = pcie1_v * pcie1_i;
                        double pcie2_v = ((Int16)(rx_buffer[5 + j * 16] << 8 | rx_buffer[4 + j * 16]) >> 4) * 0.007568f;
                        double pcie2_i = ((Int16)(rx_buffer[7 + j * 16] << 8 | rx_buffer[6 + j * 16]) >> 4) * 0.0488;
                        double pcie2_p = pcie2_v * pcie2_i;
                        double eps1_v = ((Int16)(rx_buffer[9 + j * 16] << 8 | rx_buffer[8 + j * 16]) >> 4) * 0.007568f;
                        double eps1_i = ((Int16)(rx_buffer[11 + j * 16] << 8 | rx_buffer[10 + j * 16]) >> 4) * 0.0488;
                        double eps1_p = eps1_v * eps1_i;
                        double eps2_v = ((Int16)(rx_buffer[13 + j * 16] << 8 | rx_buffer[12 + j * 16]) >> 4) * 0.007568f;
                        double eps2_i = ((Int16)(rx_buffer[15 + j * 16] << 8 | rx_buffer[14 + j * 16]) >> 4) * 0.0488;
                        double eps2_p = eps2_v * eps2_i;

                        double gpu_power = pcie1_p + pcie2_p;
                        double cpu_power = eps1_p + eps2_p;
                        double total_power = gpu_power + cpu_power;

                        // Build list
                        int i = 0;
                        sensor_data_list.Add(new SensorData(i++, total_power));
                        sensor_data_list.Add(new SensorData(i++, gpu_power));
                        sensor_data_list.Add(new SensorData(i++, cpu_power));
                        sensor_data_list.Add(new SensorData(i++, pcie1_v));
                        sensor_data_list.Add(new SensorData(i++, pcie1_i));
                        sensor_data_list.Add(new SensorData(i++, pcie1_p));
                        sensor_data_list.Add(new SensorData(i++, pcie2_v));
                        sensor_data_list.Add(new SensorData(i++, pcie2_i));
                        sensor_data_list.Add(new SensorData(i++, pcie2_p));
                        sensor_data_list.Add(new SensorData(i++, eps1_v));
                        sensor_data_list.Add(new SensorData(i++, eps1_i));
                        sensor_data_list.Add(new SensorData(i++, eps1_p));
                        sensor_data_list.Add(new SensorData(i++, eps2_v));
                        sensor_data_list.Add(new SensorData(i++, eps2_i));
                        sensor_data_list.Add(new SensorData(i++, eps2_p));

                        // Trigger event
                        DataUpdated?.Invoke(sensor_data_list);
                    }

                    lock (rx_buffer)
                    {
                        rx_buffer.RemoveRange(0, num_sets * 16);
                    }
                }

            }
        }
    }
}