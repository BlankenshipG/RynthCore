// ============================================================================
//  RynthCore.Engine - Compatibility/SessionRates.cs
//  The engine's XP / luminance session totals and per-hour rates: the status feed
//  (EngineStatusMetrics, GetEngineStatusJson) and the Mini Remote's Session Stats
//  section. Same formula as RynthSuite's Plugins/Shared/SessionStats.cs, which
//  RynthAi, RynthTracker, RynthOracle and RynthILT show (2026-10-13), so every
//  XP/hour and lum/hour on screen agrees:
//    - a total is the sum of every rise of the property since the session began;
//      a drop (spending luminance, a server resetting a session total) only moves
//      the baseline, never takes earnings back;
//    - ACECustom's bank (Int64 9005) counts its rises as income, and once it has
//      been read the character's AvailableLuminance rises are withdrawals, not income;
//      a deposit or incoming transfer seen in chat ("Deposited N luminance") is moved
//      money, so that much of the next bank rise isn't counted;
//    - Aelrynth's bank: luminance auto-banked (9102) was earned before it left the
//      character, luminance drawn (9103) landed on the character without being earned;
//    - rate = total / session hours, the session at least one second long.
//  A different character (or the first read) starts a new session.
//
//  Pure: no AC, no engine state (tools/SectionsTests compiles it). Not thread-safe:
//  one caller at a time (EngineStatusMetrics locks around it).
// ============================================================================

using System;

namespace RynthCore.Engine.Compatibility;

/// <summary>One read of the player's properties (null = not readable on this server or yet).</summary>
internal readonly record struct SessionRatesSample(
    long? TotalXp, long? AvailableLum, long? BankLum, long? AelBanked, long? AelDrawn);

/// <summary>The totals and rates as of the last sample.</summary>
internal readonly record struct SessionRatesView(
    DateTime StartUtc, double Seconds, long Xp, double XpPerHour, long Lum, double LumPerHour);

internal sealed class SessionRates
{
    private uint _character;
    private DateTime _startUtc;
    private long _xp, _lum;
    private Rise _xpRise, _lumRise, _bankRise, _aelBankedRise, _aelDrawnRise;
    private bool _bankTracked;
    // Luminance deposited (or transferred in) waiting for its bank rise, which isn't income.
    private long _movedIntoBank;
    private DateTime _movedIntoBankUntil;
    private static readonly TimeSpan MovedIntoBankLife = TimeSpan.FromSeconds(60);

    /// <summary>The character the session belongs to (0 = none yet).</summary>
    public uint Character => _character;

    /// <summary>Starts a fresh session for <paramref name="character"/> at <paramref name="nowUtc"/>.</summary>
    public void Reset(uint character, DateTime nowUtc)
    {
        _character = character;
        _startUtc = nowUtc;
        _xp = _lum = 0;
        _xpRise = default; _lumRise = default; _bankRise = default; _aelBankedRise = default; _aelDrawnRise = default;
        _bankTracked = false;
        _movedIntoBank = 0;
        _movedIntoBankUntil = DateTime.MinValue;
    }

    /// <summary>
    /// A chat line: "Deposited N luminance" (character to bank) or "Received N luminance from X"
    /// (a transfer) means the next N of bank rise is moved money, not income.
    /// </summary>
    public void OnChat(string? text, DateTime nowUtc)
    {
        if (!TryMovedIntoBank(text, out long moved)) return;
        _movedIntoBank += moved;
        _movedIntoBankUntil = nowUtc + MovedIntoBankLife;
    }

    /// <summary>The deposit / incoming-transfer lines (the shared SessionLumChat.TryMovedIntoBank).</summary>
    internal static bool TryMovedIntoBank(string? text, out long amount)
        => TryAmount(text, "Deposited ", " luminance", out amount)
        || TryAmount(text, "Received ", " luminance from ", out amount);

    // The line starts with the prefix (a player quoting it in a tell doesn't), then "1,234,567", then the suffix.
    private static bool TryAmount(string? text, string prefix, string suffix, out long amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(text)) return false;
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (string.Compare(text, i, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        i += prefix.Length;
        int digits = 0;
        long value = 0;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ',') continue;
            if (c < '0' || c > '9') break;
            if (value > (long.MaxValue - 9) / 10) return false;
            value = value * 10 + (c - '0');
            digits++;
        }
        if (digits == 0) return false;
        if (string.Compare(text, i, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        amount = value;
        return true;
    }

    /// <summary>Takes one read; a new character starts a new session first.</summary>
    public void Sample(uint character, in SessionRatesSample s, DateTime nowUtc)
    {
        if (character == 0) return;
        if (character != _character) Reset(character, nowUtc);

        if (s.TotalXp is long xp && xp > 0) _xp += _xpRise.Take(xp);

        if (nowUtc > _movedIntoBankUntil) _movedIntoBank = 0;

        long lumUp = 0;
        // ACECustom's bank first, so the character rise below already knows it is tracked.
        if (s.BankLum is long bank && bank >= 0)
        {
            _bankTracked = true;
            long up = _bankRise.Take(bank);
            long moved = Math.Min(up, _movedIntoBank);
            _movedIntoBank -= moved;
            lumUp += up - moved;
        }
        long charUp = 0;
        if (s.AvailableLum is long lum && lum >= 0)
        {
            long up = _lumRise.Take(lum);
            if (!_bankTracked) charUp += up;
        }
        if (s.AelDrawn is long drawn) charUp -= _aelDrawnRise.Take(drawn);
        if (s.AelBanked is long banked) charUp += _aelBankedRise.Take(banked);
        if (charUp > 0) lumUp += charUp;
        _lum += lumUp;
    }

    /// <summary>The totals and per-hour rates at <paramref name="nowUtc"/> (zeros before the first sample).</summary>
    public SessionRatesView View(DateTime nowUtc)
    {
        if (_character == 0) return new SessionRatesView(nowUtc, 0, 0, 0, 0, 0);
        double seconds = Math.Max(1.0, (nowUtc - _startUtc).TotalSeconds);
        double hours = seconds / 3600.0;
        return new SessionRatesView(_startUtc, seconds, _xp, _xp / hours, _lum, _lum / hours);
    }

    /// <summary>How much a value went up since the last read; the first read is the baseline.</summary>
    private struct Rise
    {
        private long _last;
        private bool _seen;

        public long Take(long value)
        {
            long up = _seen && value > _last ? value - _last : 0;
            _last = value;
            _seen = true;
            return up;
        }
    }
}
