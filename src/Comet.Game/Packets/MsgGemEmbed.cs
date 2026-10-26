namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1027</remarks>
    /// <summary>
    /// Patch 5187 gem socketing &amp; removal packet (<c>MsgGemEmbed</c>), including
    /// Thunder Gem validation for Heaven Fan (<c>201</c>) and Glory Gem validation for Star Tower (<c>202</c>).
    /// </summary>
    public sealed class MsgGemEmbed : MsgBase<Client>
    {
        public enum EmbedAction : ushort
        {
            Embed = 0,
            TakeOff = 1
        }

        public MsgGemEmbed()
        {
            this.Type = PacketType.MsgGemEmbed;
        }

        public uint Identity { get; set; }
        public uint MainIdentity { get; set; }
        public uint MinorIdentity { get; set; }
        public ushort Position { get; set; }
        public EmbedAction Action { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.MainIdentity = reader.ReadUInt32();
            this.MinorIdentity = reader.ReadUInt32();
            this.Position = reader.ReadUInt16();
            this.Action = (EmbedAction)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.MainIdentity);
            writer.Write(this.MinorIdentity);
            writer.Write(this.Position);
            writer.Write((ushort)this.Action);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null || user.Trade != null || this.Identity != user.Identity)
                return;

            Item main = user.UserPackage[this.MainIdentity];
            if (main == null || user.IsItemLockedInBooth(main.Identity))
                return;

            switch (this.Action)
            {
                case EmbedAction.Embed:
                {
                    Item gemItem = user.UserPackage[this.MinorIdentity];
                    if (gemItem == null || gemItem.GetItemSubType() != 700 || user.IsItemLockedInBooth(gemItem.Identity))
                        return;

                    var gem = (Item.SocketGem)(gemItem.Type % 1000);
                    if (!Enum.IsDefined(typeof(Item.SocketGem), gem) || gem == Item.SocketGem.NoSocket || gem == Item.SocketGem.EmptySocket)
                        return;

                    // Retail Fan (201) accepts only Thunder Gems; Tower (202) accepts only Glory Gems; standard gear rejects Thunder/Glory
                    bool isThunder = gem is Item.SocketGem.NormalThunderGem or Item.SocketGem.RefinedThunderGem or Item.SocketGem.SuperThunderGem;
                    bool isGlory = gem is Item.SocketGem.NormalGloryGem or Item.SocketGem.RefinedGloryGem or Item.SocketGem.SuperGloryGem;

                    if (main.IsAttackTalisman() && !isThunder)
                    {
                        await user.SendTalkAsync("Heaven Fan only accepts Thunder Gems.");
                        return;
                    }
                    if (main.IsDefenseTalisman() && !isGlory)
                    {
                        await user.SendTalkAsync("Star Tower only accepts Glory Gems.");
                        return;
                    }
                    if (!main.IsTalisman() && (isThunder || isGlory))
                    {
                        await user.SendTalkAsync("Thunder and Glory Gems can only be embedded into Talismans.");
                        return;
                    }

                    if (this.Position == 1 || (this.Position == 2 && main.SocketOne == Item.SocketGem.EmptySocket))
                    {
                        if (main.SocketOne != Item.SocketGem.EmptySocket)
                            return;
                        if (!await user.UserPackage.SpendItemAsync(gemItem, 1))
                            return;
                        main.SocketOne = gem;
                    }
                    else if (this.Position == 2)
                    {
                        if (main.SocketOne == Item.SocketGem.NoSocket || main.SocketOne == Item.SocketGem.EmptySocket)
                            return;
                        if (main.SocketTwo != Item.SocketGem.EmptySocket)
                            return;
                        if (!await user.UserPackage.SpendItemAsync(gemItem, 1))
                            return;
                        main.SocketTwo = gem;
                    }
                    else
                    {
                        return;
                    }

                    await main.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(main, MsgItemInfo.ItemMode.Update));
                    await user.SendAsync(this);
                    await user.RecalculateEquipmentStatsAsync();
                    break;
                }

                case EmbedAction.TakeOff:
                {
                    if (this.Position == 1)
                    {
                        if (main.SocketOne == Item.SocketGem.NoSocket || main.SocketOne == Item.SocketGem.EmptySocket)
                            return;

                        main.SocketOne = Item.SocketGem.EmptySocket;
                        if (main.SocketTwo != Item.SocketGem.NoSocket && main.SocketTwo != Item.SocketGem.EmptySocket)
                        {
                            main.SocketOne = main.SocketTwo;
                            main.SocketTwo = Item.SocketGem.EmptySocket;
                        }
                    }
                    else if (this.Position == 2)
                    {
                        if (main.SocketTwo == Item.SocketGem.NoSocket || main.SocketTwo == Item.SocketGem.EmptySocket)
                            return;
                        main.SocketTwo = Item.SocketGem.EmptySocket;
                    }
                    else
                    {
                        return;
                    }

                    await main.SaveAsync();
                    await user.SendAsync(new MsgItemInfo(main, MsgItemInfo.ItemMode.Update));
                    await user.SendAsync(this);
                    await user.RecalculateEquipmentStatsAsync();
                    break;
                }
            }
        }
    }
}
