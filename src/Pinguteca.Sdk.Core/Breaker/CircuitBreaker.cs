using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinguteca.Sdk.Core.Breaker;

internal enum BreakerState
{
    Closed,
    Open,
    HalfOpen,
}

internal readonly record struct Sample(DateTimeOffset At, bool IsSuccess);

internal readonly record struct BreakerDecision(bool Allow, TimeSpan RetryAfter, bool IsHalfOpenProbe);

/// <summary>
/// Rolling-window circuit breaker per RFC 0008. Tracks success and
/// failure samples within a configurable window, opens when the
/// failure rate exceeds the threshold given the minimum sample
/// count, allows one half-open probe after the open duration
/// expires, and re-closes on probe success or reopens on probe
/// failure.
/// </summary>
internal sealed class CircuitBreaker
{
    private readonly BreakerOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate = new();
    private readonly Queue<Sample> _samples = new();

    private BreakerState _state = BreakerState.Closed;
    private DateTimeOffset _openedAt;
    private bool _halfOpenProbeInFlight;

    public CircuitBreaker(BreakerOptions options)
    {
        _options = options;
        _utcNow = options.UtcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public BreakerDecision TryAcquire()
    {
        lock (_gate)
        {
            var now = _utcNow();
            TransitionFromOpenIfElapsed(now);

            switch (_state)
            {
                case BreakerState.Closed:
                    return new BreakerDecision(Allow: true, TimeSpan.Zero, IsHalfOpenProbe: false);
                case BreakerState.HalfOpen when !_halfOpenProbeInFlight:
                    _halfOpenProbeInFlight = true;
                    return new BreakerDecision(Allow: true, TimeSpan.Zero, IsHalfOpenProbe: true);
                default:
                    return new BreakerDecision(Allow: false, RemainingOpen(now), IsHalfOpenProbe: false);
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            var now = _utcNow();
            if (_state == BreakerState.HalfOpen && _halfOpenProbeInFlight)
            {
                _state = BreakerState.Closed;
                _halfOpenProbeInFlight = false;
                _samples.Clear();
                return;
            }
            _samples.Enqueue(new Sample(now, IsSuccess: true));
            PruneSamples(now);
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            var now = _utcNow();
            if (_state == BreakerState.HalfOpen && _halfOpenProbeInFlight)
            {
                Trip(now);
                _halfOpenProbeInFlight = false;
                return;
            }
            _samples.Enqueue(new Sample(now, IsSuccess: false));
            PruneSamples(now);

            if (_state != BreakerState.Closed || _samples.Count < _options.MinSamples)
            {
                return;
            }

            var failures = _samples.Count(s => !s.IsSuccess);
            var rate = (double)failures / _samples.Count;
            if (rate >= _options.FailureRateThreshold)
            {
                Trip(now);
            }
        }
    }

    private void Trip(DateTimeOffset now)
    {
        _state = BreakerState.Open;
        _openedAt = now;
        _samples.Clear();
    }

    private void TransitionFromOpenIfElapsed(DateTimeOffset now)
    {
        if (_state == BreakerState.Open && now - _openedAt >= _options.OpenDuration)
        {
            _state = BreakerState.HalfOpen;
            _halfOpenProbeInFlight = false;
        }
    }

    private TimeSpan RemainingOpen(DateTimeOffset now)
    {
        var elapsed = now - _openedAt;
        var remaining = _options.OpenDuration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void PruneSamples(DateTimeOffset now)
    {
        var cutoff = now - _options.WindowDuration;
        while (_samples.Count > 0 && _samples.Peek().At < cutoff)
        {
            _samples.Dequeue();
        }
    }
}
