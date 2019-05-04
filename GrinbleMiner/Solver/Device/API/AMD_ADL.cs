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
using System.Runtime.InteropServices;

namespace GrinbleMiner.Solver.Device.API
{
    public static class AMD_ADL
    {

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct ADL_AdapterInfo
        {
            public int Size;
            public int AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string UDID;
            public int BusNumber;
            public int DriverNumber;
            public int FunctionNumber;
            public int VendorID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DisplayName;
            public int Present;
            public int Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string PNPString;
            public int OSDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADL_AdapterInfoArray
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
            public ADL_AdapterInfo[] ADLAdapterInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADL_OverdriveFanControl
        {
            public int iMode;
            public int iFanControlMode;
            public int iCurrentFanSpeedMode;
            public int iCurrentFanSpeed;
            public int iTargetFanSpeed;
            public int iTargetTemperature;
            public int iMinPerformanceClock;
            public int iMinFanLimit;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADL_OverdrivePerformanceStatus
        {
            public int iCoreClock;
            public int iMemoryClock;
            public int iDCEFClock;
            public int iGFXClock;
            public int iUVDClock;
            public int iVCEClock;
            public int iGPUActivityPercent;
            public int iCurrentCorePerformanceLevel;
            public int iCurrentMemoryPerformanceLevel;
            public int iCurrentDCEFPerformanceLevel;
            public int iCurrentGFXPerformanceLevel;
            public int iUVDPerformanceLevel;
            public int iVCEPerformanceLevel;
            public int iCurrentBusSpeed;
            public int iCurrentBusLanes;
            public int iMaximumBusLanes;
            public int iVDDC;
            public int iVDDCI;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADL_PM_Activity
        {
            public int iSize;
            public int iEngineClock;
            public int iMemoryClock;
            public int iVddc;
            public int iActivityPercent;
            public int iCurrentPerformanceLevel;
            public int iCurrentBusSpeed;
            public int iCurrentBusLanes;
            public int iMaximumBusLanes;
            public int iReserved;
        }

        public struct DeviceAdapterInfo
        {
            public ADL_AdapterInfo Info { get; set; }
            public bool IsOverdriveSupported { get; set; }
            public bool IsEnabled { get; set; }
            public int OverdriveVersion { get; set; }
        }

        #endregion

        #region Delegates

        public delegate IntPtr ADL_Main_Memory_Alloc(int size);
        public delegate int ADL_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

        public delegate int ADL_Adapter_NumberOfAdapters_Get(ref int numAdapters);
        public delegate int ADL_Adapter_AdapterInfo_Get(IntPtr info, int inputSize);

        public delegate int ADL_Overdrive5_CurrentActivity_Get(int adapterIndex, ref ADL_PM_Activity pmActivity);

        public delegate int ADL2_Overdrive_Caps(IntPtr context, int adapterIndex, ref int iSupported, ref int iEnabled, ref int iVersion);

        public delegate int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, ref int temperature);
        public delegate int ADL2_OverdriveN_FanControl_Get(IntPtr context, int adapterIndex, ref ADL_OverdriveFanControl fanControl);
        public delegate int ADL2_Overdrive6_CurrentPower_Get(IntPtr context, int adapterIndex, int type, ref int powerBits);
        public delegate int ADL2_OverdriveN_PerformanceStatus_Get(IntPtr context, int adapterIndex, ref ADL_OverdrivePerformanceStatus performanceStatus);

        #endregion

        #region P/Invoke

        private static class ADLImport
        {

            public const string Atiadlxx_FileName = "atiadlxx.dll";
            public const string Kernel32_FileName = "kernel32.dll";

            [DllImport(Kernel32_FileName)]
            public static extern IntPtr GetModuleHandle(string moduleName);

            #region ADL

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Main_Control_Destroy();

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Main_Control_IsFunctionValid(IntPtr module, string procName);

            [DllImport(Atiadlxx_FileName)]
            public static extern IntPtr ADL_Main_Control_GetProcAddress(IntPtr module, string procName);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Adapter_NumberOfAdapters_Get(ref int numAdapters);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Adapter_AdapterInfo_Get(IntPtr info, int inputSize);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL_Overdrive5_CurrentActivity_Get(int adapterIndex, ref ADL_PM_Activity pmActivity);

            #endregion

            #region ADL2

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL2_Overdrive_Caps(IntPtr context, int adapterIndex, ref int iSupported, ref int iEnabled, ref int iVersion);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, ref int temperature);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL2_OverdriveN_FanControl_Get(IntPtr context, int adapterIndex, ref ADL_OverdriveFanControl fanControl);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL2_Overdrive6_CurrentPower_Get(IntPtr context, int adapterIndex, int type, ref int powerBits);

            [DllImport(Atiadlxx_FileName)]
            public static extern int ADL2_OverdriveN_PerformanceStatus_Get(IntPtr context, int adapterIndex, ref ADL_OverdrivePerformanceStatus performanceStatus);

            #endregion

        }

        #endregion

        #region Public Properties and Methods

        public static DeviceAdapterInfo[] AdapterInfos { get; private set; }

        public static DeviceAdapterInfo GetAdapterInfoByPciBusID(int pciBusID)
        {
            return AdapterInfos.FirstOrDefault(i => i.Info.BusNumber == pciBusID);
        }

        public static bool Initialize(out string errorMessage)
        {
            errorMessage = string.Empty;

            _PowerHistory = new List<Tuple<int, DateTime, decimal>>();
            try
            {
                var response = -1;

                if (ADL.ADL_Main_Control_Create != null)
                    response = ADL.ADL_Main_Control_Create?.Invoke(ADL.ADL_Main_Memory_Alloc, 1/*Get only the present adapters*/) ?? -1;

                if (response != SUCCESS)
                {
                    errorMessage = "ADL_Main_Control_Create failed.";
                    return false;
                }

                var numOfAdapters = 0;
                response = ADL.ADL_Adapter_NumberOfAdapters_Get?.Invoke(ref numOfAdapters) ?? -1;

                if (response != SUCCESS)
                {
                    errorMessage = "ADL_Adapter_NumberOfAdapters_Get failed.";
                    return false;
                }

                if (numOfAdapters < 1)
                {
                    errorMessage = "ADL returned no adapter.";
                    return false;
                }

                var adapterInfos = new ADL_AdapterInfoArray();
                int size = Marshal.SizeOf(adapterInfos);

                var adapterBuffer = Marshal.AllocCoTaskMem(size);
                try
                {
                    Marshal.StructureToPtr(adapterInfos, adapterBuffer, false);
                    response = ADL.ADL_Adapter_AdapterInfo_Get?.Invoke(adapterBuffer, size) ?? -1;

                    if (response != SUCCESS)
                    {
                        errorMessage = $"ADL_Adapter_AdapterInfo_Get() returned error code {response}";
                        return false;
                    }
                    adapterInfos = Marshal.PtrToStructure<ADL_AdapterInfoArray>(adapterBuffer);
                }
                finally { Marshal.FreeCoTaskMem(adapterBuffer); }

                var infoList = new List<DeviceAdapterInfo>(numOfAdapters);

                for (var i = 0; i < numOfAdapters; i++)
                {
                    var info = adapterInfos.ADLAdapterInfo[i];
                    int isSupported = 0, isEnabled = 0, version = 0;

                    if (infoList.Any(di => di.Info.BusNumber == info.BusNumber)) continue;

                    response = ADL.ADL2_Overdrive_Caps?.Invoke(IntPtr.Zero, info.AdapterIndex, ref isSupported, ref isEnabled, ref version) ?? -1;

                    if (response == SUCCESS)
                        infoList.Add(new DeviceAdapterInfo()
                        {
                            Info = info,
                            IsOverdriveSupported = (isSupported > 0),
                            IsEnabled = (isEnabled > 0),
                            OverdriveVersion = version
                        });
                }

                AdapterInfos = infoList.Where(i => i.IsOverdriveSupported).ToArray();
                infoList.Clear();
                return true;
            }
            catch (Exception ex)
            {
                AMD_APP.Log(ex);
                AMD_APP.Log(Logger.Level.Error, "Failed to initialize AMD ADL.");
                return false;
            }
        }

        public static DeviceQuery QueryGpuStatus(int pciBusID)
        {
            var info = GetAdapterInfoByPciBusID(pciBusID);

            var errorMessages = new string[7];
            try
            {
                return new DeviceQuery()
                {
                    FanSpeed = $"{GetAdapterFanCurrentSpeed(info, out errorMessages[0])} rpm",
                    Temperature = $"{GetAdapterTemperature(info, out errorMessages[1])} C",
                    PowerDraw = $"{Math.Round(GetAdapterNormalizedPower(info, out errorMessages[2]), 2)} W",
                    ClockGPU = $"{GetAdapterCurrentCoreClock(info, out errorMessages[3])} MHz",
                    ClockVRAM = $"{GetAdapterCurrentVramClock(info, out errorMessages[4])} MHz",
                    UtilizationGPU = $"{GetAdapterCurrentUtilization(info, out errorMessages[5])} %",
                    UtilizationVRAM = null // Not supported
                };
            }
            finally
            {
                foreach (var errorMessage in errorMessages)
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                        AMD_APP.Log(Logger.Level.Error, errorMessage);
            }
        }

        public static decimal GetAdapterTemperature(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var temperature = 0;
            int response = ADL.ADL2_OverdriveN_Temperature_Get?.Invoke(IntPtr.Zero, info.Info.AdapterIndex, 1, ref temperature) ?? -1;

            if (response == SUCCESS) return (decimal)temperature / 1000;

            errorMessage = $"ADL2_OverdriveN_Temperature_Get() returned error code {response}";
            return decimal.MinValue;
        }

        public static decimal GetAdapterFanCurrentSpeed(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var fanControl = new ADL_OverdriveFanControl();
            var response = ADL.ADL2_OverdriveN_FanControl_Get?.Invoke(IntPtr.Zero, info.Info.AdapterIndex, ref fanControl) ?? -1;

            if (response != SUCCESS)
            {
                errorMessage = $"ADL2_OverdriveN_FanControl_Get() returned error code {response}";
                return decimal.MinValue;
            }

            return fanControl.iCurrentFanSpeed;
        }

        public static decimal GetAdapterNormalizedPower(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var powerBits = 0;
            var response = ADL.ADL2_Overdrive6_CurrentPower_Get?.Invoke(IntPtr.Zero, info.Info.AdapterIndex, 0, ref powerBits) ?? -1;

            if (response != SUCCESS)
            {
                errorMessage = $"ADL2_Overdrive6_CurrentPower_Get() returned error code {response}";
                return decimal.MinValue;
            }

            var watt = (decimal)(powerBits >> 8);
            var mWatt = (decimal)(powerBits & 0xff) / 0xff;

            return GetNormalizedPower(info, watt + mWatt);
        }

        public static decimal GetAdapterCurrentCoreClock(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var performanceStatus = new ADL_OverdrivePerformanceStatus();
            var response = ADL.ADL2_OverdriveN_PerformanceStatus_Get?.Invoke(IntPtr.Zero, info.Info.AdapterIndex, ref performanceStatus) ?? -1;

            if (response == SUCCESS) return (decimal)performanceStatus.iCoreClock / 100;

            errorMessage = $"ADL2_OverdriveN_PerformanceStatus_Get() returned error code {response}";
            return decimal.MinValue;
        }

        public static decimal GetAdapterCurrentVramClock(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var performanceStatus = new ADL_OverdrivePerformanceStatus();
            var response = ADL.ADL2_OverdriveN_PerformanceStatus_Get?.Invoke(IntPtr.Zero, info.Info.AdapterIndex, ref performanceStatus) ?? -1;

            if (response == SUCCESS) return (decimal)performanceStatus.iMemoryClock / 100;

            errorMessage = $"ADL2_OverdriveN_PerformanceStatus_Get() returned error code {response}";
            return decimal.MinValue;
        }

        public static decimal GetAdapterCurrentUtilization(DeviceAdapterInfo info, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!CheckOverdriveVersion(info, ref errorMessage)) return decimal.MinValue;

            var pmActivity = new ADL_PM_Activity();
            var response = ADL.ADL_Overdrive5_CurrentActivity_Get?.Invoke(info.Info.AdapterIndex, ref pmActivity) ?? -1;

            if (response == SUCCESS) return (decimal)pmActivity.iActivityPercent;

            errorMessage = $"ADL_Overdrive5_CurrentActivity_Get() returned error code {response}";
            return decimal.MinValue;
        }

        #endregion

        #region Private

        #region Constants, Declarations and Methods

        private const int SUCCESS = 0;

        private static List<Tuple<int, DateTime, decimal>> _PowerHistory;

        private static decimal GetNormalizedPower(DeviceAdapterInfo info, decimal currentPower)
        {
            lock (_PowerHistory)
            {
                _PowerHistory.RemoveAll(p => p.Item2 < DateTime.Now.AddMinutes(-1));
                _PowerHistory.Add(new Tuple<int, DateTime, decimal>(info.Info.BusNumber, DateTime.Now, currentPower));

                return _PowerHistory.Where(p => p.Item1 == info.Info.BusNumber).Average(p => p.Item3);
            }
        }

        private static bool CheckOverdriveVersion(DeviceAdapterInfo info, ref string errorMessage)
        {
            errorMessage = (info.OverdriveVersion >= 6)
                ? string.Empty
                : $"Only Overdrive version 6 or greater is supported, current version {info.OverdriveVersion}.";

            return info.OverdriveVersion >= 6;
        }

        #endregion

        private static class ADL
        {

            #region Declarations and Constructor

            public static ADL_Main_Memory_Alloc ADL_Main_Memory_Alloc;
            private static ADL_Main_Control_Create _ADL_Main_Control_Create;
            private static ADL_Adapter_NumberOfAdapters_Get _ADL_Adapter_NumberOfAdapters_Get;
            private static ADL_Adapter_AdapterInfo_Get _ADL_Adapter_AdapterInfo_Get;
            private static ADL_Overdrive5_CurrentActivity_Get _ADL_Overdrive5_CurrentActivity_Get;
            private static ADL2_Overdrive_Caps _ADL2_Overdrive_Caps;
            private static ADL2_OverdriveN_Temperature_Get _ADL2_OverdriveN_Temperature_Get;
            private static ADL2_OverdriveN_FanControl_Get _ADL2_OverdriveN_FanControl_Get;
            private static ADL2_Overdrive6_CurrentPower_Get _ADL2_Overdrive6_CurrentPower_Get;
            private static ADL2_OverdriveN_PerformanceStatus_Get _ADL2_OverdriveN_PerformanceStatus_Get;

            private static bool ADL_Main_Control_Create_Check;
            private static bool ADL_Adapter_NumberOfAdapters_Get_Check;
            private static bool ADL_Adapter_AdapterInfo_Get_Check;
            private static bool ADL_Overdrive5_CurrentActivity_Get_Check;
            private static bool ADL2_Overdrive_Caps_Check;
            private static bool ADL2_OverdriveN_Temperature_Get_Check;
            private static bool ADL2_OverdriveN_FanControl_Get_Check;
            private static bool ADL2_Overdrive6_CurrentPower_Get_Check;
            private static bool ADL2_OverdriveN_PerformanceStatus_Get_Check;

            static ADL()
            {
                ADL_Main_Memory_Alloc = delegate (int size) { return Marshal.AllocCoTaskMem(size); };
            }

            #endregion

            #region Public methods

            public static ADL_Main_Control_Create ADL_Main_Control_Create
            {
                get
                {
                    if (!ADL_Main_Control_Create_Check && _ADL_Main_Control_Create == null)
                    {
                        ADL_Main_Control_Create_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL_Main_Control_Create"))
                            _ADL_Main_Control_Create = ADLImport.ADL_Main_Control_Create;
                    }
                    return _ADL_Main_Control_Create;
                }
            }

            public static ADL_Adapter_NumberOfAdapters_Get ADL_Adapter_NumberOfAdapters_Get
            {
                get
                {
                    if (!ADL_Adapter_NumberOfAdapters_Get_Check && _ADL_Adapter_NumberOfAdapters_Get == null)
                    {
                        ADL_Adapter_NumberOfAdapters_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL_Adapter_NumberOfAdapters_Get"))
                            _ADL_Adapter_NumberOfAdapters_Get = ADLImport.ADL_Adapter_NumberOfAdapters_Get;
                    }
                    return _ADL_Adapter_NumberOfAdapters_Get;
                }
            }

            public static ADL_Adapter_AdapterInfo_Get ADL_Adapter_AdapterInfo_Get
            {
                get
                {
                    if (!ADL_Adapter_AdapterInfo_Get_Check && _ADL_Adapter_AdapterInfo_Get == null)
                    {
                        ADL_Adapter_AdapterInfo_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL_Adapter_AdapterInfo_Get"))
                            _ADL_Adapter_AdapterInfo_Get = ADLImport.ADL_Adapter_AdapterInfo_Get;
                    }
                    return _ADL_Adapter_AdapterInfo_Get;
                }
            }

            public static ADL2_Overdrive_Caps ADL2_Overdrive_Caps
            {
                get
                {
                    if (!ADL2_Overdrive_Caps_Check && _ADL2_Overdrive_Caps == null)
                    {
                        ADL2_Overdrive_Caps_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL2_Overdrive_Caps"))
                            _ADL2_Overdrive_Caps = ADLImport.ADL2_Overdrive_Caps;
                    }
                    return _ADL2_Overdrive_Caps;
                }
            }

            public static ADL2_OverdriveN_Temperature_Get ADL2_OverdriveN_Temperature_Get
            {
                get
                {
                    if (!ADL2_OverdriveN_Temperature_Get_Check && _ADL2_OverdriveN_Temperature_Get == null)
                    {
                        ADL2_OverdriveN_Temperature_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL2_OverdriveN_Temperature_Get"))
                            _ADL2_OverdriveN_Temperature_Get = ADLImport.ADL2_OverdriveN_Temperature_Get;
                    }
                    return _ADL2_OverdriveN_Temperature_Get;
                }
            }

            public static ADL2_OverdriveN_FanControl_Get ADL2_OverdriveN_FanControl_Get
            {
                get
                {
                    if (!ADL2_OverdriveN_FanControl_Get_Check && _ADL2_OverdriveN_FanControl_Get == null)
                    {
                        ADL2_OverdriveN_FanControl_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL2_OverdriveN_FanControl_Get"))
                            _ADL2_OverdriveN_FanControl_Get = ADLImport.ADL2_OverdriveN_FanControl_Get;
                    }
                    return _ADL2_OverdriveN_FanControl_Get;
                }
            }

            public static ADL2_Overdrive6_CurrentPower_Get ADL2_Overdrive6_CurrentPower_Get
            {
                get
                {
                    if (!ADL2_Overdrive6_CurrentPower_Get_Check && _ADL2_Overdrive6_CurrentPower_Get == null)
                    {
                        ADL2_Overdrive6_CurrentPower_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL2_Overdrive6_CurrentPower_Get"))
                            _ADL2_Overdrive6_CurrentPower_Get = ADLImport.ADL2_Overdrive6_CurrentPower_Get;
                    }
                    return _ADL2_Overdrive6_CurrentPower_Get;
                }
            }

            public static ADL2_OverdriveN_PerformanceStatus_Get ADL2_OverdriveN_PerformanceStatus_Get
            {
                get
                {
                    if (!ADL2_OverdriveN_PerformanceStatus_Get_Check && _ADL2_OverdriveN_PerformanceStatus_Get == null)
                    {
                        ADL2_OverdriveN_PerformanceStatus_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL2_OverdriveN_PerformanceStatus_Get"))
                            _ADL2_OverdriveN_PerformanceStatus_Get = ADLImport.ADL2_OverdriveN_PerformanceStatus_Get;
                    }
                    return _ADL2_OverdriveN_PerformanceStatus_Get;
                }
            }

            public static ADL_Overdrive5_CurrentActivity_Get ADL_Overdrive5_CurrentActivity_Get
            {
                get
                {
                    if (!ADL_Overdrive5_CurrentActivity_Get_Check && _ADL_Overdrive5_CurrentActivity_Get == null)
                    {
                        ADL_Overdrive5_CurrentActivity_Get_Check = true;

                        if (ADLCheckLibrary.IsFunctionValid("ADL_Overdrive5_CurrentActivity_Get"))
                            _ADL_Overdrive5_CurrentActivity_Get = ADLImport.ADL_Overdrive5_CurrentActivity_Get;
                    }
                    return _ADL_Overdrive5_CurrentActivity_Get;
                }
            }

            #endregion

            #region ADLCheckLibrary

            private static class ADLCheckLibrary
            {

                private static readonly IntPtr ADLLibrary;

                static ADLCheckLibrary()
                {
                    try
                    {
                        if (ADLImport.ADL_Main_Control_IsFunctionValid(IntPtr.Zero, "ADL_Main_Control_Create") == 1)
                            ADLLibrary = ADLImport.GetModuleHandle(ADLImport.Atiadlxx_FileName);
                    }
                    catch (DllNotFoundException) { }
                    catch (EntryPointNotFoundException) { }
                    catch (Exception) { }
                }

                public static bool IsFunctionValid(string functionName)
                {
                    if (ADLLibrary == IntPtr.Zero) return false;

                    return (ADLImport.ADL_Main_Control_IsFunctionValid(ADLLibrary, functionName) == 1);
                }

                public static IntPtr GetProcAddress(string functionName)
                {
                    if (ADLLibrary == IntPtr.Zero) return IntPtr.Zero;

                    return ADLImport.ADL_Main_Control_GetProcAddress(ADLLibrary, functionName);
                }

            }

            #endregion

        }

        #endregion

    }
}
