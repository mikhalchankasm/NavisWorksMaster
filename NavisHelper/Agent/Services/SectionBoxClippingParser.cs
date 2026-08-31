using System;
using System.IO;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed class ParsedSectionBox
    {
        public BoxVector3 Minimum { get; set; }
        public BoxVector3 Maximum { get; set; }
        public BoxVector3 EulerRadians { get; set; }
    }

    internal sealed class SectionBoxParseException : Exception
    {
        public SectionBoxParseException(string errorCode, string message, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; private set; }
    }

    internal static class SectionBoxClippingParser
    {
        public static ParsedSectionBox Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new SectionBoxParseException(
                    ErrorCodes.SectionBoxNotEnabled,
                    "The active view has no enabled Section Box.");
            }

            JObject root;
            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new StrictJsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    SupportMultipleContent = false,
                })
                {
                    root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Ignore,
                    });
                    if (reader.Read())
                        throw new JsonReaderException("Unexpected content after the clipping object.");
                }
            }
            catch (JsonException ex)
            {
                throw new SectionBoxParseException(
                    ErrorCodes.SectionBoxPayloadUnsupported,
                    "The active view returned malformed clipping JSON.",
                    ex);
            }

            RequireString(root, "Type", "ClipPlaneSet", "clipping root");
            RequireVersion(root, "clipping root");
            var enabled = RequireBoolean(root, "Enabled", "clipping root");
            var orientedBoxToken = GetProperty(root, "OrientedBox");
            var planesToken = GetProperty(root, "Planes");

            if (!enabled)
            {
                throw new SectionBoxParseException(
                    ErrorCodes.SectionBoxNotEnabled,
                    "The active Section Box is disabled.");
            }

            if (planesToken != null && planesToken.Type != JTokenType.Null)
            {
                var planes = planesToken as JArray;
                if (planes == null)
                    throw Unsupported("clipping root.Planes must be an array when present.");
                if (planes.Count > 0)
                {
                    throw new SectionBoxParseException(
                        ErrorCodes.SectionBoxModeUnsupported,
                        "Sectioning is enabled in plane mode, not oriented-box mode.");
                }
            }

            if (orientedBoxToken == null || orientedBoxToken.Type == JTokenType.Null)
            {
                throw new SectionBoxParseException(
                    ErrorCodes.SectionBoxModeUnsupported,
                    "Sectioning is enabled, but the active mode is not an oriented box.");
            }

            var orientedBox = orientedBoxToken as JObject;
            if (orientedBox == null)
                throw Unsupported("OrientedBox must be an object.");
            RequireString(orientedBox, "Type", "OrientedBox3D", "OrientedBox");
            RequireVersion(orientedBox, "OrientedBox");

            var box = GetProperty(orientedBox, "Box") as JArray;
            if (box == null || box.Count != 2)
                throw Unsupported("OrientedBox.Box must contain minimum and maximum coordinate arrays.");
            var minimum = ReadVector(box[0], "OrientedBox.Box[0]");
            var maximum = ReadVector(box[1], "OrientedBox.Box[1]");
            try
            {
                SectionBoxGeometryRules.ValidateBounds(minimum, maximum);
            }
            catch (ArgumentException ex)
            {
                throw Unsupported(ex.Message, ex);
            }
            if (minimum.X == maximum.X || minimum.Y == maximum.Y || minimum.Z == maximum.Z)
                throw Unsupported("OrientedBox.Box must have positive extents on every axis.");

            var rotation = ReadVector(GetProperty(orientedBox, "Rotation"), "OrientedBox.Rotation");
            return new ParsedSectionBox
            {
                Minimum = minimum,
                Maximum = maximum,
                EulerRadians = rotation,
            };
        }

        private static BoxVector3 ReadVector(JToken token, string name)
        {
            var values = token as JArray;
            if (values == null || values.Count != 3)
                throw Unsupported(name + " must contain exactly three numbers.");
            return new BoxVector3
            {
                X = ReadFiniteNumber(values[0], name + "[0]"),
                Y = ReadFiniteNumber(values[1], name + "[1]"),
                Z = ReadFiniteNumber(values[2], name + "[2]"),
            };
        }

        private static double ReadFiniteNumber(JToken token, string name)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw Unsupported(name + " must be a number.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw Unsupported(name + " must be finite.");
            return value;
        }

        private static void RequireString(JObject value, string propertyName, string expected, string owner)
        {
            var token = GetProperty(value, propertyName);
            if (token == null || token.Type != JTokenType.String ||
                !string.Equals(token.Value<string>(), expected, StringComparison.Ordinal))
            {
                throw Unsupported(owner + "." + propertyName + " must be " + expected + ".");
            }
        }

        private static void RequireVersion(JObject value, string owner)
        {
            var token = GetProperty(value, "Version");
            if (token == null || token.Type != JTokenType.Integer ||
                !string.Equals(token.ToString(Formatting.None), "1", StringComparison.Ordinal))
                throw Unsupported(owner + ".Version must be 1.");
        }

        private static bool RequireBoolean(JObject value, string propertyName, string owner)
        {
            var token = GetProperty(value, propertyName);
            if (token == null || token.Type != JTokenType.Boolean)
                throw Unsupported(owner + "." + propertyName + " must be a boolean.");
            return token.Value<bool>();
        }

        private static JToken GetProperty(JObject value, string propertyName)
        {
            JToken token;
            return value.TryGetValue(propertyName, StringComparison.Ordinal, out token) ? token : null;
        }

        private static SectionBoxParseException Unsupported(string message, Exception innerException = null)
        {
            return new SectionBoxParseException(ErrorCodes.SectionBoxPayloadUnsupported, message, innerException);
        }

        private sealed class StrictJsonTextReader : JsonTextReader
        {
            public StrictJsonTextReader(TextReader reader)
                : base(reader)
            {
            }

            public override bool Read()
            {
                var read = base.Read();
                if (read && TokenType == JsonToken.Comment)
                    throw new JsonReaderException("JSON comments are not supported.");
                return read;
            }
        }
    }
}
