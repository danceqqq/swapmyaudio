using System;
using System.Runtime.InteropServices;

namespace swapmyaudio.Audio
{
	internal enum EDataFlow
	{
		eRender = 0,
		eCapture = 1,
		eAll = 2
	}

	internal enum ERole
	{
		eConsole = 0,
		eMultimedia = 1,
		eCommunications = 2
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PropertyKey
	{
		public Guid fmtid;
		public uint pid;

		public static readonly PropertyKey DeviceFriendlyName = new() {
			fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
			pid = 14
		};
	}

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	internal struct PropVariant
	{
		[FieldOffset(0)] public ushort vt;
		[FieldOffset(8)] public IntPtr pointerValue;
	}

	internal static class NativeMethods
	{
		internal static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
		internal static readonly Guid ImmDeviceEnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
		internal static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
		internal static readonly Guid AudioRenderClientIid = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

		internal const int DeviceStateActive = 0x1;
		internal const int DeviceStateMaskAll = 0xF;
		internal const int StgmRead = 0;
		internal const int VtLpWStr = 31;
		internal const int VtBstr = 8;
		internal const int ClsCtxAll = 23;

		internal const int RoInitSingleThreaded = 0;
		internal const int RoInitMultiThreaded = 1;
		internal const int RpcEChangedMode = unchecked((int)0x80010106);
		internal const int ProcessNoAudio = unchecked((int)0x80070057);

		internal const int IUnknownReleaseSlot = 2;
		internal const int EnumeratorEnumSlot = 3;
		internal const int EnumeratorDefaultSlot = 4;
		internal const int EnumeratorGetDeviceSlot = 5;
		internal const int CollectionGetCountSlot = 3;
		internal const int CollectionItemSlot = 4;
		internal const int DeviceActivateSlot = 3;
		internal const int DeviceOpenStoreSlot = 4;
		internal const int DeviceGetIdSlot = 5;
		internal const int AudioClientInitializeSlot = 3;
		internal const int AudioClientGetBufferSizeSlot = 4;
		internal const int AudioClientGetCurrentPaddingSlot = 6;
		internal const int AudioClientGetMixFormatSlot = 8;
		internal const int AudioClientStartSlot = 10;
		internal const int AudioClientStopSlot = 11;
		internal const int AudioClientGetServiceSlot = 14;
		internal const int RenderClientGetBufferSlot = 3;
		internal const int RenderClientReleaseBufferSlot = 4;
		internal const uint AudClntStreamflagsAutoconvertPcm = 0x80000000;
		internal const uint AudClntStreamflagsSrcDefaultQuality = 0x08000000;
		internal const ushort WaveFormatIeeeFloat = 3;
		internal const ushort WaveFormatPcm = 1;
		internal const ushort WaveFormatExtensibleTag = 0xFFFE;
		internal const int PropertyStoreGetValueSlot = 5;
		internal const int InspectableGetIidsSlot = 3;
		internal const int PolicySetSlot = 25;
		internal const int PolicyGetSlot = 26;
		internal const int PolicyClearSlot = 27;

		[DllImport("ole32.dll")]
		internal static extern int PropVariantClear(ref PropVariant pvar);

		[DllImport("ole32.dll")]
		internal static extern void CoTaskMemFree(IntPtr ptr);

		[DllImport("ole32.dll")]
		internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

		[DllImport("combase.dll")]
		internal static extern int RoInitialize(int initType);

		[DllImport("combase.dll")]
		internal static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

		[DllImport("combase.dll")]
		internal static extern int WindowsCreateString(
			[MarshalAs(UnmanagedType.LPWStr)] string src,
			uint length,
			out IntPtr hstring);

		[DllImport("combase.dll")]
		internal static extern int WindowsDeleteString(IntPtr hstring);

		[DllImport("combase.dll")]
		internal static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

		[DllImport("AudioSes.dll", EntryPoint = "DllGetActivationFactory")]
		internal static extern int AudioSesGetActivationFactory(IntPtr activatableClassId, out IntPtr factory);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int QueryInterfaceDelegate(IntPtr self, ref Guid iid, out IntPtr ppv);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate uint ReleaseDelegate(IntPtr self);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int EnumAudioEndpointsDelegate(IntPtr self, int flow, int stateMask, out IntPtr collection);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetDefaultEndpointDelegate(IntPtr self, int flow, int role, out IntPtr device);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetDeviceDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int ActivateDelegate(IntPtr self, ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr result);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int AudioClientInitializeDelegate(IntPtr self, int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetMixFormatDelegate(IntPtr self, out IntPtr format);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetBufferSizeDelegate(IntPtr self, out uint frames);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetCurrentPaddingDelegate(IntPtr self, out uint padding);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int AudioClientSimpleDelegate(IntPtr self);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetServiceDelegate(IntPtr self, ref Guid iid, out IntPtr service);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int RenderGetBufferDelegate(IntPtr self, uint frames, out IntPtr data);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int RenderReleaseBufferDelegate(IntPtr self, uint frames, uint flags);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetCountDelegate(IntPtr self, out uint count);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int ItemDelegate(IntPtr self, uint index, out IntPtr device);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int OpenPropertyStoreDelegate(IntPtr self, int access, out IntPtr store);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetIdDelegate(IntPtr self, out IntPtr id);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetValueDelegate(IntPtr self, ref PropertyKey key, out PropVariant value);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetIidsDelegate(IntPtr self, out uint count, out IntPtr iids);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int SetPersistedDelegate(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetPersistedDelegate(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int ClearPersistedDelegate(IntPtr self);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct WaveFormatEx
	{
		public ushort wFormatTag;
		public ushort nChannels;
		public uint nSamplesPerSec;
		public uint nAvgBytesPerSec;
		public ushort nBlockAlign;
		public ushort wBitsPerSample;
		public ushort cbSize;

		internal static WaveFormatEx IeeeFloat(ushort channels, uint sampleRate)
		{
			ushort block = (ushort)(channels * 4);
			return new WaveFormatEx {
				wFormatTag = NativeMethods.WaveFormatIeeeFloat,
				nChannels = channels,
				nSamplesPerSec = sampleRate,
				nAvgBytesPerSec = sampleRate * block,
				nBlockAlign = block,
				wBitsPerSample = 32,
				cbSize = 0
			};
		}
	}

	internal static class NativeMem
	{
		private const uint MemCommit = 0x1000;
		private const uint PageNoAccess = 0x01;
		private const uint PageGuard = 0x100;
		private const uint ReadableProtect = 0xEE;

		[StructLayout(LayoutKind.Sequential)]
		private struct MemoryBasicInformation
		{
			public IntPtr BaseAddress;
			public IntPtr AllocationBase;
			public uint AllocationProtect;
			public ushort PartitionId;
			public UIntPtr RegionSize;
			public uint State;
			public uint Protect;
			public uint Type;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr VirtualQuery(IntPtr address, out MemoryBasicInformation buffer, IntPtr length);

		internal static bool Readable(IntPtr address, int bytes)
		{
			if (address == IntPtr.Zero || bytes <= 0)
				return false;

			IntPtr queried = VirtualQuery(address, out MemoryBasicInformation info, (IntPtr)Marshal.SizeOf<MemoryBasicInformation>());
			if (queried == IntPtr.Zero)
				return false;
			if (info.State != MemCommit)
				return false;
			if ((info.Protect & PageNoAccess) != 0 || (info.Protect & PageGuard) != 0)
				return false;
			if ((info.Protect & ReadableProtect) == 0)
				return false;

			ulong start = (ulong)address.ToInt64();
			ulong end = start + (uint)bytes;
			ulong regionStart = (ulong)info.BaseAddress.ToInt64();
			ulong regionEnd = regionStart + info.RegionSize.ToUInt64();
			return start >= regionStart && end <= regionEnd;
		}
	}

	internal static class ComVtable
	{
		internal static IntPtr Slot(IntPtr obj, int index)
		{
			return Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), IntPtr.Size * index);
		}

		internal static T Fn<T>(IntPtr obj, int index) where T : Delegate
		{
			return Marshal.GetDelegateForFunctionPointer<T>(Slot(obj, index));
		}

		internal static uint Release(IntPtr obj)
		{
			if (obj == IntPtr.Zero)
				return 0;

			return Fn<NativeMethods.ReleaseDelegate>(obj, NativeMethods.IUnknownReleaseSlot)(obj);
		}

		internal static int QueryInterface(IntPtr obj, Guid iid, out IntPtr result)
		{
			Guid g = iid;
			return Fn<NativeMethods.QueryInterfaceDelegate>(obj, 0)(obj, ref g, out result);
		}
	}
}
