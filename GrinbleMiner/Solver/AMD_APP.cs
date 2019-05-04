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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCl.DotNetCore.CommandQueues;
using OpenCl.DotNetCore.Contexts;
using OpenCl.DotNetCore.Memory;
using GrinbleMiner.NetworkInterface.Pool;
using GrinbleMiner.POW;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.Solver
{
    public class AMD_APP : ISolver
    {

        #region Static methods

        public static void Log(Level level, string message, params object[] args)
        {
            Logger.Log(level, $"[AMD APP] {message}", args);
        }

        public static void Log(Exception ex)
        {
            Logger.Log(ex, prefix: $"[AMD APP]");
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

        private static string sKernel;

        private readonly ConcurrentQueue<Solution> solutions;

        private readonly List<Task> solvingTasks;
        private readonly List<Job> jobHistoryList;

        private bool isStopSolving;

        private JobTemplate currentJob;

        #endregion

        #region Constructors

        public AMD_APP(Device.AMD[] devices, Config settings)
        {
            Devices = devices;
            solvingTasks = new List<Task>();
            jobHistoryList = new List<Job>();
            solutions = new ConcurrentQueue<Solution>();

            using (var kernelStream = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("GrinbleMiner.kernel.cl")))
                sKernel = kernelStream.ReadToEnd();

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

            // TODO: check is parallel initialization possible
            (Devices as Device.AMD[]).ToList().ForEach(device => InitializeDevice(device));
        }

        public void FreeDevices()
        {
            if (Devices == null || Devices.All(d => !d.IsInitialized)) return;

            Log(Level.Info, "Freeing devices...");
            (Devices as Device.AMD[]).AsParallel().ForAll(device => FreeDevice(device));
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

            solvingTasks.AddRange((Devices as Device.AMD[]).AsParallel().Where(d => d.Allow).Select(device =>
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
        // ~AMD_APP() {
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

        private void InitializeDevice(Device.AMD device)
        {
            if (!device.Allow) return;

            Log(Level.Debug, $"Initializing device ID [{device.DeviceID}]");

            try
            {
                device.Context = Context.CreateContext(device.Info);
                device.Program = device.Context.CreateAndBuildProgramFromString(sKernel);
                device.CommandQueue = CommandQueue.CreateCommandQueue(device.Context, device.Info);
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed loading kernel to device ID [{device.DeviceID}]");
                return;
            }

            try
            {
                device.KernelSeedA = device.Program.CreateKernel("FluffySeed2A");
                device.KernelSeedB1 = device.Program.CreateKernel("FluffySeed2B");
                device.KernelSeedB2 = device.Program.CreateKernel("FluffySeed2B");
                device.KernelRound1 = device.Program.CreateKernel("FluffyRound1");
                device.KernelRoundO = device.Program.CreateKernel("FluffyRoundNO1");
                device.KernelRoundNA = device.Program.CreateKernel("FluffyRoundNON");
                device.KernelRoundNB = device.Program.CreateKernel("FluffyRoundNON");
                device.KernelTail = device.Program.CreateKernel("FluffyTailO");
                device.KernelRecovery = device.Program.CreateKernel("FluffyRecovery");
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Failed loading kernel to device ID [{device.DeviceID}]");
                return;
            }

            var availableMemory = Math.Round(device.AvailableMemory / Math.Pow(2, 30), 1);
            try
            {
                Log(Level.Debug, $"Allocating video memory on device ID [{device.DeviceID}]");
                Log(Level.Info, $"Available video memory on device ID [{device.DeviceID}]: {availableMemory}GB");

                device.BufferA1 = device.Context.CreateBuffer<uint>(MemoryFlag.ReadWrite, Device.AMD.BUFFER_SIZE_A1);
                device.BufferA2 = device.Context.CreateBuffer<uint>(MemoryFlag.ReadWrite, Device.AMD.BUFFER_SIZE_A2);
                device.BufferB = device.Context.CreateBuffer<uint>(MemoryFlag.ReadWrite, Device.AMD.BUFFER_SIZE_B);

                device.BufferI1 = device.Context.CreateBuffer<uint>(MemoryFlag.ReadWrite, Device.AMD.INDEX_SIZE);
                device.BufferI2 = device.Context.CreateBuffer<uint>(MemoryFlag.ReadWrite, Device.AMD.INDEX_SIZE);

                device.BufferNonce = device.Context.CreateBuffer<byte>(MemoryFlag.ReadOnly, 32);
                device.BufferR = device.Context.CreateBuffer<uint>(MemoryFlag.ReadOnly, CycleFinder.CUCKOO_42 * 2);
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, $"Out of video memory at device ID [{device.DeviceID}], required >{Device.AMD.RequiredGPUMemoryGB()}GB");
                return;
            }

            device.IsInitialized = true;
        }

        private void FreeDevice(Device.AMD device)
        {
            lock (device)
                if (device.IsInitialized)
                {
                    try
                    {
                        device.BufferA1?.Dispose();
                        device.BufferA2?.Dispose();
                        device.BufferB?.Dispose();

                        device.BufferI1?.Dispose();
                        device.BufferI2?.Dispose();

                        device.BufferNonce?.Dispose();
                        device.BufferR?.Dispose();

                        device.KernelSeedA?.Dispose();
                        device.KernelSeedB1?.Dispose();
                        device.KernelSeedB2?.Dispose();
                        device.KernelRound1?.Dispose();
                        device.KernelRoundO?.Dispose();
                        device.KernelRoundNA?.Dispose();
                        device.KernelRoundNB?.Dispose();
                        device.KernelTail?.Dispose();
                        device.KernelRecovery?.Dispose();

                        device.CommandQueue?.Dispose();
                        device.Program?.Dispose();
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

        private void StartSolvingByDevice(Device.AMD device)
        {
            if (isStopSolving) return;

            Job nextJob = null, tempJob = null;

            var cpuThreadCount = 0;
            var edgeCount = new uint[1];
            int[] edgeBuffer = new int[maxCpuHandleEdgeCount * 2];

            var clearPattern = Marshal.AllocHGlobal(4);
            Marshal.Copy(new byte[4] { 0, 0, 0, 0 }, 0, clearPattern, 4);

            var maxLogLevel = (Level)Math.Max((int)MinimumLogLevel, (int)MinimumFileLogLevel);
            var timer = (maxLogLevel == Level.Verbose) ? new Stopwatch() : null;

            try
            {
                device.KernelSeedA.SetKernelArgument(0, device.BufferNonce);
                device.KernelSeedA.SetKernelArgument(1, device.BufferB);
                device.KernelSeedA.SetKernelArgument(2, device.BufferA1);
                device.KernelSeedA.SetKernelArgument(3, device.BufferI1);

                device.KernelSeedB1.SetKernelArgument(0, device.BufferA1);
                device.KernelSeedB1.SetKernelArgument(1, device.BufferA1);
                device.KernelSeedB1.SetKernelArgument(2, device.BufferA2);
                device.KernelSeedB1.SetKernelArgument(3, device.BufferI1);
                device.KernelSeedB1.SetKernelArgument(4, device.BufferI2);
                device.KernelSeedB1.SetKernelArgumentGeneric(5, (uint)32);

                device.KernelSeedB2.SetKernelArgument(0, device.BufferB);
                device.KernelSeedB2.SetKernelArgument(1, device.BufferA1);
                device.KernelSeedB2.SetKernelArgument(2, device.BufferA2);
                device.KernelSeedB2.SetKernelArgument(3, device.BufferI1);
                device.KernelSeedB2.SetKernelArgument(4, device.BufferI2);
                device.KernelSeedB2.SetKernelArgumentGeneric(5, (uint)0);

                device.KernelRound1.SetKernelArgument(0, device.BufferA1);
                device.KernelRound1.SetKernelArgument(1, device.BufferA2);
                device.KernelRound1.SetKernelArgument(2, device.BufferB);
                device.KernelRound1.SetKernelArgument(3, device.BufferI2);
                device.KernelRound1.SetKernelArgument(4, device.BufferI1);
                device.KernelRound1.SetKernelArgumentGeneric(5, (uint)Device.AMD.DUCK_SIZE_A * 1024);
                device.KernelRound1.SetKernelArgumentGeneric(6, (uint)Device.AMD.DUCK_SIZE_B * 1024);

                device.KernelRoundO.SetKernelArgument(0, device.BufferB);
                device.KernelRoundO.SetKernelArgument(1, device.BufferA1);
                device.KernelRoundO.SetKernelArgument(2, device.BufferI1);
                device.KernelRoundO.SetKernelArgument(3, device.BufferI2);

                device.KernelRoundNA.SetKernelArgument(0, device.BufferB);
                device.KernelRoundNA.SetKernelArgument(1, device.BufferA1);
                device.KernelRoundNA.SetKernelArgument(2, device.BufferI1);
                device.KernelRoundNA.SetKernelArgument(3, device.BufferI2);

                device.KernelRoundNB.SetKernelArgument(0, device.BufferA1);
                device.KernelRoundNB.SetKernelArgument(1, device.BufferB);
                device.KernelRoundNB.SetKernelArgument(2, device.BufferI2);
                device.KernelRoundNB.SetKernelArgument(3, device.BufferI1);

                device.KernelTail.SetKernelArgument(0, device.BufferB);
                device.KernelTail.SetKernelArgument(1, device.BufferA1);
                device.KernelTail.SetKernelArgument(2, device.BufferI1);
                device.KernelTail.SetKernelArgument(3, device.BufferI2);

                device.KernelRecovery.SetKernelArgument(0, device.BufferNonce);
                device.KernelRecovery.SetKernelArgument(1, device.BufferR);
                device.KernelRecovery.SetKernelArgument(2, device.BufferI2);
            }
            catch (Exception ex)
            {
                if (isStopSolving) return;
                throw ex;
            }

            while (!isStopSolving)
            {
                try
                {
                    while (solutions.TryDequeue(out var solution))
                    {
                        device.CommandQueue.EnqueueWriteBuffer(device.BufferNonce, solution.Job.NonceHash, 32);

                        device.CommandQueue.EnqueueFillBuffer(device.BufferI2, 64 * 64 * 4, clearPattern);
                        device.CommandQueue.EnqueueWriteBuffer(device.BufferR, solution.GetLongEdges(), CycleFinder.CUCKOO_42);

                        device.CommandQueue.EnqueueNDRangeKernel(device.KernelRecovery, 1, 2048 * 256, 256, 0);

                        solution.nonces = device.CommandQueue.EnqueueReadBuffer<uint>(device.BufferI2, 42);

                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                solution.nonces = solution.nonces.OrderBy(n => n).ToArray();

                                if (solution.CheckDifficulty(out ulong currentDiff, out ulong expectDiff))
                                {
                                    Log(Level.Debug, $"Solved difficulty of >={expectDiff} ({currentDiff})");
                                    solution.Job.OnJobSolved();

                                    Log(solution.Job.Origin.Category == FeeCategory.Miner
                                        ? Level.Info : Level.Debug,
                                        $"Device #{device.DeviceID} submitting solution...");

                                    solution.Job.Origin.SubmitSolution(new SubmitParams()
                                    {
                                        Height = solution.Job.Height,
                                        Nonce = solution.Job.Nonce,
                                        JobID = solution.Job.JobID,
                                        POW = solution.nonces
                                    });
                                }
                                else { Log(Level.Debug, $"Low difficulty solution of <{expectDiff} ({currentDiff})"); }
                            }
                            catch (Exception ex) { Log(ex); }
                        });
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

                    device.CommandQueue.EnqueueWriteBuffer(device.BufferNonce, tempJob.NonceHash, 32);

                    device.CommandQueue.EnqueueFillBuffer(device.BufferI2, 64 * 64 * 4, clearPattern);
                    device.CommandQueue.EnqueueFillBuffer(device.BufferI1, 64 * 64 * 4, clearPattern);

                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelSeedA, 1, 2048 * 128, 128, 0);

                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelSeedB1, 1, 1024 * 128, 128, 0);
                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelSeedB2, 1, 1024 * 128, 128, 0);

                    device.CommandQueue.EnqueueFillBuffer(device.BufferI1, 64 * 64 * 4, clearPattern);
                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelRound1, 1, 4096 * 1024, 1024, 0);

                    device.CommandQueue.EnqueueFillBuffer(device.BufferI2, 64 * 64 * 4, clearPattern);
                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelRoundO, 1, 4096 * 1024, 1024, 0);

                    device.CommandQueue.EnqueueFillBuffer(device.BufferI1, 64 * 64 * 4, clearPattern);
                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelRoundNB, 1, 4096 * 1024, 1024, 0);

                    for (int r = 0; r < TrimRounds; r++)
                    {
                        device.CommandQueue.EnqueueFillBuffer(device.BufferI2, 64 * 64 * 4, clearPattern);
                        device.CommandQueue.EnqueueNDRangeKernel(device.KernelRoundNA, 1, 4096 * 1024, 1024, 0);

                        device.CommandQueue.EnqueueFillBuffer(device.BufferI1, 64 * 64 * 4, clearPattern);
                        device.CommandQueue.EnqueueNDRangeKernel(device.KernelRoundNB, 1, 4096 * 1024, 1024, 0);
                    }

                    device.CommandQueue.EnqueueFillBuffer(device.BufferI2, 64 * 64 * 4, clearPattern);
                    device.CommandQueue.EnqueueNDRangeKernel(device.KernelTail, 1, 4096 * 1024, 1024, 0);

                    device.CommandQueue.EnqueueReadBuffer(device.BufferI2, 1, ref edgeCount);

                    if (maxLogLevel == Level.Verbose)
                        Log(Level.Verbose, $"Device #{device.DeviceID} Job #{tempJob.JobID}: " +
                            $"Trimmed to {edgeCount[0]} edges in {timer.ElapsedMilliseconds}ms");

                    tempJob.EndSolveOn = DateTime.Now;

                    if (edgeCount[0] < maxCpuHandleEdgeCount)
                    {
                        device.CommandQueue.EnqueueReadBuffer(device.BufferA1, (int)edgeCount[0] * 2, ref edgeBuffer);

                        var handleJob = tempJob;
                        var tempEdgeCount = (int)edgeCount[0];
                        var tempEdgeBuffer = edgeBuffer.ToArray();

                        Task.Factory.StartNew(() =>
                        {
                            var uintEdgeBuffer = new uint[tempEdgeCount * sizeof(ulong) / sizeof(uint)];
                            Buffer.BlockCopy(tempEdgeBuffer, 0, uintEdgeBuffer, 0, tempEdgeCount * sizeof(uint) * 2);
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
            Marshal.FreeHGlobal(clearPattern);
        }

        #endregion

    }
}
