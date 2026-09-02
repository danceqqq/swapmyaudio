using System;
using System.Collections.Generic;
using System.Text;

namespace swapmyaudio.Audio
{
	internal sealed class AudioOutputService : IDisposable
	{
		private readonly WasapiDeviceEnumerator _enumerator;
		private readonly AppAudioPolicy _policy;
		private string _selectedId = "";

		internal AudioOutputService()
		{
			Supported = OperatingSystem.IsWindows();
			if (!Supported)
				return;

			try {
				_enumerator = new WasapiDeviceEnumerator();
				RoutingSupported = AppAudioPolicy.TryCreate(out _policy);
				Refresh(forceDevices: true);
			}
			catch (Exception e) {
				LastError = e.GetType().Name + ": " + e.Message;
				Supported = false;
			}
		}

		internal bool Supported { get; private set; }

		internal bool RoutingSupported { get; }

		internal string LastError { get; private set; } = "";

		internal string LastSetError => _policy?.LastSetError ?? "";

		internal IReadOnlyList<PlaybackDevice> Devices => _enumerator?.Devices ?? Array.Empty<PlaybackDevice>();

		internal string SelectedId => _selectedId;

		internal bool IsSystemDefault => string.IsNullOrEmpty(_selectedId);

		internal bool DevicesDirty => _enumerator != null && _enumerator.IsDirty;

		internal void Refresh(bool forceDevices = false)
		{
			if (_enumerator == null)
				return;

			bool dirty = _enumerator.ConsumeDirty();
			if (forceDevices || dirty)
				_enumerator.Refresh();

			if (!string.IsNullOrEmpty(_enumerator.LastError))
				LastError = _enumerator.LastError;
			else if (!RoutingSupported)
				LastError = AppAudioPolicy.LastError;
			else
				LastError = "";
		}

		internal PlaybackDevice FindDevice(string id)
		{
			if (string.IsNullOrEmpty(id))
				return null;

			foreach (PlaybackDevice device in Devices) {
				if (string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
					return device;
			}

			return null;
		}

		internal bool SelectDevice(string id)
		{
			id ??= "";
			bool tap;
			try {
				tap = FAudioOutputTap.SetDevice(id);
			}
			catch (Exception e) {
				LastError = e.GetType().Name + ": " + e.Message;
				return false;
			}

			if (!tap && !string.IsNullOrEmpty(FAudioOutputTap.LastError))
				LastError = FAudioOutputTap.LastError;

			_selectedId = id;
			if (tap)
				LastError = "";
			return tap;
		}

		internal string SummarizeDevices(int maxNames = 4)
		{
			var devices = Devices;
			if (devices.Count == 0)
				return "none";

			var sb = new StringBuilder();
			int n = Math.Min(maxNames, devices.Count);
			for (int i = 0; i < n; i++) {
				if (i > 0)
					sb.Append(", ");
				sb.Append(devices[i].Name);
			}

			if (devices.Count > n)
				sb.Append(", +").Append(devices.Count - n);
			return sb.ToString();
		}

		public void Dispose()
		{
			FAudioOutputTap.Shutdown();
			_enumerator?.Dispose();
		}
	}
}
