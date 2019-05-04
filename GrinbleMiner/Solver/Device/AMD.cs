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
using Newtonsoft.Json;
using OpenCl.DotNetCore.CommandQueues;
using OpenCl.DotNetCore.Contexts;
using OpenCl.DotNetCore.Devices;
using OpenCl.DotNetCore.Kernels;
using OpenCl.DotNetCore.Memory;
using OpenCl.DotNetCore.Platforms;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver.Device
{
    public class AMD : Base
    {

        #region Constants

        public const int DUCK_SIZE_A = 129; // AMD 126 + 3
        public const int DUCK_SIZE_B = 83;

        public const int BUFFER_SIZE_A1 = DUCK_SIZE_A * 1024 * (4096 - 128) * 2;
        public const int BUFFER_SIZE_A2 = DUCK_SIZE_A * 1024 * 256 * 2;
        public const int BUFFER_SIZE_B = DUCK_SIZE_B * 1024 * 4096 * 2;
        public const int BUFFER_SIZE_U32 = (DUCK_SIZE_A + DUCK_SIZE_B) * 1024 * 4096 * 2;

        public const int INDEX_SIZE = 256 * 256 * 4;

        #endregion

        #region Public static methods

        public static void Log(Level level, string message, params object[] args)
        {
            Logger.Log(level, $"[AMD] {message}", args);
        }

        public static void Log(ConsoleColor customColor, Level level, string message, params object[] args)
        {
            Logger.Log(customColor, level, $"[AMD] {message}", args);
        }

        public static void SetEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("GPU_MAX_HEAP_SIZE", "100", EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("GPU_USE_SYNC_OBJECTS", "1", EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("GPU_MAX_ALLOC_PERCENT", "100", EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("GPU_SINGLE_ALLOC_PERCENT", "100", EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("GPU_64BIT_ATOMICS", "1", EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("GPU_MAX_WORKGROUP_SIZE", "1024", EnvironmentVariableTarget.User);
        }

        public static double RequiredGPUMemoryGB() =>
            Math.Ceiling(((((long)BUFFER_SIZE_A1 + BUFFER_SIZE_A2 + BUFFER_SIZE_B)) + (INDEX_SIZE * 2) + (POW.CycleFinder.CUCKOO_42 * 2)) * sizeof(uint) / Math.Pow(2, 30) * 10) / 10;

        public static OpenCl.DotNetCore.Devices.Device GetDeviceByID(int deviceID)
        {
            var platform = Platform.GetPlatforms().FirstOrDefault(p => p.Name.IndexOf("AMD") > -1);
            return platform.GetDevices(DeviceType.Gpu).ElementAt(deviceID);
        }

        public static AMD[] GetDevices()
        {
            try
            {
                var platform = Platform.GetPlatforms().FirstOrDefault(p => p.Name.IndexOf("AMD") > -1);
                var devices = platform.GetDevices(DeviceType.Gpu).Where(d => d.IsAvailable).ToArray();
                var deviceList = new List<AMD>(devices.Length);

                Log(Level.Info, $"{devices.Length} devices found");

                for (var id = 0; id < devices.Length; id++)
                {
                    var newDevice = new AMD()
                    {
                        Type = PlatformType.AMD_APP,
                        Info = devices[id],
                        DeviceID = id,
                        Name = devices[id].Name, // AMD: codename
                        PciBusID = GetDevicePciBusID(devices[id], id)
                    };
                    deviceList.Add(newDevice);

                    newDevice.Name = GetDeviceName(devices[id], id); // AMD: actual model name
                    newDevice.AvailableMemory = GetAvailableMemory(devices[id], id);
                    newDevice.Allow = ((newDevice.AvailableMemory / Math.Pow(2, 30) * 10) / 10 > RequiredGPUMemoryGB());

                    if (deviceList.Last().AvailableMemory <= 0)
                    {
                        Console.WriteLine($"Unknown memory count from device #{id} of PCI Bus #{deviceList.Last().PciBusID}" +
                            $" ({deviceList.Last().Name})");

                        bool? useDevice = null;
                        while (useDevice == null)
                        {
                            Console.WriteLine("Do you want to use it? [Y/N]");
                            switch (Console.ReadKey().KeyChar)
                            {
                                case 'Y':
                                case 'y':
                                    useDevice = true;
                                    break;
                                case 'N':
                                case 'n':
                                    useDevice = false;
                                    break;
                            }
                            Console.WriteLine();
                        }
                        deviceList.Last().Allow = useDevice.Value;
                    }
                }

                Log(Level.Info, $"{deviceList.Count(d => d.Allow)} devices with video memory of >{RequiredGPUMemoryGB()}GB");
                return deviceList.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                Log(Level.Error, "Failed to get AMD devices");
                return Enumerable.Empty<AMD>().ToArray();
            }
        }

        public static ulong GetAvailableMemory(OpenCl.DotNetCore.Devices.Device device, int deviceID)
        {
            try { return API.AMD.GetAvailableMemory(device); }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed to get free memory from device #{deviceID}");
            }
            return 0;
        }

        #endregion

        #region Private static methods

        private static int GetDevicePciBusID(OpenCl.DotNetCore.Devices.Device device, int deviceID)
        {
            try { return API.AMD.GetAmdDevicePciBusID(device); }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed to get PCI bus ID from device #{deviceID}");
            }
            return -1;
        }

        private static string GetDeviceName(OpenCl.DotNetCore.Devices.Device device, int deviceID)
        {
            try
            {
                var deviceName = API.AMD.GetAmdDeviceName(device);
                return string.IsNullOrWhiteSpace(deviceName)
                    ? device.Name
                    : deviceName;
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed to get device name from device #{deviceID}");
            }
            return device.Name;
        }

        #endregion

        #region Properties

        [JsonIgnore]
        public OpenCl.DotNetCore.Devices.Device Info { get; set; }

        [JsonIgnore]
        public Context Context { get; set; }

        [JsonIgnore]
        public OpenCl.DotNetCore.Programs.Program Program { get; set; }

        [JsonIgnore]
        public Kernel KernelSeedA { get; set; }

        [JsonIgnore]
        public Kernel KernelSeedB1 { get; set; }

        [JsonIgnore]
        public Kernel KernelSeedB2 { get; set; }

        [JsonIgnore]
        public Kernel KernelRound1 { get; set; }

        [JsonIgnore]
        public Kernel KernelRoundO { get; set; }

        [JsonIgnore]
        public Kernel KernelRoundNA { get; set; }

        [JsonIgnore]
        public Kernel KernelRoundNB { get; set; }

        [JsonIgnore]
        public Kernel KernelTail { get; set; }

        [JsonIgnore]
        public Kernel KernelRecovery { get; set; }

        [JsonIgnore]
        public CommandQueue CommandQueue { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferA1 { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferA2 { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferB { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferI1 { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferI2 { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferNonce { get; set; }

        [JsonIgnore]
        public MemoryBuffer BufferR { get; set; }

        #endregion

    }
}
