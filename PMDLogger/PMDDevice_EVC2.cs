using EVC2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PMDLogger
{
    public class PMDDevice_EVC2 : IPMDDevice
    {

        public static List<PMDDevice_EVC2> GetAllDevices(int speed)
        {

            List<PMDDevice_EVC2> device_list = new List<PMDDevice_EVC2>();

            // Find EVC2 devices
            int num_devices = EVC2Manager.FindDevices();

            for (int dev = 0; dev < num_devices; dev++)
            {
                for (int bus = 0; bus < 3; bus++)
                {
                    // Try to find PMD device on bus
                    byte[] rx_data;
                    int i2c_error;
                    EVC2_ERROR evc2_error;

                    byte[] addr_array;
                    int addr_length;
                    evc2_error = EVC2Manager.I2cScan(dev, bus, out addr_array, out addr_length, out i2c_error);

                    if (evc2_error == EVC2_ERROR.NO_ERROR)
                    {
                        // Iterate devices and check device id
                        if (addr_array.Length > 0)
                        {
                            foreach (byte addr in addr_array)
                            {
                                evc2_error = EVC2Manager.I2cRx(dev, bus, addr, new byte[] { 0x00 }, out rx_data, 2, out i2c_error);
                                if (evc2_error == EVC2_ERROR.NO_ERROR)
                                {
                                    // Check if PMD device
                                    if (rx_data[0] == 0xEE && rx_data[1] == 0x0A)
                                    {
                                        // Add to list
                                        device_list.Add(new PMDDevice_EVC2(dev, bus, addr, speed));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return device_list;

        }

        private const byte PMD_REG_DEVID = 0x00;
        private const byte PMD_REG_MON_START = 0x03;
        private const int PMD_MON_LENGTH = 2 * 2 * 4; // 2 bytes * 2 values * 4 ch

        static int DeviceCounter = 0;

        public int Id { get; private set; }
        public string Name { get; private set; }
        public List<Sensor> Sensors { get; private set; }

        public event NewDataEventHandler DataUpdated;

        private int evc2_dev;
        private int evc2_bus;
        private int evc2_addr;
        private int evc2_speed;

        public PMDDevice_EVC2(int device, int bus, int addr, int speed)
        {
            Id = DeviceCounter++;
            Name = $"PMD{Id}";
            int i = 0;
            Sensors = new List<Sensor>() {
                new Sensor(i++, "Total Power", "POWER", "W"), new Sensor(i++, "GPU Power", "GPU", "W"), new Sensor(i++, "CPU Power", "CPU", "W"),
                new Sensor(i++, "PCIE1 Voltage", "PCIE1_V", "V"), new Sensor(i++, "PCIE1 Current", "PCIE1_I", "A"), new Sensor(i++, "PCIE1 Power", "PCIE1_P", "W"),
                new Sensor(i++, "PCIE2 Voltage", "PCIE2_V", "V"), new Sensor(i++, "PCIE2 Current", "PCIE2_I", "A"), new Sensor(i++, "PCIE2 Power", "PCIE2_P", "W"),
                new Sensor(i++, "EPS1 Voltage", "EPS1_V", "V"), new Sensor(i++, "EPS1 Current", "EPS1_I", "A"), new Sensor(i++, "EPS1 Power", "EPS1_P", "W"),
                new Sensor(i++, "EPS2 Voltage", "EPS2_V", "V"), new Sensor(i++, "EPS2 Current", "EPS2_I", "A"), new Sensor(i++, "EPS2 Power", "EPS2_P", "W")
            };

            evc2_dev = device;
            evc2_bus = bus;
            evc2_addr = addr;
            evc2_speed = speed;

        }

        volatile bool run_task = false;
        Thread monitoring_thread = null;

        public bool StartMonitoring()
        {
            if (EVC2Manager.I2cSetSpeed(evc2_dev, evc2_bus, evc2_speed) != EVC2_ERROR.NO_ERROR) return false;

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
            return true;
        }

        private void update_task()
        {
            while (run_task)
            {
                // Read new values through EVC2
                byte[] rx_data;
                int evc2_error;
                if (EVC2Manager.I2cRx(evc2_dev, evc2_bus, (byte)evc2_addr, new byte[] { PMD_REG_MON_START }, out rx_data, PMD_MON_LENGTH, out evc2_error) == EVC2_ERROR.NO_ERROR)
                {

                    // Convert data
                    double pcie1_v = ((Int16)(rx_data[1] << 8 | rx_data[0]) >> 4) * 0.007568f;
                    double pcie1_i = ((Int16)(rx_data[3] << 8 | rx_data[2]) >> 4) * 0.0488;
                    double pcie1_p = pcie1_v * pcie1_i;
                    double pcie2_v = ((Int16)(rx_data[5] << 8 | rx_data[4]) >> 4) * 0.007568f;
                    double pcie2_i = ((Int16)(rx_data[7] << 8 | rx_data[6]) >> 4) * 0.0488;
                    double pcie2_p = pcie2_v * pcie2_i;
                    double eps1_v = ((Int16)(rx_data[9] << 8 | rx_data[8]) >> 4) * 0.007568f;
                    double eps1_i = ((Int16)(rx_data[11] << 8 | rx_data[10]) >> 4) * 0.0488;
                    double eps1_p = eps1_v * eps1_i;
                    double eps2_v = ((Int16)(rx_data[13] << 8 | rx_data[12]) >> 4) * 0.007568f;
                    double eps2_i = ((Int16)(rx_data[15] << 8 | rx_data[14]) >> 4) * 0.0488;
                    double eps2_p = eps2_v * eps2_i;

                    double gpu_power = pcie1_p + pcie2_p;
                    double cpu_power = eps1_p + eps2_p;
                    double total_power = gpu_power + cpu_power;

                    // Build list
                    List<SensorData> sensor_data = new List<SensorData>();

                    int i = 0;
                    sensor_data.Add(new SensorData(i++, total_power));
                    sensor_data.Add(new SensorData(i++, gpu_power));
                    sensor_data.Add(new SensorData(i++, cpu_power));
                    sensor_data.Add(new SensorData(i++, pcie1_v));
                    sensor_data.Add(new SensorData(i++, pcie1_i));
                    sensor_data.Add(new SensorData(i++, pcie1_p));
                    sensor_data.Add(new SensorData(i++, pcie2_v));
                    sensor_data.Add(new SensorData(i++, pcie2_i));
                    sensor_data.Add(new SensorData(i++, pcie2_p));
                    sensor_data.Add(new SensorData(i++, eps1_v));
                    sensor_data.Add(new SensorData(i++, eps1_i));
                    sensor_data.Add(new SensorData(i++, eps1_p));
                    sensor_data.Add(new SensorData(i++, eps2_v));
                    sensor_data.Add(new SensorData(i++, eps2_i));
                    sensor_data.Add(new SensorData(i++, eps2_p));

                    // Trigger event
                    DataUpdated?.Invoke(sensor_data);

                }
            }
        }
    }
}
