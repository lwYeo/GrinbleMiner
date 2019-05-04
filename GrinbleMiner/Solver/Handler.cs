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
using System.Threading.Tasks;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver
{
    public static class Handler
    {

        #region Declarations and Properties

        public static List<ISolver> Solvers;

        public static bool IsAnyInitialized => Solvers?.Any(s => s.IsAnyInitialised) ?? false;

        public static bool IsAnySolving => Solvers?.Any(s => s.IsSolving) ?? false;

        public static bool IsAnySolverCrashed => Solvers?.Any(s => s.IsAnySolverCrashed) ?? false;

        #endregion

        #region Constructor

        static Handler()
        {
            Solvers = new List<ISolver>();

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    Task.Delay(15 * 1000).Wait();
                    try
                    {
                        var combinedGPS = 0.0m;

                        foreach (var solver in Solvers)
                            if (solver.IsSolving)
                            {
                                var totalGPS = 0.0m;
                                var allDevicesLogGPS = "Hashrate  :";
                                var allDevicesLogPower = "Power     :";
                                var allDevicesLogEfficiency = "Efficiency:";

                                foreach (var device in solver.Devices)
                                {
                                    var deviceGPS = solver.GetDeviceGraphPerSec(device);
                                    allDevicesLogGPS += $" #{device.DeviceID} {deviceGPS:F2}g/s";
                                    totalGPS += deviceGPS;

                                    switch (device)
                                    {
                                        case Device.CUDA cudaDevice:
                                            var deviceQuery = Device.API.NVML.QueryGpuStatus(cudaDevice.FullPciBusID);
                                            if (deviceQuery == null) break;

                                            if (decimal.TryParse(deviceQuery.PowerDraw.Split(" ")[0], out decimal tempPowerDraw))
                                            {
                                                if (tempPowerDraw <= decimal.Zero) break;

                                                allDevicesLogPower += $" #{device.DeviceID} {tempPowerDraw:F2}W";
                                                allDevicesLogEfficiency += $" #{device.DeviceID} {deviceGPS / tempPowerDraw * 1000:F1}g/MJ";
                                            }
                                            break;

                                        case Device.AMD amdDevice:
                                            var powerDraw = Device.API.AMD_ADL.GetAdapterNormalizedPower(
                                                Device.API.AMD_ADL.GetAdapterInfoByPciBusID(amdDevice.PciBusID), out string errorMessage);

                                            if (string.IsNullOrWhiteSpace(errorMessage))
                                            {
                                                if (powerDraw <= decimal.Zero) break;

                                                allDevicesLogPower += $" #{device.DeviceID} {powerDraw:F2}W";
                                                allDevicesLogEfficiency += $" #{device.DeviceID} {deviceGPS / powerDraw * 1000:F1}g/MJ";
                                            }
                                            else { AMD_APP.Log(Level.Debug, $"Failed to get current power draw: {errorMessage}"); }
                                            break;
                                    }
                                }

                                if (totalGPS == 0.0m) continue;
                                else combinedGPS += totalGPS;

                                switch (solver)
                                {
                                    case CUDA cudaSolver:
                                        CUDA.Log(Level.Info, allDevicesLogGPS); Task.Delay(50).Wait();
                                        if (allDevicesLogPower.Contains('#')) CUDA.Log(Level.Info, allDevicesLogPower); Task.Delay(50).Wait();
                                        if (allDevicesLogEfficiency.Contains('#')) CUDA.Log(Level.Info, allDevicesLogEfficiency); Task.Delay(50).Wait();
                                        break;

                                    case AMD_APP amdSolver:
                                        AMD_APP.Log(Level.Info, allDevicesLogGPS); Task.Delay(50).Wait();
                                        if (allDevicesLogPower.Contains('#')) AMD_APP.Log(Level.Info, allDevicesLogPower); Task.Delay(50).Wait();
                                        if (allDevicesLogEfficiency.Contains('#')) AMD_APP.Log(Level.Info, allDevicesLogEfficiency); Task.Delay(50).Wait();
                                        break;

                                    default:
                                        Log(Level.Info, $"[Unknown solver] {allDevicesLogGPS}"); Task.Delay(50).Wait();
                                        Log(Level.Info, $"[Unknown solver] Total: {totalGPS:F2}g/s"); Task.Delay(50).Wait();
                                        break;
                                }
                            }

                        if (Solvers.Any(s => s.IsSolving)) Log(Level.Info, $"Total Hashrate: {combinedGPS:F2}g/s");
                    }
                    catch { }
                }
            });
        }

        #endregion

        #region Public methods

        public static void Initialize(Config settings)
        {
            POW.CycleFinder.AllowLargeCpuMemory = settings.AllowLargeCpuMemory;

            if (settings.AllowCudaSolver)
            {
                CUDA.AllowLargeCpuMemory(settings.AllowLargeCpuMemory);

                if (!Device.API.NVML.Initialize(out string errorMessage))
                    Device.CUDA.Log(Level.Warn, $"Failed to initialize NVML: {errorMessage}");

                if (settings.CudaDevices == null || !settings.CudaDevices.Any())
                {
                    Device.CUDA.Log(Level.Info, "Solvers not assigned, seaching usable GPU(s)...");
                    settings.CudaDevices = Device.CUDA.GetDevices();
                    settings.Save();
                }
                else
                    foreach (var device in settings.CudaDevices)
                    {
                        try { device.Info = ManagedCuda.CudaContext.GetDeviceInfo(device.DeviceID); }
                        catch (Exception ex)
                        {
                            CUDA.Log(ex);
                            CUDA.Log(Level.Error, $"Failed to get device #[{device.DeviceID}]");
                        }
                    }

                Solvers.Add(new CUDA(settings.CudaDevices, settings));
            }

            if (settings.AllowAmdSolver)
            {
                AMD_APP.AllowLargeCpuMemory(settings.AllowLargeCpuMemory);

                Device.AMD.SetEnvironmentVariables();

                if (settings.AmdDevices == null || !settings.AmdDevices.Any())
                {
                    Device.AMD.Log(Level.Info, "Solvers not assigned, seaching usable GPU(s)...");
                    settings.AmdDevices = Device.AMD.GetDevices();
                    settings.Save();
                }
                else
                    foreach (var device in settings.AmdDevices)
                    {
                        try
                        {
                            device.Info = Device.AMD.GetDeviceByID(device.DeviceID);
                            device.AvailableMemory = Device.AMD.GetAvailableMemory(device.Info, device.DeviceID);
                        }
                        catch (Exception ex)
                        {
                            AMD_APP.Log(ex);
                            AMD_APP.Log(Level.Error, $"Failed to get device #[{device.DeviceID}]");
                        }
                    }

                Solvers.Add(new AMD_APP(settings.AmdDevices, settings));

                if (!Device.API.AMD_ADL.Initialize(out string errorMessage))
                {
                    AMD_APP.Log(Level.Error, errorMessage);
                    AMD_APP.Log(Level.Error, "AMD ADL not found.");
                }
            }
        }

        public static void StopSolving()
        {
            Solvers.AsParallel().ForAll(solver =>
            {
                solver.StopSolving();
                solver.FreeDevices();
            });
        }

        public static void StartSolving()
        {
            Solvers.AsParallel().ForAll(solver => solver.StartSolving());
        }

        public static Device.API.DeviceQuery GetMonitoringApiByDevice(Device.Base device)
        {
            switch (device)
            {
                case Device.CUDA cudaDevice:
                    return Device.API.NVML.QueryGpuStatus(cudaDevice.FullPciBusID);

                case Device.AMD amdDevice:
                    return Device.API.AMD_ADL.QueryGpuStatus(amdDevice.PciBusID);

                default:
                    return null;
            }
        }

        #endregion

    }
}
