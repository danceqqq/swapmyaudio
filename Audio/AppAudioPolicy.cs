using System;
using System.Runtime.InteropServices;

namespace swapmyaudio.Audio
{
	internal sealed class AppAudioPolicy
	{
		private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
		private const string MmdevapiToken = @"\\?\SWD#MMDEVAPI#";
		private const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

		private static readonly Guid Win11Iid = new("ab3d4648-e242-459f-b02f-541c70306324");
		private static readonly Guid Win10Iid = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
		private static readonly Guid Win16299Iid = new("32aa8e18-6496-4e24-9f94-b800e7eccc45");
		private static readonly Guid ActivationFactoryIid = new("00000035-0000-0000-C000-000000000046");
		private static readonly ERole[] Roles = { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications };

		private readonly IntPtr _factory;
		private readonly NativeMethods.SetPersistedDelegate _set;
		private readonly NativeMethods.GetPersistedDelegate _get;
		private readonly NativeMethods.ClearPersistedDelegate _clear;

		internal static string LastError { get; private set; } = "";

		internal string LastSetError { get; private set; } = "";

		private AppAudioPolicy(IntPtr factory)
		{
			_factory = factory;
			_set = ComVtable.Fn<NativeMethods.SetPersistedDelegate>(factory, NativeMethods.PolicySetSlot);
			_get = ComVtable.Fn<NativeMethods.GetPersistedDelegate>(factory, NativeMethods.PolicyGetSlot);
			_clear = ComVtable.Fn<NativeMethods.ClearPersistedDelegate>(factory, NativeMethods.PolicyClearSlot);
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
				    TryActivate(className, Win16299Iid, out factory) ||
				    TryFromInspectable(className, out factory)) {
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
			LastSetError = "";
			uint pid = (uint)Environment.ProcessId;
			if (string.IsNullOrEmpty(deviceId))
				return ClearAll(pid);

			IntPtr hstring = IntPtr.Zero;
			try {
				string packed = PackDeviceId(deviceId);
				if (NativeMethods.WindowsCreateString(packed, (uint)packed.Length, out hstring) < 0 || hstring == IntPtr.Zero) {
					LastSetError = "WindowsCreateString packed id failed";
					return false;
				}

				return Apply(pid, hstring);
			}
			catch (Exception e) {
				LastSetError = e.GetType().Name + ": " + e.Message;
				return false;
			}
			finally {
				if (hstring != IntPtr.Zero)
					NativeMethods.WindowsDeleteString(hstring);
			}
		}

		private bool ClearAll(uint pid)
		{
			bool cleared = false;
			try {
				int hr = _clear(_factory);
				cleared = hr >= 0;
				if (!cleared)
					LastSetError = "ClearAll 0x" + hr.ToString("X8");
			}
			catch (Exception e) {
				LastSetError = "ClearAll " + e.GetType().Name + ": " + e.Message;
			}

			bool zeroed = Apply(pid, IntPtr.Zero);
			return cleared || zeroed;
		}

		private bool Apply(uint pid, IntPtr deviceId)
		{
			bool ok = false;
			string errors = "";
			foreach (ERole role in Roles) {
				int hr = Set(pid, role, deviceId);
				if (hr >= 0)
					ok = true;
				else if (hr != NativeMethods.ProcessNoAudio)
					errors += ((errors.Length == 0) ? "" : ", ") + role + "=0x" + hr.ToString("X8");
				else if (string.IsNullOrEmpty(errors))
					errors = "PROCESS_NO_AUDIO 0x80070057";
			}

			if (!ok && !string.IsNullOrEmpty(errors))
				LastSetError = errors;
			else if (ok)
				LastSetError = "";

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

		private int Set(uint pid, ERole role, IntPtr deviceId)
		{
			try {
				return _set(_factory, pid, (int)EDataFlow.eRender, (int)role, deviceId);
			}
			catch {
				return unchecked((int)0x80004005);
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
			int hr = NativeMethods.RoInitialize(NativeMethods.RoInitMultiThreaded);
			if (hr < 0 && hr != NativeMethods.RpcEChangedMode)
				NativeMethods.RoInitialize(NativeMethods.RoInitSingleThreaded);
		}

		private static bool TryActivate(IntPtr className, Guid iid, out IntPtr factory)
		{
			factory = IntPtr.Zero;
			Guid g = iid;
			int hr = NativeMethods.RoGetActivationFactory(className, ref g, out factory);
			if (hr >= 0 && factory != IntPtr.Zero)
				return true;

			LastError = "RoGetActivationFactory 0x" + hr.ToString("X8");
			factory = IntPtr.Zero;
			return false;
		}

		private static bool TryFromInspectable(IntPtr className, out IntPtr factory)
		{
			factory = IntPtr.Zero;
			IntPtr activation = IntPtr.Zero;
			try {
				int hr = NativeMethods.AudioSesGetActivationFactory(className, out activation);
				if (hr < 0 || activation == IntPtr.Zero) {
					Guid activationIid = ActivationFactoryIid;
					hr = NativeMethods.RoGetActivationFactory(className, ref activationIid, out activation);
				}

				if (hr < 0 || activation == IntPtr.Zero) {
					LastError = "DllGetActivationFactory 0x" + hr.ToString("X8");
					activation = IntPtr.Zero;
					return false;
				}

				if (TryQueryKnown(activation, out factory) || TryQueryLastIid(activation, out factory))
					return true;

				LastError = "AudioPolicy QueryInterface failed";
				return false;
			}
			catch (Exception e) {
				LastError = "AudioSes: " + e.Message;
				return false;
			}
			finally {
				if (activation != IntPtr.Zero && activation != factory)
					ComVtable.Release(activation);
			}
		}

		private static bool TryQueryKnown(IntPtr source, out IntPtr factory)
		{
			if (ComVtable.QueryInterface(source, Win11Iid, out factory) >= 0 && factory != IntPtr.Zero)
				return true;
			if (ComVtable.QueryInterface(source, Win10Iid, out factory) >= 0 && factory != IntPtr.Zero)
				return true;
			return ComVtable.QueryInterface(source, Win16299Iid, out factory) >= 0 && factory != IntPtr.Zero;
		}

		private static bool TryQueryLastIid(IntPtr source, out IntPtr factory)
		{
			factory = IntPtr.Zero;
			int hr = ComVtable.Fn<NativeMethods.GetIidsDelegate>(source, NativeMethods.InspectableGetIidsSlot)(source, out uint count, out IntPtr iids);
			if (hr < 0 || iids == IntPtr.Zero || count == 0)
				return false;

			try {
				Guid last = Marshal.PtrToStructure<Guid>(iids + 16 * ((int)count - 1));
				return ComVtable.QueryInterface(source, last, out factory) >= 0 && factory != IntPtr.Zero;
			}
			finally {
				Marshal.FreeCoTaskMem(iids);
			}
		}
	}
}
