namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public enum ClientUpdateType
    {
        Hitpoints = 0,
        MaxHitpoints = 1,
        Mana = 2,
        MaxMana = 3,
        Money = 4,
        Experience = 5,
        PkPoints = 6,
        Class = 7,
        Stamina = 8,
        WhMoney = 9,
        Atributes = 10,
        Mesh = 11,
        Level = 12,
        Spirit = 13,
        Vitality = 14,
        Strength = 15,
        Agility = 16,
        HeavensBlessing = 17,
        DoubleExpTimer = 18,
        CursedTimer = 20,
        Reborn = 22,
        StatusEffect = 25,
        Hairstyle = 26,
        XpCircle = 27,
        LuckyTimeTimer = 28,
        ConquerPoints = 29,
        OnlineTraining = 31,
        ExtraBattlePower = 36,
        Merchant = 38,
        VipLevel = 39,
        QuizPoints = 40,
        EnlightenPoints = 41,
        FamilySharedBattlePower = 42,
        TotemPoleBattlePower = 44,
        BoundConquerPoints = 45,
        AzureShield = 49,
        PreviousProfession = 50,
        FirstProfession = 51
    }

    public struct UserAttribute
    {
        public readonly uint Type;
        public readonly ulong Data;

        public UserAttribute(uint type, ulong data)
        {
            this.Type = type;
            this.Data = data;
        }
    }

    /// <remarks>Packet Type 10017</remarks>
    /// <summary>
    /// Patch 5187 entity attribute synchronization packet (<c>MsgUserAttrib</c>).
    /// </summary>
    public sealed class MsgUserAttrib : MsgBase<Client>
    {
        private readonly List<UserAttribute> m_attributes = new();

        public MsgUserAttrib(uint identity, ClientUpdateType type, ulong value)
        {
            this.Type = PacketType.MsgUserAttrib;
            this.Identity = identity;
            this.Amount++;
            this.m_attributes.Add(new UserAttribute((uint)type, value));
        }

        public MsgUserAttrib(uint identity, ClientUpdateType type, uint data1, uint data2)
        {
            this.Type = PacketType.MsgUserAttrib;
            this.Identity = identity;
            this.Amount++;
            this.m_attributes.Add(new UserAttribute((uint)type, ((ulong)data1 << 32) | data2));
        }

        public uint Identity { get; set; }
        public int Amount { get; set; }

        public void Append(ClientUpdateType type, ulong data)
        {
            this.Amount++;
            this.m_attributes.Add(new UserAttribute((uint)type, data));
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Identity);
            writer.Write(this.Amount);
            for (int i = 0; i < this.Amount; i++)
            {
                writer.Write(this.m_attributes[i].Type);
                writer.Write(this.m_attributes[i].Data);
            }
            return writer.ToArray();
        }
    }
}
