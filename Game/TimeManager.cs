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
    }

    private void UpdateSunPosition()
    {
        // 1. Calculate the Angle (0 to 2PI)
        // Subtracting PI/2 ensures the sun starts at the horizon (Dawn)
        float dayAngle = (DayProgress * MathF.PI * 2.0f) - (MathF.PI / 2.0f);

        // 2. Simplified Minecraft-style Orbit
        // X moves East to West
        // Y moves Up and Down
        // Z is 0.0 so it travels in a perfectly straight line overhead
        float x = MathF.Cos(dayAngle);
        float y = MathF.Sin(dayAngle);
        float z = 0.0f; 

        // 3. Small Y-bias for shadow stability
        float finalY = y;
        if (MathF.Abs(y) < 0.02f)
        {
            finalY = (y >= 0) ? 0.02f : -0.02f;
        }

        SunDirection = Vector3.Normalize(new Vector3(x, finalY, z));
    }
}