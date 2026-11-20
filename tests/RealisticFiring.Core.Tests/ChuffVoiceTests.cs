using System;
using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The chimney's beats as heard: loudness by the steam let go, the clip sets
/// giving way smoothly, each engine's own uneven beat, the panting of a
/// drifting engine, and echoes and distance.
/// </summary>
public class ChuffVoiceTests
{
    [Fact]
    public void Single_beats_play_to_three_beats_a_second_and_hand_over_to_the_loops_by_five()
    {
        Assert.Equal(1f, ChuffVoice.BeatShare(0.5f));
        Assert.Equal(1f, ChuffVoice.BeatShare(3f));
        Assert.Equal(0.5f, ChuffVoice.BeatShare(4f), 4);
        Assert.Equal(0f, ChuffVoice.BeatShare(5f));
        for (var f = 0f; f < 8f; f += 0.25f)
            Assert.Equal(1f, ChuffVoice.BeatShare(f) + ChuffVoice.LoopShare(f), 4);
    }

    [Fact]
    public void A_beat_is_as_loud_as_its_steam_hooked_up_quiet_full_gear_at_the_top()
    {
        Assert.Equal(0f, ChuffVoice.Loudness(1f));
        Assert.Equal(0f, ChuffVoice.Loudness(0.5f));
        Assert.Equal(1f, ChuffVoice.Loudness(7f), 4);
        Assert.Equal(1f, ChuffVoice.Loudness(14f), 4);
        // Hooked up, 2 bar at release: about a quarter (the game's beat is at 70 % there).
        Assert.InRange(ChuffVoice.Loudness(2f), 0.2f, 0.3f);
        var last = 0f;
        for (var p = 1f; p <= 8f; p += 0.25f)
        {
            var loudness = ChuffVoice.Loudness(p);
            Assert.True(loudness >= last);
            last = loudness;
        }
    }

    [Fact]
    public void The_clip_sets_give_way_over_a_bar_either_side_of_the_games_thresholds_at_constant_power()
    {
        var soft = ChuffVoice.Tiers(3f, 6f, 9f);
        Assert.Equal((1f, 0f, 0f), (soft.Soft, soft.Medium, soft.Hard));
        var mid = ChuffVoice.Tiers(6f, 6f, 9f);
        Assert.Equal(mid.Soft, mid.Medium, 4);
        var medium = ChuffVoice.Tiers(7.5f, 6f, 9f);
        Assert.Equal((0f, 1f, 0f), (medium.Soft, medium.Medium, medium.Hard));
        var hard = ChuffVoice.Tiers(12f, 6f, 9f);
        Assert.Equal((0f, 0f, 1f), (hard.Soft, hard.Medium, hard.Hard));
        for (var p = 1f; p <= 12f; p += 0.1f)
        {
            var t = ChuffVoice.Tiers(p, 6f, 9f);
            Assert.Equal(1f, t.Soft * t.Soft + t.Medium * t.Medium + t.Hard * t.Hard, 3);
        }
    }

    [Fact]
    public void Each_engine_keeps_its_own_slightly_uneven_beat()
    {
        var a = ChuffVoice.Character(1234, 4);
        var again = ChuffVoice.Character(1234, 4);
        var other = ChuffVoice.Character(98765, 4);
        Assert.Equal(4, a.Length);
        Assert.Equal(a.Select(b => (b.Volume, b.Lateness, b.Pitch)), again.Select(b => (b.Volume, b.Lateness, b.Pitch)));
        Assert.NotEqual(a.Select(b => b.Volume), other.Select(b => b.Volume));
        foreach (var beat in a.Concat(other))
        {
            Assert.InRange(beat.Volume, 0.85f, 1.14f);
            Assert.InRange(beat.Lateness, 0f, 0.066f);
            Assert.InRange(beat.Pitch, 0.979f, 1.021f);
        }
        // The first side sets the time; only the second is late beyond a touch.
        Assert.True(a[0].Lateness <= 0.015f && a[2].Lateness <= 0.015f);
        Assert.Single(ChuffVoice.Character(7, 1));
    }

    [Fact]
    public void Beats_sharpen_toward_the_hand_over_to_the_loops()
    {
        Assert.Equal(1f, ChuffVoice.Sharpen(0.5f));
        Assert.Equal(1f + ChuffVoice.SharpestPitch, ChuffVoice.Sharpen(5f), 4);
        Assert.True(ChuffVoice.Sharpen(3f) > 1f && ChuffVoice.Sharpen(3f) < ChuffVoice.Sharpen(4f));
    }

    [Fact]
    public void A_drifting_engine_pants_as_hard_as_its_cylinders_pump()
    {
        Assert.Equal(0f, ChuffVoice.Breath(0f, false));
        Assert.Equal(ChuffVoice.BreathLoudness, ChuffVoice.Breath(-Drifting.DragHookedUp, false), 4);
        Assert.Equal(ChuffVoice.BreathLoudness * 1.5f, ChuffVoice.Breath(-10f, false), 4);
        Assert.True(ChuffVoice.Breath(-Drifting.DragHookedUp, true) > ChuffVoice.Breath(-Drifting.DragHookedUp, false));
    }

    [Fact]
    public void Walls_give_back_an_echo_after_the_sound_has_been_there_and_back()
    {
        Assert.Equal(0.1166f, ChuffVoice.EchoDelay(20f), 3);
        Assert.Equal(0f, ChuffVoice.EchoLoudness(5f));
        Assert.Equal(ChuffVoice.Reflection, ChuffVoice.EchoLoudness(ChuffVoice.NearestEcho), 4);
        Assert.Equal(ChuffVoice.Reflection / 4f, ChuffVoice.EchoLoudness(4f * ChuffVoice.NearestEcho), 4);
        Assert.Equal(0f, ChuffVoice.EchoLoudness(100f));
    }

    [Fact]
    public void Distance_takes_the_highs_first_and_less_on_a_still_damp_day()
    {
        Assert.Equal(22000f, ChuffVoice.HeardCutoff(0f, 0.5f), 0);
        Assert.Equal(2500f, ChuffVoice.HeardCutoff(5000f, 0f), 0);
        Assert.True(ChuffVoice.HeardCutoff(600f, 1f) > ChuffVoice.HeardCutoff(600f, 0f));
        Assert.True(ChuffVoice.HeardCutoff(300f, 0.5f) > ChuffVoice.HeardCutoff(900f, 0.5f));
        Assert.Equal(1f, ChuffVoice.Carry(0f, 1f), 4);
        Assert.Equal(0f, ChuffVoice.Carry(ChuffVoice.CarryWind, 0f), 4);
        Assert.True(ChuffVoice.Carry(2f, 0.8f) > ChuffVoice.Carry(8f, 0.8f));
        Assert.True(ChuffVoice.Carry(2f, 0.9f) > ChuffVoice.Carry(2f, 0.3f));
    }

    [Fact]
    public void A_tunnel_gives_back_quick_fading_returns()
    {
        var last = (Delay: 0f, Loudness: 1f);
        for (var i = 0; i < ChuffVoice.TunnelEchoes; i++)
        {
            var echo = (Delay: ChuffVoice.TunnelEchoDelay(i), Loudness: ChuffVoice.TunnelEchoLoudness(i));
            Assert.True(echo.Delay > last.Delay && echo.Delay < 0.15f);
            Assert.True(echo.Loudness < last.Loudness && echo.Loudness > 0f);
            last = echo;
        }
    }

    [Fact]
    public void The_loops_take_over_as_loud_as_the_beats()
    {
        // The game's loops rise evenly over 6 bar: their share there is the level over 6.
        foreach (var p in new[] { 1f, 2f, 4f, 7f, 10f })
            Assert.Equal(ChuffVoice.Loudness(p), ChuffVoice.LoopLevel(p) / ChuffVoice.LoudestGauge, 4);
        Assert.Equal(ChuffVoice.LoudestGauge, ChuffVoice.LoopLevel(9f), 4);
    }
}
