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
using System.Linq;
using System.Threading.Tasks;
using GrinbleMiner.POW;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.NetworkInterface.Pool
{
    public static class Handler
    {

        #region Events

        public delegate void NewJobEventHandler(Stratum currentStratum, JobTemplate job);

        public static event NewJobEventHandler OnNewJob;

        #endregion

        #region Declarations and Properties

        private static uint MaxJobValidMinutes { get; set; }

        private static Task UpdateJobMonitorTask { get; set; }

        private static Stratum MinerStratum { get; set; }

        private static Stratum MinerDevStratum { get; set; }

        private static Stratum GrinDevStratum { get; set; }

        public static JobTemplate CurrentJob { get; set; }

        public static ulong SharesSubmitted => new Stratum[] { MinerStratum, MinerDevStratum, GrinDevStratum }.
            Aggregate(0ul, (s, stratum) => s += (stratum?.SharesSubmitted ?? 0ul));

        public static ulong SharesStale => new Stratum[] { MinerStratum, MinerDevStratum, GrinDevStratum }.
            Aggregate(0ul, (s, stratum) => s += (stratum?.SharesStale ?? 0ul));

        public static ulong SharesRejected => new Stratum[] { MinerStratum, MinerDevStratum, GrinDevStratum }.
            Aggregate(0ul, (s, stratum) => s += (stratum?.SharesRejected ?? 0ul));

        public static ulong SharesAccepted => new Stratum[] { MinerStratum, MinerDevStratum, GrinDevStratum }.
            Aggregate(0ul, (s, stratum) => s += (stratum?.SharesAccepted ?? 0ul));

        public static bool IsConnected => new Stratum[] { MinerStratum, MinerDevStratum, GrinDevStratum }.
            All(s => s?.IsConnected ?? false);

        #endregion

        #region Public methods

        public static Stratum GetCurrentStratum()
        {
            switch (DevFee.GetFeeCategoryByShareCount(MinerStratum.SharesSubmitted, MinerDevStratum.SharesSubmitted, GrinDevStratum.SharesSubmitted))
            {
                case FeeCategory.MinerDev:  return MinerDevStratum;
                case FeeCategory.GrinDev:   return GrinDevStratum;
                default:                    return MinerStratum;
            }
        }

        public static void Initialize(Config settings)
        {
            CheckSettings(settings);

            MinerStratum = new Stratum(FeeCategory.Miner, settings.Connections);
            GrinDevStratum = new Stratum(FeeCategory.GrinDev, DevFee.GrinDevConnections);
            MinerDevStratum = new Stratum(FeeCategory.MinerDev, DevFee.MinerDevConnections);

            Task.WaitAll(new Task[]
            {
                Task.Factory.StartNew(() => MinerDevStratum.ConnectAndOpen()),
                Task.Factory.StartNew(() => GrinDevStratum.ConnectAndOpen()),
                Task.Factory.StartNew(() => MinerStratum.ConnectAndOpen())
            });

            Task.WaitAll(new Task[]
            {
                Task.Factory.StartNew(() => { if (MinerDevStratum.IsConnected && !MinerDevStratum.IsInvalidConnection) MinerDevStratum.RequestJobTemplate(); }),
                Task.Factory.StartNew(() => { if (GrinDevStratum.IsConnected && !GrinDevStratum.IsInvalidConnection) GrinDevStratum.RequestJobTemplate(); }),
                Task.Factory.StartNew(() => { if (MinerStratum.IsConnected && !MinerStratum.IsInvalidConnection) MinerStratum.RequestJobTemplate(); })
            });

            if (UpdateJobMonitorTask == null || Connection.TaskEndedStatuses.Any(s => UpdateJobMonitorTask.Status == s))
            {
                UpdateJobMonitorTask = Task.Factory.StartNew(() => UpdateJobMonitor());
            }
        }

        public static void CloseAllConnections()
        {
            Task.WaitAll(new Task[]
            {
                Task.Factory.StartNew(() => MinerDevStratum?.Close(andTerminate:true)),
                Task.Factory.StartNew(() => GrinDevStratum?.Close(andTerminate:true)),
                Task.Factory.StartNew(() => MinerStratum?.Close(andTerminate:true))
            });
        }

        #endregion

        #region Private methods

        private static void CheckSettings(Config settings)
        {
            if (settings.MaxJobValidMinutes == 0) settings.MaxJobValidMinutes = 3;
            MaxJobValidMinutes = settings.MaxJobValidMinutes;

            if (settings.Connections == null || !settings.Connections.Any())
            {
                var ipAddress = string.Empty;
                while (string.IsNullOrWhiteSpace(ipAddress))
                {
                    Console.WriteLine("Enter pool address, e.g. : us-east-stratum.grinmint.com");
                    ipAddress = Console.ReadLine();
                }

                ushort port = 0;
                while (port == 0)
                {
                    Console.WriteLine("Enter pool port number, e.g. : 3416");
                    if (!ushort.TryParse(Console.ReadLine(), out port))
                        Console.WriteLine("Invalid port entered.");
                }

                bool? useTLS = null;
                while (useTLS == null)
                {
                    Console.WriteLine("Connect using TLS (SSL)? [Y/N]");
                    switch (Console.ReadKey().KeyChar)
                    {
                        case 'Y':
                        case 'y':
                            useTLS = true;
                            break;
                        case 'N':
                        case 'n':
                            useTLS = false;
                            break;
                    }
                    Console.WriteLine();
                }

                var userID = string.Empty;
                while (string.IsNullOrWhiteSpace(userID))
                {
                    Console.WriteLine("Enter pool login, e.g. : user@email.com/rig1");
                    userID = Console.ReadLine();
                }

                Console.WriteLine("Enter pool pass (leave empty when not required)");
                var userPass = Console.ReadLine();

                var connectionSettings = new ConnectionParams(ipAddress, port, userID, userPass, useTLS.Value, 10);
                settings.Connections = new ConnectionParams[] { connectionSettings };
            }
            settings.Save();
        }

        private static void UpdateJobMonitor()
        {
            Stratum currentStratum = null;
            var validUntil = DateTime.MaxValue;

            while (true)
            {
                validUntil = DateTime.Now.AddMinutes(-MaxJobValidMinutes);
                try
                {
                    currentStratum = GetCurrentStratum();
                    var newJob = currentStratum?.CurrentJob;

                    if (newJob == null)
                    {
                        Log(Level.Info, string.Format("Waiting for new job...."));
                        Task.Delay(1000).Wait();
                    }
                    else if (newJob.Timestamp < validUntil)
                    {
                        Log(Level.Info, string.Format("Current job expired..."));

                        CurrentJob = null;
                        OnNewJob?.Invoke(currentStratum, CurrentJob);

                        currentStratum.Close();
                        currentStratum.ConnectAndOpen();

                        while (!currentStratum.IsConnected || currentStratum.IsInvalidConnection)
                        {
                            if (GetCurrentStratum() != currentStratum) continue;

                            currentStratum.Close();
                            currentStratum.ConnectAndOpen();
                        }

                        if (GetCurrentStratum() != currentStratum) continue;
                        currentStratum.RequestJobTemplate();
                    }
                    else if (CurrentJob == null || CurrentJob.JobID != newJob.JobID || CurrentJob.Height != newJob.Height)
                    {
                        CurrentJob = newJob;
                        OnNewJob?.Invoke(currentStratum, CurrentJob);
                    }
                }
                catch (Exception ex) { Log(ex); }

                Task.Delay(100).Wait();
            }
        }

        #endregion

    }
}
