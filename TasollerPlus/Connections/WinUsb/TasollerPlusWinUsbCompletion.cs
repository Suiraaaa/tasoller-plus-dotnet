using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus.Connections.WinUsb
{
    internal sealed class TasollerPlusWinUsbCompletion : IDisposable
    {
        private const string KernelLibrary = "Kernel32.dll";
        private static readonly TasollerPlusWinUsbWaitCallback CompletionCallback = OnWaitCompleted;

        private readonly EventWaitHandle _signal;
        private readonly TaskCompletionSource<bool> _completion;
        private GCHandle _callbackContext;
        private IntPtr _wait;

        public TasollerPlusWinUsbCompletion()
        {
            _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _signal = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);

            try
            {
                _callbackContext = GCHandle.Alloc(_completion);
                _wait = CreateThreadpoolWait(CompletionCallback, GCHandle.ToIntPtr(_callbackContext), IntPtr.Zero);
                if (_wait == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new IOException("Could not register WinUSB transfer completion.", new Win32Exception(error));
                }

                SetThreadpoolWait(_wait, EventHandle, IntPtr.Zero);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IntPtr EventHandle => _signal.SafeWaitHandle.DangerousGetHandle();

        public Task Completed => _completion.Task;

        public void Dispose()
        {
            if (_wait != IntPtr.Zero)
            {
                // Disarm the wait and drain any queued callback before releasing its context.
                SetThreadpoolWait(_wait, IntPtr.Zero, IntPtr.Zero);
                WaitForThreadpoolWaitCallbacks(_wait, cancelPendingCallbacks: true);
                CloseThreadpoolWait(_wait);
                _wait = IntPtr.Zero;
            }

            if (_callbackContext.IsAllocated)
            {
                _callbackContext.Free();
            }

            _signal.Dispose();
        }

        private static void OnWaitCompleted(IntPtr instance, IntPtr context, IntPtr wait, uint waitResult)
        {
            var completion = (TaskCompletionSource<bool>)GCHandle.FromIntPtr(context).Target!;
            completion.TrySetResult(true);
        }

        [DllImport(KernelLibrary, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateThreadpoolWait(TasollerPlusWinUsbWaitCallback callback, IntPtr context, IntPtr callbackEnvironment);

        [DllImport(KernelLibrary, ExactSpelling = true)]
        private static extern void SetThreadpoolWait(IntPtr wait, IntPtr handle, IntPtr timeout);

        [DllImport(KernelLibrary, ExactSpelling = true)]
        private static extern void WaitForThreadpoolWaitCallbacks(IntPtr wait, [MarshalAs(UnmanagedType.Bool)] bool cancelPendingCallbacks);

        [DllImport(KernelLibrary, ExactSpelling = true)]
        private static extern void CloseThreadpoolWait(IntPtr wait);
    }
}
