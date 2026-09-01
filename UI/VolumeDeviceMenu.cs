using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using swapmyaudio.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace swapmyaudio.UI
{
	public class VolumeDeviceMenu : ModSystem
	{
		private const int TitleVolumeMenuMode = 26;
		private const int PollInterval = 30;

		private static AudioOutputService _service;
		private static readonly List<Entry> Entries = new();

		private static bool _optionsVolumePage;
		private static bool _wasTitlePage;
		private static bool _wasOptionsPage;
		private static bool _armed;
		private static bool _drawingPicker;
		private static int _bars;
		private static int _barDepth;
		private static int _lastPercentIndex = -1;
		private static int _hoverSide;
		private static int _poll;
		private static int _index;
		private static bool _leftPressed;
		private static bool _wasMouseLeft;
		private static Vector2 _anchor;
		private static Vector2 _offset;

		public override void Load()
		{
			if (!OperatingSystem.IsWindows())
				return;

			try {
				_service = new AudioOutputService();
			}
			catch {
				_service = null;
				return;
			}

			if (_service == null || !_service.Supported)
				return;

			if (!_service.RoutingSupported && !string.IsNullOrEmpty(AppAudioPolicy.LastError))
				ModContent.GetInstance<swapmyaudio>().Logger.Warn("Audio policy: " + AppAudioPolicy.LastError);

			ModContent.GetInstance<swapmyaudio>().Logger.Info($"Audio devices: {_service.Devices.Count}, policy: {_service.RoutingSupported}");

			On_Main.DrawMenu += DrawMenuHook;
			On_IngameOptions.Draw += DrawOptionsHook;
			On_IngameOptions.DrawRightSide += DrawRightSideHook;
			On_IngameOptions.DrawValueBar += DrawValueBarHook;
		}

		public override void PostUpdateInput()
		{
			if (Main.mouseLeft && !_wasMouseLeft)
				_leftPressed = true;
			_wasMouseLeft = Main.mouseLeft;
		}

		public override void Unload()
		{
			_service?.Dispose();
			_service = null;
			Entries.Clear();
		}

		private static void DrawMenuHook(On_Main.orig_DrawMenu orig, Main self, GameTime time)
		{
			_bars = 0;
			if (Main.mouseLeft && Main.mouseLeftRelease)
				_leftPressed = true;
			bool page = Main.gameMenu && Main.menuMode == TitleVolumeMenuMode;
			if (page)
				BeginVolumePage(!_wasTitlePage);
			else if (_wasTitlePage)
				EndVolumePage();

			orig(self, time);
			_wasTitlePage = page;
		}

		private static void DrawOptionsHook(On_IngameOptions.orig_Draw orig, Main main, SpriteBatch sb)
		{
			_optionsVolumePage = false;
			_lastPercentIndex = -1;
			_bars = 0;
			if (!Main.gameMenu && Main.mouseLeft && Main.mouseLeftRelease)
				_leftPressed = true;
			orig(main, sb);

			if (_optionsVolumePage)
				BeginVolumePage(!_wasOptionsPage);
			else if (_wasOptionsPage)
				EndVolumePage();

			_wasOptionsPage = _optionsVolumePage;
		}

		private static bool DrawRightSideHook(
			On_IngameOptions.orig_DrawRightSide orig,
			SpriteBatch sb,
			string txt,
			int i,
			Vector2 anchor,
			Vector2 offset,
			float scale,
			float colorScale,
			Color over)
		{
			bool result = orig(sb, txt, i, anchor, offset, scale, colorScale, over);
			if (_drawingPicker || string.IsNullOrEmpty(txt) || !txt.Contains('%'))
				return result;

			if (txt.StartsWith(Lang.menu[99].Value, StringComparison.Ordinal) ||
			    txt.StartsWith(Lang.menu[98].Value, StringComparison.Ordinal) ||
			    txt.StartsWith(Lang.menu[119].Value, StringComparison.Ordinal)) {
				_optionsVolumePage = true;
				_anchor = anchor;
				_offset = offset;
				_lastPercentIndex = i;
			}

			return result;
		}

		private static float DrawValueBarHook(On_IngameOptions.orig_DrawValueBar orig, SpriteBatch sb, float scale, float perc, int lockState, Utils.ColorLerpMethod colorMethod)
		{
			_barDepth++;
			try {
				Vector2 row = IngameOptions.valuePosition;
				float result = orig(sb, scale, perc, lockState, colorMethod);
				if (_drawingPicker || _barDepth != 1)
					return result;

				bool title = Main.gameMenu && Main.menuMode == TitleVolumeMenuMode;
				if (!title && !_optionsVolumePage)
					return result;

				_bars++;
				if (_bars == 3)
					DrawPicker(sb, row, title);

				return result;
			}
			finally {
				_barDepth--;
			}
		}

		private static void BeginVolumePage(bool entered)
		{
			if (entered) {
				_armed = !Main.mouseLeft;
				_hoverSide = 0;
				_poll = 0;
			}
			else if (!Main.mouseLeft) {
				_armed = true;
			}

			_poll++;
			bool dirty = _service != null && _service.DevicesDirty;
			if (entered || dirty || _poll >= PollInterval) {
				_poll = 0;
				_service?.Refresh(forceDevices: entered || dirty);
			}

			RebuildEntries();
		}

		private static void EndVolumePage()
		{
			_armed = false;
			_hoverSide = 0;
		}

		private static void RebuildEntries()
		{
			string keep = "";
			if (_index >= 0 && _index < Entries.Count)
				keep = Entries[_index].Id;
			else if (_service != null)
				keep = _service.SelectedId;

			Entries.Clear();
			Entries.Add(new Entry("", L("SystemDefault")));
			if (_service != null) {
				foreach (PlaybackDevice device in _service.Devices)
					Entries.Add(new Entry(device.Id, device.Name));
			}

			_index = 0;
			for (int i = 0; i < Entries.Count; i++) {
				if (string.Equals(Entries[i].Id, keep, StringComparison.OrdinalIgnoreCase)) {
					_index = i;
					break;
				}
			}
		}

		private static void DrawPicker(SpriteBatch sb, Vector2 row, bool title)
		{
			if (_service == null)
				return;

			_drawingPicker = true;
			try {
				if (title)
					DrawTitlePicker(sb, row);
				else
					DrawOptionsPicker(sb);
			}
			finally {
				_drawingPicker = false;
			}
		}

		private static void DrawTitlePicker(SpriteBatch sb, Vector2 row)
		{
			DynamicSpriteFont font = FontAssets.DeathText.Value;
			const float scale = 0.5f;
			float y = row.Y + 36f;
			float x = Main.screenWidth * 0.5f;
			DrawCycleRow(sb, font, x, y, scale, Main.screenWidth * 0.72f, centered: true);
		}

		private static void DrawOptionsPicker(SpriteBatch sb)
		{
			int i = _lastPercentIndex + 1;
			DynamicSpriteFont font = FontAssets.DeathText.Value;
			if (i >= 0 && i < IngameOptions.rightScale.Length) {
				Vector2 pos = _anchor + _offset * i;
				DrawCycleRow(sb, font, pos.X, pos.Y, 0.45f, IngameOptions.width - 90f, centered: true);
				return;
			}

			DrawCycleRow(sb, font, Main.screenWidth * 0.5f + 80f, IngameOptions.valuePosition.Y + 28f, 0.45f, 360f, centered: false);
		}

		private static void DrawCycleRow(SpriteBatch sb, DynamicSpriteFont font, float x, float y, float scale, float maxWidth, bool centered)
		{
			string name = Truncate(font, CurrentName(), scale, Math.Max(80f, maxWidth - 90f));
			string text = "<  " + name + "  >";
			string line = L("AudioDevice") + ": " + text;
			line = Truncate(font, line, scale, maxWidth);

			Vector2 size = font.MeasureString(line) * scale;
			Vector2 origin = centered ? font.MeasureString(line) * 0.5f : Vector2.Zero;
			float left = centered ? x - size.X * 0.5f : x;
			float top = centered ? y - size.Y * 0.5f : y;
			Rectangle hit = new((int)left - 10, (int)top - 4, (int)size.X + 20, (int)size.Y + 8);
			bool hovered = hit.Contains(Main.mouseX, Main.mouseY);
			int side = 0;
			if (hovered) {
				Main.blockMouse = true;
				side = Main.mouseX < hit.Center.X ? -1 : 1;
				int wheel = PlayerInput.ScrollWheelDelta;
				if (wheel == 0)
					wheel = PlayerInput.ScrollWheelDeltaForUI;
				if (wheel != 0) {
					Cycle(wheel > 0 ? -1 : 1);
					PlayerInput.ScrollWheelDelta = 0;
					PlayerInput.ScrollWheelDeltaForUI = 0;
				}
			}

			if (side != _hoverSide) {
				if (side != 0)
					SoundEngine.PlaySound(SoundID.MenuTick);
				_hoverSide = side;
			}

			Color color = hovered ? new Color(255, 215, 0) : Color.White;
			float drawScale = scale * (hovered ? 1.06f : 1f);
			Utils.DrawBorderStringFourWay(sb, font, line, x, y, color, Color.Black, origin, drawScale);

			if (_armed && hovered && _leftPressed) {
				_leftPressed = false;
				Main.mouseLeftRelease = false;
				Cycle(side == 0 ? 1 : side);
			}
		}

		private static string CurrentName()
		{
			if (Entries.Count == 0)
				return L("SystemDefault");

			int i = _index;
			if (i < 0 || i >= Entries.Count)
				i = 0;
			return Entries[i].Name;
		}

		private static void Cycle(int delta)
		{
			if (_service == null || Entries.Count == 0 || delta == 0)
				return;

			_index += delta;
			while (_index < 0)
				_index += Entries.Count;
			_index %= Entries.Count;

			_service.SelectDevice(Entries[_index].Id);
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		private static string Truncate(DynamicSpriteFont font, string text, float scale, float maxWidth)
		{
			if (string.IsNullOrEmpty(text) || font.MeasureString(text).X * scale <= maxWidth)
				return text;

			const string ellipsis = "...";
			while (text.Length > 0 && font.MeasureString(text + ellipsis).X * scale > maxWidth)
				text = text[..^1];
			return text + ellipsis;
		}

		private static string L(string key) => Language.GetTextValue("Mods.swapmyaudio.UI." + key);

		private readonly struct Entry
		{
			internal Entry(string id, string name)
			{
				Id = id;
				Name = name;
			}

			internal string Id { get; }
			internal string Name { get; }
		}
	}
}
