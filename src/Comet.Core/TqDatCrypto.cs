namespace Comet.Core
{
    /// <summary>
    /// Decrypts and encrypts official TQ Digital `.dat` data files (such as `ini/itemtype.dat`
    /// and `ini/magictype.dat` in Patch 5187) using the 128-byte LCG + bit-rotation cipher.
    /// </summary>
    public sealed class TqDatCrypto
    {
        public const int ItemtypeSeed = 0x2537;
        public const int LevExpSeed = 0x04D2;

        private readonly byte[] m_key = new byte[128];

        public TqDatCrypto(int seed = ItemtypeSeed)
        {
            long state = seed;
            for (int i = 0; i < this.m_key.Length; i++)
            {
                state = (state * 214013L) + 2531011L;
                int rand = (int)((state >> 16) & 0x7FFF);
                this.m_key[i] = (byte)(rand % 256);
            }
        }

        public byte[] Decrypt(byte[] input)
        {
            var output = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                int xored = input[i] ^ this.m_key[i % 128];
                int shift = i % 8;
                output[i] = (byte)(((xored << (8 - shift)) + (xored >> shift)) & 0xFF);
            }
            return output;
        }
    }
}
