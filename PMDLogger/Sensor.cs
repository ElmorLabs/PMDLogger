using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PMDLogger
{
    public class Sensor
    {
        public int Id;
        public string DescriptionLong { get; }
        public string DescriptionShort { get; }
        public string Unit { get; }

        public Sensor(int id, string description_long, string description_short, string unit)
        {
            Id = id;
            DescriptionLong = description_long;
            DescriptionShort = description_short;
            Unit = unit;
        }
    }

    public class SensorData
    {
        public int Id;
        public double Value;

        public SensorData(int id, double value)
        {
            Id = id;
            Value = value;
        }
    }
}
