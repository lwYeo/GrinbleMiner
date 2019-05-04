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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace GrinbleMiner.POW
{
    public class Solution
    {

        #region Declarations and Properties

        private static readonly BigInteger UINT256_MAX = (BigInteger.One << 256) - 1;

        private readonly Task<long[]> getLongEdges;

        private readonly Task<ulong[]> getULongEdges;

        public uint[] nonces;

        public Job Job { get; }

        public List<Edge> Edges { get; }

        #endregion

        #region Constructors

        public Solution(Job job, List<Edge> edges)
        {
            nonces = new uint[CycleFinder.CUCKOO_42];
            Job = job;
            Edges = edges;
            getLongEdges = Task.Factory.StartNew(() => Edges.Select(e => e.u | (((long)e.v) << 32)).ToArray());
            getULongEdges = Task.Factory.StartNew(() => Edges.Select(e => e.u | (((ulong)e.v) << 32)).ToArray());
        }

        #endregion

        #region Public methods

        public ulong[] GetUlongEdges()
        {
            return getULongEdges.Result;
        }

        public long[] GetLongEdges()
        {
            return getLongEdges.Result;
        }

        public bool CheckDifficulty(out ulong currentDifficulty, out ulong expectDifficulty)
        {
            currentDifficulty = 0;
            expectDifficulty = Job?.Difficulty ?? 0;

            var blake2B = Crypto.Blake2B_Helper.GetInstance();
            try
            {
                var packed = new BitArray(CycleFinder.CUCKOO_42 * CycleFinder.C29);
                byte[] packedSolution = new byte[153]; // CUCKOO_42 * C29 (proof_size) / 8 (padding)

                var p = 0;
                foreach (var n in nonces)
                    for (int i = 0; i < CycleFinder.C29; i++)
                        packed.Set(p++, (n & (1ul << i)) != 0);

                packed.CopyTo(packedSolution, 0);

                var hash256 = new BigInteger(blake2B.ComputeHash(packedSolution), isUnsigned: true, isBigEndian: true);
                currentDifficulty = (ulong)(UINT256_MAX / hash256);
            }
            catch { return false; }
            finally { Crypto.Blake2B_Helper.ReturnInstance(blake2B); }
            return currentDifficulty >= expectDifficulty;
        }

        #endregion

    }
}
