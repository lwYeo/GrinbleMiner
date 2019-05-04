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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using GrinbleMiner.NetworkInterface.Pool;
using GrinbleMiner.POW;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver
{

    public class CUDA : ISolver
    {

        #region Static methods

        public static void Log(Level level, string message, params object[] args)
        {
            Logger.Log(level, $"[CUDA] {message}", args);
        }

        public static void Log(Exception ex)
        {
            Logger.Log(ex, prefix: $"[CUDA]");
        }

        public static void AllowLargeCpuMemory(bool allowLargeCpuMemory)
        {
            maxCpuHandleEdgeCount = allowLargeCpuMemory
                ? (int)Math.Pow(2, 20)
                : (int)Math.Pow(2, 18);
        }

        #endregion

        #region Declarations

        private static readonly Dictionary<int, uint> defaultTrimRounds = new Dictionary<int, uint>()
        {
            { 2, 256 },
            { 4, 192 },
            { 6, 128 },
            { 8, 80 }
        };

        private static int maxCpuHandleEdgeCount;

        private static byte[] kernelBin_61;
        private static byte[] kernelBin_75;

        private readonly ConcurrentQueue<Solution> solutions;

        private readonly List<Task> solvingTasks;
        private readonly List<Job> jobHistoryList;

        private bool isStopSolving;
        
        private JobTemplate currentJob;

        #endregion

        #region Constructors

        public CUDA(Device.CUDA[] devices, Config settings)
        {
            Devices = devices;
            solvingTasks = new List<Task>();
            jobHistoryList = new List<Job>();
            solutions = new ConcurrentQueue<Solution>();

            using (var kernelStream_61 = typeof(Program).Assembly.GetManifestResourceStream("GrinbleMiner.kernel_61.ptx"))
            {
                kernelBin_61 = Array.CreateInstance(typeof(byte), kernelStream_61.Length) as byte[];
                kernelStream_61.Read(kernelBin_61, 0, kernelBin_61.Length);
            }

            using (var kernelStream_75 = typeof(Program).Assembly.GetManifestResourceStream("GrinbleMiner.kernel_75.ptx"))
            {
                kernelBin_75 = Array.CreateInstance(typeof(byte), kernelStream_75.Length) as byte[];
                kernelStream_75.Read(kernelBin_75, 0, kernelBin_75.Length);
            }

            InitializeDevices(settings);
        }

        #endregion

        #region IMiner

        #region Properties

        public Device.Base[] Devices { get; }

        public bool IsAnyInitialised => Devices?.Any(d => d.IsInitialized) ?? false;

        public bool IsAnySolverCrashed { get; private set; }

        public bool IsSolving => solvingTasks?.Any(t => !(t.IsCanceled || t.IsCompleted || t.IsFaulted)) ?? false;

        public bool IsPause { get; set; }

        public uint TrimRounds { get; private set; }

        #endregion

        #region Public methods

        public void InitializeDevices(Config settings)
        {
            try
            {
                if (settings.CpuLevel == 0)
                    TrimRounds = defaultTrimRounds.Last(t => t.Key <= Environment.ProcessorCount).Value;
                else
                    TrimRounds = defaultTrimRounds.ElementAt((int)(settings.CpuLevel - 1)).Value;
            }
            catch (Exception ex) { Log(ex); }

            if (TrimRounds == 0) TrimRounds = 128;

            (Devices as Device.CUDA[]).AsParallel().ForAll(device => InitializeDevice(device));
        }

        public void FreeDevices()
        {
            if (Devices == null || Devices.All(d => !d.IsInitialized)) return;

            Log(Level.Info, "Freeing devices...");
            (Devices as Device.CUDA[]).AsParallel().ForAll(device => FreeDevice(device));
        }

        public void StopSolving()
        {
            isStopSolving = true;
            var cooldown = 10;

            if (solvingTasks != null)
                Task.WaitAll(solvingTasks.ToArray(), cooldown * 1000);
        }

        public void StartSolving()
        {
            if (Devices.All(d => !d.Allow)) return;

            Log(Level.Info, $"Starting {Devices.Count(d => d.Allow)} solvers...");

            solvingTasks.Clear();

            solvingTasks.AddRange((Devices as Device.CUDA[]).AsParallel().Where(d => d.Allow).Select(device =>
            {
                return Task.Factory.StartNew(() => StartSolvingByDevice(device), TaskCreationOptions.LongRunning);
            }).AsEnumerable());

            currentJob = NetworkInterface.Pool.Handler.CurrentJob;
            NetworkInterface.Pool.Handler.OnNewJob += OnNewJobHandler;
        }

        public decimal GetDeviceGraphPerSec(Device.Base device)
        {
            if (!jobHistoryList.Any()) return 0;

            return jobHistoryList.
                Where(j => j.DeviceID == device.DeviceID && j.EndSolveOn > DateTime.MinValue).
                Select(j =>
                {
                    var interval = j.EndSolveOn - j.BeginSolveOn;

                    return (interval.TotalSeconds > 0)
                        ? j.GraphAttempts / (decimal)(interval.TotalSeconds)
                        : 0m;
                }).
                DefaultIfEmpty(0.0m).
                Average();
        }

        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~MinerBase() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

        #endregion

        #region Private methods

        private void OnNewJobHandler(NetworkInterface.Pool.Stratum currentStratum, JobTemplate newJob)
        {
            currentJob = newJob;
        }

        private void AddToJobHistory(Job job)
        {
            if (job == null) return;
            Task.Factory.StartNew(() =>
            {
                lock (jobHistoryList)
                {
                    jobHistoryList.RemoveAll(j => j.JobID == job.JobID && j.Height == job.Height && j.DeviceID == job.DeviceID);

                    jobHistoryList.
                    GroupBy(j => j.DeviceID).
                    Where(g => g.Count() > 1).
                    ToList().
                    ForEach(g => jobHistoryList.RemoveAll(j => j.BeginSolveOn.AddMinutes(1) < DateTime.Now));

                    jobHistoryList.Add(job);
                }
            });
        }

        private void InitializeDevice(Device.CUDA device)
        {
            if (!device.Allow) return;

            Log(Level.Debug, $"Initializing device ID [{device.DeviceID}]");

            var resourceBin = kernelBin_61; // 10-series

            if (device.Info.ComputeCapability.Major >= 7 && device.Info.ComputeCapability.Minor >= 5)
                resourceBin = kernelBin_75; // 16/20-series

            try
            {
                Log(Level.Debug, $"Loading kernel to device ID [{device.DeviceID}]");

                device.Context = new CudaContext(device.DeviceID, CUCtxFlags.MapHost | CUCtxFlags.BlockingSync);
                device.Context.SetSharedMemConfig(CUsharedconfig.EightByteBankSize);

                device.MeanSeedA = device.Context.LoadKernelPTX(resourceBin, "FluffySeed2A");
                device.MeanSeedA.BlockDimensions = 128;
                device.MeanSeedA.GridDimensions = 2048;
                device.MeanSeedA.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanSeedB_4 = device.Context.LoadKernelPTX(resourceBin, "FluffySeed2B");
                device.MeanSeedB_4.BlockDimensions = 128;
                device.MeanSeedB_4.GridDimensions = 512;
                device.MeanSeedB_4.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanRound = device.Context.LoadKernelPTX(resourceBin, "FluffyRound");
                device.MeanRound.BlockDimensions = 512;
                device.MeanRound.GridDimensions = 4096;
                device.MeanRound.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanRound_2 = device.Context.LoadKernelPTX(resourceBin, "FluffyRound");
                device.MeanRound_2.BlockDimensions = 512;
                device.MeanRound_2.GridDimensions = 2048;
                device.MeanRound_2.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanRoundJoin = device.Context.LoadKernelPTX(resourceBin, "FluffyRound_J");
                device.MeanRoundJoin.BlockDimensions = 512;
                device.MeanRoundJoin.GridDimensions = 4096;
                device.MeanRoundJoin.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanTail = device.Context.LoadKernelPTX(resourceBin, "FluffyTail");
                device.MeanTail.BlockDimensions = 1024;
                device.MeanTail.GridDimensions = 4096;
                device.MeanTail.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;

                device.MeanRecover = device.Context.LoadKernelPTX(resourceBin, "FluffyRecovery");
                device.MeanRecover.BlockDimensions = 256;
                device.MeanRecover.GridDimensions = 2048;
                device.MeanRecover.PreferredSharedMemoryCarveout = CUshared_carveout.MaxShared;
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed loading kernel to device ID [{device.DeviceID}]");
                return;
            }

            device.AvailableMemory = device.Context.GetFreeDeviceMemorySize();
            var availableMemory = Math.Round(device.AvailableMemory / Math.Pow(2, 30), 1);
            try
            {
                Log(Level.Debug, $"Allocating video memory on device ID [{device.DeviceID}]");
                Log(Level.Info, $"Available video memory on device ID [{device.DeviceID}]: {availableMemory}GB");

                device.Buffer_device = new CudaDeviceVariable<ulong>(Device.CUDA.BUFFER_SIZE_U64);
                device.BufferMid_device = new CudaDeviceVariable<ulong>(device.Buffer_device.DevicePointer + (Device.CUDA.BUFFER_SIZE_B * 8));
                device.BufferB_device = new CudaDeviceVariable<ulong>(device.Buffer_device.DevicePointer + (Device.CUDA.BUFFER_SIZE_A * 8));

                device.IndexesA_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                device.IndexesB_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                device.IndexesC_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                device.IndexesX_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                device.IndexesY_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                device.IndexesZ_device = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);

                device.Indexes_device = new CudaDeviceVariable<uint>[TrimRounds + 1, 2];

                for (var i = 0; i < TrimRounds + 1; i++)
                {
                    device.Indexes_device[i, 0] = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                    device.Indexes_device[i, 1] = new CudaDeviceVariable<uint>(Device.CUDA.INDEX_SIZE * 2);
                }

                device.Stream1 = new CudaStream(CUStreamFlags.NonBlocking);
                device.Stream2 = new CudaStream(CUStreamFlags.NonBlocking);
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Out of video memory at device ID [{device.DeviceID}], required >{Device.CUDA.RequiredGPUMemoryGB()}GB");
                return;
            }

            device.IsInitialized = true;
        }

        private void FreeDevice(Device.CUDA device)
        {
            lock (device)
                if (device.IsInitialized)
                {
                    try
                    {
                        device.Buffer_device?.Dispose();
                        device.BufferMid_device?.Dispose();
                        device.BufferB_device?.Dispose();

                        device.IndexesA_device?.Dispose();
                        device.IndexesB_device?.Dispose();
                        device.IndexesC_device?.Dispose();
                        device.IndexesX_device?.Dispose();
                        device.IndexesY_device?.Dispose();
                        device.IndexesZ_device?.Dispose();

                        if (device.Indexes_device != null)
                            foreach (var index in device.Indexes_device)
                                index?.Dispose();

                        device.Stream1?.Dispose();
                        device.Stream2?.Dispose();

                        device.Context?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                        Log(Level.Error, $"Failed to free buffers at device ID [{device.DeviceID}]");
                    }
                    finally { device.IsInitialized = false; }
                }
        }

        private void StartSolvingByDevice(Device.CUDA device)
        {
            if (isStopSolving) return;

            Job nextJob = null, tempJob = null;
            Solution foundSolution = null;

            var cpuThreadCount = 0;
            var edgeCount = new uint[2];
            var edgeBuffer = new ulong[maxCpuHandleEdgeCount];

            var maxLogLevel = (Level)Math.Max((int)MinimumLogLevel, (int)MinimumFileLogLevel);
            var timer = (maxLogLevel == Level.Verbose) ? new Stopwatch() : null;

            device.Context.SetCurrent();

            while (!isStopSolving)
            {
                try
                {
                    if (solutions.TryDequeue(out Solution solution))
                    {
                        foundSolution = solution;

                        device.IndexesB_device.MemsetAsync(0, device.Stream2.Stream);

                        device.MeanRecover.SetConstantVariable<byte>("nonce", foundSolution.Job.NonceHash);

                        device.MeanRecover.SetConstantVariable<ulong>("recovery", foundSolution.GetUlongEdges());

                        device.MeanRecover.RunAsync(device.Stream2.Stream,
                            device.IndexesB_device.DevicePointer);
                    }

                    while (IsPause) Task.Delay(500).Wait();

                    if (currentJob == null)
                    {
                        Task.Delay(500).Wait();
                        continue;
                    }
                    else if (tempJob == null || tempJob.JobID != currentJob.JobID || tempJob.Height != currentJob.Height)
                    {
                        nextJob = new Job(currentJob, device.DeviceID).NextNonce();
                    }

                    tempJob = nextJob;

                    var nextJobTask = Task.Factory.StartNew(() => tempJob.NextNonce(), TaskCreationOptions.LongRunning);

                    if (maxLogLevel == Level.Verbose) timer.Restart();

                    device.IndexesA_device.MemsetAsync(0, device.Stream1.Stream);
                    device.IndexesC_device.MemsetAsync(0, device.Stream2.Stream);

                    device.MeanSeedA.SetConstantVariable<byte>("nonce", tempJob.NonceHash);

                    device.MeanSeedA.RunAsync(device.Stream1.Stream,
                        device.BufferMid_device.DevicePointer,
                        device.IndexesA_device.DevicePointer);

                    if (foundSolution != null)
                    {
                        device.Stream2.Synchronize();

                        device.IndexesB_device.CopyToHost(foundSolution.nonces, 0, 0, CycleFinder.CUCKOO_42 * sizeof(uint));

                        var recoverSolution = foundSolution;
                        foundSolution = null;

                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                recoverSolution.nonces = recoverSolution.nonces.OrderBy(n => n).ToArray();

                                if (recoverSolution.CheckDifficulty(out ulong currentDiff, out ulong expectDiff))
                                {
                                    Log(Level.Debug, $"Solved difficulty of >={expectDiff} ({currentDiff})");
                                    recoverSolution.Job.OnJobSolved();

                                    Log(recoverSolution.Job.Origin.Category == FeeCategory.Miner
                                        ? Level.Info : Level.Debug,
                                        $"Device #{device.DeviceID} submitting solution...");

                                    recoverSolution.Job.Origin.SubmitSolution(new SubmitParams()
                                    {
                                        Height = recoverSolution.Job.Height,
                                        Nonce = recoverSolution.Job.Nonce,
                                        JobID = recoverSolution.Job.JobID,
                                        POW = recoverSolution.nonces
                                    });
                                }
                                else { Log(Level.Debug, $"Low difficulty solution of <{expectDiff} ({currentDiff})"); }
                            }
                            catch (Exception ex) { Log(ex); }
                        });
                    }

                    device.IndexesB_device.MemsetAsync(0, device.Stream1.Stream);

                    device.Stream2.Synchronize();
                    device.Stream1.Synchronize();

                    for (var i = 0; i < 4; i++)
                    {
                        device.MeanSeedB_4.RunAsync(((i % 2) == 0) ? device.Stream1.Stream : device.Stream2.Stream,
                            device.BufferMid_device.DevicePointer,
                            device.Buffer_device.DevicePointer + (Device.CUDA.BUFFER_SIZE_A * 8 / 4 * i),
                            device.IndexesA_device.DevicePointer,
                            device.IndexesB_device.DevicePointer,
                            i * 16);
                    }

                    device.Stream1.Synchronize();
                    device.Stream2.Synchronize();

                    device.MeanRound_2.RunAsync(device.Stream1.Stream,
                        device.Buffer_device.DevicePointer + ((Device.CUDA.BUFFER_SIZE_A * 8) / 4) * 2,
                        device.BufferB_device.DevicePointer,
                        device.IndexesB_device.DevicePointer + (2048 * 4),
                        device.IndexesC_device.DevicePointer + (4096 * 4),
                        Device.CUDA.DUCK_EDGES_A,
                        Device.CUDA.DUCK_EDGES_B / 2);
                    
                    device.MeanRound_2.RunAsync(device.Stream2.Stream,
                        device.Buffer_device.DevicePointer,
                        device.BufferB_device.DevicePointer - (Device.CUDA.BUFFER_SIZE_B * 8),
                        device.IndexesB_device.DevicePointer,
                        device.IndexesC_device.DevicePointer,
                        Device.CUDA.DUCK_EDGES_A,
                        Device.CUDA.DUCK_EDGES_B / 2);

                    device.Stream1.Synchronize();
                    device.Stream2.Synchronize();

                    device.IndexesA_device.MemsetAsync(0, device.Stream2.Stream);
                    device.IndexesB_device.MemsetAsync(0, device.Stream2.Stream);

                    device.MeanRoundJoin.RunAsync(device.Stream1.Stream,
                        device.BufferB_device.DevicePointer - (Device.CUDA.BUFFER_SIZE_B * 8),
                        device.BufferB_device.DevicePointer,
                        device.Buffer_device.DevicePointer,
                        device.IndexesC_device.DevicePointer,
                        device.IndexesX_device.DevicePointer,
                        Device.CUDA.DUCK_EDGES_B / 2,
                        Device.CUDA.DUCK_EDGES_B / 2);

                    device.MeanRound.RunAsync(device.Stream1.Stream,
                        device.Buffer_device.DevicePointer,
                        device.BufferB_device.DevicePointer,
                        device.IndexesX_device.DevicePointer,
                        device.IndexesY_device.DevicePointer,
                        Device.CUDA.DUCK_EDGES_B / 2,
                        Device.CUDA.DUCK_EDGES_B / 2);

                    device.MeanRound.RunAsync(device.Stream1.Stream,
                        device.BufferB_device.DevicePointer,
                        device.Buffer_device.DevicePointer,
                        device.IndexesY_device.DevicePointer,
                        device.IndexesZ_device.DevicePointer,
                        Device.CUDA.DUCK_EDGES_B / 2,
                        Device.CUDA.DUCK_EDGES_B / 2);

                    device.Stream2.Synchronize();

                    device.MeanRound.RunAsync(device.Stream1.Stream,
                        device.Buffer_device.DevicePointer,
                        device.BufferB_device.DevicePointer,
                        device.IndexesZ_device.DevicePointer,
                        device.Indexes_device[0, 0].DevicePointer,
                        Device.CUDA.DUCK_EDGES_B / 2,
                        Device.CUDA.DUCK_EDGES_B / 4);

                    device.Stream1.Synchronize();

                    device.IndexesX_device.MemsetAsync(0, device.Stream2.Stream);
                    device.IndexesY_device.MemsetAsync(0, device.Stream2.Stream);
                    device.IndexesZ_device.MemsetAsync(0, device.Stream2.Stream);

                    for (var i = 0; i < TrimRounds; i++)
                    {
                        device.MeanRound.RunAsync(device.Stream1.Stream,
                               device.BufferB_device.DevicePointer,
                               device.Buffer_device.DevicePointer,
                               device.Indexes_device[i, 0].DevicePointer,
                               device.Indexes_device[i, 1].DevicePointer,
                               Device.CUDA.DUCK_EDGES_B / 4,
                               Device.CUDA.DUCK_EDGES_B / 4);

                        device.MeanRound.RunAsync(device.Stream1.Stream,
                            device.Buffer_device.DevicePointer,
                            device.BufferB_device.DevicePointer,
                            device.Indexes_device[i, 1].DevicePointer,
                            device.Indexes_device[i + 1, 0].DevicePointer,
                            Device.CUDA.DUCK_EDGES_B / 4,
                            Device.CUDA.DUCK_EDGES_B / 4);
                    }

                    device.MeanTail.RunAsync(device.Stream1.Stream,
                        device.BufferB_device.DevicePointer,
                        device.Buffer_device.DevicePointer,
                        device.Indexes_device[TrimRounds, 0].DevicePointer,
                        device.Indexes_device[TrimRounds, 1].DevicePointer);

                    device.Stream1.Synchronize();

                    device.Indexes_device[TrimRounds, 1].CopyToHost(edgeCount, 0, 0, 8);

                    foreach (var index in device.Indexes_device)
                        index.MemsetAsync(0, device.Stream2.Stream);

                    if (maxLogLevel == Level.Verbose)
                        Log(Level.Verbose, $"Device #{device.DeviceID} Job #{tempJob.JobID}: " +
                            $"Trimmed to {edgeCount[0]} edges in {timer.ElapsedMilliseconds}ms");

                    tempJob.EndSolveOn = DateTime.Now;

                    if (edgeCount[0] < maxCpuHandleEdgeCount)
                    {
                        device.Buffer_device.CopyToHost(edgeBuffer, 0, 0, edgeCount[0] * 8);

                        var handleJob = tempJob;
                        var tempEdgeCount = (int)edgeCount[0];
                        var tempEdgeBuffer = edgeBuffer.ToArray();

                        Task.Factory.StartNew(() =>
                        {
                            var uintEdgeBuffer = new uint[tempEdgeCount * sizeof(ulong) / sizeof(uint)];
                            Buffer.BlockCopy(tempEdgeBuffer, 0, uintEdgeBuffer, 0, tempEdgeCount * sizeof(ulong));
                            tempEdgeBuffer = null;

                            var cf = CycleFinder.GetInstance();
                            cf.SetJob(handleJob);
                            cf.SetEdges(uintEdgeBuffer, tempEdgeCount);
                            try
                            {
                                if (cpuThreadCount++ < Math.Max(3, Environment.ProcessorCount))
                                    cf.FindSolutions(solutions);
                                else
                                    Log(Level.Warn, "CPU overloaded!");
                            }
                            finally
                            {
                                cpuThreadCount--;
                                CycleFinder.ReturnInstance(cf);
                            }
                        }, TaskCreationOptions.LongRunning);
                    }

                    AddToJobHistory(tempJob);
                    nextJob = nextJobTask.Result;
                }
                catch (Exception ex)
                {
                    Log(ex);
                    FreeDevice(device);
                    IsAnySolverCrashed = true;
                    break;
                }
            }
        }

        #endregion

    }
}
