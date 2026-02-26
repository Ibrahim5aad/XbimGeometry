using System;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Materials
{
    /// <summary>
    /// Represents colour properties applied to a specific shape within a BRep document,
    /// including ambient, diffuse, specular, and emissive channels.
    /// </summary>
    internal class XShapeColour : IXShapeColour
    {
        private static readonly IXColourRGB DefaultColour = new XColourRGB(0, 0, 0);
        private static readonly IXColourRGB DefaultDiffuse = new XColourRGB(0.65, 0.65, 0.65);

        public int Id { get; set; }
        public NodeType NodeType { get; set; } = NodeType.Colour;

        public IXColourRGB AmbientColor { get; set; }
        public IXColourRGB DiffuseColor { get; set; }
        public IXColourRGB SpecularColor { get; set; }
        public IXColourRGB EmissiveColor { get; set; }
        public float Shininess { get; set; }
        public float Transparency { get; set; }

        public XShapeColour(string name, IIfcSurfaceStyleElementSelect surfaceStyle)
        {
            AmbientColor = DefaultColour;
            DiffuseColor = DefaultDiffuse;
            SpecularColor = DefaultColour;
            EmissiveColor = DefaultColour;
            Shininess = 0;
            Transparency = 0;

            SetStyle(surfaceStyle);
        }

        private void SetStyle(IIfcSurfaceStyleElementSelect styling)
        {
            if (styling is IIfcSurfaceStyleRendering rendering)
                SetFromRendering(rendering);
            else if (styling is IIfcSurfaceStyleShading shading)
                SetFromShading(shading);
        }

        private void SetFromShading(IIfcSurfaceStyleShading shading)
        {
            if (shading.SurfaceColour != null)
            {
                var c = shading.SurfaceColour;
                DiffuseColor = new XColourRGB(c.Red, c.Green, c.Blue);
                AmbientColor = new XColourRGB(c.Red * 0.3, c.Green * 0.3, c.Blue * 0.3);
            }

            Transparency = (float)(shading.Transparency ?? 0.0);
        }

        private void SetFromRendering(IIfcSurfaceStyleRendering rendering)
        {
            SetFromShading(rendering);

            if (rendering.DiffuseColour != null)
            {
                if (rendering.DiffuseColour is IIfcColourRgb diffColour)
                {
                    DiffuseColor = new XColourRGB(diffColour.Red, diffColour.Green, diffColour.Blue);
                }
                else if (rendering.DiffuseColour is Xbim.Ifc4.MeasureResource.IfcNormalisedRatioMeasure ratio)
                {
                    double factor = (double)ratio.Value;
                    var sc = rendering.SurfaceColour;
                    if (sc != null)
                        DiffuseColor = new XColourRGB(sc.Red * factor, sc.Green * factor, sc.Blue * factor);
                }
            }

            if (rendering.ReflectionColour is IIfcColourRgb reflColour)
                EmissiveColor = new XColourRGB(reflColour.Red, reflColour.Green, reflColour.Blue);

            if (rendering.SpecularColour is IIfcColourRgb specColour)
                SpecularColor = new XColourRGB(specColour.Red, specColour.Green, specColour.Blue);

            if (rendering.SpecularHighlight != null)
            {
                if (rendering.SpecularHighlight is Xbim.Ifc4.PresentationAppearanceResource.IfcSpecularExponent exponent)
                {
                    double seVal = (double)exponent.Value;
                    Shininess = seVal > 1.0 ? Math.Clamp((float)(seVal / 128.0), 0f, 1f) : (float)seVal;
                }
                else if (rendering.SpecularHighlight is Xbim.Ifc4.PresentationAppearanceResource.IfcSpecularRoughness roughness)
                    Shininess = (float)(double)roughness.Value;
            }
        }
    }
}
