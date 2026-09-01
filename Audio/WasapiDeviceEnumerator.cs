using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace swapmyaudio.Audio
{
	internal sealed class WasapiDeviceEnumerator : IDisposable
	{
		private readonly object _gate = new();
		private readonly DeviceNotificationClient _client;
		private IMMDeviceEnumerator _enumerator;
		private List<PlaybackDevice> _devices = new();
		private int _dirty = 1;
		private bool _listening;

		internal WasapiDeviceEnumerator()
		{
			_client = new DeviceNotificationClient(this);
			_enumerator = CreateEnumerator();
			try {
				if (_enumerator != null && _enumerator.RegisterEndpointNotificationCallback(_client) >= 0)
					_listening = true;
			}
			catch {
				_listening = false;
			}

			Refresh();
		}

		private static IMMDeviceEnumerator CreateEnumerator()
		{
			try {
				return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
			}
			catch {
			}

			Guid clsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
			Guid iid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
			if (NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, 23, ref iid, out IntPtr unknown) < 0 || unknown == IntPtr.Zero)
				return null;

			try {
				if (OperatingSystem.IsWindows())
					return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(unknown);
				return null;
			}
			catch {
				return null;
			}
			finally {
				Marshal.Release(unknown);
			}
		}

		internal IReadOnlyList<PlaybackDevice> Devices
		{
			get
			{
				lock (_gate)
					return _devices;
			}
		}

		internal void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

		internal bool IsDirty => Volatile.Read(ref _dirty) != 0;

		internal bool ConsumeDirty() => Interlocked.Exchange(ref _dirty, 0) != 0;

		internal void Refresh()
		{
			var next = new List<PlaybackDevice>();
			IMMDeviceEnumerator enumerator = _enumerator;
			if (enumerator == null)
				return;

			try {
				Collect(enumerator, NativeMethods.DeviceStateActive, next);
				if (next.Count == 0)
					Collect(enumerator, NativeMethods.DeviceStateMaskAll, next);

				if (next.Count == 0)
					TryAddDefault(enumerator, next);
			}
			catch {
				return;
			}

			next.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
			lock (_gate)
				_devices = next;
		}

		private static void Collect(IMMDeviceEnumerator enumerator, int stateMask, List<PlaybackDevice> next)
		{
			if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, stateMask, out IMMDeviceCollection collection) < 0 || collection == null)
				return;

			try {
				if (collection.GetCount(out uint count) < 0)
					return;

				for (uint i = 0; i < count; i++) {
					if (collection.Item(i, out IMMDevice device) < 0 || device == null)
						continue;

					try {
						AddDevice(device, next);
					}
					finally {
						Release(device);
					}
				}
			}
			finally {
				Release(collection);
			}
		}

		private static void TryAddDefault(IMMDeviceEnumerator enumerator, List<PlaybackDevice> next)
		{
			if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out IMMDevice device) < 0 || device == null)
				return;

			try {
				AddDevice(device, next);
			}
			finally {
				Release(device);
			}
		}

		private static void AddDevice(IMMDevice device, List<PlaybackDevice> next)
		{
			string id = ReadId(device);
			if (string.IsNullOrEmpty(id))
				return;

			for (int i = 0; i < next.Count; i++) {
				if (string.Equals(next[i].Id, id, StringComparison.OrdinalIgnoreCase))
					return;
			}

			next.Add(new PlaybackDevice(id, ReadFriendlyName(device)));
		}

		private static string ReadId(IMMDevice device)
		{
			if (device.GetId(out IntPtr ptr) < 0 || ptr == IntPtr.Zero)
				return "";

			try {
				return Marshal.PtrToStringUni(ptr) ?? "";
			}
			finally {
				Marshal.FreeCoTaskMem(ptr);
			}
		}

		public void Dispose()
		{
			IMMDeviceEnumerator enumerator = _enumerator;
			if (enumerator == null)
				return;

			_enumerator = null;
			try {
				if (_listening)
					enumerator.UnregisterEndpointNotificationCallback(_client);
			}
			catch {
			}

			_listening = false;
			try {
				Release(enumerator);
			}
			catch {
			}
		}

		private static string ReadFriendlyName(IMMDevice device)
		{
			if (device.OpenPropertyStore(NativeMethods.StgmRead, out IPropertyStore store) < 0 || store == null)
				return "";

			try {
				PropertyKey key = PropertyKey.DeviceFriendlyName;
				if (store.GetValue(ref key, out PropVariant value) < 0)
					return "";

				try {
					if (value.vt is NativeMethods.VtLpWStr or NativeMethods.VtBstr)
						return Marshal.PtrToStringUni(value.pointerValue) ?? "";
				}
				finally {
					NativeMethods.PropVariantClear(ref value);
				}
			}
			catch {
			}
			finally {
				Release(store);
			}

			return "";
		}

		private static void Release(object comObject)
		{
			if (OperatingSystem.IsWindows())
				Marshal.ReleaseComObject(comObject);
		}

		[ComVisible(true)]
		private sealed class DeviceNotificationClient : IMMNotificationClient
		{
			private readonly WasapiDeviceEnumerator _owner;

			internal DeviceNotificationClient(WasapiDeviceEnumerator owner)
			{
				_owner = owner;
			}

			public int OnDeviceStateChanged(string pwstrDeviceId, int dwNewState)
			{
				_owner.MarkDirty();
				return 0;
			}

			public int OnDeviceAdded(string pwstrDeviceId)
			{
				_owner.MarkDirty();
				return 0;
			}

			public int OnDeviceRemoved(string pwstrDeviceId)
			{
				_owner.MarkDirty();
				return 0;
			}

			public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
			{
				_owner.MarkDirty();
				return 0;
			}

			public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
			{
				if (key.fmtid == PropertyKey.DeviceFriendlyName.fmtid && key.pid == PropertyKey.DeviceFriendlyName.pid)
					_owner.MarkDirty();
				return 0;
			}
		}
	}
}
