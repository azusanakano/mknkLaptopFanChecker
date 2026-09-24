using System;

namespace Mknk.LaptopFanChecker
{
    internal sealed class SmartBoostPolicy
    {
        public const double ThresholdPercent = 85.0;
        private DateTime? _highSinceUtc;
        private DateTime? _lastSampleUtc;
        public DateTime? LastAttemptUtc { get; private set; }
        public bool HasPendingImmediateRun { get; private set; }

        public SmartBoostPolicy(DateTime? lastAttemptUtc) { LastAttemptUtc = lastAttemptUtc; }

        public void OnEnabledChanged(bool enabled)
        {
            HasPendingImmediateRun = enabled;
            _highSinceUtc = null;
            _lastSampleUtc = null;
        }

        public bool ShouldRun(DateTime nowUtc, double? memoryPercent, bool enabled, bool blocked)
        {
            if (_lastSampleUtc.HasValue && (nowUtc < _lastSampleUtc.Value || nowUtc - _lastSampleUtc.Value > TimeSpan.FromSeconds(15)))
                _highSinceUtc = null;
            _lastSampleUtc = nowUtc;
            if (!enabled)
                HasPendingImmediateRun = false;
            if (!enabled || blocked)
            {
                _highSinceUtc = null;
                return false;
            }
            // Only an explicit OFF-to-ON change requests this; restoring settings does not.
            if (HasPendingImmediateRun)
            {
                HasPendingImmediateRun = false;
                LastAttemptUtc = nowUtc;
                _highSinceUtc = null;
                return true;
            }
            if (!memoryPercent.HasValue || Double.IsNaN(memoryPercent.Value) ||
                memoryPercent.Value < ThresholdPercent || memoryPercent.Value > 100.0)
            {
                _highSinceUtc = null;
                return false;
            }
            if (!_highSinceUtc.HasValue)
                _highSinceUtc = nowUtc;
            if (nowUtc - _highSinceUtc.Value < TimeSpan.FromSeconds(30) ||
                (LastAttemptUtc.HasValue && nowUtc - LastAttemptUtc.Value < TimeSpan.FromMinutes(30)))
                return false;
            LastAttemptUtc = nowUtc;
            _highSinceUtc = null;
            return true;
        }
    }
}
