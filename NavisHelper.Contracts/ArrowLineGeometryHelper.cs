using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class ArrowLineGeometryHelper
    {
        public static IList<MarkupRedlineLine> Build(double tailX, double tailY, double tipX, double tipY)
        {
            var result = new List<MarkupRedlineLine>
            {
                new MarkupRedlineLine { StartX = tailX, StartY = tailY, EndX = tipX, EndY = tipY },
            };
            var vectorX = tailX - tipX;
            var vectorY = tailY - tipY;
            var length = Math.Sqrt(vectorX * vectorX + vectorY * vectorY);
            if (length < 1e-9)
                return result;

            var unitX = vectorX / length;
            var unitY = vectorY / length;
            var headLength = length * 0.30;
            var headHalfWidth = length * 0.16;
            var baseX = tipX + unitX * headLength;
            var baseY = tipY + unitY * headLength;
            var perpendicularX = -unitY;
            var perpendicularY = unitX;
            result.Add(new MarkupRedlineLine
            {
                StartX = tipX,
                StartY = tipY,
                EndX = baseX + perpendicularX * headHalfWidth,
                EndY = baseY + perpendicularY * headHalfWidth,
            });
            result.Add(new MarkupRedlineLine
            {
                StartX = tipX,
                StartY = tipY,
                EndX = baseX - perpendicularX * headHalfWidth,
                EndY = baseY - perpendicularY * headHalfWidth,
            });
            return result;
        }
    }

    public sealed class MarkupRedlineLine
    {
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
    }
}
