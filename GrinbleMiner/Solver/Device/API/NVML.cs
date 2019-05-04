/*
   Copyright 2019 Lip Wee Yeo Amano

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver.Device.API
{
    public static class NVML
    {

        #region Enums and structs

        public enum NvmlTemperatureSensors
        {
            Gpu = 0
        }

        public enum NvmlClockType
        {
            Graphics = 0,
            SM = 1,
            Mem = 2,
            Video = 3
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlDevice
        {
            public IntPtr Pointer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization
        {
            public uint Gpu;
            public uint Mem;
        }

        #endregion

        #region Declarations and constructors

        private const int SUCCESS = 0;

        public static bool Initialized;

        private static List<Tuple<string, DateTime, decimal>> _PowerHistory;

        static NVML()
        {
            _PowerHistory = new List<Tuple<string, DateTime, decimal>>();
        }

        #endregion

        #region P/Invoke

        private static class PInvokeWindows
        {

            public const string NVML_DLL_PATH = @"c:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll";

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlInit_v2")]
            public static extern int NvmlInit();

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlErrorString")]
            public static extern IntPtr NvmlErrorString(int result);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetHandleByPciBusId_v2")]
            public static extern int NvmlDeviceGetHandleByPciBusId([MarshalAs(UnmanagedType.LPStr)] string pciBusId, ref NvmlDevice device);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetTemperature")]
            public static extern int NvmlDeviceGetTemperature(NvmlDevice device, NvmlTemperatureSensors sensorType, ref uint temp);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetFanSpeed")]
            public static extern int NvmlDeviceGetFanSpeed(NvmlDevice device, ref uint speed);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetPowerUsage")]
            public static extern int NvmlDeviceGetPowerUsage(NvmlDevice device, ref uint power);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetClockInfo")]
            public static extern int NvmlDeviceGetClockInfo(NvmlDevice device, NvmlClockType type, ref uint clock);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetUtilizationRates")]
            public static extern int NvmlDeviceGetUtilizationRates(NvmlDevice device, ref NvmlUtilization utilization);

        }

        private static class PInvokeLinux
        {

            public const string NVML_DLL_PATH = @"libnvidia-ml.so";

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlInit_v2")]
            public static extern int NvmlInit();

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlErrorString")]
            public static extern IntPtr NvmlErrorString(int result);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetHandleByPciBusId_v2")]
            public static extern int NvmlDeviceGetHandleByPciBusId([MarshalAs(UnmanagedType.LPStr)] string pciBusId, ref NvmlDevice device);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetTemperature")]
            public static extern int NvmlDeviceGetTemperature(NvmlDevice device, NvmlTemperatureSensors sensorType, ref uint temp);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetFanSpeed")]
            public static extern int NvmlDeviceGetFanSpeed(NvmlDevice device, ref uint speed);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetPowerUsage")]
            public static extern int NvmlDeviceGetPowerUsage(NvmlDevice device, ref uint power);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetClockInfo")]
            public static extern int NvmlDeviceGetClockInfo(NvmlDevice device, NvmlClockType type, ref uint clock);

            [DllImport(NVML_DLL_PATH, EntryPoint = "nvmlDeviceGetUtilizationRates")]
            public static extern int NvmlDeviceGetUtilizationRates(NvmlDevice device, ref NvmlUtilization utilization);

        }

        #endregion

        #region Public methods

        public static bool Initialize(out string errorMessage)
        {
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlInit()
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlInit()
                : 12;

            Initialized = (response == SUCCESS);

            errorMessage = Initialized ? string.Empty : NvmlErrorString(response);

            return Initialized;
        }

        public static DeviceQuery QueryGpuStatus(string fullPciBusID)
        {
            var errorMessage = string.Empty;
            if (!Initialized) return null;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return null;

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                CUDA.Log(Level.Error, $"API failed to get device by PCI bus ID ({fullPciBusID}): {errorMessage}");
                return null;
            }

            var utilization = new NvmlUtilization();
            uint fanSpeed = 0u, temperature = 0u, power = 0u, gpuClock = 0u, vramClock = 0u;

            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetFanSpeed(device, ref fanSpeed)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetFanSpeed(device, ref fanSpeed)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetFanSpeed() responded with an error: {NvmlErrorString(response)}");

            response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetTemperature(device, NvmlTemperatureSensors.Gpu, ref temperature)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetTemperature(device, NvmlTemperatureSensors.Gpu, ref temperature)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetTemperature() responded with an error: {NvmlErrorString(response)}");

            response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetPowerUsage(device, ref power)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetPowerUsage(device, ref power)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetPowerUsage() responded with an error: {NvmlErrorString(response)}");

            response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetClockInfo(device, NvmlClockType.Graphics, ref gpuClock)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetClockInfo(device, NvmlClockType.Graphics, ref gpuClock)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetClockInfo() responded with an error: {NvmlErrorString(response)}");

            response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetClockInfo(device, NvmlClockType.Mem, ref vramClock)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetClockInfo(device, NvmlClockType.Mem, ref vramClock)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetClockInfo() responded with an error: {NvmlErrorString(response)}");

            response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : 12;

            if (response != SUCCESS)
                CUDA.Log(Level.Error, $"nvmlDeviceGetUtilizationRates() responded with an error: {NvmlErrorString(response)}");

            if (utilization.Gpu == 0u && utilization.Mem == 0u && fanSpeed == 0u && temperature == 0u && power == 0u && gpuClock == 0u && vramClock == 0u)
                return null;

            return new DeviceQuery()
            {
                FanSpeed = $"{fanSpeed} %",
                Temperature = $"{temperature} C",
                PowerDraw = $"{GetNormalizedPower(fullPciBusID, (decimal)power / 1000):F2} W",
                ClockGPU = $"{gpuClock} MHz",
                ClockVRAM = $"{vramClock} MHz",
                UtilizationGPU = $"{utilization.Gpu} %",
                UtilizationVRAM = $"{utilization.Mem} %"
            };
        }

        public static decimal GetDeviceFanSpeed(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var fanSpeed = 0u;
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetFanSpeed(device, ref fanSpeed)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetFanSpeed(device, ref fanSpeed)
                : 12;

            if (response == SUCCESS) return fanSpeed;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceTemperature(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var temperature = 0u;
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetTemperature(device, NvmlTemperatureSensors.Gpu, ref temperature)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetTemperature(device, NvmlTemperatureSensors.Gpu, ref temperature)
                : 12;

            if (response == SUCCESS) return temperature;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceNormalizedPower(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var power = 0u;
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetPowerUsage(device, ref power)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetPowerUsage(device, ref power)
                : 12;

            if (response == SUCCESS) return GetNormalizedPower(fullPciBusID, (decimal)power / 1000);

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceGpuClock(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var clock = 0u;
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetClockInfo(device, NvmlClockType.Graphics, ref clock)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetClockInfo(device, NvmlClockType.Graphics, ref clock)
                : 12;

            if (response == SUCCESS) return clock;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceVramClock(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var clock = 0u;
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetClockInfo(device, NvmlClockType.Mem, ref clock)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetClockInfo(device, NvmlClockType.Mem, ref clock)
                : 12;

            if (response == SUCCESS) return clock;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceGpuUtilization(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var utilization = new NvmlUtilization();
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : 12;

            if (response == SUCCESS) return utilization.Gpu;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        public static decimal GetDeviceVramUtilization(string fullPciBusID, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Initialized) return decimal.MinusOne;

            if (!GetDeviceByPciBusID(fullPciBusID, out NvmlDevice device, out errorMessage)) return decimal.MinValue;

            var utilization = new NvmlUtilization();
            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetUtilizationRates(device, ref utilization)
                : 12;

            if (response == SUCCESS) return utilization.Mem;

            errorMessage = NvmlErrorString(response);
            return decimal.MinValue;
        }

        #endregion

        #region Private methods

        private static string NvmlErrorString(int response)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                switch (response)
                {
                    case 0: return "Success";
                    case 1: return "Uninitialized";
                    case 2: return "InvalidArgument";
                    case 3: return "NotSupported";
                    case 4: return "NoPermission";
                    case 6: return "NotFound";
                    case 9: return "DriverNotLoaded";
                    case 12: return "LibraryNotFound";
                    case 13: return "FunctionNotFound";
                    case 17: return "OperatingSystem";
                    case 18: return "LibRMVersionMismatch";
                    case 999: return "Unknown";
                    default: return string.Empty;
                }
            }

            return Marshal.PtrToStringAnsi(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlErrorString(response)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlErrorString(response)
                : IntPtr.Zero).TrimEnd('\0');
        }

        private static bool GetDeviceByPciBusID(string fullPciBusID, out NvmlDevice device, out string errorMessage)
        {
            device = new NvmlDevice();

            var response =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeWindows.NvmlDeviceGetHandleByPciBusId(fullPciBusID, ref device)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? PInvokeLinux.NvmlDeviceGetHandleByPciBusId(fullPciBusID, ref device)
                : 12;

            errorMessage = (response == SUCCESS)
                 ? string.Empty
                 : NvmlErrorString(response);

            return (response == SUCCESS);
        }

        private static decimal GetNormalizedPower(string fullPciBusID, decimal currentPower)
        {
            lock (_PowerHistory)
            {
                _PowerHistory.RemoveAll(p => p.Item2 < DateTime.Now.AddMinutes(-1));
                _PowerHistory.Add(new Tuple<string, DateTime, decimal>(fullPciBusID, DateTime.Now, currentPower));

                return _PowerHistory.Where(p => p.Item1 == fullPciBusID).Average(p => p.Item3);
            }
        }

        #endregion

    }
}
