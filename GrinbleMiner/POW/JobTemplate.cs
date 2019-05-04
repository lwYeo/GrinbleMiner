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

using Newtonsoft.Json;
using System;
using System.Linq;
using System.Numerics;
using GrinbleMiner.NetworkInterface.Pool;

namespace GrinbleMiner.POW
{
    public class JobTemplate
    {

        #region Properties and Constructor

        [JsonProperty(PropertyName = "difficulty")]
        public ulong Difficulty { get; set; }

        [JsonProperty(PropertyName = "height")]
        public BigInteger Height { get; set; }

        [JsonProperty(PropertyName = "job_id")]
        public ulong JobID { get; set; }

        [JsonProperty(PropertyName = "pre_pow")]
        public string PrePOW { get; set; }

        [JsonIgnore]
        public DateTime Timestamp { get; }

        [JsonIgnore]
        public Stratum Origin { get; private set; }

        [JsonIgnore]
        public Job SolvedJob { get; private set; }

        [JsonIgnore]
        public bool IsSolved => SolvedJob != null;

        public JobTemplate()
        {
            PrePOW = string.Empty;
            Timestamp = DateTime.Now;
        }

        #endregion

        #region Public methods

        public void SetOrigin(Stratum origin)
        {
            Origin = origin;
        }

        public void OnJobSolved(Job job)
        {
            if (!IsSolved) SolvedJob = job;
        }

        public ulong GetScale()
        {
            if (string.IsNullOrWhiteSpace(PrePOW)) return 1;
            try
            {
                byte[] header = GetHeader().Reverse().ToArray();
                if (header.Length > 20) return BitConverter.ToUInt32(header, 0);
            }
            catch { }

            return 1;
        }

        public byte[] GetHeader()
        {
            return Enumerable.
                Range(0, PrePOW?.Length ?? 0).
                Where(x => (x % 2) == 0).
                Select(x => Convert.ToByte(PrePOW?.Substring(x, 2) ?? string.Empty, 16)).
                ToArray();
        }

        #endregion

    }
}
