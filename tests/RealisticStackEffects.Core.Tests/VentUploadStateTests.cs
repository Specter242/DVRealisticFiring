using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests
{
    public class VentUploadStateTests
    {
        [Fact]
        public void FirstIdleFrameDoesNotUpload()
        {
            var state = new VentUploadState();

            Assert.False(state.TakeFront(0));
            Assert.False(state.TakeBehind(0));
            Assert.Equal(0, state.LastFrontCount);
            Assert.Equal(0, state.LastBehindCount);
        }

        [Fact]
        public void ActiveFrameUploads()
        {
            var state = new VentUploadState();

            Assert.True(state.TakeFront(12));
            Assert.Equal(12, state.LastFrontCount);
            Assert.True(state.TakeFront(7));
            Assert.Equal(7, state.LastFrontCount);
        }

        [Fact]
        public void TransitionToIdleUploadsOnceToClearAndThenSkips()
        {
            var state = new VentUploadState();

            Assert.True(state.TakeFront(8));
            Assert.True(state.TakeFront(0));
            Assert.Equal(0, state.LastFrontCount);

            Assert.False(state.TakeFront(0));
            Assert.False(state.TakeFront(0));
        }

        [Fact]
        public void FrontAndBehindTransitionIndependently()
        {
            var state = new VentUploadState();

            // Front active, behind stays idle: only front uploads.
            Assert.True(state.TakeFront(10));
            Assert.False(state.TakeBehind(0));

            // Both active.
            Assert.True(state.TakeFront(6));
            Assert.True(state.TakeBehind(4));

            // Front drops to 0 while behind stays active: front clears once, behind uploads.
            Assert.True(state.TakeFront(0));
            Assert.True(state.TakeBehind(4));

            // Next frame: front stays idle and skips, behind still uploads.
            Assert.False(state.TakeFront(0));
            Assert.True(state.TakeBehind(3));

            // Behind drops to 0 while front stays idle: front skips, behind clears once.
            Assert.False(state.TakeFront(0));
            Assert.True(state.TakeBehind(0));

            // Subsequent idle frame: both skip.
            Assert.False(state.TakeFront(0));
            Assert.False(state.TakeBehind(0));

            // Front becomes active again while behind stays idle.
            Assert.True(state.TakeFront(2));
            Assert.False(state.TakeBehind(0));
        }

        [Fact]
        public void ResetClearsTrackedCounts()
        {
            var state = new VentUploadState();
            Assert.True(state.TakeFront(5));
            Assert.True(state.TakeBehind(3));

            state.Reset();

            Assert.Equal(0, state.LastFrontCount);
            Assert.Equal(0, state.LastBehindCount);
            Assert.False(state.TakeFront(0));
            Assert.False(state.TakeBehind(0));
        }

        [Fact]
        public void NegativeCountIsTreatedAsIdle()
        {
            var state = new VentUploadState();

            Assert.False(state.TakeFront(-1));
            Assert.Equal(0, state.LastFrontCount);

            Assert.True(state.TakeBehind(4));
            Assert.True(state.TakeBehind(-2));
            Assert.Equal(0, state.LastBehindCount);
            Assert.False(state.TakeBehind(0));
        }
    }
}
