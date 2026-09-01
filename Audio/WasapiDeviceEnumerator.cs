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
			try {
				_enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
				if (_enumerator.RegisterEndpointNotificationCallback(_client) >= 0)
					_listening = true;
			}
			catch {
				_enumerator = null;
			}

			Refresh();
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
				if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, NativeMethods.DeviceStateActive, out IMMDeviceCollection collection) < 0 || collection == null)
					return;

				try {
					if (collection.GetCount(out uint count) < 0)
						return;

					for (uint i = 0; i < count; i++) {
						if (collection.Item(i, out IMMDevice device) < 0 || device == null)
							continue;

						try {
							if (device.GetId(out string id) < 0 || string.IsNullOrEmpty(id))
								continue;

							string name = ReadFriendlyName(device);
							next.Add(new PlaybackDevice(id, name));
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
			catch {
				return;
			}

			next.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
			lock (_gate)
				_devices = next;
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
