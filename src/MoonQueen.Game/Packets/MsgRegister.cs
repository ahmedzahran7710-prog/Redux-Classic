namespace MoonQueen.Game.Packets
{
    using System;
    using System.Runtime.Caching;
    using System.Threading.Tasks;
    using MoonQueen.Game.Database.Models;
    using MoonQueen.Game.Database.Repositories;
    using MoonQueen.Game.States;
    using MoonQueen.Network.Packets;
    using MoonQueen.Shared.Models;

    /// <remarks>Packet Type 1001</remarks>
    /// <summary>
    /// Handles character creation on Patch 5187 (Trojan, Warrior, Archer, Ninja, Taoist).
    /// </summary>
    public sealed class MsgRegister : MsgBase<Client>
    {
        private static readonly char[] InvalidCharacters = new char[]
        {
            ' ', '[', ']', '{', '}', '(', ')', ',', '*', '\\', '/', '<', '>', ':', ';', '"', '\'', '|', '=', '+'
        };

        private static readonly byte[] Hairstyles = new byte[]
        {
            10, 11, 13, 14, 15, 24, 30, 35, 37, 38, 39, 40
        };

        public int Cancel { get; set; }
        public string Username { get; set; }
        public string CharacterName { get; set; }
        public string MaskedPassword { get; set; }
        public ushort Mesh { get; set; }
        public ushort Class { get; set; }
        public uint Token { get; set; }

        public override void Decode(byte[] bytes)
        {
            using var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Cancel = reader.ReadInt32();          // 4
            this.Username = reader.ReadString(12);     // 8..19
            this.CharacterName = reader.ReadString(16);// 20..35
            this.MaskedPassword = reader.ReadString(16);// 36..51
            this.Mesh = reader.ReadUInt16();           // 52..53
            this.Class = reader.ReadUInt16();          // 54..55
            this.Token = reader.ReadUInt32();          // 56..59
        }

        public override async Task ProcessAsync(Client client)
        {
            if (this.Cancel != 0)
            {
                client.Disconnect();
                return;
            }

            if (client.Creation == null || this.Token != client.Creation.Token ||
                !Kernel.Registration.Contains(this.Token))
            {
                await client.SendAsync(MsgTalk.RegisterInvalid);
                client.Disconnect();
                return;
            }

            if (string.IsNullOrEmpty(this.CharacterName) ||
                this.CharacterName.Length > 15 ||
                this.CharacterName.IndexOfAny(InvalidCharacters) >= 0)
            {
                await client.SendAsync(MsgTalk.RegisterInvalid);
                return;
            }

            if (await CharactersRepository.ExistsAsync(this.CharacterName))
            {
                await client.SendAsync(MsgTalk.RegisterNameTaken);
                return;
            }

            if (!Enum.IsDefined(typeof(BodyType), this.Mesh) ||
                !Enum.IsDefined(typeof(BaseClassType), this.Class))
            {
                await client.SendAsync(MsgTalk.RegisterInvalid);
                return;
            }

            BaseClassType baseClass = (BaseClassType)this.Class;
            BodyType body = (BodyType)this.Mesh;

            // Base stats by class
            ushort str = 4, agi = 6, vit = 12, spi = 0;
            if (baseClass == BaseClassType.Taoist)
            {
                str = 2; agi = 5; vit = 3; spi = 10;
            }
            else if (baseClass == BaseClassType.Archer || baseClass == BaseClassType.Ninja)
            {
                str = 4; agi = 8; vit = 8; spi = 0;
            }

            ushort hp = (ushort)(str * 3 + agi * 3 + spi * 3 + vit * 24);
            ushort mp = (ushort)(spi * 5);

            // Patch 5187 Avatar portrait matching Body + Class (including Ninja!)
            ushort avatar;
            if (body == BodyType.AgileFemale || body == BodyType.MuscularFemale)
            {
                avatar = baseClass == BaseClassType.Ninja
                    ? (ushort)291
                    : (ushort)await Kernel.Services.Randomness.NextIntegerAsync(201, 210);
            }
            else
            {
                avatar = baseClass == BaseClassType.Ninja
                    ? (ushort)103
                    : (ushort)await Kernel.Services.Randomness.NextIntegerAsync(1, 15);
            }

            ushort hairColor = (ushort)(await Kernel.Services.Randomness.NextIntegerAsync(3, 9) * 100);
            byte hairStyle = Hairstyles[await Kernel.Services.Randomness.NextIntegerAsync(0, Hairstyles.Length - 1)];

            var character = new DbCharacter
            {
                AccountID = client.Creation.AccountID,
                Name = this.CharacterName,
                CurrentClass = (byte)this.Class,
                Mesh = this.Mesh,
                Avatar = avatar,
                Hairstyle = (ushort)(hairColor + hairStyle),
                Silver = 10000,
                Level = 1,
                MapID = 1002, // Twin City center spawn
                X = 430,
                Y = 380,
                Strength = str,
                Agility = agi,
                Vitality = vit,
                Spirit = spi,
                HealthPoints = hp,
                ManaPoints = mp,
                Registered = DateTime.UtcNow
            };

            try
            {
                await CharactersRepository.CreateAsync(character);
                Kernel.Registration.Remove(client.Creation.Token);

                // Refresh login token & attach character so both reconnect and immediate spawn work seamlessly
                ulong tokenKey = this.Token;
                Kernel.Logins.Set(tokenKey.ToString(), new TransferAuthArgs
                {
                    AccountID = client.AccountID,
                    AuthorityID = client.AuthorityLevel,
                    IPAddress = client.IPAddress
                }, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromSeconds(120) });

                client.Character = new Character(character);
                client.Creation = null;
                Server.Players[client.Character.CharacterID] = client;

                Console.WriteLine($"[Game] Created new character '{character.Name}' (Class={baseClass}, ID={character.CharacterID})!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Game] Character creation error: {ex.Message}");
                await client.SendAsync(MsgTalk.RegisterTryAgain);
                return;
            }

            await client.SendAsync(MsgTalk.RegisterOk);
        }
    }
}
