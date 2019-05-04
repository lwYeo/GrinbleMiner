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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GrinbleMiner.Solver.Device
{
    public abstract class Base
    {

        #region Static methods

        public static void Log(Exception ex) => Logger.Log(ex);

        #endregion

        #region Properties

        [JsonConverter(typeof(StringEnumConverter))]
        public PlatformType Type { get; set; }

        public int DeviceID { get; set; }

        public int PciBusID { get; set; }

        public string Name { get; set; }

        public bool Allow { get; set; }

        [JsonIgnore]
        public ulong AvailableMemory { get; set; }

        [JsonIgnore]
        public bool IsInitialized { get; set; }

        [JsonIgnore]
        public bool IsPause { get; set; }

        #endregion

    }
}
