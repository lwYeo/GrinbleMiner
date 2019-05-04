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
using System.Reflection;
using System.Runtime.InteropServices;
using OpenCl.DotNetCore.Interop;
using OpenCl.DotNetCore.Interop.Devices;

namespace GrinbleMiner.Solver.Device.API
{
    public static class AMD
    {

        #region Enums and Constants

        private enum DeviceInformation : uint
        {
            CL_DEVICE_TOPOLOGY_AMD = 0x4037,
            CL_DEVICE_BOARD_NAME_AMD = 0x4038,
            CL_DEVICE_GLOBAL_FREE_MEMORY_AMD = 0x4039
        }

        private const uint CL_DEVICE_TOPOLOGY_TYPE_PCIE_AMD = 1;

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Explicit, Size = 24, Pack = 1)]
        private struct CL_device_topology_amd
        {           
            [FieldOffset(0), MarshalAs(UnmanagedType.U4)]
            public uint type;
            [FieldOffset(4), MarshalAs(UnmanagedType.U1)]
            public byte unused01, unused02, unused03, unused04, unused05, unused06, unused07, unused08;
            [FieldOffset(12), MarshalAs(UnmanagedType.U1)]
            public byte unused09, unused10, unused11, unused12, unused13, unused14, unused15, unused16, unused17;
            [FieldOffset(21), MarshalAs(UnmanagedType.U1)]
            public byte bus;
            [FieldOffset(22), MarshalAs(UnmanagedType.U1)]
            public byte device;
            [FieldOffset(23), MarshalAs(UnmanagedType.U1)]
            public byte function;
        }

        #endregion

        #region Public methods

        public static int GetAmdDevicePciBusID(OpenCl.DotNetCore.Devices.Device device)
        {
            var deviceHandle = GetDeviceHandle(device);
            var deviceInformation = (OpenCl.DotNetCore.Interop.Devices.DeviceInformation)DeviceInformation.CL_DEVICE_TOPOLOGY_AMD;

            var temp = new byte[256];
            var res = DevicesNativeApi.GetDeviceInformation(deviceHandle,
                (OpenCl.DotNetCore.Interop.Devices.DeviceInformation)DeviceInformation.CL_DEVICE_BOARD_NAME_AMD,
                new UIntPtr(256), temp, out UIntPtr rVal);
            var name = System.Text.Encoding.ASCII.GetString(temp).TrimEnd(char.MinValue);

            var result = DevicesNativeApi.GetDeviceInformation(deviceHandle, deviceInformation, UIntPtr.Zero, null, out UIntPtr returnValueSize);
            if (result != Result.Success)
                throw new Exception($"The device information could not be retrieved. Error code: {result}.");

            var output = new byte[returnValueSize.ToUInt32()];
            result = DevicesNativeApi.GetDeviceInformation(deviceHandle, deviceInformation, new UIntPtr((uint)output.Length), output, out returnValueSize);
            if (result != Result.Success)
                throw new Exception($"The device information could not be retrieved. Error code: {result}.");

            var topology = ByteArrayToStructure<CL_device_topology_amd>(output);

            return (topology.type == CL_DEVICE_TOPOLOGY_TYPE_PCIE_AMD)
                ? topology.bus
                : -1;
        }

        public static string GetAmdDeviceName(OpenCl.DotNetCore.Devices.Device device)
        {
            var deviceHandle = GetDeviceHandle(device);
            var deviceInformation = (OpenCl.DotNetCore.Interop.Devices.DeviceInformation)DeviceInformation.CL_DEVICE_BOARD_NAME_AMD;

            var nameBin = new byte[256];
            var result = DevicesNativeApi.GetDeviceInformation(deviceHandle, deviceInformation, new UIntPtr(256), nameBin, out UIntPtr rVal);
            if (result != Result.Success)
                throw new Exception($"The device information could not be retrieved. Error code: {result}.");

            return System.Text.Encoding.ASCII.GetString(nameBin).TrimEnd(char.MinValue);
        }

        public static ulong GetAvailableMemory(OpenCl.DotNetCore.Devices.Device device)
        {
            var deviceHandle = GetDeviceHandle(device);
            var deviceInformation = (OpenCl.DotNetCore.Interop.Devices.DeviceInformation)DeviceInformation.CL_DEVICE_GLOBAL_FREE_MEMORY_AMD;

            var output = new byte[8];
            var result = DevicesNativeApi.GetDeviceInformation(deviceHandle, deviceInformation, new UIntPtr(8), output, out UIntPtr rVal);
            if (result != Result.Success)
                throw new Exception($"The device information could not be retrieved. Error code: {result}.");

            var memory = new ulong[1];
            Buffer.BlockCopy(output, 0, memory, 0, 8);

            return memory[0] * (ulong)Math.Pow(2, 10);
        }

        public static IntPtr GetDeviceHandle<T>(T device)
        {
            return device.GetPropertyValue<IntPtr>("Handle");
        }

        #endregion

        #region Private methods

        private static T GetPropertyValue<T>(this object obj, string propertyName)
        {
            if (obj == null) throw new ArgumentNullException("obj");

            var objType = obj.GetType();
            var propInfo = GetPropertyInfo(objType, propertyName);

            if (propInfo == null)
                throw new ArgumentOutOfRangeException("propertyName", string.Format("Couldn't find property {0} in type {1}", propertyName, objType.FullName));

            return (T)propInfo.GetValue(obj, null);
        }

        private static PropertyInfo GetPropertyInfo(Type type, string propertyName)
        {
            PropertyInfo propInfo = null;
            do
            {
                propInfo = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            while (propInfo == null && type != null);

            return propInfo;
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T)); }
            finally { handle.Free(); }
        }

        #endregion

    }
}
