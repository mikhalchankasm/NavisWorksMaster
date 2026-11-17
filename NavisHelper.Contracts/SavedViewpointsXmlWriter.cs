using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SavedViewpointsXmlNode
    {
        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public SavedViewpointCameraParseResult Camera { get; set; }
        public List<SavedViewpointsXmlNode> Children { get; set; } = new List<SavedViewpointsXmlNode>();
    }

    /// <summary>Writes the folder and camera subset read by saved_viewpoints_import.</summary>
    public static class SavedViewpointsXmlWriter
    {
        public static XDocument Write(IEnumerable<SavedViewpointsXmlNode> nodes, string units)
        {
            var viewpoints = new XElement("viewpoints");
            foreach (var node in nodes)
                viewpoints.Add(WriteNode(node));
            return new XDocument(new XElement("exchange",
                string.IsNullOrWhiteSpace(units) ? null : new XAttribute("units", units), viewpoints));
        }

        private static XElement WriteNode(SavedViewpointsXmlNode node)
        {
            if (node.IsFolder)
            {
                var folder = new XElement("viewfolder", new XAttribute("name", node.Name ?? string.Empty));
                foreach (var child in node.Children)
                    folder.Add(WriteNode(child));
                return folder;
            }

            var c = node.Camera;
            if (c == null)
                return null;
            var camera = new XElement("camera",
                new XAttribute("projection", c.Projection == SavedViewpointProjectionKind.Orthographic ? "ortho" : "persp"),
                Number("near", c.NearPlaneDistance), Number("far", c.FarPlaneDistance),
                Number("aspect", c.AspectRatio), Number("height", c.HeightField),
                new XElement("position", new XElement("pos3f",
                    Number("x", c.PositionX), Number("y", c.PositionY), Number("z", c.PositionZ))),
                new XElement("rotation", new XElement("quaternion",
                    Number("a", c.RotationA), Number("b", c.RotationB), Number("c", c.RotationC), Number("d", c.RotationD))));
            var viewpoint = new XElement("viewpoint",
                new XAttribute("render", c.RenderStyle == SavedViewpointRenderStyleKind.FullRender
                    ? "full" : c.RenderStyle.ToString().ToLowerInvariant()),
                new XAttribute("lighting", c.Lighting.ToString().ToLowerInvariant()),
                Number("focal", c.FocalDistance), Number("linear", c.LinearSpeed), Number("angular", c.AngularSpeed), camera);
            if (c.ViewerCameraMode != SavedViewpointCameraModeKind.Unspecified || !string.IsNullOrEmpty(c.ViewerAvatar))
            {
                var viewer = new XElement("viewer");
                if (c.ViewerCameraMode != SavedViewpointCameraModeKind.Unspecified)
                    viewer.Add(new XAttribute("camera_mode", c.ViewerCameraMode == SavedViewpointCameraModeKind.ThirdPerson ? "third" : "first"));
                if (!string.IsNullOrEmpty(c.ViewerAvatar))
                    viewer.Add(new XAttribute("avatar", c.ViewerAvatar));
                viewpoint.Add(viewer);
            }
            if (c.HasWorldUpVector)
                viewpoint.Add(new XElement("up", new XElement("vec3f",
                    Number("x", c.WorldUpX), Number("y", c.WorldUpY), Number("z", c.WorldUpZ))));
            return new XElement("view", new XAttribute("name", node.Name ?? string.Empty), viewpoint);
        }

        private static XAttribute Number(string name, double? value)
        {
            return value.HasValue ? new XAttribute(name, value.Value.ToString("R", CultureInfo.InvariantCulture)) : null;
        }
    }
}
