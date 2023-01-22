using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;
using Microsoft.Win32;
using EVC2Lib;

namespace PMDLogger {
    public class LoggingItem {

        public int ParentId;
        public int Id;
        public bool Enabled;
        public string Description;
        public string DescriptionShort;
        public string Unit;
        public double Value;
        public string HwinfoKey;

        public LoggingItem(int id, string desc, string desc_short, string unit) {
            Id = id;
            Description = desc;
            DescriptionShort = desc_short;
            Unit = unit;
            Enabled = true;
        }

        override public string ToString() {
            return Description;
        }

    }

    public class DataLogger {

        private const string HWINFO_REG_KEY = "Software\\HWiNFO64\\Sensors\\Custom";
        private const string EVC2_REG_KEY = "ElmorLabs EVC2";

        public List<LoggingItem> LoggingItemList;

        public bool IsLogging;
        
        int IdCounter;
        string FilePath;
        bool CsvMode;
        bool Hwinfo;

        private CultureInfo culture_info;

        public DataLogger() {
            IsLogging = false;
            IdCounter = 0;
            LoggingItemList = new List<LoggingItem>();
            culture_info = new CultureInfo("en-US");
        }

        public void SetHwinfo(bool hwinfo) {
            Hwinfo = hwinfo;
        }

        public bool SetFilePath(string path, bool csv) {
            //if(File.Exists(path)) {
                FilePath = path;
                CsvMode = csv;
                return true;
            //}
            //return false;
        }

        public string GetFilePath() {
            return FilePath;
        }

        public int AddLogItem(string desc, string desc_short, string unit) {
            LoggingItemList.Add(new LoggingItem(IdCounter, desc, desc_short, unit));
            return IdCounter++;
        }

        public bool RemoveLogItem(int id) {
            int index = LoggingItemList.FindIndex(s => s.Id == id);

            if(index != -1) {
                LoggingItemList.RemoveAt(index);
                return true;
            }

            return false;
        }

        public bool UpdateValue(int id, double value) {
            int index = LoggingItemList.FindIndex(s => s.Id == id);
            if(index != -1) {
                LoggingItemList[index].Value = value;
                return true;
            }

            return false;
        }

        public RegistryKey hwinfo_reg_key;

        public void Start() {
            if(!string.IsNullOrEmpty(FilePath) && CsvMode) {
                string header_line = "Timestamp,";
                foreach(LoggingItem logging_item in LoggingItemList) {
                    if(logging_item.Enabled) {
                        header_line += logging_item.Description + ",";
                    }
                }
                header_line = header_line.Substring(0, header_line.Length - 1);
                header_line += Environment.NewLine;
                File.WriteAllText(FilePath, header_line);
            }

            if(Hwinfo) {
                try {
                    hwinfo_reg_key = Registry.CurrentUser.OpenSubKey(HWINFO_REG_KEY, true);
                    if(hwinfo_reg_key == null) {
                        hwinfo_reg_key = Registry.CurrentUser.CreateSubKey(HWINFO_REG_KEY, true);
                    }
                } catch(Exception ex) {
                    throw new Exception(ex.Message);
                }

                if(hwinfo_reg_key == null) {
                    throw new Exception("Error accessing registry.");
                }

                try {

                    if(hwinfo_reg_key.OpenSubKey(EVC2_REG_KEY) != null) {
                        hwinfo_reg_key.DeleteSubKeyTree("");
                    }

                    hwinfo_reg_key = hwinfo_reg_key.CreateSubKey(EVC2_REG_KEY);

                    if(hwinfo_reg_key == null) {
                        throw new Exception("Error accessing registry.");
                    }
                    //hwinfo_reg_key.OpenSubKey("ElmorLabs EVC2", true);

                    int idx = 0;
                    foreach(LoggingItem logging_item in LoggingItemList) {

                        switch(logging_item.Unit) {
                            case "°C":
                                logging_item.HwinfoKey = $"Temp{idx}"; break;
                            case "A":
                                logging_item.HwinfoKey = $"Current{idx}"; break;
                            case "V":
                                logging_item.HwinfoKey = $"Volt{idx}"; break;
                            case "W":
                                logging_item.HwinfoKey = $"Power{idx}"; break;
                            case "RPM":
                                logging_item.HwinfoKey = $"Fan{idx}"; break;
                            case "%":
                                logging_item.HwinfoKey = $"Usage{idx}"; break;
                            default:
                                logging_item.Enabled = false; break;
                        }
                        idx++;

                        if(logging_item.Enabled) {
                            RegistryKey key = hwinfo_reg_key.CreateSubKey(logging_item.HwinfoKey);
                            if(key == null) {
                                throw new Exception("Error accessing registry.");
                            }
                            key.SetValue("Name", logging_item.Description, RegistryValueKind.String);
                            key.SetValue("Value", "0", RegistryValueKind.String);
                        }

                    }
                } catch(Exception ex) {
                    throw new Exception(ex.Message);
                }
                
            }

            IsLogging = true;
        }

        public void WriteEntry() {

            string text;
            List<string> oled_lines = new List<string>();

            if(!string.IsNullOrEmpty(FilePath) && CsvMode) {
                text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ",";
            } else {
                text = "";
            }

            // Build output
            foreach(LoggingItem logging_item in LoggingItemList) {
                if(logging_item.Enabled) {
                    string value = logging_item.Value.ToString(culture_info);
                    if(!string.IsNullOrEmpty(FilePath)) {
                        if(CsvMode) {
                            text += value + ",";
                        } else {
                            text += value + logging_item.Unit + Environment.NewLine;
                        }
                    }
                    if(Hwinfo) {
                        RegistryKey key = hwinfo_reg_key.OpenSubKey(logging_item.HwinfoKey, true);
                        key.SetValue("Value", value, RegistryValueKind.String);
                    }
                    
                }
            }

            // Write to file
            if(!string.IsNullOrEmpty(FilePath)) {
                if(CsvMode) {
                    text = text.Substring(0, text.Length - 1);
                    text += Environment.NewLine;
                    File.AppendAllText(FilePath, text);
                } else {
                    File.WriteAllText(FilePath, text);
                }
            }
    
        }

        public void Stop() {

            // Remove registry values
            if(Hwinfo) {
                RemoveHwinfoRegEntry();
            }

            IsLogging = false;
        }

        private void RemoveHwinfoRegEntry() {
            hwinfo_reg_key.DeleteSubKeyTree("");
        }
    }
}
