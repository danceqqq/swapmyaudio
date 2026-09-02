using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace swapmyaudio.Audio
{
	internal sealed class WasapiDeviceEnumerator : IDisposable
	{
		private readonly object _gate = new();
		private IntPtr _enumerator;
		private List<PlaybackDevice> _devices = new();
		private int _dirty = 1;

		internal WasapiDeviceEnumerator()
		{
			_enumerator = CreateEnumerator(out string error);
			LastError = error;
			Refresh();
		}

		internal string LastError { get; private set; } = "";

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
			IntPtr enumerator = _enumerator;
			if (enumerator == IntPtr.Zero) {
				if (string.IsNullOrEmpty(LastError))
					LastError = "MMDeviceEnumerator missing";
				return;
			}

			try {
				Collect(enumerator, NativeMethods.DeviceStateActive, next);
				if (next.Count == 0)
					Collect(enumerator, NativeMethods.DeviceStateMaskAll, next);
				if (next.Count == 0)
					TryAddDefault(enumerator, next);

				LastError = next.Count == 0 ? "EnumAudioEndpoints returned 0 devices" : "";
			}
			catch (Exception e) {
				LastError = e.GetType().Name + ": " + e.Message;
				return;
			}

			next.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
			lock (_gate)
				_devices = next;
		}

		public void Dispose()
		{
			IntPtr enumerator = _enumerator;
			_enumerator = IntPtr.Zero;
			ComVtable.Release(enumerator);
		}

		private static IntPtr CreateEnumerator(out string error)
		{
			error = "";
			Guid clsid = NativeMethods.MmDeviceEnumeratorClsid;
			Guid iid = NativeMethods.ImmDeviceEnumeratorIid;
			int hr = NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, NativeMethods.ClsCtxAll, ref iid, out IntPtr enumerator);
			if (hr < 0 || enumerator == IntPtr.Zero) {
				error = "CoCreateInstance 0x" + hr.ToString("X8");
				return IntPtr.Zero;
			}

			return enumerator;
		}

		private static void Collect(IntPtr enumerator, int stateMask, List<PlaybackDevice> next)
		{
			int hr = ComVtable.Fn<NativeMethods.EnumAudioEndpointsDelegate>(enumerator, NativeMethods.EnumeratorEnumSlot)(
				enumerator, (int)EDataFlow.eRender, stateMask, out IntPtr collection);
			if (hr < 0 || collection == IntPtr.Zero)
				return;

			try {
				hr = ComVtable.Fn<NativeMethods.GetCountDelegate>(collection, NativeMethods.CollectionGetCountSlot)(collection, out uint count);
				if (hr < 0)
					return;

				for (uint i = 0; i < count; i++) {
					hr = ComVtable.Fn<NativeMethods.ItemDelegate>(collection, NativeMethods.CollectionItemSlot)(collection, i, out IntPtr device);
					if (hr < 0 || device == IntPtr.Zero)
						continue;

					try {
						AddDevice(device, next);
					}
					finally {
						ComVtable.Release(device);
					}
				}
			}
			finally {
				ComVtable.Release(collection);
			}
		}

		private static void TryAddDefault(IntPtr enumerator, List<PlaybackDevice> next)
		{
			int hr = ComVtable.Fn<NativeMethods.GetDefaultEndpointDelegate>(enumerator, NativeMethods.EnumeratorDefaultSlot)(
				enumerator, (int)EDataFlow.eRender, (int)ERole.eConsole, out IntPtr device);
			if (hr < 0 || device == IntPtr.Zero)
				return;

			try {
				AddDevice(device, next);
			}
			finally {
				ComVtable.Release(device);
			}
		}

		private static void AddDevice(IntPtr device, List<PlaybackDevice> next)
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

		private static string ReadId(IntPtr device)
		{
			int hr = ComVtable.Fn<NativeMethods.GetIdDelegate>(device, NativeMethods.DeviceGetIdSlot)(device, out IntPtr ptr);
			if (hr < 0 || ptr == IntPtr.Zero)
				return "";

			try {
				return Marshal.PtrToStringUni(ptr) ?? "";
			}
			finally {
				Marshal.FreeCoTaskMem(ptr);
			}
		}

		private static string ReadFriendlyName(IntPtr device)
		{
			int hr = ComVtable.Fn<NativeMethods.OpenPropertyStoreDelegate>(device, NativeMethods.DeviceOpenStoreSlot)(
				device, NativeMethods.StgmRead, out IntPtr store);
			if (hr < 0 || store == IntPtr.Zero)
				return "";

			try {
				PropertyKey key = PropertyKey.DeviceFriendlyName;
				hr = ComVtable.Fn<NativeMethods.GetValueDelegate>(store, NativeMethods.PropertyStoreGetValueSlot)(store, ref key, out PropVariant value);
				if (hr < 0)
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
				ComVtable.Release(store);
			}

			return "";
		}
	}
}
