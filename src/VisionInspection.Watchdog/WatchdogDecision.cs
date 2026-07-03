namespace VisionInspection.Watchdog
{
    public enum WatchdogAction
    {
        None,
        Start,
        Kill
    }

    public static class WatchdogDecision
    {
        public const int MaxBackoffMs = 60000;

        public static WatchdogAction Decide(bool running, bool heartbeatFresh, bool inGrace)
        {
            if (!running) return WatchdogAction.Start;
            if (inGrace || heartbeatFresh) return WatchdogAction.None;
            return WatchdogAction.Kill;
        }

        public static int CalculateBackoffMs(int consecutiveRestarts)
        {
            if (consecutiveRestarts <= 1) return 0;
            int backoff = 2000 * (consecutiveRestarts - 1);
            return backoff > MaxBackoffMs ? MaxBackoffMs : backoff;
        }
    }
}
