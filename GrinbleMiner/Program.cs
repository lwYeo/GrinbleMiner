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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static GrinbleMiner.Logger;

namespace GrinbleMiner
{
    class Program
    {

        public static string GetApplicationName() => typeof(Program).Assembly.GetName().Name;

        public static string GetCompanyName() => typeof(Program).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>().Company;

        public static string GetApplicationVersion() => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        public static string GetApplicationYear() => "2019";

        public static string GetAppDirPath() => Path.GetDirectoryName(typeof(Program).Assembly.Location);

        public static string GetAppConfigPath() => Path.Combine(GetAppDirPath(), GetApplicationName() + ".conf");

        public static ManualResetEvent ExitTrigger;

        public static Stopwatch Uptime;

        private static Config settings;

        private static int exitCode;

        private static string GetHeader()
        {
            return "\n" +
                "*** " + GetApplicationName() + " " + GetApplicationVersion() + " by " + GetCompanyName() + " (" + GetApplicationYear() + ") ***\n" +
                "*** Built with .NET Core 2.2.0 SDK, VC++ 2017, nVidia CUDA SDK 10.0 64-bit, and AMD APP SDK v3.0.130.135 (OpenCL)\n" +
                "\n" +
                "Donation addresses:\n" +
                "ETH (or any ERC 20/918 tokens)	: 0x9172ff7884CEFED19327aDaCe9C470eF1796105c\n" +
                "BTC                             : 3GS5J5hcG6Qcu9xHWGmJaV5ftWLmZuR255\n" +
                "LTC                             : LbFkAto1qYt8RdTFHL871H4djendcHyCyB\n";
        }
        
        #region Console exit handler

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private delegate bool EventHandler(CtrlType sig);

        private static EventHandler onConsoleExitHandler;

        private static readonly object exitLock = new object();

        internal static bool IsUserRequestExit;

        private static bool ConsoleExitHandler(CtrlType sig)
        {
            lock (exitLock)
            {
                try
                {
                    //if (m_allMiners != null)
                    //{
                    //    Task.WaitAll(m_allMiners.Select(m => Task.Factory.StartNew(() => m.StopMining())).ToArray());
                    //    Task.WaitAll(m_allMiners.Select(m => Task.Factory.StartNew(() => m.Dispose())).ToArray());
                    //}

                    //if (m_waitCheckTimer != null) m_waitCheckTimer.Stop();
                }
                catch { }

                IsUserRequestExit = true;

                if (ExitTrigger != null) ExitTrigger.Set();

                return true;
            }
        }

        #endregion closing handler

        static void Main(string[] args)
        {
            ExitTrigger = new ManualResetEvent(false);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                onConsoleExitHandler += new EventHandler(ConsoleExitHandler);
                SetConsoleCtrlHandler(onConsoleExitHandler, true);
            }
            else
            {
                AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
                {
                    ConsoleExitHandler(CtrlType.CTRL_CLOSE_EVENT);
                };
                Console.CancelKeyPress += (s, ev) =>
                {
                    ConsoleExitHandler(CtrlType.CTRL_C_EVENT);
                };
            }

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.Title = string.Format("{0} {1} by {2} ({3})", GetApplicationName(), GetApplicationVersion(), GetCompanyName(), GetApplicationYear());

            Log(Level.None, GetHeader());

            Uptime = new Stopwatch();

            settings = Config.LoadInstance();

            MinimumLogLevel = settings.MinimumLogLevel;

            IsLogToFile = settings.IsLogToFile;

            MinimumFileLogLevel = settings.MinimumFileLogLevel;

            Solver.Handler.Initialize(settings);

            if (Solver.Handler.IsAnyInitialized)
            {
                NetworkInterface.Pool.Handler.Initialize(settings);

                Uptime.Start();

                API.Start(ref settings);

                Solver.Handler.StartSolving();

                Task.Factory.StartNew(() =>
                {
                    while (!Solver.Handler.IsAnySolverCrashed)
                        Task.Delay(1000).Wait();

                    exitCode = 22;
                    ExitTrigger.Set();
                });
            }
            else
            {
                Log(Level.Warn, "Solver not initialized, exiting...");
                ExitTrigger.Set();
            }

            ExitTrigger.WaitOne();

            Task.Factory.StartNew(() =>
            {
                Solver.Handler.StopSolving();
                NetworkInterface.Pool.Handler.CloseAllConnections();
            }).Wait(10 * 1000);

            API.Stop();

            Task.Delay(1000).Wait();

            Environment.Exit(exitCode);
        }

    }
}
