using System.Numerics;

public class Frustum
{
    private readonly Plane[] _planes = new Plane[6];

    public void Update(Matrix4x4 matrix)
    {
        // Left
        _planes[0] = Plane.Normalize(new Plane(matrix.M14 + matrix.M11, matrix.M24 + matrix.M21, matrix.M34 + matrix.M31, matrix.M44 + matrix.M41));
        // Right
        _planes[1] = Plane.Normalize(new Plane(matrix.M14 - matrix.M11, matrix.M24 - matrix.M21, matrix.M34 - matrix.M31, matrix.M44 - matrix.M41));
        // Bottom
        _planes[2] = Plane.Normalize(new Plane(matrix.M14 + matrix.M12, matrix.M24 + matrix.M22, matrix.M34 + matrix.M32, matrix.M44 + matrix.M42));
        // Top
        _planes[3] = Plane.Normalize(new Plane(matrix.M14 - matrix.M12, matrix.M24 - matrix.M22, matrix.M34 - matrix.M32, matrix.M44 - matrix.M42));
        // Near
        _planes[4] = Plane.Normalize(new Plane(matrix.M14 + matrix.M13, matrix.M24 + matrix.M23, matrix.M34 + matrix.M33, matrix.M44 + matrix.M43));
        // Far
        _planes[5] = Plane.Normalize(new Plane(matrix.M14 - matrix.M13, matrix.M24 - matrix.M23, matrix.M34 - matrix.M33, matrix.M44 - matrix.M43));
    }

    public bool IsBoxVisible(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector3 normal = _planes[i].Normal;
            float distance = _planes[i].D;

            Vector3 positiveSide = new Vector3(
                normal.X >= 0 ? max.X : min.X,
                normal.Y >= 0 ? max.Y : min.Y,
                normal.Z >= 0 ? max.Z : min.Z
            );

            if (Vector3.Dot(normal, positiveSide) + distance < 0)
                return false;
        }
        return true;
    }
}