using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Converts a world-space point to the stable redline coordinate system of an
    /// orthographic viewpoint. The calculation deliberately has no viewer/window
    /// dependency, so the saved redline round-trips across different window sizes.
    /// </summary>
    public static class OrthographicRedlineProjectionHelper
    {
        public static bool TryProject(
            double worldX,
            double worldY,
            double worldZ,
            double cameraX,
            double cameraY,
            double cameraZ,
            double rotationA,
            double rotationB,
            double rotationC,
            double rotationD,
            double heightField,
            double aspectRatio,
            out double redlineX,
            out double redlineY)
        {
            redlineX = 0;
            redlineY = 0;
            if (!IsFinite(worldX) || !IsFinite(worldY) || !IsFinite(worldZ) ||
                !IsFinite(cameraX) || !IsFinite(cameraY) || !IsFinite(cameraZ) ||
                !IsFinite(rotationA) || !IsFinite(rotationB) || !IsFinite(rotationC) || !IsFinite(rotationD) ||
                !IsFinite(heightField) || !IsFinite(aspectRatio) || heightField <= 1e-9 || aspectRatio <= 1e-9)
            {
                return false;
            }

            // Navisworks Rotation3D stores the quaternion vector part in A/B/C
            // (x/y/z) and the scalar part in D (w). Transform the world-space
            // delta by the inverse rotation to get camera-space axes.
            var norm = Math.Sqrt(rotationA * rotationA + rotationB * rotationB + rotationC * rotationC + rotationD * rotationD);
            if (!IsFinite(norm) || norm <= 1e-12)
                return false;

            var x = rotationA / norm;
            var y = rotationB / norm;
            var z = rotationC / norm;
            var w = rotationD / norm;
            var dx = worldX - cameraX;
            var dy = worldY - cameraY;
            var dz = worldZ - cameraZ;

            // Transpose of the quaternion rotation matrix: world -> camera.
            // Transpose of the quaternion rotation matrix. Navisworks stores
            // redline coordinates directly in these camera-space document units.
            redlineX = (1 - 2 * y * y - 2 * z * z) * dx + (2 * x * y + 2 * w * z) * dy + (2 * x * z - 2 * w * y) * dz;
            redlineY = (2 * x * y - 2 * w * z) * dx + (1 - 2 * x * x - 2 * z * z) * dy + (2 * y * z + 2 * w * x) * dz;
            return IsFinite(redlineX) && IsFinite(redlineY);
        }

        public static double GetMinimumRadiusFromSize(double documentUnits)
        {
            return Math.Max(0, documentUnits) / 2.0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
