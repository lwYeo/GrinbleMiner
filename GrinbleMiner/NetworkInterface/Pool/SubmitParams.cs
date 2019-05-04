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

using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using GrinbleMiner.POW;

namespace GrinbleMiner.NetworkInterface.Pool
{
    public class SubmitParams
    {

        [JsonProperty(PropertyName = "height")]
        public BigInteger Height { get; set; }

        [JsonProperty(PropertyName = "job_id")]
        public ulong JobID { get; set; }

        [JsonProperty(PropertyName = "edge_bits")]
        public uint EdgeBits { get; set; }

        [JsonProperty(PropertyName = "nonce")]
        public ulong Nonce { get; set; }

        [JsonProperty(PropertyName = "pow")]
        public uint[] POW { get; set; }

        public SubmitParams()
        {
            EdgeBits = CycleFinder.C29;
            POW = Enumerable.Empty<uint>().ToArray();
        }

    }
}
