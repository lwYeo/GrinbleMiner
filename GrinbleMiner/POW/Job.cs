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
using System.Numerics;
using GrinbleMiner.NetworkInterface.Pool;

namespace GrinbleMiner.POW
{
    public class Job
    {

        #region Declarations and Properties

        private static readonly Random RandomGenerator =
            new Random(
                BitConverter.ToInt32(
                    BitConverter.GetBytes(
                        BitConverter.ToUInt32(BitConverter.GetBytes(Guid.NewGuid().GetHashCode()))
                        + BitConverter.ToUInt32(BitConverter.GetBytes(DateTime.Now.GetHashCode())))));

        private readonly JobTemplate job;

        public bool IsSolved => job.IsSolved;
        public DateTime TimeStamp => job.Timestamp;
        public BigInteger Height => job.Height;
        public ulong Difficulty => job.Difficulty;
        public ulong JobID => job.JobID;
        public ulong Scale => job.GetScale();
        public string PrePOW => job.PrePOW ?? string.Empty;

        public Stratum Origin => job.Origin;

        public int DeviceID { get; }
        public DateTime BeginSolveOn { get; }
        public DateTime EndSolveOn { get; set; }
        public uint GraphAttempts { get; set; }
        public ulong Nonce { get; private set; }
        public byte[] NonceHash { get; set; }

        #endregion

        #region Constructors

        public Job(JobTemplate job, int deviceID)
        {
            this.job = job;
            DeviceID = deviceID;
            BeginSolveOn = DateTime.Now;
        }

        private Job(Job job)
        {
            this.job = job.job;
            DeviceID = job.DeviceID;
            BeginSolveOn = job.BeginSolveOn;
            GraphAttempts = job.GraphAttempts;
        }

        #endregion

        #region Public methods

        public void OnJobSolved()
        {
            job.OnJobSolved(this);
        }

        public Job NextNonce()
        {
            var next = new Job(this);
            var randomBytes = Array.CreateInstance(typeof(byte), 8) as byte[];
            RandomGenerator.NextBytes(randomBytes);

            next.Nonce = BitConverter.ToUInt64(randomBytes.Reverse().ToArray());

            var blake2B = Crypto.Blake2B_Helper.GetInstance();
            next.NonceHash = blake2B.ComputeHash(GetHeaderWithBodyBytes(randomBytes));

            Crypto.Blake2B_Helper.ReturnInstance(blake2B);
            next.GraphAttempts++;

            return next;
        }

        #endregion

        #region Private methods

        private byte[] GetHeaderWithBodyBytes(byte[] body)
        {
            return Enumerable.Range(0, PrePOW.Length).
                Where(x => (x % 2) == 0).
                Select(x => Convert.ToByte(PrePOW.Substring(x, 2), 16)).
                Concat(body).
                ToArray();
        }

        #endregion

    }
}
