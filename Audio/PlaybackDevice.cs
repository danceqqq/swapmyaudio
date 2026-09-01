namespace swapmyaudio.Audio
{
	internal sealed class PlaybackDevice
	{
		internal PlaybackDevice(string id, string name)
		{
			Id = id ?? "";
			Name = string.IsNullOrWhiteSpace(name) ? Id : name;
		}

		internal string Id { get; }

		internal string Name { get; }
	}
}
