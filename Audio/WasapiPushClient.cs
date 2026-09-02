using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace swapmyaudio.Audio
{
	internal sealed class WasapiPushClient : IDisposable
	{
		private IntPtr _client;
		private IntPtr _render;
		private uint _bufferFrames;
		private float[] _scratch;
		private readonly object _gate = new();

		internal string LastError { get; private set; } = "";

		internal static WasapiPushClient TryOpen(string deviceId, ushort channels, uint sampleRate, out string error)
		{
			error = "";
			var client = new WasapiPushClient();
			if (!client.Open(deviceId, channels, sampleRate)) {
				error = client.LastError;
				client.Dispose();
				return null;
			}

			return client;
		}

		internal bool Write(IntPtr source, int frames, int channels)
		{
			if (source == IntPtr.Zero || frames <= 0 || channels <= 0)
				return false;

			if (!Monitor.TryEnter(_gate, 0))
				return false;

			try {
				if (_client == IntPtr.Zero || _render == IntPtr.Zero || _scratch == null)
					return false;

				int hr = ComVtable.Fn<NativeMethods.GetCurrentPaddingDelegate>(_client, NativeMethods.AudioClientGetCurrentPaddingSlot)(_client, out uint padding);
				if (hr < 0)
					return false;

				uint available = _bufferFrames > padding ? _bufferFrames - padding : 0;
				uint write = (uint)frames;
				if (write > available)
					write = available;
				if (write == 0)
					return false;

				int floats = (int)write * channels;
				if (floats <= 0 || floats > _scratch.Length)
					return false;

				hr = ComVtable.Fn<NativeMethods.RenderGetBufferDelegate>(_render, NativeMethods.RenderClientGetBufferSlot)(_render, write, out IntPtr dest);
				if (hr < 0 || dest == IntPtr.Zero)
					return false;

				Marshal.Copy(source, _scratch, 0, floats);
				Marshal.Copy(_scratch, 0, dest, floats);
				ComVtable.Fn<NativeMethods.RenderReleaseBufferDelegate>(_render, NativeMethods.RenderClientReleaseBufferSlot)(_render, write, 0);
				return write == (uint)frames;
			}
			catch {
				return false;
			}
			finally {
				Monitor.Exit(_gate);
			}
		}

		public void Dispose()
		{
			lock (_gate) {
				if (_client != IntPtr.Zero) {
					try {
						ComVtable.Fn<NativeMethods.AudioClientSimpleDelegate>(_client, NativeMethods.AudioClientStopSlot)(_client);
					}
					catch {
					}
				}

				ComVtable.Release(_render);
				_render = IntPtr.Zero;
				ComVtable.Release(_client);
				_client = IntPtr.Zero;
				_scratch = null;
			}
		}

		private bool Open(string deviceId, ushort channels, uint sampleRate)
		{
			Guid clsid = NativeMethods.MmDeviceEnumeratorClsid;
			Guid iid = NativeMethods.ImmDeviceEnumeratorIid;
			int hr = NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, NativeMethods.ClsCtxAll, ref iid, out IntPtr enumerator);
			if (hr < 0 || enumerator == IntPtr.Zero) {
				LastError = "CoCreate 0x" + hr.ToString("X8");
				return false;
			}

			IntPtr device = IntPtr.Zero;
			try {
				hr = ComVtable.Fn<NativeMethods.GetDeviceDelegate>(enumerator, NativeMethods.EnumeratorGetDeviceSlot)(enumerator, deviceId, out device);
				if (hr < 0 || device == IntPtr.Zero) {
					LastError = "GetDevice 0x" + hr.ToString("X8");
					return false;
				}

				Guid clientIid = NativeMethods.AudioClientIid;
				hr = ComVtable.Fn<NativeMethods.ActivateDelegate>(device, NativeMethods.DeviceActivateSlot)(device, ref clientIid, NativeMethods.ClsCtxAll, IntPtr.Zero, out _client);
				if (hr < 0 || _client == IntPtr.Zero) {
					LastError = "Activate 0x" + hr.ToString("X8");
					_client = IntPtr.Zero;
					return false;
				}

				WaveFormatEx format = WaveFormatEx.IeeeFloat(channels, sampleRate);
				IntPtr pFormat = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
				try {
					Marshal.StructureToPtr(format, pFormat, false);
					uint flags = NativeMethods.AudClntStreamflagsAutoconvertPcm | NativeMethods.AudClntStreamflagsSrcDefaultQuality;
					hr = ComVtable.Fn<NativeMethods.AudioClientInitializeDelegate>(_client, NativeMethods.AudioClientInitializeSlot)(
						_client, 0, flags, 5000000, 0, pFormat, IntPtr.Zero);
					if (hr < 0) {
						LastError = "Initialize 0x" + hr.ToString("X8");
						return false;
					}
				}
				finally {
					Marshal.FreeHGlobal(pFormat);
				}

				Guid renderIid = NativeMethods.AudioRenderClientIid;
				hr = ComVtable.Fn<NativeMethods.GetServiceDelegate>(_client, NativeMethods.AudioClientGetServiceSlot)(_client, ref renderIid, out _render);
				if (hr < 0 || _render == IntPtr.Zero) {
					LastError = "GetService 0x" + hr.ToString("X8");
					_render = IntPtr.Zero;
					return false;
				}

				hr = ComVtable.Fn<NativeMethods.GetBufferSizeDelegate>(_client, NativeMethods.AudioClientGetBufferSizeSlot)(_client, out _bufferFrames);
				if (hr < 0) {
					LastError = "GetBufferSize 0x" + hr.ToString("X8");
					return false;
				}

				hr = ComVtable.Fn<NativeMethods.AudioClientSimpleDelegate>(_client, NativeMethods.AudioClientStartSlot)(_client);
				if (hr < 0) {
					LastError = "Start 0x" + hr.ToString("X8");
					return false;
				}

				int scratch = (int)Math.Max(_bufferFrames, 1) * Math.Max((int)channels, 1);
				_scratch = new float[scratch];
				LastError = "";
				return true;
			}
			finally {
				ComVtable.Release(device);
				ComVtable.Release(enumerator);
			}
		}
	}
}
