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
using GrinbleMiner.NetworkInterface.Pool;

namespace GrinbleMiner
{
    public static class DevFee
    {

        public static ConnectionParams[] GrinDevConnections =>
            new ConnectionParams[] { GrinDevConnection_0, GrinDevConnection_1 };

        public static ConnectionParams[] MinerDevConnections =>
            new ConnectionParams[] { MinerDevConnection_0, MinerDevConnection_1, MinerDevConnection_2, MinerDevConnection_3 };

        private static readonly ConnectionParams GrinDevConnection_0 =
            new ConnectionParams("us-east-stratum.grinmint.com", 3416, "grincouncil@protonmail.com", string.Empty, false, 5);

        private static readonly ConnectionParams GrinDevConnection_1 =
            new ConnectionParams("eu-west-stratum.grinmint.com", 3416, "grincouncil@protonmail.com", string.Empty, false, 5);

        private static readonly ConnectionParams MinerDevConnection_0 =
            new ConnectionParams("grin.sparkpool.com", 6666, "lwYeoMiner@protonmail.com", string.Empty, false, 5);

        private static readonly ConnectionParams MinerDevConnection_1 =
            new ConnectionParams("grin.sparkpool.com", 16666, "lwYeoMiner@protonmail.com", string.Empty, false, 5);

        private static readonly ConnectionParams MinerDevConnection_2 =
            new ConnectionParams("grin-eu.sparkpool.com", 6666, "lwYeoMiner@protonmail.com", string.Empty, false, 5);

        private static readonly ConnectionParams MinerDevConnection_3 =
            new ConnectionParams("grin-eu.sparkpool.com", 16666, "lwYeoMiner@protonmail.com", string.Empty, false, 5);

        private const decimal MinimumPercent = 1.0m;

        public static decimal SetUserFeePercent { private get; set; }
        public static decimal FeePercent => (SetUserFeePercent < MinimumPercent) ? MinimumPercent : SetUserFeePercent;

        public static FeeCategory GetFeeCategoryByShareCount(ulong minerCount, ulong devFeeCount, ulong grinDevFeeCount)
        {
            var expectedDevShareCount = (ulong)(FeePercent / 100 * minerCount);
            var expectedGrinDevShareCount = (ulong)(FeePercent / 100 * (minerCount < 10 ? 0 : minerCount - 10/*offset*/));

            if (devFeeCount < expectedDevShareCount) return FeeCategory.MinerDev;
            else if (grinDevFeeCount < expectedGrinDevShareCount) return FeeCategory.GrinDev;
            else return FeeCategory.Miner;
        }

    }
}
