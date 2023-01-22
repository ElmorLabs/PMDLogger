using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PMDLogger
{
    public delegate void NewDataEventHandler(List<SensorData> sensor_data);

    public interface IPMDDevice
    {

        event NewDataEventHandler DataUpdated;

        int Id { get; }
        string Name { get; }
        List<Sensor> Sensors { get; }

        bool StartMonitoring();
        bool StopMonitoring();
    }
}
