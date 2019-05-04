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
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.NetworkInterface.Pool
{
    public abstract class Connection
    {

        #region Declarations and Properties

        public static readonly TaskStatus[] TaskEndedStatuses =
            { TaskStatus.RanToCompletion, TaskStatus.Canceled, TaskStatus.Faulted };

        public FeeCategory Category { get; }
        public ConnectionParams CurrentParams { get; protected set; }

        public bool IsConnected { get; protected set; }
        public bool IsTerminated { get; protected set; }
        public bool IsInvalidConnection { get; protected set; }
        public int InvalidPacketCount { get; protected set; }
        public DateTime LastUpdateTime { get; protected set; }

        protected readonly ConnectionParams[] @params;
        protected int currentParamIndex;

        protected Task keepAliveHandler;
        protected TcpClient client;
        protected Stream stream;
        protected StreamReader reader;

        private int connectionAttempts;

        #endregion

        #region Constructors

        public Connection(FeeCategory category, ConnectionParams[] @params)
        {
            Category = category;
            this.@params = @params;
            CurrentParams = this.@params.ElementAt(currentParamIndex);
        }

        #endregion

        #region Public methods

        public virtual void ConnectAndOpen()
        {
            connectionAttempts++;
            try
            {
                if (client != null) client.Dispose();

                if (IsInvalidConnection)
                {
                    IsConnected = false;
                    return;
                }

                if (keepAliveHandler == null || TaskEndedStatuses.Any(s => keepAliveHandler.Status == s))
                    Log(Level.Info, false, $"Connecting...");
                else
                    Log(Level.Info, true, $"Reconnecting...");

                var tempClient = new TcpClient();

                var connectObject = tempClient.BeginConnect(CurrentParams.IP, CurrentParams.Port, null, null);

                while (!connectObject.AsyncWaitHandle.WaitOne(500, false))
                {
                    if (Program.IsUserRequestExit) return;
                }

                InvalidPacketCount = 0;
                client = tempClient;

                IsConnected = client.Connected;
                if (!IsConnected) return;

                Log(Level.Info, true, $"Connected");

                if (CurrentParams.IsUseTLS)
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    stream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateTLSServerCertificate), null);
                    (stream as SslStream).AuthenticateAsClient(CurrentParams.IP);
                    reader = new StreamReader(stream);
                }
                else
                {
                    stream = client.GetStream();
                    reader = new StreamReader(stream);
                }

                if (keepAliveHandler == null || TaskEndedStatuses.Any(s => keepAliveHandler.Status == s))
                    keepAliveHandler = Task.Factory.StartNew(() => { KeepAliveMonitor(); }, TaskCreationOptions.LongRunning);
            }
            catch (SocketException ex)
            {
                IsConnected = false;
                Log(ex);
                if (connectionAttempts < 5)
                {
                    Log(Level.Error, false, $"({CurrentParams.IP}:{CurrentParams.Port}) - Failed to connect, retrying...");
                    Task.Delay(1000).Wait();
                    ConnectAndOpen();
                }
                else Log(Level.Error, false, $"({CurrentParams.IP}:{CurrentParams.Port}) - Failed to connect");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Log(ex);
            }
            finally
            {
                if (IsTerminated) IsConnected = false;
                else if (IsConnected) connectionAttempts = 0;
            }
        }

        public void Close(bool andTerminate = false)
        {
            try
            {
                if (andTerminate) IsTerminated = true;
                IsConnected = false;

                Log(Level.Debug, false, $"({CurrentParams.IP}:{CurrentParams.Port}) Closing connection...");

                if (client != null && client.Connected)
                {
                    stream.Close();
                    stream.Dispose();

                    client.Close();
                    client.Dispose();
                }

                Log(Level.Info, false, $"Connection closed");
            }
            catch (Exception ex) { Log(ex); }
        }

        #endregion

        #region Protected methods

        protected abstract void KeepAliveMonitor();

        protected void Log(Level level, bool logAll, string message, params object[] args)
        {
            if (Category == FeeCategory.Miner || logAll)
                Logger.Log(level, $"[{Category}] ({CurrentParams.IP}:{CurrentParams.Port}) - {message}", args);
            else
                Logger.Log((level == Level.Info) ? Level.Debug : level, $"[{Category}] ({CurrentParams.IP}:{CurrentParams.Port}) - {message}", args);
        }

        protected void Log(ConsoleColor customColor, Level level, bool logAll, string message, params object[] args)
        {
            if (Category == FeeCategory.Miner || logAll)
                Logger.Log(customColor, level, $"[{Category}] ({CurrentParams.IP}:{CurrentParams.Port}) - {message}", args);
            else
                Logger.Log(customColor, (level == Level.Info) ? Level.Debug : level, $"[{Category}] ({CurrentParams.IP}:{CurrentParams.Port}) - {message}", args);
        }

        protected void Log(Exception ex)
        {
            Logger.Log(ex, prefix:$"[{Category}] ({CurrentParams.IP}:{CurrentParams.Port}) -");
        }

        protected bool ValidateTLSServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (new SslPolicyErrors[] { SslPolicyErrors.None, SslPolicyErrors.RemoteCertificateNameMismatch }.All(e => sslPolicyErrors != e))
            {
                // Do not allow client to communicate with unauthenticated servers
                Log(Level.Error, false, $"Certificate error: {sslPolicyErrors}");
                return false;
            }
            return true;
        }

        #endregion

    }
}
