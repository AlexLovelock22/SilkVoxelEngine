using System.Numerics;

namespace VoxelEngine_Silk.Net_1._0.Game;

public class TimeManager
{
    public const int TicksPerDay = 24000;
    public const double TicksPerSecond = 600.0;
    private const double SecondsPerTick = 1.0 / TicksPerSecond;

    public long TotalTicks { get; private set; }
    private double _tickAccumulator;

    public float DayProgress => (TotalTicks % TicksPerDay) / (float)TicksPerDay;
    public Vector3 SunDirection { get; private set; }

    public void Update(double deltaTime)
    {
        _tickAccumulator += deltaTime;

        while (_tickAccumulator >= SecondsPerTick)
        {
            OnTick();
            _tickAccumulator -= SecondsPerTick;
        }
    }

    private void OnTick()
    {
        TotalTicks++;
        UpdateSunPosition();

        // Future home for: 
        // if (TotalTicks % 100 == 0) DoRandomCropGrowth();
    }
    private void UpdateSunPosition()
    {
        float angle = (DayProgress * MathF.PI * 2.0f) - (MathF.PI / 2.0f);

        float x = MathF.Cos(angle);
        float y = MathF.Sin(angle);

        // We increase the Z-tilt to 0.4f to emphasize those diagonal lines you fixed.
        // Clamping Y to 0.02f prevents the ray from going parallel to the floor (infinite shadow).
        SunDirection = Vector3.Normalize(new Vector3(x, MathF.Max(y, 0.02f), 0.4f));

        if (TotalTicks % 1000 == 0)
        {
            Console.WriteLine($"[TIME] Progress: {DayProgress:F2} | SunDir: {SunDirection.X:F2}, {SunDirection.Y:F2}, {SunDirection.Z:F2}");
        }
    }
}