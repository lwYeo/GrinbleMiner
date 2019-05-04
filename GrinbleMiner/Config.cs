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
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using GrinbleMiner.NetworkInterface.Pool;
using GrinbleMiner.Solver.Device;
using static GrinbleMiner.Logger;

namespace GrinbleMiner
{
    public class Config
    {

        #region Static Declarations and methods

        public static class Defaults
        {
            public const bool AllowCudaSolver = true;
            public const bool AllowAmdSolver = true;
            public const bool AllowLargeCpuMemory = true;
            public const bool IsLogToFile = false;
            public const Level MinimumLogLevel = Level.Info;
            public const Level MinimumFileLogLevel = Level.Warn;
            public const string ApiPath = "http://127.0.0.1:4078";
        }

        public static Config LoadInstance()
        {
            if (!File.Exists(Program.GetAppConfigPath())) return new Config();

            return Utils.Json.DeserializeFromFile<Config>(Program.GetAppConfigPath()) ?? new Config();
        }

        #endregion

        #region Properties and Constructor

        public ConnectionParams[] Connections { get; set; }

        public uint MaxJobValidMinutes { get; set; }

        public uint CpuLevel { get; set; }

        public bool AllowLargeCpuMemory { get; set; }

        public bool AllowCudaSolver { get; set; }

        public CUDA[] CudaDevices { get; set; }

        public bool AllowAmdSolver { get; set; }

        public AMD[] AmdDevices { get; set; }

        public string ApiPath { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Level MinimumLogLevel { get; set; }

        public bool IsLogToFile { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Level MinimumFileLogLevel { get; set; }

        public Config()
        {
            Connections = Array.CreateInstance(typeof(CUDA), 0) as ConnectionParams[];
            CudaDevices = Array.CreateInstance(typeof(CUDA), 0) as CUDA[];
            AmdDevices = Array.CreateInstance(typeof(AMD), 0) as AMD[];
            AllowCudaSolver = Defaults.AllowCudaSolver;
            AllowAmdSolver = Defaults.AllowAmdSolver;
            AllowLargeCpuMemory = Defaults.AllowLargeCpuMemory;
            IsLogToFile = Defaults.IsLogToFile;
            MinimumLogLevel = Defaults.MinimumLogLevel;
            MinimumFileLogLevel = Defaults.MinimumFileLogLevel;
            ApiPath = Defaults.ApiPath;
        }

        #endregion

        #region Public methods

        public void Save()
        {
            if (!Utils.Json.SerializeToFile(this, Program.GetAppConfigPath()))
                Log(Level.Error, $"Failed saving settings to {Program.GetAppConfigPath()}");
        }

        #endregion

    }
}
