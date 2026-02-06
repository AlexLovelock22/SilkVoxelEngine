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

    public void Update(double dt, IKeyboard keyboard, VoxelWorld world)
    {
        float delta = (float)dt;

        if (keyboard.IsKeyPressed(Key.Space))
        {
            long currentTime = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
            if (currentTime - _lastSpaceTime < 250 && currentTime - _lastSpaceTime > 50)
            {
                IsFlying = !IsFlying;
                Velocity = Vector3.Zero;
            }
            _lastSpaceTime = currentTime;
        }

        Vector3 inputDir = GetInputDirection(keyboard);
        float currentSpeed = IsFlying ? 20.0f : WALK_SPEED;

        if (IsFlying)
        {
            Velocity.Y = 0;
            if (keyboard.IsKeyPressed(Key.Space)) Position.Y += currentSpeed * delta;
            if (keyboard.IsKeyPressed(Key.ShiftLeft)) Position.Y -= currentSpeed * delta;
            IsGrounded = false;
        }
        else
        {
            if (IsGrounded)
            {
                Velocity.Y = 0;
                if (keyboard.IsKeyPressed(Key.Space))
                {
                    Velocity.Y = JUMP_FORCE;
                    IsGrounded = false;
                }
            }
            else
            {
                Velocity.Y += GRAVITY * delta;
            }
        }

        float moveX = inputDir.X * currentSpeed * delta;
        if (moveX != 0)
        {
            Vector3 nextX = Position + new Vector3(moveX, 0, 0);
            if (!CheckCollision(nextX, world))
                Position.X = nextX.X;
        }

        float moveZ = inputDir.Z * currentSpeed * delta;
        if (moveZ != 0)
        {
            Vector3 nextZ = Position + new Vector3(0, 0, moveZ);
            if (!CheckCollision(nextZ, world))
                Position.Z = nextZ.Z;
        }

        if (!IsFlying)
        {
            float moveY = Velocity.Y * delta;
            Vector3 nextY = Position + new Vector3(0, moveY, 0);

            if (CheckCollision(nextY, world))
            {
                if (Velocity.Y < 0) // Falling
                {
                    Position.Y = MathF.Floor(nextY.Y + 0.05f) + 1.0f;
                    IsGrounded = true;
                }
                Velocity.Y = 0;
            }
            else
            {
                Position.Y = nextY.Y;
                IsGrounded = CheckCollision(Position + new Vector3(0, -0.01f, 0), world);
            }
        }
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

                    if (world.GetBlock(bx, by, bz) != 0) return true;
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