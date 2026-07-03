using VisionInspection.Watchdog;
using Xunit;

namespace VisionInspection.Tests
{
    public class WatchdogDecisionTests
    {
        [Theory]
        [InlineData(false, false, false, WatchdogAction.Start)]
        [InlineData(true, true, true, WatchdogAction.None)]
        [InlineData(true, false, true, WatchdogAction.None)]
        [InlineData(true, false, false, WatchdogAction.Kill)]
        public void Decide_Uses_Running_Heartbeat_And_Grace(bool running, bool heartbeatFresh, bool inGrace, WatchdogAction expected)
        {
            var actual = WatchdogDecision.Decide(running, heartbeatFresh, inGrace);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 2000)]
        [InlineData(10, 18000)]
        [InlineData(100, 60000)]
        public void Restart_Backoff_Is_Capped(int consecutiveRestarts, int expectedMs)
        {
            Assert.Equal(expectedMs, WatchdogDecision.CalculateBackoffMs(consecutiveRestarts));
        }
    }
}
