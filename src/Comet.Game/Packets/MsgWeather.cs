namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1016</remarks>
    /// <summary>
    /// Controls dynamic map weather effects on the Conquer Online client (rain, snow,
    /// autumn leaves, cherry blossoms, sandstorm, fireflies).
    /// </summary>
    public sealed class MsgWeather : MsgBase<Client>
    {
        public MsgWeather()
        {
            this.Type = PacketType.MsgWeather;
        }

        public uint WeatherType { get; set; }
        public uint Intensity { get; set; }
        public uint Direction { get; set; }
        public uint Color { get; set; }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.WeatherType);
            writer.Write(this.Intensity);
            writer.Write(this.Direction);
            writer.Write(this.Color);
            return writer.ToArray();
        }
    }
}
