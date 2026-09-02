using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace swapmyaudio.Audio
{
	internal static class FAudioOutputTap
	{
		private const int FactAudioOffset = 152;
		private const int FAudioMasterOffset = 16;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void EngineCall(IntPtr audio, IntPtr output);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void EngineProc(IntPtr defaultProc, IntPtr audio, IntPtr output, IntPtr user);

		[DllImport("FAudio", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FAudio_SetEngineProcedureEXT")]
		private static extern void SetEngineProcedure(IntPtr audio, IntPtr proc, IntPtr user);

		[DllImport("FAudio", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FAudio_GetDeviceCount")]
		private static extern uint GetDeviceCount(IntPtr audio, out uint count);

		[DllImport("FAudio", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FAudioVoice_GetVoiceDetails")]
		private static extern void GetVoiceDetails(IntPtr voice, out VoiceDetails details);

		[DllImport("FAudio", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FACTAudioEngine_GetFinalMixFormat")]
		private static extern uint GetFactMixFormat(IntPtr engine, out FactMixFormat format);

		private static readonly EngineProc Hook = OnEngine;
		private static readonly IntPtr HookPtr = Marshal.GetFunctionPointerForDelegate(Hook);
		private static EngineCall _generate;
		private static readonly object Gate = new();
		private static readonly List<IntPtr> Hooked = new();
		private static Tap[] _taps = Array.Empty<Tap>();

		internal static string LastError { get; private set; } = "";

		internal static string LastStatus { get; private set; } = "";

		internal static bool SetDevice(string deviceId)
		{
			deviceId ??= "";
			lock (Gate) {
				try {
					if (string.IsNullOrEmpty(deviceId)) {
						Detach();
						LastError = "";
						LastStatus = "FAudio tap off";
						return true;
					}

					if (!TryCollectEngines(out List<EngineInfo> engines, out string collectError)) {
						LastError = collectError;
						return false;
					}

					var taps = new List<Tap>(engines.Count);
					foreach (EngineInfo engine in engines) {
						WasapiPushClient sink = WasapiPushClient.TryOpen(deviceId, engine.Mix.Channels, engine.Mix.SampleRate, out string openError);
						if (sink == null) {
							foreach (Tap created in taps)
								created.Dispose();
							LastError = openError;
							return false;
						}

						taps.Add(new Tap(engine.Audio, engine.Mix, sink));
					}

					Detach();
					_taps = taps.ToArray();
					Thread.MemoryBarrier();
					foreach (EngineInfo engine in engines) {
						SetEngineProcedure(engine.Audio, HookPtr, IntPtr.Zero);
						Hooked.Add(engine.Audio);
					}

					LastError = "";
					LastStatus = taps.Count > 1
						? "FAudio tap sfx+music (" + taps.Count + ")"
						: "FAudio tap sfx only";
					return true;
				}
				catch (DllNotFoundException) {
					LastError = "FAudio.dll not loaded";
					return false;
				}
				catch (EntryPointNotFoundException) {
					LastError = "FAudio export missing";
					return false;
				}
				catch (Exception e) {
					LastError = e.GetType().Name + ": " + e.Message;
					return false;
				}
			}
		}

		internal static void Shutdown()
		{
			lock (Gate)
				Detach();
		}

		private static void OnEngine(IntPtr defaultProc, IntPtr audio, IntPtr output, IntPtr user)
		{
			try {
				if (_generate == null && defaultProc != IntPtr.Zero)
					_generate = Marshal.GetDelegateForFunctionPointer<EngineCall>(defaultProc);

				_generate?.Invoke(audio, output);

				Tap[] taps = Volatile.Read(ref _taps);
				Tap tap = null;
				for (int i = 0; i < taps.Length; i++) {
					if (taps[i].Audio == audio) {
						tap = taps[i];
						break;
					}
				}

				if (tap == null || output == IntPtr.Zero)
					return;

				if (tap.Sink.Write(output, tap.Mix.Frames, tap.Mix.Channels))
					tap.Silence(output);
			}
			catch {
			}
		}

		private static void Detach()
		{
			foreach (IntPtr engine in Hooked) {
				try {
					SetEngineProcedure(engine, IntPtr.Zero, IntPtr.Zero);
				}
				catch {
				}
			}

			Hooked.Clear();
			Tap[] taps = _taps;
			_taps = Array.Empty<Tap>();
			Thread.MemoryBarrier();
			foreach (Tap tap in taps)
				tap.Dispose();
		}

		private static bool TryCollectEngines(out List<EngineInfo> engines, out string error)
		{
			engines = new List<EngineInfo>();
			error = "";

			if (TryGetSfxEngine(out EngineInfo sfx))
				engines.Add(sfx);

			if (TryGetMusicEngine(sfx.Audio, out EngineInfo music))
				engines.Add(music);

			if (engines.Count == 0) {
				error = "FAudio context not ready";
				return false;
			}

			return true;
		}

		private static bool TryGetSfxEngine(out EngineInfo engine)
		{
			engine = default;
			try {
				Type contextType = typeof(SoundEffect).GetNestedType("FAudioContext", BindingFlags.NonPublic | BindingFlags.Public);
				object context = contextType?.GetField("Context", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
				if (context == null)
					return false;

				object handleObj = contextType.GetField("Handle", BindingFlags.Public | BindingFlags.Instance)?.GetValue(context);
				if (handleObj is not IntPtr handle || handle == IntPtr.Zero)
					return false;
				if (!IsFAudio(handle))
					return false;

				ushort channels = 2;
				uint rate = 48000;
				object masterObj = contextType.GetField("MasterVoice", BindingFlags.Public | BindingFlags.Instance)?.GetValue(context);
				if (masterObj is IntPtr master && master != IntPtr.Zero)
					ApplyVoiceDetails(master, ref channels, ref rate);

				TryReadDeviceDetails(contextType, context, ref channels, ref rate);
				engine = new EngineInfo(handle, MakeMix(channels, rate));
				return true;
			}
			catch {
				return false;
			}
		}

		private static bool TryGetMusicEngine(IntPtr sfxAudio, out EngineInfo engine)
		{
			engine = default;
			try {
				IntPtr fact = GetFactHandle();
				if (fact == IntPtr.Zero)
					return false;

				if (GetFactMixFormat(fact, out FactMixFormat factMix) != 0)
					return false;

				ushort channels = factMix.Format.nChannels;
				uint rate = factMix.Format.nSamplesPerSec;
				if (channels is < 1 or > 8)
					channels = 2;
				if (rate is < 8000 or > 192000)
					rate = 48000;

				if (!TryFindFactFAudio(fact, sfxAudio, out IntPtr audio, out IntPtr master))
					return false;
				if (!IsFAudio(audio))
					return false;

				ApplyVoiceDetails(master, ref channels, ref rate);
				engine = new EngineInfo(audio, MakeMix(channels, rate));
				return true;
			}
			catch {
				return false;
			}
		}

		private static IntPtr GetFactHandle()
		{
			object system = Main.audioSystem;
			if (system is not LegacyAudioSystem legacy)
				return IntPtr.Zero;

			object engine = typeof(LegacyAudioSystem).GetField("Engine")?.GetValue(legacy)
			                ?? typeof(LegacyAudioSystem).GetField("Engine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(legacy);
			if (engine == null)
				return IntPtr.Zero;

			object handle = engine.GetType().GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(engine);
			return handle is IntPtr ptr ? ptr : IntPtr.Zero;
		}

		private static bool TryFindFactFAudio(IntPtr fact, IntPtr sfxAudio, out IntPtr audio, out IntPtr master)
		{
			audio = IntPtr.Zero;
			master = IntPtr.Zero;
			if (!NativeMem.Readable(fact, FactAudioOffset + IntPtr.Size * 2))
				return false;

			if (TryPair(fact, FactAudioOffset, sfxAudio, out audio, out master))
				return true;

			for (int offset = 128; offset <= 176; offset += IntPtr.Size) {
				if (offset == FactAudioOffset)
					continue;
				if (TryPair(fact, offset, sfxAudio, out audio, out master))
					return true;
			}

			return false;
		}

		private static bool TryPair(IntPtr fact, int offset, IntPtr sfxAudio, out IntPtr audio, out IntPtr master)
		{
			audio = Marshal.ReadIntPtr(fact, offset);
			master = Marshal.ReadIntPtr(fact, offset + IntPtr.Size);
			if (audio == IntPtr.Zero || audio == sfxAudio || master == IntPtr.Zero)
				return false;
			if (!NativeMem.Readable(audio, FAudioMasterOffset + IntPtr.Size))
				return false;
			if (!NativeMem.Readable(master, IntPtr.Size))
				return false;
			if (Marshal.ReadIntPtr(master) != audio)
				return false;
			if (Marshal.ReadIntPtr(audio, FAudioMasterOffset) != master)
				return false;
			return true;
		}

		private static bool IsFAudio(IntPtr audio)
		{
			if (audio == IntPtr.Zero)
				return false;
			try {
				return GetDeviceCount(audio, out uint count) == 0 && count is > 0 and <= 64;
			}
			catch {
				return false;
			}
		}

		private static void ApplyVoiceDetails(IntPtr master, ref ushort channels, ref uint rate)
		{
			if (master == IntPtr.Zero)
				return;
			try {
				GetVoiceDetails(master, out VoiceDetails details);
				if (details.InputChannels is >= 1 and <= 8)
					channels = (ushort)details.InputChannels;
				if (details.InputSampleRate is >= 8000 and <= 192000)
					rate = details.InputSampleRate;
			}
			catch {
			}
		}

		private static MixInfo MakeMix(ushort channels, uint rate)
		{
			int frames = (int)Math.Max(64, rate / 100);
			if (frames > 8192)
				frames = 8192;
			return new MixInfo(frames, channels, rate);
		}

		private static bool TryReadDeviceDetails(Type contextType, object context, ref ushort channels, ref uint rate)
		{
			try {
				object details = contextType.GetField("DeviceDetails", BindingFlags.Public | BindingFlags.Instance)?.GetValue(context);
				if (details == null)
					return false;

				object output = details.GetType().GetField("OutputFormat")?.GetValue(details);
				object format = output?.GetType().GetField("Format")?.GetValue(output);
				if (format == null)
					return false;

				object ch = format.GetType().GetField("nChannels")?.GetValue(format);
				object sr = format.GetType().GetField("nSamplesPerSec")?.GetValue(format);
				if (ch == null || sr == null)
					return false;

				ushort parsedCh = Convert.ToUInt16(ch);
				uint parsedRate = Convert.ToUInt32(sr);
				if (parsedCh is >= 1 and <= 8)
					channels = parsedCh;
				if (parsedRate is >= 8000 and <= 192000)
					rate = parsedRate;
				return true;
			}
			catch {
				return false;
			}
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct VoiceDetails
		{
			public uint CreationFlags;
			public uint ActiveFlags;
			public uint InputChannels;
			public uint InputSampleRate;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct FactMixFormat
		{
			public WaveFormatEx Format;
			public ushort Samples;
			public uint dwChannelMask;
			public Guid SubFormat;
		}

		private readonly struct EngineInfo
		{
			internal EngineInfo(IntPtr audio, MixInfo mix)
			{
				Audio = audio;
				Mix = mix;
			}

			internal IntPtr Audio { get; }
			internal MixInfo Mix { get; }
		}

		private sealed class Tap
		{
			internal Tap(IntPtr audio, MixInfo mix, WasapiPushClient sink)
			{
				Audio = audio;
				Mix = mix;
				Sink = sink;
				_silence = new float[mix.FloatCount];
			}

			internal IntPtr Audio { get; }
			internal MixInfo Mix { get; }
			internal WasapiPushClient Sink { get; }

			private readonly float[] _silence;

			internal void Silence(IntPtr output)
			{
				if (_silence.Length == 0 || output == IntPtr.Zero)
					return;

				Marshal.Copy(_silence, 0, output, _silence.Length);
			}

			internal void Dispose()
			{
				Sink?.Dispose();
			}
		}

		private readonly struct MixInfo
		{
			internal MixInfo(int frames, ushort channels, uint sampleRate)
			{
				Frames = frames;
				Channels = channels;
				SampleRate = sampleRate;
			}

			internal int Frames { get; }
			internal ushort Channels { get; }
			internal uint SampleRate { get; }
			internal int FloatCount => Frames * Channels;
		}
	}
}
