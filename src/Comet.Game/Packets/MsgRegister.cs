namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States;
    using Comet.Game.World.Managers;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1001</remarks>
    /// <summary>
    /// Handles new character creation using official Patch 5187 class validation (Trojan, Warrior,
    /// Archer, Ninja, Intern Taoist), retail `cq_point_allot` Level-1 attributes, and Birth Village
    /// (`1010`, `61, 109`) initial spawn.
    /// </summary>
    public sealed class MsgRegister : MsgBase<Client>
    {
        private static readonly char[] InvalidCharacters =
        {
            ' ', '~', '`', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '+', '=',
            '|', '\\', '{', '}', '[', ']', ':', ';', '"', '\'', '<', '>', ',', '.', '?', '/'
        };

        private static readonly ushort[] ValidStyles = { 1003, 1004, 2001, 2002 };
        private static readonly ushort[] ValidClasses = { 10, 20, 40, 50, 100 };

        private static readonly byte[] Hairstyles =
        {
            10, 11, 13, 14, 15, 24, 30, 35, 37, 38, 39, 40
        };

        public string Username { get; set; }
        public string CharacterName { get; set; }
        public string MaskedPassword { get; set; }
        public ushort Mesh { get; set; }
        public ushort Class { get; set; }
        public uint Token { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Username = reader.ReadString(16);
            this.CharacterName = reader.ReadString(16);
            this.MaskedPassword = reader.ReadString(16);
            this.Mesh = reader.ReadUInt16();
            this.Class = reader.ReadUInt16();
            this.Token = reader.ReadUInt32();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (client.Creation == null ||
                this.Token != client.Creation.Token ||
                !Kernel.Registration.Contains(this.Token))
            {
                await client.SendAsync(MsgTalk.RegisterInvalid);
                client.Disconnect();
                return;
            }

            if (string.IsNullOrEmpty(this.CharacterName) ||
                this.CharacterName.Length < 3 ||
                this.CharacterName.Length > 15 ||
                this.CharacterName.IndexOfAny(InvalidCharacters) >= 0 ||
                !IsPrintableAscii(this.CharacterName) ||
                this.CharacterName.Contains('[') ||
                this.CharacterName.Contains(']') ||
                this.CharacterName.Contains("GM", StringComparison.OrdinalIgnoreCase) ||
                this.CharacterName.Contains("PM", StringComparison.OrdinalIgnoreCase) ||
                this.CharacterName.Contains("ADMIN", StringComparison.OrdinalIgnoreCase) ||
                this.CharacterName.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                await client.SendAsync(MsgTalk.RegisterInvalidCharacters);
                return;
            }

            if (Array.IndexOf(ValidStyles, this.Mesh) < 0)
            {
                await client.SendAsync(MsgTalk.RegisterInvalidBody);
                return;
            }

            if (Array.IndexOf(ValidClasses, this.Class) < 0)
            {
                await client.SendAsync(MsgTalk.RegisterInvalidProfession);
                return;
            }

            // Prevent race-condition double character creation on the same account
            if (await CharactersRepository.FindAsync(client.Creation.AccountID) != null)
            {
                Kernel.Registration.Remove(client.Creation.Token);
                client.Creation = null;
                await client.SendAsync(MsgTalk.RegisterInvalid);
                return;
            }

            if (await CharactersRepository.ExistsAsync(this.CharacterName))
            {
                await client.SendAsync(MsgTalk.RegisterNameTaken);
                return;
            }

            DbPointAllot allot = ExperienceManager.GetPointAllot(this.Class, 1);

            var character = new DbCharacter
            {
                AccountID = client.Creation.AccountID,
                Name = this.CharacterName,
                MateName = "None",
                Mesh = this.Mesh,
                Avatar = this.Mesh < 2000
                    ? (ushort)await Kernel.NextAsync(1, 50)
                    : (ushort)await Kernel.NextAsync(201, 250),
                Hairstyle = (ushort)(
                    (await Kernel.NextAsync(3, 10) * 100) +
                    Hairstyles[await Kernel.NextAsync(0, Hairstyles.Length)]),
                CurrentClass = (byte)this.Class,
                Level = 1,
                Experience = 0,
                Silver = 1000,
                Jewels = 0,
                // Retail Conquer Birth Village spawn (1010: 61, 109) + Twin City record point
                MapID = 1010,
                X = 61,
                Y = 109,
                RecordMapID = 1002,
                RecordX = 430,
                RecordY = 380,
                Strength = allot.Strength,
                Agility = allot.Agility,
                Vitality = allot.Vitality,
                Spirit = allot.Spirit,
                AttributePoints = 0,
                PkMode = (byte)PkModeType.Capture,
                AutoAllot = 1,
                Registered = DateTime.UtcNow
            };

            int baseHp = (character.Strength * 3) + (character.Agility * 3) + (character.Spirit * 3) + (character.Vitality * 24);
            int manaMult = character.CurrentClass >= 100 ? 3 : 1;
            character.HealthPoints = (ushort)Math.Max(1, baseHp);
            character.ManaPoints = (ushort)(character.Spirit * 5 * manaMult);

            try
            {
                await CharactersRepository.CreateAsync(character);
                Kernel.Registration.Remove(client.Creation.Token);
                client.Creation = null;
            }
            catch
            {
                await client.SendAsync(MsgTalk.RegisterTryAgain);
                return;
            }

            var createdChar = new Character(client, character);
            await AwardStarterItemsAsync(createdChar);

            await Comet.Shared.Log.WriteLogAsync(
                Comet.Shared.LogLevel.Info,
                $"Created new character '{character.Name}' (#{character.CharacterID}, Class={character.CurrentClass}).");

            // Retail Patch 5187: send ANSWER_OK on Register channel (2100); client immediately follows up with MsgConnect to enter world.
            await client.SendAsync(MsgTalk.RegisterOk);
        }

        private static async Task AwardStarterItemsAsync(Character user)
        {
            // Retail starter Tunic + Class weapon + potions (and starter Taoist spells)
            await States.Items.Item.CreateAsync(user, 132005, States.Items.Item.ItemPosition.Armor);

            uint starterWeapon = user.Profession switch
            {
                40 => 500301u,  // Bow (Archer)
                50 => 601301u,  // Katana (Ninja)
                100 => 421301u, // Backsword (Taoist)
                _ => 410301u    // Blade (Trojan / Warrior)
            };
            await States.Items.Item.CreateAsync(user, starterWeapon, States.Items.Item.ItemPosition.RightHand);

            if (user.Profession == 40)
            {
                await States.Items.Item.CreateAsync(user, 1050000, States.Items.Item.ItemPosition.LeftHand);
                for (int i = 0; i < 3; i++)
                    await States.Items.Item.CreateAsync(user, 1050000, States.Items.Item.ItemPosition.Inventory);
            }
            else if (user.Profession == 100)
            {
                for (int i = 0; i < 3; i++)
                    await States.Items.Item.CreateAsync(user, 1001000, States.Items.Item.ItemPosition.Inventory);
                await MagicRepository.SaveAsync(new DbMagic { OwnerId = user.Identity, Type = 1000, Level = 0 }); // Thunder
                await MagicRepository.SaveAsync(new DbMagic { OwnerId = user.Identity, Type = 1005, Level = 0 }); // Cure
            }

            for (int i = 0; i < 3; i++)
                await States.Items.Item.CreateAsync(user, 1000000, States.Items.Item.ItemPosition.Inventory);
        }
        private static bool IsPrintableAscii(string s)
        {
            foreach (char c in s)
            {
                if (c < 33 || c > 126)
                    return false;
            }
            return true;
        }
    }
}
