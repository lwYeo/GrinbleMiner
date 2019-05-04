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
using ManagedCuda;
using Newtonsoft.Json;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver.Device
{
    public class CUDA : Base
    {

        #region Constants

        public const long DUCK_SIZE_A = 129L;
        public const long DUCK_SIZE_B = 82L;

        public const long DUCK_EDGES_A = DUCK_SIZE_A * 1024;
        public const long DUCK_EDGES_B = DUCK_SIZE_B * 1024;

        public const long BUFFER_SIZE_A = DUCK_SIZE_A * 1024 * 4096;
        public const long BUFFER_SIZE_B = DUCK_SIZE_B * 1024 * 4096 / 2;
        public const long BUFFER_SIZE_U64 = (DUCK_SIZE_A + DUCK_SIZE_B / 2) * 1024 * 4096;

        public const long INDEX_SIZE = 64 * 64 * 4;

        #endregion

        #region Static methods

        public static void Log(Level level, string message, params object[] args)
        {
            Logger.Log(level, $"[CUDA] {message}", args);
        }

        public static void Log(ConsoleColor customColor, Level level, string message, params object[] args)
        {
            Logger.Log(customColor, level, $"[CUDA] {message}", args);
        }

        public static double RequiredGPUMemoryGB() =>
            Math.Ceiling(((BUFFER_SIZE_U64 * sizeof(ulong)) + (INDEX_SIZE * 2 * sizeof(uint) * 256)) / Math.Pow(2, 30) * 10) / 10;

        public static int GetDeviceCount() => CudaContext.GetDeviceCount();

        public static CUDA[] GetDevices()
        {
            try
            {
                var deviceCount = GetDeviceCount();
                var deviceList = new List<CUDA>(deviceCount);

                Log(Level.Info, $"{deviceCount} devices found");

                for (var i = 0; i < deviceCount; i++)
                {
                    var deviceInfo = CudaContext.GetDeviceInfo(i);
                    var availableMemory = 0ul;

                    using (var context = new CudaContext(deviceInfo.PciDeviceId))
                        availableMemory = context.GetFreeDeviceMemorySize();

                    deviceList.Add(new CUDA()
                    {
                        Type = PlatformType.NVIDIA_CUDA,
                        DeviceID = i,
                        Info = deviceInfo,
                        AvailableMemory = availableMemory,
                        Allow = (Math.Round(availableMemory / Math.Pow(2, 30), 1) >= RequiredGPUMemoryGB()) &&
                                (deviceInfo.ComputeCapability.Major >= 6 && deviceInfo.ComputeCapability.Minor >= 1)
                    });
                }

                Log(Level.Info, $"{deviceList.Count(d => d.Allow)} devices with video memory of >{RequiredGPUMemoryGB()}GB");
                return deviceList.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                Log(Level.Error, "Failed to get CUDA devices");
                return Enumerable.Empty<CUDA>().ToArray();
            }
        }

        #endregion

        #region Properties

        private CudaDeviceProperties _Info;

        [JsonIgnore]
        public CudaDeviceProperties Info
        {
            get => _Info;
            set
            {
                _Info = value;
                PciBusID = _Info.PciBusId;
                Name = _Info.DeviceName;
                FullPciBusID = string.Join(":", new string[]
                {
                    _Info.PCIDomainID.ToString("X8"), _Info.PciBusId.ToString("X2"), _Info.PciDeviceId.ToString("X2")
                }) + ".0"/*Function*/;
            }
        }

        [JsonIgnore]
        public string FullPciBusID { get; private set; }

        [JsonIgnore]
        public CudaContext Context { get; set; }

        [JsonIgnore]
        public CudaKernel MeanSeedA { get; set; }

        [JsonIgnore]
        public CudaKernel MeanSeedB_4 { get; set; }

        [JsonIgnore]
        public CudaKernel MeanRound { get; set; }

        [JsonIgnore]
        public CudaKernel MeanRound_2 { get; set; }

        [JsonIgnore]
        public CudaKernel MeanTail { get; set; }

        [JsonIgnore]
        public CudaKernel MeanRecover { get; set; }

        [JsonIgnore]
        public CudaKernel MeanRoundJoin { get; set; }

        [JsonIgnore]
        public CudaStream Stream1 { get; set; }

        [JsonIgnore]
        public CudaStream Stream2 { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<ulong> Buffer_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<ulong> BufferMid_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<ulong> BufferB_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesA_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesB_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesC_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesX_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesY_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint> IndexesZ_device { get; set; }

        [JsonIgnore]
        public CudaDeviceVariable<uint>[,] Indexes_device { get; set; }

        #endregion

    }
}
