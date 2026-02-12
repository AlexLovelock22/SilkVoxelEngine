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
        // The angle moves the sun from East to West
        float angle = (DayProgress * MathF.PI * 2.0f) - (MathF.PI / 2.0f);

        float x = MathF.Cos(angle);
        float y = MathF.Sin(angle);

        // THE FIX: Increased the Z-axis tilt to 0.5f to force diagonal light rays.
        // Also ensuring Y doesn't go too far below the horizon during tests.
        Vector3 rawDir = new Vector3(x, MathF.Max(y, -0.2f), 0.5f);
        SunDirection = Vector3.Normalize(rawDir);

        if (TotalTicks % 1000 == 0)
        {
            Console.WriteLine($"[TIME] Progress: {DayProgress:F2} | SunDir: {SunDirection.X:F2}, {SunDirection.Y:F2}, {SunDirection.Z:F2}");
        }
    }
}