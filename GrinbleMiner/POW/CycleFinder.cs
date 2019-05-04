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
using System.Linq;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.POW
{
    public class CycleFinder
    {

        #region Static methods

        public static CycleFinder GetInstance()
        {
            if (instances.TryTake(out CycleFinder instance))
                return instance;
            else
                return new CycleFinder();
        }

        public static void ReturnInstance(CycleFinder instance)
        {
            instance.job = null;
            instance.edges = null;

            instance.graphU?.Clear();
            instance.graphU?.TrimExcess();
            instance.graphU = null;

            instance.graphV?.Clear();
            instance.graphV?.TrimExcess();
            instance.graphV = null;

            if (instances.Count < Math.Max(Environment.ProcessorCount, AllowLargeCpuMemory ? 12 : 8))
                instances.Add(instance);
        }

        #endregion

        #region Constants and Declarations

        public const int C29 = 29;
        public const int CUCKOO_42 = 42;
        private const int MAX_LENGTH = 8192;

        public static bool AllowLargeCpuMemory { get; set; }

        private static readonly ConcurrentBag<CycleFinder> instances =
            new ConcurrentBag<CycleFinder>();

        private Job job;
        private uint[] edges;
        private int edgeCount;
        private int duplicateCount;
        private Dictionary<uint, uint> graphU;
        private Dictionary<uint, uint> graphV;

        #endregion

        #region Public methods

        public void SetJob(Job job)
        {
            this.job = job;
        }

        public void SetEdges(uint[] edgeBuffer, int count)
        {
            edgeCount = count;
            edges = edgeBuffer;

            graphU = new Dictionary<uint, uint>(count);
            graphV = new Dictionary<uint, uint>(count);
        }

        public void FindSolutions(ConcurrentQueue<Solution> solutions, int cyclen = CUCKOO_42)
        {
            var pathU = new List<uint>(32);
            var pathV = new List<uint>(32);
            try
            {
                for (int e = 0; e < edgeCount; e++)
                {
                    var edge = new Edge(edges[e * 2 + 0], edges[e * 2 + 1]);

                    if ((graphU.TryGetValue(edge.u, out uint u) && u == edge.v) ||
                        (graphV.TryGetValue(edge.v, out uint v) && v == edge.u))
                    {
                        duplicateCount++;
                        continue;
                    }

                    pathU.Clear(); pathV.Clear();

                    Path(ref pathU, true, edge.u);
                    Path(ref pathV, false, edge.v);

                    int joinA = -1, joinB = -1;

                    for (int i = 0; i < pathU.Count; i++)
                        if (pathV.Contains(pathU[i]))
                        {
                            var path2Idx = pathV.IndexOf(pathU[i]);
                            joinA = i;
                            joinB = path2Idx;
                            break;
                        }

                    var cycle = (joinA != -1) ? (1 + joinA + joinB) : 0;

                    if (cycle > 3 && cycle != cyclen)
                        Log(Level.Verbose, $"{cycle} cycles found");

                    else if (cycle == cyclen) // initiate nonce recovery procedure
                    {
                        Log(Level.Verbose, $"{cyclen} cycles found");

                        var path1t = pathU.Take(joinA + 1).ToArray();
                        var path2t = pathV.Take(joinB + 1).ToArray();
                        var cycleEdges = new List<Edge>(CUCKOO_42) { edge };

                        cycleEdges.AddRange(path1t.Zip(path1t.Skip(1), (second, first) => new Edge(first, second)));
                        cycleEdges.AddRange(path2t.Zip(path2t.Skip(1), (second, first) => new Edge(first, second)));

                        solutions.Enqueue(new Solution(job, cycleEdges));
                    }
                    else
                    {
                        if (pathU.Count > pathV.Count)
                        {
                            Reverse(pathV, false);
                            graphV[edge.v] = edge.u;
                        }
                        else
                        {
                            Reverse(pathU, true);
                            graphU[edge.u] = edge.v;
                        }
                    }
                }
            }
            catch (Exception ex) { Log(ex); }
        }

        #endregion

        #region Private methods

        private void Reverse(List<uint> path, bool isStartInU)
        {
            for (var i = path.Count - 2; i > -1; i--)
            {
                uint A = path[i];
                uint B = path[i + 1];

                if (isStartInU)
                {
                    if ((i & 1) == 0)
                    {
                        graphU.Remove(A);
                        graphV[B] = A;
                    }
                    else
                    {
                        graphV.Remove(A);
                        graphU[B] = A;
                    }
                }
                else
                {
                    if ((i & 1) == 0)
                    {
                        graphV.Remove(A);
                        graphU[B] = A;
                    }
                    else
                    {
                        graphU.Remove(A);
                        graphV[B] = A;
                    }
                }
            }
        }

        private void Path(ref List<uint> path, bool isStartInGraphU, uint key)
        {
            path.Add(key);

            var graph = isStartInGraphU ? graphU : graphV;

            while (graph.TryGetValue(key, out uint v))
            {
                if (path.Count >= MAX_LENGTH) break;

                path.Add(v);
                key = v;

                isStartInGraphU = !isStartInGraphU;
                graph = isStartInGraphU ? graphU : graphV;
            }
        }

        #endregion

    }
}
