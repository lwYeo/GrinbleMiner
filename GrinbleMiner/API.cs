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
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using GrinbleMiner.Solver.Device;
using GrinbleMiner.Solver.Device.API;
using static GrinbleMiner.Logger;

namespace GrinbleMiner
{
    public class API
    {

        #region Public Properties and Constructor

        public DateTime SystemDateTime { get; } = DateTime.Now;

        public string Uptime { get; set; }

        public string CurrentConnection { get; set; }

        public string CurrentUserID { get; set; }

        public BigInteger CurrentBlockHeight { get; set; }

        public ulong CurrentDifficulty { get; set; }

        public ulong CurrentJobID { get; set; }

        public decimal GraphsPerSecTotal { get; set; }

        public ulong SharesAccepted { get; set; }

        public ulong SharesStale { get; set; }

        public ulong SharesRejected { get; set; }

        public List<Device> Devices { get; set; }

        public class Device
        {

            [JsonConverter(typeof(StringEnumConverter))]
            public PlatformType Type { get; set; }

            public int DeviceID { get; set; }

            public string PciBusID { get; set; }

            public string Name { get; set; }

            public decimal GraphsPerSec { get; set; }

            public string Efficiency { get; set; }

            public DeviceQuery MonitoringAPI { get; set; }

        }

        public API() { Devices = new List<Device>(); }

        #endregion

        #region Static Declarations, Properties and Constructor

        private static bool _IsOngoing;
        private static HttpListener _Listener;

        public static bool IsSupported { get; }

        static API()
        {
            IsSupported = HttpListener.IsSupported;
        }

        #endregion

        #region Public static methods

        public static void Start(ref Config settings)
        {
            _IsOngoing = false;

            if (!IsSupported)
            {
                Log(Level.Error, "Obsolete OS detected, API will not start.");
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiPath))
            {
                settings.ApiPath = Config.Defaults.ApiPath;
                settings.Save();
            }

            var httpBind = settings.ApiPath.ToString();
            if (string.IsNullOrWhiteSpace(httpBind))
            {
                Log(Level.Info, $"API path is null or empty, using default {Config.Defaults.ApiPath}");
                httpBind = Config.Defaults.ApiPath;
            }
            else if (settings.ApiPath == "0")
            {
                Log(Level.Info, "API is disabled.");
                return;
            }

            if (!httpBind.StartsWith("http://") || httpBind.StartsWith("https://")) httpBind = "http://" + httpBind;
            if (!httpBind.EndsWith("/")) httpBind += "/";

            if (!ushort.TryParse(httpBind.Split(':')[2].TrimEnd('/'), out ushort port))
            {
                Log(Level.Error, $"Invalid port provided for API: {httpBind}");
                return;
            }

            var tempIPAddress = httpBind.Split(new string[] { "//" }, StringSplitOptions.None)[1].Split(':')[0];
            if (!IPAddress.TryParse(tempIPAddress, out IPAddress ipAddress))
            {
                Log(Level.Error, $"Invalid IP address provided for API: {httpBind}");
                return;
            }

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try { socket.Bind(new IPEndPoint(ipAddress, port)); }
                catch (Exception)
                {
                    Log(Level.Error, $"API failed to bind to: {settings.ApiPath}");
                    return;
                }
            };

            try
            {
                _Listener = new HttpListener();
                _Listener.Prefixes.Add(httpBind);

                Process(_Listener);
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, "An error has occured while starting API service.");
                return;
            }
        }

        public static void Stop()
        {
            if (!_IsOngoing) return;
            try
            {
                Log(Level.Info, "API service stopping...");
                _IsOngoing = false;
                _Listener.Stop();
            }
            catch (Exception ex)
            {
                Log(ex);
                Log(Level.Error, "An error has occured while stopping API service.");
            }
        }

        #endregion

        #region Private static methods

        private async static void Process(HttpListener listener)
        {
            listener.Start();
            Log(Level.Info, $"API service started at {listener.Prefixes.ElementAt(0)}...");

            _IsOngoing = true;
            while (_IsOngoing)
            {
                HttpListenerContext context = null;

                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException ex)
                {
                    if (ex.ErrorCode == 995) break; // connection likely closed

                    Log(Level.Error, $"{ex.GetType().Name}: Error code: {ex.ErrorCode}, Message: {ex.Message}");
                    await Task.Delay(1000);
                    continue;
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log(ex);
                    await Task.Delay(1000);
                    continue;
                }

                HttpListenerResponse response = context.Response;
                if (response != null)
                {
                    response.AppendHeader("Pragma", "no-cache");
                    response.AppendHeader("Expires", "0");
                    response.ContentType = "application/json";
                    response.StatusCode = (int)HttpStatusCode.OK;

                    ProcessApiDataResponse(response);
                }
            }
        }

        private static void ProcessApiDataResponse(HttpListenerResponse response)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var api = new API()
                    {
                        Uptime = $"{Program.Uptime.Elapsed.Days:000}d {Program.Uptime.Elapsed.Hours:00}h {Program.Uptime.Elapsed.Minutes:00}m {Program.Uptime.Elapsed.Seconds:00}s",
                        CurrentConnection = NetworkInterface.Pool.Handler.GetCurrentStratum().CurrentParams.ConnectionString,
                        CurrentUserID = NetworkInterface.Pool.Handler.GetCurrentStratum().CurrentParams.UserID,
                        CurrentBlockHeight = NetworkInterface.Pool.Handler.CurrentJob.Height,
                        CurrentDifficulty = NetworkInterface.Pool.Handler.CurrentJob.Difficulty,
                        CurrentJobID = NetworkInterface.Pool.Handler.CurrentJob.JobID,
                        GraphsPerSecTotal = decimal.MinusOne, // to be summed from individual devices below
                        SharesAccepted = NetworkInterface.Pool.Handler.SharesAccepted,
                        SharesStale = NetworkInterface.Pool.Handler.SharesStale,
                        SharesRejected = NetworkInterface.Pool.Handler.SharesRejected,
                        Devices = new List<Device>()
                    };

                    foreach (var solver in Solver.Handler.Solvers.ToArray())
                        foreach (var device in solver.Devices.ToArray())
                            api.Devices.Add(new Device()
                            {
                                Type = device.Type,
                                DeviceID = device.DeviceID,
                                PciBusID = device.PciBusID.ToString("X2"),
                                Name = device.Name,
                                GraphsPerSec = Math.Round(solver.GetDeviceGraphPerSec(device), 2),
                                Efficiency = null, // To be calculated after assigning Monitoring API below
                                MonitoringAPI = Solver.Handler.GetMonitoringApiByDevice(device)
                            });

                    foreach (var device in api.Devices)
                        if (!string.IsNullOrWhiteSpace(device.MonitoringAPI?.PowerDraw))
                            if (decimal.TryParse(device.MonitoringAPI.PowerDraw.Split(' ')[0], out decimal tempPowerDraw))
                                if (tempPowerDraw >= decimal.Zero)
                                    device.Efficiency = $"{device.GraphsPerSec / tempPowerDraw * 1000:F1} g/MJ";

                    api.GraphsPerSecTotal = api.Devices.Sum(d => d.GraphsPerSec);

                    api.Devices.Sort((x, y) => x.PciBusID.CompareTo(y.PciBusID));

                    var buffer = Encoding.UTF8.GetBytes(Utils.Json.SerializeFromObject(api, Utils.Json.BaseClassFirstSettings));

                    if (buffer != null)
                        using (var output = response.OutputStream)
                        {
                            output.Write(buffer, 0, buffer.Length);
                            output.Flush();
                        }
                }
                catch (Exception ex)
                {
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    Log(ex);
                }
                finally
                {
                    try { if (response != null) response.Close(); }
                    catch (Exception ex) { Log(ex); }
                }
            }, TaskCreationOptions.LongRunning);
        }

        #endregion

    }
}
