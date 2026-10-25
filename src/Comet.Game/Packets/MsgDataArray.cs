namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.States.Items;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 2036</remarks>
    /// <summary>
    /// Patch 5187 equipment &amp; steed +1..+12 composition packet (<c>MsgDataArray</c>),
    /// implementing official TQ composition point progress (<c>20, 20, 80, 240, 720, 2160, 6480,
    /// 19440, 58320, 2700, 5500, 9000</c>) and Steed RGB coat blending (<c>90% main + 10% minor</c>).
    /// </summary>
    public sealed class MsgDataArray : MsgBase<Client>
    {
        public enum DataArrayMode : byte
        {
            Composition = 0,
            CompositionSteedOriginal = 2,
            CompositionSteedNew = 3,
            QuickCompose = 4,
            QuickComposeMount = 5,
            UpgradeItemLevel = 6,
            UpgradeItemQuality = 7
        }

        public MsgDataArray()
        {
            this.Type = PacketType.MsgDataArray;
        }

        public DataArrayMode Action { get; set; }
        public byte Count { get; set; }
        public List<uint> Items { get; } = new();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (DataArrayMode)reader.ReadByte();
            this.Count = reader.ReadByte();
            _ = reader.ReadUInt16();
            for (int i = 0; i < this.Count; i++)
                this.Items.Add(reader.ReadUInt32());
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write((byte)this.Action);
            writer.Write((byte)this.Items.Count);
            writer.Write((ushort)0);
            foreach (uint id in this.Items)
                writer.Write(id);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            Character user = client?.Character;
            if (user == null || this.Items.Count == 0)
                return;

            Item target = user.UserPackage[this.Items[0]];
            if (target == null)
                return;

            byte oldPlus = target.Plus;

            switch (this.Action)
            {
                case DataArrayMode.Composition:
                {
                    if (target.Plus >= 12 || this.Items.Count < 2)
                        return;

                    for (int i = 1; i < this.Items.Count && target.Plus < 12; i++)
                    {
                        Item minor = user.UserPackage[this.Items[i]];
                        if (minor == null || minor.Identity == target.Identity || minor.Position != Item.ItemPosition.Inventory)
                            continue;

                        bool isStone = minor.Type >= Item.TYPE_STONE1 && minor.Type <= Item.TYPE_STONE8;
                        if (!isStone)
                        {
                            if (minor.Plus == 0 || minor.Plus > 8)
                                continue;
                            if (!AreCompatibleForCompose(target, minor))
                                continue;
                        }

                        uint addPts = GetMinorComposePoints(minor.Plus, isSteed: false);
                        if (addPts == 0)
                            continue;

                        if (!await user.UserPackage.SpendItemAsync(minor, 1))
                            continue;

                        target.CompositionProgress += addPts;
                        while (target.Plus < 12)
                        {
                            uint req = GetRequiredComposePoints(target.Plus, isSteed: false);
                            if (req == 0 || target.CompositionProgress < req)
                                break;
                            target.CompositionProgress -= req;
                            target.ChangeAddition();
                        }

                        if (target.Plus >= 12)
                            target.CompositionProgress = 0;
                    }
                    break;
                }

                case DataArrayMode.CompositionSteedOriginal:
                case DataArrayMode.CompositionSteedNew:
                {
                    if (!target.IsMount() || this.Items.Count < 2)
                        return;

                    for (int i = 1; i < this.Items.Count; i++)
                    {
                        Item minor = user.UserPackage[this.Items[i]];
                        if (minor == null || minor.Identity == target.Identity || !minor.IsMount() || minor.Position != Item.ItemPosition.Inventory)
                            continue;

                        if (this.Action == DataArrayMode.CompositionSteedNew)
                        {
                            // Official TQ 90% main + 10% minor RGB steed coat lineage blending
                            int c1 = (int)target.SocketProgress;
                            int c2 = (int)minor.SocketProgress;
                            int g1 = c1 & 0xFF, g2 = c2 & 0xFF;
                            int b1 = (c1 >> 8) & 0xFF, b2 = (c2 >> 8) & 0xFF;
                            int r1 = (c1 >> 16) & 0xFF, r2 = (c2 >> 16) & 0xFF;

                            int newG = (int)Math.Floor(0.9 * g1) + (int)Math.Floor(0.1 * g2);
                            int newB = (int)Math.Floor(0.9 * b1) + (int)Math.Floor(0.1 * b2);
                            int newR = (int)Math.Floor(0.9 * r1) + (int)Math.Floor(0.1 * r2);

                            target.Blessing = (byte)newR;
                            target.AntiMonster = (byte)newG;
                            target.Enchantment = (byte)newB;
                            target.SocketProgress = (uint)(newG | (newB << 8) | (newR << 16));
                        }

                        if (target.Plus < 12)
                        {
                            target.CompositionProgress += GetMinorComposePoints(minor.Plus, isSteed: true);
                            while (target.Plus < 12)
                            {
                                uint req = GetRequiredComposePoints(target.Plus, isSteed: true);
                                if (req == 0 || target.CompositionProgress < req)
                                    break;
                                target.CompositionProgress -= req;
                                target.ChangeAddition();
                            }
                            if (target.Plus >= 12)
                                target.CompositionProgress = 0;
                        }

                        await user.UserPackage.SpendItemAsync(minor, 1);
                    }
                    break;
                }

                case DataArrayMode.QuickCompose:
                case DataArrayMode.QuickComposeMount:
                {
                    bool isMount = this.Action == DataArrayMode.QuickComposeMount;
                    if (target.Plus >= 12)
                        return;

                    uint req = GetRequiredComposePoints(target.Plus, isMount);
                    if (req == 0 || target.CompositionProgress == 0)
                        return;

                    int chancePercent = (int)Math.Min(100, (target.CompositionProgress * 100UL) / req);
                    target.CompositionProgress = 0;
                    if (await Kernel.NextAsync(0, 100) < chancePercent)
                        target.ChangeAddition();
                    break;
                }

                default:
                    return;
            }

            await target.SaveAsync();
            await user.SendAsync(new MsgItemInfo(target, MsgItemInfo.ItemMode.Update));
            await user.RecalculateEquipmentStatsAsync(broadcastAppearance: target.IsMount() && target.Position == Item.ItemPosition.Steed);

            if (target.Plus > oldPlus && target.Plus >= 6)
            {
                await RoleManager.BroadcastTalkAsync(
                    $"Congratulations! {user.Name} has composed {target.Name} to +{target.Plus}!",
                    MsgTalk.TalkChannel.TopLeft,
                    Color.Red);
            }
        }

        private static bool AreCompatibleForCompose(Item target, Item minor)
        {
            if (target.IsWeaponOneHand())
                return minor.IsWeaponOneHand();
            if (target.IsBow())
                return minor.IsBow();
            if (target.IsWeaponTwoHand())
                return minor.IsWeaponTwoHand() && !minor.IsBow();
            return target.GetItemSubType() == minor.GetItemSubType();
        }

        public static ushort GetMinorComposePoints(uint plus, bool isSteed) =>
            plus switch
            {
                0 => isSteed ? (ushort)1 : (ushort)0,
                1 => 10,
                2 => 40,
                3 => 120,
                4 => 360,
                5 => 1080,
                6 => 3240,
                7 => 9720,
                8 => 29160,
                _ => 0
            };

        public static ushort GetRequiredComposePoints(uint currentPlus, bool isSteed) =>
            currentPlus switch
            {
                0 => 20,
                1 => 20,
                2 => isSteed ? (ushort)90 : (ushort)80,
                3 => 240,
                4 => 720,
                5 => 2160,
                6 => 6480,
                7 => 19440,
                8 => 58320,
                9 => 2700,
                10 => 5500,
                11 => 9000,
                _ => 0
            };
    }
}
