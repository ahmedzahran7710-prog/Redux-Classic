namespace MoonQueen.Account.Database.Tests
{
    using Xunit;
    using MoonQueen.Account.Database;

    public class ConfigurationTests
    {
        [Theory]
        [InlineData()]
        [InlineData("--Network:Port=9961")]
        [InlineData("/Network:Port=9961")]
        [InlineData("--Network:Port", "9961")]
        [InlineData("/Network:Port", "9961")]
        [InlineData("Network:Port=9961")]
        public void ValidConfiguration(params string[] args)
        {
            var config = new ServerConfiguration(args);
            Assert.True(config.Valid);
            Assert.Equal("0.0.0.0", config.Network.IPAddress);
            Assert.Equal("127.0.0.1", config.Database.Hostname);
            Assert.Equal(args.Length > 0 ? 9961 : 9960, config.Network.Port);
        }
    }
}
