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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GrinbleMiner.POW;
using static GrinbleMiner.Logger;

namespace GrinbleMiner.NetworkInterface.Pool
{
    public class Stratum : Connection
    {

        #region Declarations and Properties

        private const int MAX_RECENT_LIMIT = 50;

        private readonly JsonSerializerSettings serializeSettings =
            new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };

        public bool IsLogin { get; private set; }

        public ulong BlocksMined { get; private set; }

        public ulong SharesSubmitted { get; private set; }

        public ulong SharesAccepted { get; private set; }

        public ulong SharesStale { get; private set; }

        public ulong SharesRejected { get; private set; }

        public List<DateTime> RecentShares { get; set; }

        public JobTemplate CurrentJob { get; private set; }

        private Task updateHandler;
        private bool isUpdating;
        private DateTime lastUpdateTime;

        #endregion

        #region Constructors

        public Stratum(FeeCategory category, ConnectionParams[] paramsList) : base(category, paramsList)
        {
            RecentShares = new List<DateTime>();
        }

        #endregion

        #region Override base methods

        public override void ConnectAndOpen()
        {
            IsLogin = false;
            base.ConnectAndOpen();

            if (IsConnected && !IsTerminated)
            {
                if (updateHandler == null || TaskEndedStatuses.Any(s => updateHandler.Status == s))
                    updateHandler = Task.Factory.StartNew(() => { UpdateParameters(); }, TaskCreationOptions.LongRunning);

                while (!IsLogin && !IsInvalidConnection)
                {
                    SendLogin();
                    while (IsConnected && !IsLogin && !IsInvalidConnection) Task.Delay(100).Wait();
                }
            }
        }

        protected override void KeepAliveMonitor()
        {
            while (!IsTerminated)
            {
                try
                {
                    if (!IsConnected)
                    {
                        Log(Level.Info, false, $"Disconnect detected");
                        ConnectAndOpen();
                    }
                    else if (IsInvalidConnection)
                    {
                        IsConnected = false;
                        break;
                    }
                    else if ((LastUpdateTime != DateTime.MinValue) && (DateTime.Now - LastUpdateTime) > TimeSpan.FromMinutes(CurrentParams.TimeoutMinutes))
                    {
                        Log(Level.Info, false, $"Timeout detected");
                        Close();
                        ConnectAndOpen();
                    }

                    if (IsConnected && !IsTerminated && !isUpdating)
                    {
                        if (updateHandler == null || TaskEndedStatuses.Any(s => updateHandler.Status == s))
                            updateHandler = Task.Factory.StartNew(() => { UpdateParameters(); }, TaskCreationOptions.LongRunning);
                    }
                }
                catch (Exception ex) { Log(ex); }

                Task.Delay(1000).Wait();
            }
            Task.Factory.StartNew(() =>
            {
                Task.Delay(0).Wait();
                try { keepAliveHandler.Dispose(); }
                catch { }
                keepAliveHandler = null;
            });
        }

        #endregion

        #region Public methods

        public void SendLogin()
        {
            IsLogin = false;

            SendToStream(new StratumRpcRequest(StratumCommand.login)
            {
                Params = new LoginParams(CurrentParams.UserID, CurrentParams.UserPass)
            });
        }

        public void RequestJobTemplate()
        {
            SendToStream(new StratumRpcRequest(StratumCommand.getjobtemplate));
        }

        public void SubmitSolution(SubmitParams pow)
        {
            if (SendToStream(new StratumRpcRequest(StratumCommand.submit) { Params = pow }))
            {
                lock (RecentShares)
                {
                    SharesSubmitted++;
                    RecentShares.Add(DateTime.Now);
                    var removeCount = RecentShares.Count - MAX_RECENT_LIMIT;
                    if (removeCount < 0) removeCount = 0;
                    RecentShares.RemoveRange(0, removeCount);
                }
            }
        }

        #endregion

        #region Private methods

        private bool SendToStream<T>(T message)
        {
            if (IsTerminated) return false;
            try
            {
                string output = JsonConvert.SerializeObject(message, Formatting.None, serializeSettings);

                Log(Level.Verbose, false, $"Sending:{Environment.NewLine}{output}");

                byte[] bin = Encoding.UTF8.GetBytes(output + "\n");

                lock (stream)
                {
                    if (stream.CanWrite)
                    {
                        stream.Write(bin, 0, bin.Length);
                        stream.Flush();
                        return true;
                    }
                    else
                    {
                        Log(Level.Debug, false, $"Cannot write to stream");
                        IsConnected = false;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Log(ex);
                return false;
            }
        }

        private void UpdateParameters()
        {
            if (IsTerminated || isUpdating) return;
            isUpdating = true;
            lock (updateHandler)
                {
                try
                {
                    while (client.Connected)
                    {
                        if (IsTerminated || InvalidPacketCount > 10)
                            break;

                        if ((lastUpdateTime != DateTime.MinValue) && (DateTime.Now - lastUpdateTime) > TimeSpan.FromMinutes(CurrentParams.TimeoutMinutes))
                            break;

                        string message = reader.ReadLine();

                        Log(ConsoleColor.DarkCyan, Level.Verbose, false, $"Packet received:{Environment.NewLine}{message}");

                        if (string.IsNullOrWhiteSpace(message))
                        {
                            Log(Level.Warn, false, $"Empty packet received");
                            InvalidPacketCount++;
                            Task.Delay(1000).Wait();
                            continue;
                        }
                        try
                        {
                            var jMessage = JsonConvert.DeserializeObject(message, serializeSettings) as JObject;

                            var method = (string)jMessage["method"];
                            var @params = jMessage.ContainsKey("params") ? jMessage["params"].ToString() : string.Empty;

                            var command = StratumCommand.unknown;
                            Enum.TryParse(method, out command);

                            InvalidPacketCount = 0;

                            switch (command)
                            {
                                case StratumCommand.getjobtemplate:
                                    UpdateJobTemplate(jMessage);
                                    break;

                                case StratumCommand.job:
                                    UpdateJob(@params);
                                    break;

                                case StratumCommand.submit:
                                    UpdateSubmission(jMessage);
                                    break;

                                case StratumCommand.login:
                                    UpdateLogin(jMessage);
                                    break;

                                case StratumCommand.keepalive:
                                    if (!string.IsNullOrEmpty(@params))
                                        Log(Level.Info, false, $"{@params}");
                                    break;

                                default:
                                    if (jMessage.ContainsKey("error"))
                                        try
                                        {
                                            var errorMessage = (string)jMessage["error"]["message"];

                                            if (!string.IsNullOrWhiteSpace(errorMessage))
                                                Log(Level.Error, false, $"{errorMessage}");
                                        }
                                        catch { }
                                    break;
                            }
                        }
                        catch (Exception ex) { if (!IsTerminated) Log(ex); }
                    }
                    IsConnected = false;
                    if (!IsTerminated) Log(Level.Warn, false, $"Connection dropped");
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    if (!IsTerminated) Log(ex);
                }
                finally
                {
                    var tempHandler = updateHandler;
                    updateHandler = null;
                    isUpdating = false;

                    Task.Factory.StartNew(() =>
                    {
                        try { tempHandler.Dispose(); }
                        catch { }
                    });
                }
            }
        }

        private void UpdateJobTemplate(JObject jMessage)
        {
            if (jMessage.ContainsKey("result"))
            {
                var tempJob = JsonConvert.DeserializeObject<JobTemplate>(jMessage["result"].ToString(), serializeSettings);
                if (tempJob == null || string.IsNullOrWhiteSpace(tempJob.PrePOW)) return;

                tempJob.SetOrigin(this);

                if (CurrentJob == null || (CurrentJob.JobID != tempJob.JobID || CurrentJob.Height != tempJob.Height))
                {
                    lastUpdateTime = DateTime.Now;
                    CurrentJob = tempJob;
                    Log(Level.Info, false, $"New job #{CurrentJob.JobID} detected, height: {CurrentJob.Height}, difficulty: {CurrentJob.Difficulty}");
                }
            }
        }
        
        private void UpdateJob(string @params)
        {
            var tempJob = JsonConvert.DeserializeObject<JobTemplate>(@params, serializeSettings);
            if (tempJob == null || string.IsNullOrWhiteSpace(tempJob.PrePOW)) return;

            tempJob.SetOrigin(this);

            if (tempJob.GetScale() == 1)
                Log(Level.Warn, false, $"Invalid PrePOW:{Environment.NewLine}{CurrentJob.PrePOW}");

            else if (CurrentJob == null || (CurrentJob.JobID != tempJob.JobID || CurrentJob.Height != tempJob.Height))
            {
                lastUpdateTime = DateTime.Now;
                CurrentJob = tempJob;
                Log(Level.Info, false, $"New job #{CurrentJob.JobID} detected, height: {CurrentJob.Height}, difficulty: {CurrentJob.Difficulty}");
            }
        }

        private void UpdateSubmission(JObject jMessage)
        {
            if (jMessage.ContainsKey("result") && jMessage["result"].ToString() == "ok")
            {
                SharesAccepted++;
                Log(ConsoleColor.Cyan, Level.Info, false, $"Share[{SharesAccepted}] accepted");
            }
            else if (jMessage.ContainsKey("result") && jMessage["result"].ToString().StartsWith("blockfound"))
            {
                SharesAccepted++;
                BlocksMined++;
                Log(ConsoleColor.Cyan, Level.Info, false, $"Share[{SharesAccepted}] accepted (is a mined block)");
                Log(ConsoleColor.Cyan, Level.Info, false, $"Blocks mined [{BlocksMined}]");
            }
            if (jMessage.ContainsKey("error"))
            {
                try
                {
                    // -32501 "Share rejected due to low difficulty"
                    // -32504 "Shares rejected: duplicates"
                    var code = (int)jMessage["error"]["code"];
                    switch (code)
                    {
                        case -32501:
                            SharesRejected++;
                            Log(Level.Error, false, $"Share rejected due to low difficulty");
                            break;

                        case -32502:
                            SharesRejected++;
                            Log(Level.Error, false, $"Failed to validate solution");
                            break;

                        case -32503:
                            SharesStale++;
                            Log(Level.Warn, false, $"Stale solution submitted");
                            break;

                        default:
                            Log(Level.Error, false, $"{(string)jMessage["error"]["message"]}");
                            break;
                    }
                }
                catch { }
            }
        }

        private void UpdateLogin(JObject jMessage)
        {
            // {"id":"Stratum","jsonrpc":"2.0","method":"login","error":{"code":-32501,"message":"invalid login format"}} 
            if (jMessage.ContainsKey("error"))
            {
                try
                {
                    var errorMessage = (string)jMessage["error"]["message"];
                    if (!string.IsNullOrWhiteSpace(errorMessage)) Log(Level.Error, true, $"{errorMessage}");
                }
                catch { }
                try
                {
                    var code = (int)jMessage["error"]["code"];
                    if (code == -32501)
                    {
                        IsTerminated = true;
                        IsConnected = false;
                        IsInvalidConnection = true;

                        Log(Level.Error, true, $"Invalid login detected");
                        Close();
                    }
                }
                catch { }
            }
            // {"id":"Stratum","jsonrpc":"2.0","method":"login","result":"ok","error":null} 
            else if (jMessage.ContainsKey("result"))
            {
                IsLogin = true;

                if ((string)jMessage["result"] == "ok")
                    Log(Level.Info, true, $"Login OK");
                else
                    Log(Level.Warn, true, $"Login {(string)jMessage["result"]}");
            }
        }

        #endregion

    }
}
