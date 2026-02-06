using System.Numerics;
using Silk.NET.Input;
using VoxelEngine_Silk.Net_1._0.World;
using System.Diagnostics;

namespace VoxelEngine_Silk.Net_1._0;

public class Player
{
    public Vector3 Position;
    public Vector3 Velocity;
    public bool IsGrounded { get; private set; }
    public bool IsFlying = false;
    private long _lastSpaceTime = 0;

    public float Yaw = -90f;
    public float Pitch = 0f;
    public Vector3 CameraFront { get; private set; } = new Vector3(0, 0, -1);
    public Vector3 CameraUp => Vector3.UnitY;


    public float Height = 1.8f;
    public float Radius = 0.28f;
    public float EyeOffset = 1.6f;
    public float LookSensitivity = 0.1f;

    private const float GRAVITY = -25f;
    private const float JUMP_FORCE = 8.5f;
    private const float WALK_SPEED = 5.0f;
    private const float SKIN_WIDTH = 0.01f;

    public Player(Vector3 startPosition)
    {
        Position = startPosition;
        UpdateCameraVectors();
    }

    public Vector3 GetEyePosition() => Position + new Vector3(0, EyeOffset, 0);

    public void Rotate(Vector2 mouseOffset)
    {
        Yaw += mouseOffset.X * LookSensitivity;
        Pitch -= mouseOffset.Y * LookSensitivity;

        if (Pitch > 89.0f) Pitch = 89.0f;
        if (Pitch < -89.0f) Pitch = -89.0f;

        UpdateCameraVectors();
    }

    private void UpdateCameraVectors()
    {
        float yawRad = Yaw * (float)Math.PI / 180.0f;
        float pitchRad = Pitch * (float)Math.PI / 180.0f;

        Vector3 direction;
        direction.X = MathF.Cos(yawRad) * MathF.Cos(pitchRad);
        direction.Y = MathF.Sin(pitchRad);
        direction.Z = MathF.Sin(yawRad) * MathF.Cos(pitchRad);

        CameraFront = Vector3.Normalize(direction);
    }

    public void Update(double deltaTime, IKeyboard keyboard, VoxelWorld world)
    {
        float dt = (float)deltaTime;
        long currentTime = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

        // 1. Double-Tap Space to Toggle Fly
        // We check this at the very start so it works regardless of being in water/air
        if (keyboard.IsKeyPressed(Key.Space))
        {
            if (currentTime - _lastSpaceTime < 250 && currentTime - _lastSpaceTime > 50)
            {
                IsFlying = !IsFlying;
                Velocity.Y = 0; // Reset momentum on toggle
                _lastSpaceTime = 0; // Prevent triple-tap triggers
            }
            else
            {
                _lastSpaceTime = currentTime;
            }
        }

        // 2. Detect Environment
        Vector3 eyePos = GetEyePosition();
        bool inWater = world.GetBlock((int)Math.Floor(Position.X), (int)Math.Floor(Position.Y), (int)Math.Floor(Position.Z)) == 2 ||
                       world.GetBlock((int)Math.Floor(eyePos.X), (int)Math.Floor(eyePos.Y), (int)Math.Floor(eyePos.Z)) == 2;

        UpdateCameraVectors();
        Vector3 inputDir = GetInputDirection(keyboard);

        // 3. Apply Movement Physics
        if (IsFlying)
        {
            // FLYING PHYSICS
            float flySpeed = WALK_SPEED * 3.0f;
            Velocity.X = inputDir.X * flySpeed;
            Velocity.Z = inputDir.Z * flySpeed;

            if (keyboard.IsKeyPressed(Key.Space)) Velocity.Y = flySpeed;
            else if (keyboard.IsKeyPressed(Key.ShiftLeft)) Velocity.Y = -flySpeed;
            else Velocity.Y = 0;
        }
        else if (inWater)
        {
            // SWIMMING PHYSICS
            IsGrounded = false;
            float swimSpeed = WALK_SPEED * 0.7f;

            // Move in camera direction (allows swimming up/down by looking)
            Vector3 moveDir = CameraFront * inputDir.Z + Vector3.Cross(CameraFront, CameraUp) * inputDir.X;

            if (inputDir.Length() > 0)
            {
                Velocity = moveDir * swimSpeed;
            }
            else
            {
                Velocity.X = 0;
                Velocity.Z = 0;
                Velocity.Y -= GRAVITY * 0.05f * dt; // Slow sink
            }

            if (keyboard.IsKeyPressed(Key.Space)) Velocity.Y = swimSpeed * 0.8f;
            if (keyboard.IsKeyPressed(Key.ShiftLeft)) Velocity.Y = -swimSpeed * 0.8f;
        }
        else
        {
            // WALKING PHYSICS
            Velocity.X = inputDir.X * WALK_SPEED;
            Velocity.Z = inputDir.Z * WALK_SPEED;

            if (!IsGrounded) Velocity.Y += GRAVITY * dt;

            if (keyboard.IsKeyPressed(Key.Space) && IsGrounded)
            {
                Velocity.Y = JUMP_FORCE;
                IsGrounded = false;
            }
        }

        // 4. Collision Resolution (X, Y, Z)
        Vector3 nextPos = Position + Velocity * dt;

        if (!CheckCollision(new Vector3(nextPos.X, Position.Y, Position.Z), world))
            Position.X = nextPos.X;
        else Velocity.X = 0;

        if (!CheckCollision(new Vector3(Position.X, nextPos.Y, Position.Z), world))
            Position.Y = nextPos.Y;
        else
        {
            if (Velocity.Y < 0) IsGrounded = true;
            Velocity.Y = 0;
        }

        if (!CheckCollision(new Vector3(Position.X, Position.Y, nextPos.Z), world))
            Position.Z = nextPos.Z;
        else Velocity.Z = 0;
    }

    private bool CheckCollision(Vector3 pos, VoxelWorld world)
    {
        float[] yOffsets = { 0.1f, Height / 2, Height - 0.05f };
        float[] xzOffsets = { -Radius, Radius };

        foreach (float y in yOffsets)
        {
            foreach (float xOff in xzOffsets)
            {
                foreach (float zOff in xzOffsets)
                {
                    Vector3 checkPos = pos + new Vector3(xOff, y, zOff);

                    int bx = (int)Math.Floor(checkPos.X);
                    int by = (int)Math.Floor(checkPos.Y);
                    int bz = (int)Math.Floor(checkPos.Z);

                    byte block = world.GetBlock(bx, by, bz);

                    // Ignore Air (0) and Water (2) for physical movement collision
                    if (block != 0 && block != 2) return true;
                }
            }
        }
        return false;
    }

    private Vector3 GetInputDirection(IKeyboard keyboard)
    {
        Vector3 move = Vector3.Zero;
        Vector3 forward = Vector3.Normalize(new Vector3(CameraFront.X, 0, CameraFront.Z));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        if (keyboard.IsKeyPressed(Key.W)) move += forward;
        if (keyboard.IsKeyPressed(Key.S)) move -= forward;
        if (keyboard.IsKeyPressed(Key.A)) move -= right;
        if (keyboard.IsKeyPressed(Key.D)) move += right;

        return move != Vector3.Zero ? Vector3.Normalize(move) : Vector3.Zero;
    }
}