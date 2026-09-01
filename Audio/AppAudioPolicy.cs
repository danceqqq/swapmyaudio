using System;
using System.Runtime.InteropServices;

namespace swapmyaudio.Audio
{
	internal sealed class AppAudioPolicy
	{
		private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
		private const string MmdevapiToken = @"\\?\SWD#MMDEVAPI#";
		private const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
		private const int SetSlot = 25;
		private const int GetSlot = 26;

		private static readonly Guid Win11Iid = new("ab3d4648-e242-459f-b02f-541c70306324");
		private static readonly Guid Win10Iid = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
		private static readonly Guid ActivationFactoryIid = new("00000035-0000-0000-C000-000000000046");
		private static readonly ERole[] Roles = { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications };

		private readonly IntPtr _factory;
		private readonly NativeMethods.SetPersistedDelegate _set;
		private readonly NativeMethods.GetPersistedDelegate _get;

		internal static string LastError { get; private set; } = "";

		private AppAudioPolicy(IntPtr factory)
		{
			_factory = factory;
			IntPtr vtable = Marshal.ReadIntPtr(factory);
			int size = IntPtr.Size;
			_set = Marshal.GetDelegateForFunctionPointer<NativeMethods.SetPersistedDelegate>(Marshal.ReadIntPtr(vtable, size * SetSlot));
			_get = Marshal.GetDelegateForFunctionPointer<NativeMethods.GetPersistedDelegate>(Marshal.ReadIntPtr(vtable, size * GetSlot));
		}

		internal static bool TryCreate(out AppAudioPolicy policy)
		{
			policy = null;
			LastError = "";
			if (!OperatingSystem.IsWindows())
				return false;

			EnsureWinRt();
			if (NativeMethods.WindowsCreateString(ClassName, (uint)ClassName.Length, out IntPtr className) < 0 || className == IntPtr.Zero) {
				LastError = "WindowsCreateString failed";
				return false;
			}

			try {
				if (TryActivate(className, Win11Iid, out IntPtr factory) ||
				    TryActivate(className, Win10Iid, out factory) ||
				    (TryActivate(className, ActivationFactoryIid, out factory) && TryQueryKnown(factory, out factory)) ||
				    TryAudioSes(className, out factory)) {
					policy = new AppAudioPolicy(factory);
					return true;
				}

				if (string.IsNullOrEmpty(LastError))
					LastError = "RoGetActivationFactory failed";
				return false;
			}
			catch (Exception e) {
				LastError = e.GetType().Name + ": " + e.Message;
				return false;
			}
			finally {
				NativeMethods.WindowsDeleteString(className);
			}
		}

		internal string GetPersistedRenderEndpoint()
		{
			uint pid = (uint)Environment.ProcessId;
			foreach (ERole role in Roles) {
				string unpacked = UnpackDeviceId(Get(pid, role));
				if (!string.IsNullOrEmpty(unpacked))
					return unpacked;
			}

			return "";
		}

		internal bool SetPersistedRenderEndpoint(string deviceId)
		{
			uint pid = (uint)Environment.ProcessId;
			IntPtr hstring = IntPtr.Zero;
			bool created = false;
			try {
				if (!string.IsNullOrEmpty(deviceId)) {
					string packed = PackDeviceId(deviceId);
					if (NativeMethods.WindowsCreateString(packed, (uint)packed.Length, out hstring) < 0)
						return false;
					created = true;
				}

				bool ok = Apply(pid, hstring);
				if (ok || created)
					return ok;

				if (NativeMethods.WindowsCreateString("", 0, out hstring) < 0)
					return false;
				created = true;
				return Apply(pid, hstring);
			}
			catch {
				return false;
			}
			finally {
				if (created && hstring != IntPtr.Zero)
					NativeMethods.WindowsDeleteString(hstring);
			}
		}

		private bool Apply(uint pid, IntPtr deviceId)
		{
			bool ok = false;
			foreach (ERole role in Roles) {
				if (Set(pid, role, deviceId))
					ok = true;
			}

			return ok;
		}

		private string Get(uint pid, ERole role)
		{
			try {
				int hr = _get(_factory, pid, (int)EDataFlow.eRender, (int)role, out IntPtr hstring);
				if (hr < 0 || hstring == IntPtr.Zero)
					return "";

				try {
					IntPtr buffer = NativeMethods.WindowsGetStringRawBuffer(hstring, out uint length);
					return buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(buffer, (int)length) ?? "";
				}
				finally {
					NativeMethods.WindowsDeleteString(hstring);
				}
			}
			catch {
				return "";
			}
		}

		private bool Set(uint pid, ERole role, IntPtr deviceId)
		{
			try {
				return _set(_factory, pid, (int)EDataFlow.eRender, (int)role, deviceId) >= 0;
			}
			catch {
				return false;
			}
		}

		internal static string PackDeviceId(string deviceId)
		{
			if (string.IsNullOrEmpty(deviceId) || deviceId.StartsWith(MmdevapiToken, StringComparison.OrdinalIgnoreCase))
				return deviceId ?? "";

			return MmdevapiToken + deviceId + RenderInterface;
		}

		internal static string UnpackDeviceId(string deviceId)
		{
			if (string.IsNullOrEmpty(deviceId))
				return "";

			if (deviceId.StartsWith(MmdevapiToken, StringComparison.OrdinalIgnoreCase))
				deviceId = deviceId[MmdevapiToken.Length..];

			if (deviceId.EndsWith(RenderInterface, StringComparison.OrdinalIgnoreCase))
				deviceId = deviceId[..^RenderInterface.Length];

			return deviceId;
		}

		private static void EnsureWinRt()
		{
			int hr = NativeMethods.RoInitialize(NativeMethods.RoInitSingleThreaded);
			if (hr < 0 && hr != NativeMethods.RpcEChangedMode)
				NativeMethods.RoInitialize(1);
		}

		private static bool TryActivate(IntPtr className, Guid iid, out IntPtr factory)
		{
			factory = IntPtr.Zero;
			int hr = NativeMethods.RoGetActivationFactory(className, ref iid, out factory);
			if (hr >= 0 && factory != IntPtr.Zero)
				return true;

			LastError = "RoGetActivationFactory 0x" + hr.ToString("X8");
			factory = IntPtr.Zero;
			return false;
		}

		private static bool TryAudioSes(IntPtr className, out IntPtr factory)
		{
			factory = IntPtr.Zero;
			try {
				int hr = NativeMethods.AudioSesGetActivationFactory(className, out IntPtr activation);
				if (hr < 0 || activation == IntPtr.Zero) {
					LastError = "AudioSes DllGetActivationFactory 0x" + hr.ToString("X8");
					return false;
				}

				if (TryQueryKnown(activation, out factory))
					return true;

				LastError = "AudioSes QueryInterface failed";
				return false;
			}
			catch (Exception e) {
				LastError = "AudioSes: " + e.Message;
				return false;
			}
		}

		private static bool TryQueryKnown(IntPtr source, out IntPtr factory)
		{
			return TryQuery(source, Win11Iid, out factory) || TryQuery(source, Win10Iid, out factory);
		}

		private static bool TryQuery(IntPtr source, Guid iid, out IntPtr ppv)
		{
			ppv = IntPtr.Zero;
			IntPtr vtable = Marshal.ReadIntPtr(source);
			var qi = Marshal.GetDelegateForFunctionPointer<NativeMethods.QueryInterfaceDelegate>(Marshal.ReadIntPtr(vtable));
			return qi(source, ref iid, out ppv) >= 0 && ppv != IntPtr.Zero;
		}
	}
}
