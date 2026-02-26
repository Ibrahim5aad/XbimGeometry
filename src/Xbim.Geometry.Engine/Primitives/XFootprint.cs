using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Represents a 2D footprint projected from a 3D shape onto the XY plane.
    /// Contains polygon boundaries with optional holes, serializable to
    /// SFA Well-Known Binary and Well-Known Text formats.
    /// </summary>
    internal class XFootprint : IXFootprint
    {
        private const byte SfaByteOrder = 1; // Little Endian
        private const uint WkbPolygon = 3;
        private const uint WkbMultiPolygon = 6;

        /// <summary>
        /// Parsed polygon bounds. Each entry is a list of rings (outer + holes).
        /// Each ring is an array of (x,y) coordinate pairs.
        /// </summary>
        private readonly List<List<double[][]>> _bounds;

        public double MinZ { get; }
        public double MaxZ { get; }
        public bool IsClose { get; }

        public SFAGeometryType SfaGeometryType
        {
            get
            {
                if (_bounds.Count == 0) return SFAGeometryType.Empty;
                return _bounds.Count == 1 ? SFAGeometryType.Polygon : SFAGeometryType.MultiPolygon;
            }
        }

        public IEnumerable<IEnumerable<IXPolyLoop2d>> Bounds
        {
            get
            {
                var result = new List<IEnumerable<IXPolyLoop2d>>();
                foreach (var bound in _bounds)
                {
                    var rings = new List<IXPolyLoop2d>();
                    foreach (var ring in bound)
                    {
                        var points = new IXPoint[ring.Length];
                        for (int i = 0; i < ring.Length; i++)
                            points[i] = new XPoint(ring[i][0], ring[i][1]);
                        rings.Add(new XPolyLoop2d(points));
                    }
                    result.Add(rings);
                }
                return result;
            }
        }

        /// <summary>
        /// Creates a footprint from the native serialized double buffer returned by
        /// xbim_projection_create_footprint.
        /// </summary>
        internal XFootprint(IntPtr buffer, int bufferLen)
        {
            if (buffer == IntPtr.Zero || bufferLen < 4)
                throw new ArgumentException("Invalid footprint buffer.");

            var data = new double[bufferLen];
            Marshal.Copy(buffer, data, 0, bufferLen);

            int idx = 0;
            MinZ = data[idx++];
            MaxZ = data[idx++];
            IsClose = data[idx++] > 0.5;
            int numBounds = (int)data[idx++];

            _bounds = new List<List<double[][]>>(numBounds);
            for (int b = 0; b < numBounds; b++)
            {
                int numRings = (int)data[idx++];
                var rings = new List<double[][]>(numRings);
                for (int r = 0; r < numRings; r++)
                {
                    int numPoints = (int)data[idx++];
                    var points = new double[numPoints][];
                    for (int p = 0; p < numPoints; p++)
                    {
                        double x = data[idx++];
                        double y = data[idx++];
                        points[p] = new double[] { x, y };
                    }
                    rings.Add(points);
                }
                _bounds.Add(rings);
            }
        }

        /// <summary>
        /// Writes the footprint in SFA Well-Known Binary format.
        /// </summary>
        public void Write(BinaryWriter binaryWriter)
        {
            if (_bounds.Count == 0) return;

            bool isMultiPolygon = _bounds.Count > 1;
            if (isMultiPolygon)
            {
                binaryWriter.Write(SfaByteOrder);
                binaryWriter.Write(WkbMultiPolygon);
                binaryWriter.Write((uint)_bounds.Count);

                foreach (var bound in _bounds)
                {
                    binaryWriter.Write(SfaByteOrder);
                    binaryWriter.Write(WkbPolygon);
                    binaryWriter.Write((uint)bound.Count);

                    foreach (var ring in bound)
                    {
                        binaryWriter.Write((uint)ring.Length);
                        foreach (var pt in ring)
                        {
                            binaryWriter.Write(pt[0]);
                            binaryWriter.Write(pt[1]);
                        }
                    }
                }
            }
            else
            {
                var bound = _bounds[0];
                binaryWriter.Write(SfaByteOrder);
                binaryWriter.Write(WkbPolygon);
                binaryWriter.Write((uint)bound.Count);

                foreach (var ring in bound)
                {
                    binaryWriter.Write((uint)ring.Length);
                    foreach (var pt in ring)
                    {
                        binaryWriter.Write(pt[0]);
                        binaryWriter.Write(pt[1]);
                    }
                }
            }
        }

        /// <summary>
        /// Writes the footprint in SFA Well-Known Text format.
        /// </summary>
        public void Write(TextWriter textWriter)
        {
            textWriter.Write(ToString());
        }

        public override string ToString()
        {
            if (_bounds.Count == 0) return "MULTIPOLYGON EMPTY";

            bool isMultiPolygon = _bounds.Count > 1;
            var sb = new StringBuilder();

            sb.Append(isMultiPolygon ? "MULTIPOLYGON(" : "POLYGON(");

            for (int b = 0; b < _bounds.Count; b++)
            {
                var bound = _bounds[b];
                if (isMultiPolygon) sb.Append('(');

                for (int r = 0; r < bound.Count; r++)
                {
                    sb.Append('(');
                    var ring = bound[r];
                    for (int p = 0; p < ring.Length; p++)
                    {
                        sb.Append(ring[p][0].ToString(CultureInfo.InvariantCulture));
                        sb.Append(' ');
                        sb.Append(ring[p][1].ToString(CultureInfo.InvariantCulture));
                        if (p < ring.Length - 1)
                            sb.Append(", ");
                    }
                    sb.Append(')');
                    if (r < bound.Count - 1)
                        sb.Append(", ");
                }

                if (isMultiPolygon) sb.Append(')');
                if (b < _bounds.Count - 1)
                    sb.Append(", ");
            }

            sb.Append(')');
            return sb.ToString();
        }
    }
}
