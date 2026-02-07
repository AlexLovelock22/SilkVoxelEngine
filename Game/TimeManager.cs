using System.Numerics;

namespace VoxelEngine_Silk.Net_1._0.Game;

public class TimeManager
{
    public const int TicksPerDay = 24000;
    public const double TicksPerSecond = 2000.0;
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
        // Calculate angle based on day progress (0.0 to 1.0)
        // Offset by PI/2 so the sun starts at the horizon (Sunrise)
        float angle = (DayProgress * MathF.PI * 2.0f) - (MathF.PI / 2.0f);

        float x = MathF.Cos(angle);
        float y = MathF.Sin(angle);
        
        // Z gives it a slight tilt so it's not perfectly overhead
        SunDirection = Vector3.Normalize(new Vector3(x, y, 0.2f));
    }
}