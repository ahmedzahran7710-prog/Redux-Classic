namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Game.World.Maps;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1056</remarks>
    /// <summary>
    /// Conquer Online Patch 5187 2-player Peer-to-Peer Trade packet (<c>MsgTrade</c>).
    /// </summary>
    public sealed class MsgTrade : MsgBase<Client>
    {
        public enum TradeAction : uint
        {
            Apply = 1,
            Quit = 2,
            Open = 3,
            Success = 4,
            Fail = 5,
            AddItem = 6,
            AddMoney = 7,
            ShowMoney = 8,
            Accept = 10,
            AddItemFail = 11,
            ShowConquerPoints = 12,
            AddConquerPoints = 13,
            Timeout = 17
        }

        public MsgTrade()
        {
            this.Type = PacketType.MsgTrade;
        }

        public uint Data { get; set; }
        public TradeAction Action { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Data = reader.ReadUInt32();
            this.Action = (TradeAction)reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Data);
            writer.Write((uint)this.Action);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client.Character;
            if (user == null)
                return;

            switch (this.Action)
            {
                case TradeAction.Apply:
                {
                    if (this.Data == 0 || this.Data == user.Identity || !user.IsAlive || user.Booth != null)
                        return;

                    Character target = RoleManager.GetUser(this.Data);
                    if (target == null || !target.IsAlive || target.Booth != null ||
                        target.Map != user.Map || user.GetDistance(target) > Screen.VIEW_SIZE)
                    {
                        await user.SendTalkAsync("The target is unavailable for trade right now.");
                        return;
                    }

                    if (user.Trade != null || target.Trade != null)
                    {
                        await user.SendTalkAsync("One of you is already trading.");
                        return;
                    }

                    // Did target already invite user? Open the trade session!
                    if (target.PendingTradePartnerId == user.Identity)
                    {
                        target.PendingTradePartnerId = 0;
                        user.PendingTradePartnerId = 0;
                        var trade = new Trade(target, user);
                        target.Trade = trade;
                        user.Trade = trade;
                        await trade.OpenAsync();
                        return;
                    }

                    user.PendingTradePartnerId = target.Identity;
                    await target.SendAsync(new MsgTrade { Data = user.Identity, Action = TradeAction.Apply });
                    await user.SendTalkAsync("Trade request has been sent.");
                    break;
                }

                case TradeAction.Quit:
                case TradeAction.Timeout:
                    if (user.Trade != null)
                        await user.Trade.CancelAsync();
                    break;

                case TradeAction.AddItem:
                    if (user.Trade != null)
                        await user.Trade.AddItemAsync(user, this.Data);
                    break;

                case TradeAction.AddMoney:
                    if (user.Trade != null)
                        await user.Trade.AddMoneyAsync(user, this.Data);
                    break;

                case TradeAction.AddConquerPoints:
                    if (user.Trade != null)
                        await user.Trade.AddConquerPointsAsync(user, this.Data);
                    break;

                case TradeAction.Accept:
                    if (user.Trade != null)
                        await user.Trade.AcceptAsync(user, this.Data);
                    break;
            }
        }
    }
}
