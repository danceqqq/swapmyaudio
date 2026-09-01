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

	[StructLayout(LayoutKind.Explicit)]
	internal struct PropVariant
	{
		[FieldOffset(0)] public ushort vt;
		[FieldOffset(8)] public IntPtr pointerValue;
	}

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	internal class MMDeviceEnumeratorComObject
	{
	}

	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

		[PreserveSig]
		int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

		[PreserveSig]
		int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

		[PreserveSig]
		int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
	}

	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387FC4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceCollection
	{
		[PreserveSig]
		int GetCount(out uint pcDevices);

		[PreserveSig]
		int Item(uint nDevice, out IMMDevice ppDevice);
	}

	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDevice
	{
		[PreserveSig]
		int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

		[PreserveSig]
		int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

		[PreserveSig]
		int GetState(out int pdwState);
	}

	[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IPropertyStore
	{
		[PreserveSig]
		int GetCount(out uint cProps);

		[PreserveSig]
		int GetAt(uint iProp, out PropertyKey pkey);

		[PreserveSig]
		int GetValue(ref PropertyKey key, out PropVariant pv);

		[PreserveSig]
		int SetValue(ref PropertyKey key, ref PropVariant pv);

		[PreserveSig]
		int Commit();
	}

	[Guid("7991EEC9-7E65-4D19-B973-D16CC314B4E5")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMNotificationClient
	{
		[PreserveSig]
		int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, int dwNewState);

		[PreserveSig]
		int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

		[PreserveSig]
		int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

		[PreserveSig]
		int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);

		[PreserveSig]
		int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
	}

	internal static class NativeMethods
	{
		internal const int DeviceStateActive = 0x1;
		internal const int StgmRead = 0;
		internal const int VtLpWStr = 31;
		internal const int VtBstr = 8;

		internal const int RoInitSingleThreaded = 0;
		internal const int RpcEChangedMode = unchecked((int)0x80010106);

		[DllImport("ole32.dll")]
		internal static extern int PropVariantClear(ref PropVariant pvar);

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
		internal delegate int SetPersistedDelegate(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		internal delegate int GetPersistedDelegate(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);
	}
}
