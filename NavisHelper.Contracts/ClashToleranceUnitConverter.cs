using System;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashToleranceUnitConverter
    {
        public static double ToMillimeters(double value, string unitName)
        {
            switch ((unitName ?? string.Empty).Trim())
            {
                case "Centimeters": return value * 10.0;
                case "Meters": return value * 1000.0;
                case "Kilometers": return value * 1000000.0;
                case "Inches": return value * 25.4;
                case "Feet": return value * 304.8;
                case "Yards": return value * 914.4;
                case "Miles": return value * 1609344.0;
                case "Millimeters": return value;
                case "Micrometers": return value * 0.001;
                case "Mils": return value * 0.0254;
                case "Microinches": return value * 0.0000254;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unitName), unitName, "Unsupported Navisworks document units.");
            }
        }
    }
}
