using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Parses the saved-viewpoint camera subset supported by saved_viewpoints_import without Navisworks runtime types.
    /// </summary>
    public static class SavedViewpointCameraXmlParser
    {
        public static SavedViewpointCameraParseResult Parse(XElement viewElement)
        {
            var result = new SavedViewpointCameraParseResult();
            var viewpointElement = ChildElement(viewElement, "viewpoint");
            var cameraElement = ChildElement(viewpointElement, "camera");
            if (cameraElement == null)
            {
                result.Warning = "Skipped viewpoint without camera node: " + ReadStringAttribute(viewElement, "name");
                return result;
            }

            var positionElement = cameraElement.Descendants().FirstOrDefault(element => IsElement(element, "pos3f"));
            var quaternionElement = cameraElement.Descendants().FirstOrDefault(element => IsElement(element, "quaternion"));
            if (positionElement == null || quaternionElement == null)
            {
                result.Warning = "Skipped viewpoint without camera position or rotation: " + ReadStringAttribute(viewElement, "name");
                return result;
            }

            result.PositionX = ReadDoubleAttribute(positionElement, "x", 0);
            result.PositionY = ReadDoubleAttribute(positionElement, "y", 0);
            result.PositionZ = ReadDoubleAttribute(positionElement, "z", 0);
            result.RotationA = ReadDoubleAttribute(quaternionElement, "a", 1);
            result.RotationB = ReadDoubleAttribute(quaternionElement, "b", 0);
            result.RotationC = ReadDoubleAttribute(quaternionElement, "c", 0);
            result.RotationD = ReadDoubleAttribute(quaternionElement, "d", 0);

            var projection = ReadStringAttribute(cameraElement, "projection").Trim().ToLowerInvariant();
            result.Projection = projection == "ortho" || projection == "orthographic"
                ? SavedViewpointProjectionKind.Orthographic
                : SavedViewpointProjectionKind.Perspective;
            result.NearPlaneDistance = ReadOptionalDoubleAttribute(cameraElement, "near");
            result.FarPlaneDistance = ReadOptionalDoubleAttribute(cameraElement, "far");
            result.AspectRatio = ReadOptionalDoubleAttribute(cameraElement, "aspect");
            result.HeightField = ReadOptionalDoubleAttribute(cameraElement, "height");

            result.FocalDistance = ReadOptionalDoubleAttribute(viewpointElement, "focal");
            result.LinearSpeed = ReadOptionalDoubleAttribute(viewpointElement, "linear");
            result.AngularSpeed = ReadOptionalDoubleAttribute(viewpointElement, "angular");

            var upElement = viewpointElement.Elements().FirstOrDefault(element => IsElement(element, "up"));
            var upVector = upElement == null
                ? null
                : upElement.Descendants().FirstOrDefault(element => IsElement(element, "vec3f"));
            if (upVector != null)
            {
                result.HasWorldUpVector = true;
                result.WorldUpX = ReadDoubleAttribute(upVector, "x", 0);
                result.WorldUpY = ReadDoubleAttribute(upVector, "y", 0);
                result.WorldUpZ = ReadDoubleAttribute(upVector, "z", 1);
            }

            result.RenderStyle = ParseRenderStyle(ReadStringAttribute(viewpointElement, "render"));
            result.Lighting = ParseLighting(ReadStringAttribute(viewpointElement, "lighting"));

            var viewerElement = ChildElement(viewpointElement, "viewer");
            if (viewerElement != null)
            {
                result.ViewerAvatar = ReadStringAttribute(viewerElement, "avatar");
                result.ViewerCameraMode = ParseCameraMode(ReadStringAttribute(viewerElement, "camera_mode"));
            }

            result.Success = true;
            return result;
        }

        private static SavedViewpointRenderStyleKind ParseRenderStyle(string value)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "full" || value == "fullrender" || value == "rendered")
                return SavedViewpointRenderStyleKind.FullRender;
            if (value == "wireframe" || value == "wire")
                return SavedViewpointRenderStyleKind.Wireframe;
            if (value == "hiddenline" || value == "hidden_line")
                return SavedViewpointRenderStyleKind.HiddenLine;

            return SavedViewpointRenderStyleKind.Shaded;
        }

        private static SavedViewpointLightingKind ParseLighting(string value)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "headlight")
                return SavedViewpointLightingKind.Headlight;
            if (value == "fulllights" || value == "full")
                return SavedViewpointLightingKind.FullLights;
            if (value == "none")
                return SavedViewpointLightingKind.None;

            return SavedViewpointLightingKind.SceneLights;
        }

        private static SavedViewpointCameraModeKind ParseCameraMode(string value)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "third")
                return SavedViewpointCameraModeKind.ThirdPerson;
            if (value == "first")
                return SavedViewpointCameraModeKind.FirstPerson;

            return SavedViewpointCameraModeKind.Unspecified;
        }

        private static XElement ChildElement(XElement element, string localName)
        {
            return element == null
                ? null
                : element.Elements().FirstOrDefault(child => IsElement(child, localName));
        }

        private static bool IsElement(XElement element, string localName)
        {
            return element != null && string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadStringAttribute(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static double ReadDoubleAttribute(XElement element, string name, double fallback)
        {
            var parsed = ReadOptionalDoubleAttribute(element, name);
            return parsed.GetValueOrDefault(fallback);
        }

        private static double? ReadOptionalDoubleAttribute(XElement element, string name)
        {
            double parsed;
            return double.TryParse(
                ReadStringAttribute(element, name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : (double?)null;
        }
    }

    public sealed class SavedViewpointCameraParseResult
    {
        public bool Success { get; set; }
        public string Warning { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public double RotationA { get; set; }
        public double RotationB { get; set; }
        public double RotationC { get; set; }
        public double RotationD { get; set; }
        public SavedViewpointProjectionKind Projection { get; set; }
        public double? NearPlaneDistance { get; set; }
        public double? FarPlaneDistance { get; set; }
        public double? AspectRatio { get; set; }
        public double? HeightField { get; set; }
        public double? FocalDistance { get; set; }
        public double? LinearSpeed { get; set; }
        public double? AngularSpeed { get; set; }
        public bool HasWorldUpVector { get; set; }
        public double WorldUpX { get; set; }
        public double WorldUpY { get; set; }
        public double WorldUpZ { get; set; }
        public SavedViewpointRenderStyleKind RenderStyle { get; set; }
        public SavedViewpointLightingKind Lighting { get; set; }
        public string ViewerAvatar { get; set; }
        public SavedViewpointCameraModeKind ViewerCameraMode { get; set; }
    }

    public enum SavedViewpointProjectionKind
    {
        Perspective,
        Orthographic,
    }

    public enum SavedViewpointRenderStyleKind
    {
        Shaded,
        FullRender,
        Wireframe,
        HiddenLine,
    }

    public enum SavedViewpointLightingKind
    {
        SceneLights,
        Headlight,
        FullLights,
        None,
    }

    public enum SavedViewpointCameraModeKind
    {
        Unspecified,
        ThirdPerson,
        FirstPerson,
    }
}
