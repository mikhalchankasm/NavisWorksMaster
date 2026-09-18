using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace NavisHelper.Agent.Services
{
    internal sealed class SelectionGeometryExportService
    {
        public ExportSelectionGeometryResponse Export(Document document, ExportSelectionGeometryRequest request, MatchSessionStore store)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ExportSelectionGeometryRequest();
            var format = Option(request.Format, "obj", "obj", "jsonl", "stl", "ply");
            var space = Option(request.CoordinateSpace, "world", "world", "item_local");
            var groupBy = Option(request.GroupBy, "fragment", "item", "fragment");
            if (!GeometryExportPath.IsFullyQualifiedWindowsPath(request.OutputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath must be an absolute file path on the Navisworks host.");
            string output;
            try { output = Path.GetFullPath(request.OutputPath); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid outputPath: " + ex.Message); }
            if (!string.Equals(Path.GetExtension(output), "." + format, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath extension must match format.");
            var itemLimit = request.ItemLimit ?? 1000;
            var maxTriangles = request.MaxTriangles ?? 1000000;
            if (itemLimit < 1 || itemLimit > 10000 || maxTriangles < 1 || maxTriangles > 5000000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "itemLimit must be 1..10000; maxTriangles must be 1..5000000.");
            if (File.Exists(output) && request.Overwrite != true)
                throw new IOException("Output exists. Pass overwrite=true to replace it.");
            var inputs = ModelItemScopeResolver.Resolve(document, request.Scope, request.MatchHandles, store);
            if (inputs.Count == 0) throw new AgentCommandException(ErrorCodes.CommandFailed, "The geometry scope is empty.");
            if (inputs.Count > itemLimit) throw new AgentCommandException(ErrorCodes.SchemaViolation, "Input itemLimit exceeded. Narrow the scope.");
            var response = new ExportSelectionGeometryResponse
            { OutputPath=output, Format=format, CoordinateSpace=space, Units=space=="world" ? document.Units.ToString() : "item_native",
                DocumentUnits=document.Units.ToString(), InputItemCount=inputs.Count };
            var timer = Stopwatch.StartNew();
            Action checkDeadline = () =>
            {
                if (timer.ElapsedMilliseconds > 40000)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Geometry export exceeded 40 seconds. Narrow the scope; no output was published.");
            };
            var staging = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var spool = staging + ".mesh";
            var fragments = new List<GeometryFragmentInfo>();
            try
            {
                BinaryWriter binary = null;
                if (request.Apply == true)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    binary = new BinaryWriter(new FileStream(spool, FileMode.CreateNew, FileAccess.Write, FileShare.None));
                }
                using (binary)
                {
                    var visited = new HashSet<ModelItem>();
                    var pending = new Stack<ModelItem>(inputs.AsEnumerable().Reverse());
                    while (pending.Count > 0)
                    {
                        checkDeadline();
                        var item = pending.Pop();
                        if (!visited.Add(item)) continue;
                        if (visited.Count > 1000000) throw new AgentCommandException(ErrorCodes.CommandFailed, "Geometry traversal exceeds 1,000,000 nodes. Narrow the scope.");
                        foreach(var child in item.Children.Reverse()) pending.Push(child);
                        if (!item.HasGeometry) continue;
                        if (++response.GeometryItemCount > itemLimit)
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Geometry itemLimit exceeded. Narrow the scope; partial meshes are not published.");
                        double[] itemToWorld = null;
                        double[] worldToItem = null;
                        var path = (ComApi.InwOaPath3)ComBridge.ToInwOaPath(item);
                        foreach (ComApi.InwOaFragment3 fragment in path.Fragments())
                        {
                            checkDeadline();
                            // A container path can enumerate descendant fragments: only
                            // export the owner here. Traversal visits descendants separately.
                            if (!item.Equals(ComBridge.ToModelItem(fragment.path))) continue;
                            if (fragments.Count >= 20000) throw new AgentCommandException(ErrorCodes.CommandFailed, "Fragment limit (20000) exceeded. Narrow the scope.");
                            var transform = fragment.GetLocalToWorldMatrix();
                            var matrix = GeometryExportMath.ReadArray(transform.Matrix, 16);
                            if (itemToWorld == null)
                            {
                                itemToWorld = matrix;
                                if (space == "item_local") worldToItem = GeometryExportMath.Inverse(itemToWorld);
                            }
                            var info = new GeometryFragmentInfo
                            {
                                ItemId=response.GeometryItemCount, FragmentId=fragments.Count+1,
                                DisplayName=item.DisplayName, Path=ModelItemScopeResolver.PathOf(item),
                                FragmentToWorld=matrix, OutputToWorld=space=="world" ? GeometryExportMath.Identity : itemToWorld,
                                ReversesOrientation=fragment.IsTransformReverses,
                            };
                            fragments.Add(info);
                            var callback = new PrimitivesCallback(binary, info, response, matrix, worldToItem, maxTriangles, checkDeadline);
                            fragment.GenerateSimplePrimitives(ComApi.nwEVertexProperty.eNONE, callback);
                            callback.ThrowIfFailed();
                        }
                    }
                }
                response.FragmentCount = fragments.Count;
                if (response.TriangleCount == 0) response.Warnings.Add("No triangle geometry found in this scope.");
                if (response.LineCount + response.PointCount + response.SnapPointCount > 0)
                    response.Warnings.Add("This is a triangle export. Lines, points and snap points are counted but not exported.");
                if (space=="item_local") response.Warnings.Add("Each geometry item uses its first owned fragment's local frame. OutputToWorld reconstructs world coordinates; local units may vary with source transforms.");
                if (format=="stl") response.Warnings.Add("STL readers may discard solid names. Prefer OBJ, PLY or JSONL for reliable node/fragment metadata.");
                if (request.Apply == true)
                {
                    checkDeadline();
                    GeometryMeshWriter.Write(staging, spool, format, groupBy, space, response.DocumentUnits, fragments, checkDeadline);
                    checkDeadline();
                    if (File.Exists(output))
                    {
                        if (request.Overwrite != true) throw new IOException("Output appeared during export; overwrite is disabled.");
                        File.Replace(staging, output, null);
                    }
                    else File.Move(staging, output);
                    response.Applied = true;
                    response.FileSizeBytes = new FileInfo(output).Length;
                }
                return response;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
                if (File.Exists(spool)) File.Delete(spool);
            }
        }

        private static string Option(string value, string fallback, params string[] allowed)
        {
            value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
            if (!allowed.Contains(value)) throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported export option: " + value);
            return value;
        }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class PrimitivesCallback : ComApi.InwSimplePrimitivesCB
        {
            private readonly BinaryWriter _writer;
            private readonly GeometryFragmentInfo _fragment;
            private readonly ExportSelectionGeometryResponse _response;
            private readonly double[] _toWorld, _worldToItem;
            private readonly int _limit;
            private readonly Action _deadline;
            private Exception _failure;
            public PrimitivesCallback(BinaryWriter writer, GeometryFragmentInfo fragment, ExportSelectionGeometryResponse response,
                double[] toWorld, double[] worldToItem, int limit, Action deadline)
            { _writer=writer; _fragment=fragment; _response=response; _toWorld=toWorld; _worldToItem=worldToItem; _limit=limit; _deadline=deadline; }
            public void ThrowIfFailed() { if (_failure != null) throw _failure; }
            private bool CanContinue()
            {
                if (_failure != null) return false;
                try { _deadline(); return true; }
                catch (Exception ex) { _failure=ex; return false; }
            }
            public void Triangle(ComApi.InwSimpleVertex a, ComApi.InwSimpleVertex b, ComApi.InwSimpleVertex c)
            {
                try
                {
                    if (!CanContinue()) return;
                    if (_response.TriangleCount >= _limit) throw new AgentCommandException(ErrorCodes.CommandFailed, "maxTriangles exceeded. Narrow the scope; partial meshes are not published.");
                    foreach(var vertex in new[] { a,b,c })
                    {
                        var p=GeometryExportMath.Transform(_toWorld, GeometryExportMath.ReadArray(vertex.coord,3));
                        if (_worldToItem != null) p=GeometryExportMath.Transform(_worldToItem,p);
                        if (_writer != null) foreach(var coordinate in p) _writer.Write(coordinate);
                    }
                    _fragment.TriangleCount++;
                    _response.TriangleCount++;
                }
                // Never throw a managed exception into Autodesk's COM traversal.
                // Subsequent callbacks become no-ops; the caller raises the saved
                // failure after GenerateSimplePrimitives returns.
                catch(Exception ex) { _failure=ex; }
            }
            public void Line(ComApi.InwSimpleVertex a, ComApi.InwSimpleVertex b) { if (CanContinue()) _response.LineCount++; }
            public void Point(ComApi.InwSimpleVertex a) { if (CanContinue()) _response.PointCount++; }
            public void SnapPoint(ComApi.InwSimpleVertex a) { if (CanContinue()) _response.SnapPointCount++; }
        }
    }
}
