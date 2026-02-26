using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Materials;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Factories
{
    /// <summary>
    /// Builds visual material and colour objects from IFC surface style definitions.
    /// </summary>
    internal class MaterialFactory : IXMaterialFactory
    {
        public IXVisualMaterial BuildVisualMaterial(string name, IIfcSurfaceStyleElementSelect styling)
        {
            return new XVisualMaterial(name, styling);
        }

        public IXVisualMaterial BuildVisualMaterial(string name)
        {
            return new XVisualMaterial(name);
        }

        public IXColourRGB BuildColourRGB(double red, double green, double blue)
        {
            return new XColourRGB(red, green, blue);
        }

        public IXShapeColour BuildShapeColour(string name, IIfcSurfaceStyleElementSelect surfaceStyle)
        {
            return new XShapeColour(name, surfaceStyle);
        }
    }
}
