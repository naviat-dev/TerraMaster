using System.Numerics;

public class GreatCircleInterpolator
{
    // Convert lat/lon (degrees) to a 3D Cartesian point on the unit sphere
    private static Vector3 LatLonToVector(double latDeg, double lonDeg)
    {
        double lat = DegreesToRadians(latDeg);
        double lon = DegreesToRadians(lonDeg);
        double x = Math.Cos(lat) * Math.Cos(lon);
        double y = Math.Cos(lat) * Math.Sin(lon);
        double z = Math.Sin(lat);
        return new Vector3((float)x, (float)y, (float)z);
    }

    // Convert 3D vector back to lat/lon
    private static (double latDeg, double lonDeg) VectorToLatLon(Vector3 vec)
    {
        vec = Vector3.Normalize(vec); // Ensure it's on the unit sphere
        double lat = Math.Asin(vec.Z);
        double lon = Math.Atan2(vec.Y, vec.X);
        return (RadiansToDegrees(lat), RadiansToDegrees(lon));
    }

    // Spherical linear interpolation
    private static Vector3 Slerp(Vector3 p0, Vector3 p1, double t)
    {
        float dot = Vector3.Dot(p0, p1);
        dot = Math.Clamp(dot, -1.0f, 1.0f);
        double omega = Math.Acos(dot);
        double sinOmega = Math.Sin(omega);

        if (sinOmega < 1e-6)
        {
            return Vector3.Normalize(Vector3.Lerp(p0, p1, (float)t)); // fallback to lerp if very close
        }

        double a = Math.Sin((1 - t) * omega) / sinOmega;
        double b = Math.Sin(t * omega) / sinOmega;

        return Vector3.Normalize((float)a * p0 + (float)b * p1);
    }

    // Main method to get points along the great circle
    public static List<(double lat, double lon)> GetGreatCirclePoints(double lat1, double lon1, double lat2, double lon2, int numPoints)
    {
        var p0 = LatLonToVector(lat1, lon1);
        var p1 = LatLonToVector(lat2, lon2);

        var result = new List<(double, double)>();

        for (int i = 0; i < numPoints; i++)
        {
            double t = (double)i / (numPoints - 1);
            var pi = Slerp(p0, p1, t);
            result.Add(VectorToLatLon(pi));
        }

        return result;
    }

    // Utility methods
    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
