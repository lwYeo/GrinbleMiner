
#region Using Directives

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCl.DotNetCore.Contexts;
using OpenCl.DotNetCore.Devices;
using OpenCl.DotNetCore.Events;
using OpenCl.DotNetCore.Interop;
using OpenCl.DotNetCore.Interop.CommandQueues;
using OpenCl.DotNetCore.Interop.EnqueuedCommands;
using OpenCl.DotNetCore.Kernels;
using OpenCl.DotNetCore.Memory;

#endregion

namespace OpenCl.DotNetCore.CommandQueues
{
    /// <summary>
    /// Represents an OpenCL command queue.
    /// </summary>
    public class CommandQueue : HandleBase
    {
        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="CommandQueue"/> instance.
        /// </summary>
        /// <param name="handle">The handle to the OpenCL command queue.</param>
        internal CommandQueue(IntPtr handle)
            : base(handle)
        {
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reads the specified memory object associated with this command queue asynchronously.
        /// </summary>
        /// <param name="memoryObject">The memory object that is to be read.</param>
        /// <param name="outputSize">The number of array elements that are to be returned.</param>
        /// <typeparam name="T">The type of the array that is to be returned.</typeparam>
        /// <returns>Returns the value of the memory object.</param>
        public Task<T[]> EnqueueReadBufferAsync<T>(MemoryObject memoryObject, int outputSize) where T : struct
        {
            // Creates a new task completion source, which is used to signal when the command has completed
            TaskCompletionSource<T[]> taskCompletionSource = new TaskCompletionSource<T[]>();

            // Allocates enough memory for the result value
            IntPtr resultValuePointer = IntPtr.Zero;
            int size = Marshal.SizeOf<T>() * outputSize;
            resultValuePointer = Marshal.AllocHGlobal(size);

            // Reads the memory object, by enqueuing the read operation to the command queue
            IntPtr waitEventPointer;
            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)size), resultValuePointer, 0, null, out waitEventPointer);
            
            // Checks if the read operation was queued successfuly, if not, an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The memory object could not be read.", result);

            // Subscribes to the completed event of the wait event that was returned, when the command finishes, the task completion source is resolved
            AwaitableEvent awaitableEvent = new AwaitableEvent(waitEventPointer);
            awaitableEvent.OnCompleted += (sender, e) =>
            {
                try
                {
                    // Checks if the command was executed successfully, if not, then an exception is thrown
                    if (awaitableEvent.CommandExecutionStatus == CommandExecutionStatus.Error)
                    {
                        taskCompletionSource.TrySetException(new OpenClException($"The command completed with the error code {awaitableEvent.CommandExecutionStatusCode}."));
                        return;
                    }

                    // Goes through the result and converts the content of the result to an array
                    T[] resultValue = new T[outputSize];
                    for (int i = 0; i < outputSize; i++)
                        resultValue[i] = Marshal.PtrToStructure<T>(IntPtr.Add(resultValuePointer, i * Marshal.SizeOf<T>()));

                    // Sets the result
                    taskCompletionSource.TrySetResult(resultValue);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
                finally
                {
                    // Finally the allocated memory has to be freed and the allocated resources are disposed of
                    if (resultValuePointer != IntPtr.Zero)
                        Marshal.FreeHGlobal(resultValuePointer);
                    awaitableEvent.Dispose();
                }
            };

            // Returns the task completion source, which resolves when the command has finished
            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Reads the specified memory object associated with this command queue.
        /// </summary>
        /// <param name="memoryObject">The memory object that is to be read.</param>
        /// <param name="outputLength">The number of array elements that are to be returned.</param>
        /// <typeparam name="T">The type of the array that is to be returned.</typeparam>
        /// <returns>Returns the value of the memory object.</param>
        public T[] EnqueueReadBuffer<T>(MemoryObject memoryObject, int outputLength) where T : struct
        {
            T[] resultValue = new T[outputLength];
#if UNSAFE
            switch (resultValue)
            {
                case int[] intArray:
                    unsafe
                    {
                        fixed (int* intPtr = intArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(int) * outputLength)), (IntPtr)((void*)intPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        return resultValue;
                    }
                case uint[] uintArray:
                    unsafe
                    {
                        fixed (uint* uintPtr = uintArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(uint) * outputLength)), (IntPtr)((void*)uintPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        return resultValue;
                    }
                default:
                    // Tries to read the memory object
                    IntPtr resultValuePointer = IntPtr.Zero;
                    try
                    {
                        // Allocates enough memory for the result value
                        int size = Marshal.SizeOf<T>() * outputLength;
                        resultValuePointer = Marshal.AllocHGlobal(size);

                        // Reads the memory object, by enqueuing the read operation to the command queue
                        IntPtr waitEventPointer;
                        Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)size), resultValuePointer, 0, null, out waitEventPointer);

                        // Checks if the read operation was queued successfuly, if not, an exception is thrown
                        if (result != Result.Success)
                            throw new OpenClException("The memory object could not be read.", result);

                        // Goes through the result and converts the content of the result to an array
                        for (int i = 0; i < outputLength; i++)
                            resultValue[i] = Marshal.PtrToStructure<T>(IntPtr.Add(resultValuePointer, i * Marshal.SizeOf<T>()));

                        // Returns the content of the memory object
                        return resultValue;
                    }
                    finally
                    {
                        // Finally the allocated memory has to be freed
                        if (resultValuePointer != IntPtr.Zero)
                            Marshal.FreeHGlobal(resultValuePointer);
                    }
            }
#else
            // Tries to read the memory object
            IntPtr resultValuePointer = IntPtr.Zero;
            try
            {
                // Allocates enough memory for the result value
                int size = Marshal.SizeOf<T>() * outputLength;
                resultValuePointer = Marshal.AllocHGlobal(size);

                // Reads the memory object, by enqueuing the read operation to the command queue
                IntPtr waitEventPointer;
                Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)size), resultValuePointer, 0, null, out waitEventPointer);
                
                // Checks if the read operation was queued successfuly, if not, an exception is thrown
                if (result != Result.Success)
                    throw new OpenClException("The memory object could not be read.", result);

                // Goes through the result and converts the content of the result to an array
                for (int i = 0; i < outputLength; i++)
                    resultValue[i] = Marshal.PtrToStructure<T>(IntPtr.Add(resultValuePointer, i * Marshal.SizeOf<T>()));
                
                // Returns the content of the memory object
                return resultValue;
            }
            finally
            {
                // Finally the allocated memory has to be freed
                if (resultValuePointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(resultValuePointer);
            }
#endif
        }

#if UNSAFE

        /// <summary>
        /// Reads the specified memory object associated with this command queue.
        /// </summary>
        /// <param name="memoryObject">The memory object that is to be read.</param>
        /// <param name="outputLength">The number of array elements that are to be filled.</param>
        /// <typeparam name="T">The type of the array that is to be returned.</typeparam>
        /// <returns>Returns the value of the memory object.</param>
        public unsafe void EnqueueReadBuffer<T>(MemoryObject memoryObject, int outputLength, ref T[] output)
        {
            switch (output)
            {
                case uint[] uintArray:
                    unsafe
                    {
                        fixed (uint* uintPtr = uintArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(uint) * outputLength)), (IntPtr)((void*)uintPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                case int[] intArray:
                    unsafe
                    {
                        fixed (int* intPtr = intArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(int) * outputLength)), (IntPtr)((void*)intPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                case byte[] byteArray:
                    unsafe
                    {
                        fixed (byte* bytePtr = byteArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(outputLength)), (IntPtr)((void*)bytePtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                case sbyte[] sbyteArray:
                    unsafe
                    {
                        fixed (sbyte* sbytePtr = sbyteArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(outputLength)), (IntPtr)((void*)sbytePtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                case ulong[] ulongArray:
                    unsafe
                    {
                        fixed (ulong* ulongPtr = ulongArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(ulong) * outputLength)), (IntPtr)((void*)ulongPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                case long[] longArray:
                    unsafe
                    {
                        fixed (long* longPtr = longArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueReadBuffer(this.Handle, memoryObject.Handle, 1, UIntPtr.Zero, new UIntPtr((uint)(sizeof(long) * outputLength)), (IntPtr)((void*)longPtr), 0, null, out IntPtr zero);

                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                        break;
                    }
                default:
                    throw new NotSupportedException("Non-blittable types are not supported.");
            }
        }

#endif

        /// <summary>
        /// Enqueues a n-dimensional kernel to the command queue, which is executed asynchronously.
        /// </summary>
        /// <param name="kernel">The kernel that is to be enqueued.</param>
        /// <param name="workDimension">The dimensionality of the work.</param>
        /// <param name="workUnitsPerKernel">The number of work units per kernel.</param>
        /// <exception cref="OpenClException">If the kernel could not be enqueued, then an <see cref="OpenClException"/> is thrown.</exception>
        public Task EnqueueNDRangeKernelAsync(Kernel kernel, int workDimension, int workUnitsPerKernel)
        {
            // Creates a new task completion source, which is used to signal when the command has completed
            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            // Enqueues the kernel
            IntPtr waitEventPointer;
            Result result = EnqueuedCommandsNativeApi.EnqueueNDRangeKernel(this.Handle, kernel.Handle, (uint)workDimension, null, new IntPtr[] { new IntPtr(workUnitsPerKernel)}, null, 0, null, out waitEventPointer);

            // Checks if the kernel was enqueued successfully, if not, then an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The kernel could not be enqueued.", result);

            // Subscribes to the completed event of the wait event that was returned, when the command finishes, the task completion source is resolved
            AwaitableEvent awaitableEvent = new AwaitableEvent(waitEventPointer);
            awaitableEvent.OnCompleted += (sender, e) =>
            {
                try
                {
                    if (awaitableEvent.CommandExecutionStatus == CommandExecutionStatus.Error)
                        taskCompletionSource.TrySetException(new OpenClException($"The command completed with the error code {awaitableEvent.CommandExecutionStatusCode}."));
                    else
                        taskCompletionSource.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.TrySetException(exception);
                }
                finally
                {
                    awaitableEvent.Dispose();
                }
            };
            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Enqueues a n-dimensional kernel to the command queue.
        /// </summary>
        /// <param name="kernel">The kernel that is to be enqueued.</param>
        /// <param name="workDimension">The dimensionality of the work.</param>
        /// <param name="workUnitsPerKernel">The number of work units per kernel.</param>
        /// <exception cref="OpenClException">If the kernel could not be enqueued, then an <see cref="OpenClException"/> is thrown.</exception>
        public void EnqueueNDRangeKernel(Kernel kernel, int workDimension, int workUnitsPerKernel)
        {
            // Enqueues the kernel
            IntPtr waitEventPointer;
            Result result = EnqueuedCommandsNativeApi.EnqueueNDRangeKernel(this.Handle, kernel.Handle, (uint)workDimension, null, new IntPtr[] { new IntPtr(workUnitsPerKernel)}, null, 0, null, out waitEventPointer);

            // Checks if the kernel was enqueued successfully, if not, then an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The kernel could not be enqueued.", result);
        }

        /// <summary>
        /// Enqueues a n-dimensional kernel to the command queue.
        /// </summary>
        /// <param name="kernel">The kernel that is to be enqueued.</param>
        /// <param name="workDimension">The dimensionality of the work.</param>
        /// <param name="workUnitsPerKernel">The number of work units per kernel.</param>
        /// <exception cref="OpenClException">If the kernel could not be enqueued, then an <see cref="OpenClException"/> is thrown.</exception>
        public void EnqueueNDRangeKernel(Kernel kernel, int workDimension, int globalSize, int localSize, int offset = 0)
        {
            // Enqueues the kernel
            IntPtr waitEventPointer;
            Result result = EnqueuedCommandsNativeApi.EnqueueNDRangeKernel(this.Handle, kernel.Handle, (uint)workDimension, new IntPtr[] { new IntPtr(offset) }, new IntPtr[] { new IntPtr(globalSize) }, new IntPtr[] { new IntPtr(localSize) }, 0, null, out waitEventPointer);

            // Checks if the kernel was enqueued successfully, if not, then an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The kernel could not be enqueued.", result);
        }

        public void EnqueueWriteBuffer<T>(MemoryObject memoryObject, T[] buffer, int length)
        {
#if UNSAFE
            switch (buffer)
            {
                case long[] longArray:
                    unsafe
                    {
                        fixed (long* longPtr = longArray)
                        {
                            Result result = EnqueuedCommandsNativeApi.EnqueueWriteBuffer(this.Handle, memoryObject.Handle, 1, new UIntPtr(0), new UIntPtr((uint)(length * Marshal.SizeOf<T>())), (IntPtr)((void*)longPtr), 0, null, out IntPtr waitEventPointer);

                            // Checks if the read operation was queued successfuly, if not, an exception is thrown
                            if (result != Result.Success)
                                throw new OpenClException("The memory object could not be read.", result);
                        }
                    }
                    break;
                default:
                    byte[] tempBuffer = new byte[length * Marshal.SizeOf<T>()];
                    Buffer.BlockCopy(buffer, 0, tempBuffer, 0, tempBuffer.Length);

                    IntPtr bufferPtr = Marshal.AllocHGlobal(tempBuffer.Length);
                    try
                    {
                        Marshal.Copy(tempBuffer, 0, bufferPtr, tempBuffer.Length);

                        Result result = EnqueuedCommandsNativeApi.EnqueueWriteBuffer(this.Handle, memoryObject.Handle, 1, new UIntPtr(0), new UIntPtr((uint)tempBuffer.Length), bufferPtr, 0, null, out IntPtr waitEventPointer);

                        // Checks if the read operation was queued successfuly, if not, an exception is thrown
                        if (result != Result.Success)
                            throw new OpenClException("The memory object could not be read.", result);
                    }
                    finally { Marshal.FreeHGlobal(bufferPtr); }
                    break;
            }
#else
            byte[] tempBuffer = new byte[length * Marshal.SizeOf<T>()];
            Buffer.BlockCopy(buffer, 0, tempBuffer, 0, tempBuffer.Length);

            IntPtr bufferPtr = Marshal.AllocHGlobal(tempBuffer.Length);
            try
            {
                Marshal.Copy(tempBuffer, 0, bufferPtr, tempBuffer.Length);

                Result result = EnqueuedCommandsNativeApi.EnqueueWriteBuffer(this.Handle, memoryObject.Handle, 1, new UIntPtr(0), new UIntPtr((uint)tempBuffer.Length), bufferPtr, 0, null, out IntPtr waitEventPointer);

                // Checks if the read operation was queued successfuly, if not, an exception is thrown
                if (result != Result.Success)
                    throw new OpenClException("The memory object could not be read.", result);
            }
#endif
        }

        public void EnqueueFillBuffer(MemoryObject memoryObject, int size, IntPtr pattern)
        {
            Result result = EnqueuedCommandsNativeApi.EnqueueFillBuffer(this.Handle, memoryObject.Handle, pattern, new UIntPtr(4), UIntPtr.Zero, new UIntPtr((uint)size), 0, null, out IntPtr waitEventPointer);

            // Checks if the read operation was queued successfuly, if not, an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The memory object could not be read.", result);
        }

#endregion

#region Public Static Methods

        /// <summary>
        /// Creates a new command queue for the specified context and device.
        /// </summary>
        /// <param name="context">The context for which the command queue is to be created.</param>
        /// <param name="device">The devices for which the command queue is to be created.</param>
        /// <exception cref="OpenClException">If the command queue could not be created, then an <see cref="OpenClException"/> exception is thrown.</exception>
        /// <returns>Returns the created command queue.</returns>
        public static CommandQueue CreateCommandQueue(Context context, Device device)
        {
            // Creates the new command queue for the specified context and device
            Result result;
            IntPtr commandQueuePointer = CommandQueuesNativeApi.CreateCommandQueue(context.Handle, device.Handle, 0, out result);

            // Checks if the command queue creation was successful, if not, then an exception is thrown
            if (result != Result.Success)
                throw new OpenClException("The command queue could not be created.", result);

            // Creates the new command queue object from the pointer and returns it
            return new CommandQueue(commandQueuePointer);
        }

#endregion
        
#region IDisposable Implementation

        /// <summary>
        /// Disposes of the resources that have been acquired by the command queue.
        /// </summary>
        /// <param name="disposing">Determines whether managed object or managed and unmanaged resources should be disposed of.</param>
        protected override void Dispose(bool disposing)
        {
            // Checks if the command queue has already been disposed of, if not, then the command queue is disposed of
            if (!this.IsDisposed)
                CommandQueuesNativeApi.ReleaseCommandQueue(this.Handle);

            // Makes sure that the base class can execute its dispose logic
            base.Dispose(disposing);
        }

#endregion
    }
}