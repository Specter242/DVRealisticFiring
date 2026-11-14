using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests
{
    public class PlumeOrderTests
    {
        [Fact]
        public void SteamFartherThanThePlumeIsDrawnBehindIt()
        {
            // From ahead of the engine: the dynamo's steam 2 m behind the stack.
            Assert.True(PlumeOrder.Behind(false, 22f, 20f));
            // From the cab: the same steam, nearer than the stack.
            Assert.False(PlumeOrder.Behind(false, 8f, 10f));
        }

        [Fact]
        public void AParcelAtThePlumesDepthKeepsItsSide()
        {
            Assert.True(PlumeOrder.Behind(true, 20.1f, 20f));
            Assert.True(PlumeOrder.Behind(true, 19.9f, 20f));
            Assert.False(PlumeOrder.Behind(false, 19.9f, 20f));
            Assert.False(PlumeOrder.Behind(false, 20.1f, 20f));
        }

        [Fact]
        public void AParcelWellPastThePlumeChangesSides()
        {
            Assert.False(PlumeOrder.Behind(true, 20f - 2f * PlumeOrder.Margin, 20f));
            Assert.True(PlumeOrder.Behind(false, 20f + 2f * PlumeOrder.Margin, 20f));
        }
    }
}
