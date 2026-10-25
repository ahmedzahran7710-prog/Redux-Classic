namespace Comet.Core.Tests
{
    using Comet.Core.Mathematics;
    using Xunit;

    public class CoreFoundationTests
    {
        [Fact]
        public void ScreenCalculations_ChebyshevDistance_MatchesConquerViewBox()
        {
            Assert.Equal(0, ScreenCalculations.GetDistance(430, 380, 430, 380));
            Assert.Equal(18, ScreenCalculations.GetDistance(430, 380, 448, 392));
            Assert.Equal(19, ScreenCalculations.GetDistance(430, 380, 449, 380));
        }

        [Fact]
        public void ScreenCalculations_DirectionSector_CoversCompassAxes()
        {
            Assert.Equal(0, ScreenCalculations.GetDirectionSector(100, 100, 100, 110)); // +Y -> SouthWest (0)
            Assert.Equal(4, ScreenCalculations.GetDirectionSector(100, 100, 100, 90));  // -Y -> NorthEast (4)
            Assert.Equal(6, ScreenCalculations.GetDirectionSector(100, 100, 110, 100)); // +X -> SouthEast (6)
            Assert.Equal(2, ScreenCalculations.GetDirectionSector(100, 100, 90, 100));  // -X -> NorthWest (2)
        }

        [Fact]
        public void ScreenCalculations_BresenhamLine_IncludesStartAndEnd()
        {
            var pts = ScreenCalculations.BresenhamLine(61, 109, 68, 115);
            Assert.NotEmpty(pts);
            Assert.Equal(61, pts[0].X);
            Assert.Equal(109, pts[0].Y);
            Assert.Equal(68, pts[^1].X);
            Assert.Equal(115, pts[^1].Y);
        }

        [Fact]
        public void TimeOutMS_MonotonicStartupAndClear_WorkCorrectly()
        {
            var timer = new TimeOutMS();
            Assert.False(timer.IsActive());
            timer.Startup(5000);
            Assert.True(timer.IsActive());
            Assert.False(timer.IsTimeOut());
            Assert.InRange(timer.GetRemain(), 1L, 5000L);
            timer.Clear();
            Assert.False(timer.IsActive());
        }

        [Fact]
        public void TqDatCrypto_DecryptProducesDeterministicBytes()
        {
            var crypto = new TqDatCrypto(TqDatCrypto.ItemtypeSeed);
            byte[] sample = { 0x12, 0x34, 0x56, 0x78, 0x9A };
            byte[] dec1 = crypto.Decrypt(sample);
            byte[] dec2 = crypto.Decrypt(sample);
            Assert.Equal(dec1, dec2);
        }
    }
}
