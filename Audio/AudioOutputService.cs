using System;
using System.Collections.Generic;

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
			catch {
				Supported = false;
			}
		}

		internal bool Supported { get; private set; }

		internal bool RoutingSupported { get; }

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

			if (_policy == null)
				return;

			string persisted = _policy.GetPersistedRenderEndpoint();
			if (string.IsNullOrEmpty(persisted)) {
				_selectedId = "";
				return;
			}

			if (FindDevice(persisted) == null) {
				_selectedId = "";
				return;
			}

			_selectedId = persisted;
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

		internal bool SelectSystemDefault() => SelectDevice("");

		internal bool SelectDevice(string id)
		{
			id ??= "";
			if (_policy != null) {
				if (!_policy.SetPersistedRenderEndpoint(id))
					return false;

				Refresh(forceDevices: true);
				return true;
			}

			_selectedId = id;
			return true;
		}

		internal string DisplayName(string systemDefaultLabel)
		{
			if (IsSystemDefault)
				return systemDefaultLabel;

			PlaybackDevice device = FindDevice(_selectedId);
			return device?.Name ?? systemDefaultLabel;
		}

		public void Dispose()
		{
			_enumerator?.Dispose();
		}
	}
}
