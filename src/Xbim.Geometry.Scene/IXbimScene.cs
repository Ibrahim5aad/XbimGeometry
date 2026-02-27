using Xbim.Common.Geometry;

namespace Xbim.Geometry.Scene
{
    public interface IXbimScene
    {
        void Close();
        TransformGraph Graph { get; }
        // ReSharper disable once InconsistentNaming
        XbimLOD LOD { get; set; }
    }
}
