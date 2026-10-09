using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>Shared helpers: a stock-formula reference and a scripted fireman.</summary>
internal static class FireTestKit
{
    public const float Dt = 0.05f;

    /// <summary>Air mass per kg of coal in the stock model (stoichiometric × 1.2).</summary>
    public const float StockAirPerCoal = 13.899433f;

    /// <summary>
    /// Stock Firebox heat at a steady combustion rate with full oxygen
    /// availability: rate × 32 MJ/kg × lerp(1, efficiencyAtMax, rate / maxRate).
    /// </summary>
    public static double StockHeat(FireboxParameters p, double rate)
    {
        var t = Math.Clamp(rate / p.MaxCombustionRate, 0, 1);
        var efficiency = 1 + (p.EfficiencyAtMaxCombustion - 1) * t;
        return rate * FireboxParameters.CoalHeatContent * efficiency;
    }

    public static FireInputs Inputs(float airFlow, float door = 0f) => new FireInputs
    {
        AirFlow = airFlow,
        DoorOpening = door,
        StartupMultiplier = 1f,
        BoilerTemperature = 100f,
    };

    /// <summary>Coal that never clinkers, for tests about something else.</summary>
    public static readonly FireTuning CleanCoal = new FireTuning { ClinkerTendency = 0f };

    /// <summary>A lit fire with the given coal spread evenly and already burning.</summary>
    public static FireModel BurningFire(FireboxParameters p, float coal, FireTuning tuning = null)
    {
        var fire = new FireModel(p, tuning);
        fire.Restore(fireOn: true, coalMass: coal);
        return fire;
    }

    public static void Run(FireModel fire, FireInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
            fire.Step(Dt, inputs);
    }
}

/// <summary>A fire tended by a <see cref="Fireman"/> at a share of the stock maximum rate.</summary>
internal sealed class TendedFire
{
    public TendedFire(FireboxParameters p, float load, float coalFactor = 1f, float settleSeconds = 600f, FireTuning tuning = null)
    {
        var rate = load * p.MaxCombustionRate;
        Inputs = FireTestKit.Inputs(rate * FireTestKit.StockAirPerCoal);
        TargetCoal = Math.Min(p.MaxCoalCapacity, coalFactor * rate * p.BurnTime);
        Fire = FireTestKit.BurningFire(p, TargetCoal, tuning);
        Run(settleSeconds);
    }

    public FireModel Fire { get; }

    public Fireman Fireman { get; } = new Fireman();

    public FireInputs Inputs;

    public float TargetCoal { get; }

    /// <summary>Runs with tending; returns the coal burned.</summary>
    public double Run(float seconds)
    {
        double burned = 0;
        for (var t = 0f; t < seconds; t += FireTestKit.Dt)
        {
            Fireman.Tend(Fire, TargetCoal, FireTestKit.Dt);
            burned += Fire.Step(FireTestKit.Dt, Inputs);
        }
        return burned;
    }
}

/// <summary>
/// A competent fireman: follows the fire, shoveling where it needs coal most
/// to hold the coal on the grate around a target, keeps the door closed, and
/// shakes the grate on a fixed interval.
/// </summary>
internal sealed class Fireman
{
    private readonly float shovelMass;
    private readonly float shakeInterval;
    private float sinceShake;

    public Fireman(float shovelMass = 2.5f, float shakeInterval = 300f)
    {
        this.shovelMass = shovelMass;
        this.shakeInterval = shakeInterval;
    }

    public float CoalFed { get; private set; }

    public void Tend(FireModel fire, float targetCoal, float dt)
    {
        while (fire.CoalMass + shovelMass / 2f <= targetCoal)
            CoalFed += fire.AddCoal(shovelMass, fire.NeediestZone());

        sinceShake += dt;
        if (sinceShake >= shakeInterval)
        {
            fire.ShakeGrate();
            sinceShake = 0f;
        }
    }
}
