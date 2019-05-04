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

namespace GrinbleMiner.Solver.Device.API
{
    public class DeviceQuery
    {

        public string FanSpeed { get; set; }
        public string Temperature { get; set; }
        public string PowerDraw { get; set; }
        public string ClockGPU { get; set; }
        public string ClockVRAM { get; set; }
        public string UtilizationGPU { get; set; }
        public string UtilizationVRAM { get; set; }

    }
}
