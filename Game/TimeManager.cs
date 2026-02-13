using System.Numerics;

namespace VoxelEngine_Silk.Net_1._0.Game;

public class TimeManager
{
    public const int TicksPerDay = 24000;
    public const double TicksPerSecond = 200.0;
    private const double SecondsPerTick = 1.0 / TicksPerSecond;

    public static long StartTick = 6000;
    public long TotalTicks { get; private set; } // Private set is fine here!
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

    public void OnLoad()
    {
        // This works because OnLoad is INSIDE the class, 
        // so it has permission to change the private TotalTicks.
        TotalTicks = StartTick;
        UpdateSunPosition();
    }

    private void OnTick()
    {
        TotalTicks++;
        UpdateSunPosition();
    }

    private void UpdateSunPosition()
    {
        float dayAngle = (DayProgress * MathF.PI * 2.0f) - (MathF.PI / 2.0f);

        float x = MathF.Cos(dayAngle);
        float y = MathF.Sin(dayAngle);
        float z = 0.0f; 

        float finalY = y;
        if (MathF.Abs(y) < 0.02f)
        {
            finalY = (y >= 0) ? 0.02f : -0.02f;
        }

        SunDirection = Vector3.Normalize(new Vector3(x, finalY, z));
    }
}