namespace Comet.Game.Packets
{
    using System;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1033</remarks>
    /// <summary>
    /// Synchronizes server date and time with the Patch 5187 client clock UI.
    /// </summary>
    public sealed class MsgData : MsgBase<Client>
    {
        public enum DataAction : uint
        {
            SetServerTime = 0,
            SetMountMovePoint = 2,
            AntiCheatAnswerMsgTypeCount = 3,
            AntiCheatAskMsgTypeCount = 4
        }

        public MsgData()
        {
            this.Type = PacketType.MsgData;
        }

        public MsgData(DateTime time)
        {
            this.Type = PacketType.MsgData;
            this.Action = DataAction.SetServerTime;
            this.Year = time.Year - 1900;
            this.Month = time.Month - 1;
            this.DayOfYear = time.DayOfYear;
            this.Day = time.Day;
            this.Hours = time.Hour;
            this.Minutes = time.Minute;
            this.Seconds = time.Second;
        }

        public DataAction Action { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int DayOfYear { get; set; }
        public int Day { get; set; }
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public int Seconds { get; set; }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write((uint)this.Action);
            writer.Write(this.Year);
            writer.Write(this.Month);
            writer.Write(this.DayOfYear);
            writer.Write(this.Day);
            writer.Write(this.Hours);
            writer.Write(this.Minutes);
            writer.Write(this.Seconds);
            return writer.ToArray();
        }
    }
}
