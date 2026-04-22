using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Scene
{
    /// <summary>
    /// Receives geometry and product instances as they are processed, enabling progressive
    /// rendering without waiting for the entire model to complete.
    /// </summary>
    public interface ISceneStreamSink : IAsyncDisposable
    {
        /// <summary>
        /// Called once at the start with model metadata. The estimated bounds come from the
        /// spatial structure and may be refined as products stream in.
        /// </summary>
        ValueTask WriteHeader(StreamHeader header);

        /// <summary>
        /// Called for each surface style (material) before any instances reference it.
        /// </summary>
        ValueTask WriteStyle(int styleId, float r, float g, float b, float a);

        /// <summary>
        /// Called for each unique tessellated geometry. The mesh data is in PolyhedronBinary
        /// format (same encoding as WexBIM). Subsequent instances reference this by geometryId.
        /// </summary>
        ValueTask WriteGeometry(int geometryId, byte[] meshData, XbimRect3D bounds);

        /// <summary>
        /// Called for each product shape instance that should appear in the scene.
        /// The geometryId references a previously-written geometry.
        /// </summary>
        ValueTask WriteInstance(SceneInstance instance);

        /// <summary>
        /// Called when all products have been streamed. The consumer can finalize
        /// the scene (optimize instancing, fit camera, etc.).
        /// </summary>
        ValueTask Complete();
    }

    /// <summary>
    /// Model-level metadata sent at the start of a streaming session.
    /// </summary>
    public readonly struct StreamHeader
    {
        public StreamHeader(float oneMeter, double wcsX, double wcsY, double wcsZ,
            XbimRect3D estimatedBounds, int estimatedProductCount)
        {
            OneMeter = oneMeter;
            WcsX = wcsX;
            WcsY = wcsY;
            WcsZ = wcsZ;
            EstimatedBounds = estimatedBounds;
            EstimatedProductCount = estimatedProductCount;
        }

        public float OneMeter { get; }
        public double WcsX { get; }
        public double WcsY { get; }
        public double WcsZ { get; }
        public XbimRect3D EstimatedBounds { get; }
        public int EstimatedProductCount { get; }
    }

    /// <summary>
    /// A single product shape instance in the streamed scene.
    /// </summary>
    public readonly struct SceneInstance
    {
        public SceneInstance(int productLabel, short typeId, int geometryId,
            int styleId, XbimMatrix3D transform, XbimRect3D boundingBox)
        {
            ProductLabel = productLabel;
            TypeId = typeId;
            GeometryId = geometryId;
            StyleId = styleId;
            Transform = transform;
            BoundingBox = boundingBox;
        }

        public int ProductLabel { get; }
        public short TypeId { get; }
        public int GeometryId { get; }
        public int StyleId { get; }
        public XbimMatrix3D Transform { get; }
        public XbimRect3D BoundingBox { get; }
    }

    internal enum SinkEventKind : byte { Header, Style, Geometry, Instance }

    /// <summary>
    /// A tagged union for events flowing through the streaming channel from the
    /// geometry processing pipeline to the async sink consumer.
    /// </summary>
    internal readonly struct SinkEvent
    {
        public readonly SinkEventKind Kind;
        public readonly StreamHeader Header;
        public readonly int Id;
        public readonly float R, G, B, A;
        public readonly byte[] MeshData;
        public readonly XbimRect3D Bounds;
        public readonly SceneInstance Instance;

        private SinkEvent(SinkEventKind kind, StreamHeader header, int id,
            float r, float g, float b, float a,
            byte[] meshData, XbimRect3D bounds, SceneInstance instance)
        {
            Kind = kind; Header = header; Id = id;
            R = r; G = g; B = b; A = a;
            MeshData = meshData; Bounds = bounds; Instance = instance;
        }

        public static SinkEvent ForHeader(StreamHeader header) =>
            new(SinkEventKind.Header, header, 0, 0, 0, 0, 0, null, default, default);

        public static SinkEvent ForStyle(int id, float r, float g, float b, float a) =>
            new(SinkEventKind.Style, default, id, r, g, b, a, null, default, default);

        public static SinkEvent ForGeometry(int id, byte[] data, XbimRect3D bounds) =>
            new(SinkEventKind.Geometry, default, id, 0, 0, 0, 0, data, bounds, default);

        public static SinkEvent ForInstance(SceneInstance inst) =>
            new(SinkEventKind.Instance, default, 0, 0, 0, 0, 0, null, default, inst);
    }

    /// <summary>
    /// Consumes <see cref="SinkEvent"/> items from a channel and dispatches them
    /// to an <see cref="ISceneStreamSink"/>. Runs as a single async reader task
    /// concurrent with the geometry processing pipeline.
    /// </summary>
    internal static class SinkEventConsumer
    {
        public static async Task ConsumeAsync(
            ChannelReader<SinkEvent> reader,
            ISceneStreamSink sink,
            CancellationToken ct)
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                switch (evt.Kind)
                {
                    case SinkEventKind.Header:
                        await sink.WriteHeader(evt.Header);
                        break;
                    case SinkEventKind.Style:
                        await sink.WriteStyle(evt.Id, evt.R, evt.G, evt.B, evt.A);
                        break;
                    case SinkEventKind.Geometry:
                        await sink.WriteGeometry(evt.Id, evt.MeshData, evt.Bounds);
                        break;
                    case SinkEventKind.Instance:
                        await sink.WriteInstance(evt.Instance);
                        break;
                }
            }
        }
    }
}
