using System;

namespace Mknk.LaptopFanChecker
{
    internal sealed class SmartBoostPolicy
    {
        private DateTime? _lowSinceUtc;
        private DateTime? _lastSampleUtc;
        public DateTime? LastAttemptUtc { get; private set; }
        public bool HasPendingImmediateRun { get; private set; }

        public SmartBoostPolicy(DateTime? lastAttemptUtc) { LastAttemptUtc = lastAttemptUtc; }

        public void OnEnabledChanged(bool enabled)
        {
            HasPendingImmediateRun = enabled;
            _lowSinceUtc = null;
            _lastSampleUtc = null;
        }

        public bool ShouldRun(DateTime nowUtc, bool? lowMemory, bool enabled, bool blocked)
        {
            if (_lastSampleUtc.HasValue && (nowUtc < _lastSampleUtc.Value || nowUtc - _lastSampleUtc.Value > TimeSpan.FromSeconds(15)))
                _lowSinceUtc = null;
            _lastSampleUtc = nowUtc;
            if (!enabled)
                HasPendingImmediateRun = false;
            if (!enabled || blocked)
            {
                _lowSinceUtc = null;
                return false;
            }
            // Only an explicit OFF-to-ON change requests this; restoring settings does not.
            if (HasPendingImmediateRun)
            {
                HasPendingImmediateRun = false;
                LastAttemptUtc = nowUtc;
                _lowSinceUtc = null;
                return true;
            }
            // An unavailable notification never counts as low memory.
            if (lowMemory != true)
            {
                _lowSinceUtc = null;
                return false;
            }
            if (!_lowSinceUtc.HasValue)
                _lowSinceUtc = nowUtc;
            if (nowUtc - _lowSinceUtc.Value < TimeSpan.FromSeconds(30) ||
                (LastAttemptUtc.HasValue && nowUtc - LastAttemptUtc.Value < TimeSpan.FromMinutes(30)))
                return false;
            LastAttemptUtc = nowUtc;
            _lowSinceUtc = null;
            return true;
        }
    }
}
