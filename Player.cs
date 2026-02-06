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
        if (keyboard.IsKeyPressed(Key.Space))
        {
            if (currentTime - _lastSpaceTime < 250 && currentTime - _lastSpaceTime > 50)
            {
                IsFlying = !IsFlying;
                Velocity.Y = 0;
                _lastSpaceTime = 0;
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
        Vector3 moveDir = GetInputDirection(keyboard);

        float currentMaxSpeed = IsFlying ? 20.0f : (WALK_SPEED * 1.6f);
        float swimSpeed = WALK_SPEED * 0.7f;

        // 3. Apply Vertical Physics
        if (IsFlying)
        {
            Velocity.Y = 0;
            if (keyboard.IsKeyPressed(Key.Space)) Position.Y += 20.0f * dt;
            if (keyboard.IsKeyPressed(Key.ShiftLeft)) Position.Y -= 20.0f * dt;
            IsGrounded = false;
        }
        else if (inWater)
        {
            IsGrounded = false;
            Velocity.Y -= Velocity.Y * 3.0f * dt; // Water Drag
            Velocity.Y -= 4.0f * dt;             // Sinking force

            if (keyboard.IsKeyPressed(Key.Space)) Velocity.Y += (swimSpeed * 8.0f) * dt;
            if (keyboard.IsKeyPressed(Key.ShiftLeft)) Velocity.Y -= (swimSpeed * 5.0f) * dt;
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
                Velocity.Y += GRAVITY * dt;
            }
        }

        // 4. Horizontal Velocity with Tuned Inertia
        if (IsFlying)
        {
            Velocity.X = moveDir.X * currentMaxSpeed;
            Velocity.Z = moveDir.Z * currentMaxSpeed;
        }
        else
        {
            // --- TUNED PHYSICS CONSTANTS ---
            float groundDrag = 12.0f;  // Higher = stops faster on ground
            float airDrag = 1.0f;     // Lower = carries momentum in air
            float currentDrag = IsGrounded ? groundDrag : airDrag;

            // Acceleration power (8.0f on ground feels heavy but responsive)
            float accelPower = IsGrounded ? 8.0f : 1.2f;

            // 1. Apply Drag (Friction)
            Velocity.X -= Velocity.X * currentDrag * dt;
            Velocity.Z -= Velocity.Z * currentDrag * dt;

            // 2. Add movement thrust
            if (moveDir.Length() > 0)
            {
                float targetSpeed = inWater ? swimSpeed : currentMaxSpeed;
                Velocity.X += moveDir.X * (targetSpeed * accelPower) * dt;
                Velocity.Z += moveDir.Z * (targetSpeed * accelPower) * dt;

                // Apply camera-look swimming if in water
                if (inWater) Velocity.Y += moveDir.Y * (targetSpeed * accelPower) * dt;
            }

            // 3. Speed Governor (Hard Cap)
            // Prevents acceleration math from making the player "super-sonic"
            float horizontalSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
            if (horizontalSpeed > currentMaxSpeed && !inWater)
            {
                Velocity.X = (Velocity.X / horizontalSpeed) * currentMaxSpeed;
                Velocity.Z = (Velocity.Z / horizontalSpeed) * currentMaxSpeed;
            }
        }

        // 5. PER-AXIS COLLISION RESOLUTION
        // X Axis
        float moveX = Velocity.X * dt;
        if (moveX != 0)
        {
            Vector3 nextX = Position + new Vector3(moveX, 0, 0);
            if (!CheckCollision(nextX, world))
                Position.X = nextX.X;
            else
                Velocity.X = 0;
        }

        // Z Axis
        float moveZ = Velocity.Z * dt;
        if (moveZ != 0)
        {
            Vector3 nextZ = Position + new Vector3(0, 0, moveZ);
            if (!CheckCollision(nextZ, world))
                Position.Z = nextZ.Z;
            else
                Velocity.Z = 0;
        }

        // Y Axis
        if (!IsFlying)
        {
            float moveY = Velocity.Y * dt;
            Vector3 nextY = Position + new Vector3(0, moveY, 0);

            if (CheckCollision(nextY, world))
            {
                if (Velocity.Y < 0) // Landing
                {
                    Position.Y = MathF.Floor(nextY.Y + 0.05f) + 1.0f;
                    IsGrounded = true;
                }
                Velocity.Y = 0;
            }
            else
            {
                Position.Y = nextY.Y;
                // Constant check to see if we've walked off an edge
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