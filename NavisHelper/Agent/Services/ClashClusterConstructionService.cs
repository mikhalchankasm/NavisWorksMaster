using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashClusterConstructionService
    {
        public static List<List<T>> Build<T>(
            IList<T> rows,
            string groupMode,
            double clusterDistanceUnits,
            Func<T, string> associationKey,
            Func<T, Point3D> clashPoint)
        {
            return ClashClusterBuilder.Build(rows, groupMode, clusterDistanceUnits, associationKey, row =>
            {
                var point = clashPoint(row);
                return point == null ? null : new ClashClusterPoint(point.X, point.Y, point.Z);
            });
        }
    }
}
