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

namespace GrinbleMiner.NetworkInterface.Pool
{
    public class ConnectionParams
    {

        public string IP { get; }

        public ushort Port { get; }

        public string UserID { get; }

        public string UserPass { get; }

        public bool IsUseTLS { get; }

        public int TimeoutMinutes { get; }

        public string ConnectionString => string.Format("{0}:{1}", IP ?? string.Empty, Port);

        public ConnectionParams(string ip, ushort port, string userID, string userPass, bool isUseTLS, int timeoutMinutes)
        {
            IP = ip;
            Port = port;
            UserID = userID;
            UserPass = userPass;
            IsUseTLS = isUseTLS;
            TimeoutMinutes = timeoutMinutes;
        }

    }
}
