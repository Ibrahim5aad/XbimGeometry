using System;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Materials
{
    /// <summary>
    /// Represents visual material properties extracted from IFC surface style definitions,
    /// including ambient, diffuse, specular, and emissive colour channels plus
    /// shininess and transparency.
    /// </summary>
    internal class XVisualMaterial : IXVisualMaterial
    {
        private static readonly IXColourRGB DefaultColour = new XColourRGB(0, 0, 0);
        private static readonly IXColourRGB DefaultDiffuse = new XColourRGB(0.65, 0.65, 0.65);

        public string Name { get; }
        public IXBRepDocumentItem Label => null!;
        public IXColourRGB AmbientColor { get; set; }
        public IXColourRGB DiffuseColor { get; set; }
        public IXColourRGB SpecularColor { get; set; }
        public IXColourRGB EmissiveColor { get; set; }
        public float Shininess { get; set; }
        public float Transparency { get; set; }
        public bool IsDefined { get; private set; }

        /// <summary>
        /// Creates a visual material with default grey diffuse colour.
        /// </summary>
        public XVisualMaterial(string name)
        {
            Name = name ?? string.Empty;
            AmbientColor = DefaultColour;
            DiffuseColor = DefaultDiffuse;
            SpecularColor = DefaultColour;
            EmissiveColor = DefaultColour;
            Shininess = 0;
            Transparency = 0;
            IsDefined = false;
        }

        /// <summary>
        /// Creates a visual material from an IFC surface style element.
        /// </summary>
        public XVisualMaterial(string name, IIfcSurfaceStyleElementSelect styling)
            : this(name)
        {
            SetStyle(styling);
        }

        private void SetStyle(IIfcSurfaceStyleElementSelect styling)
        {
            if (styling is IIfcSurfaceStyleRendering rendering)
            {
                SetFromRendering(rendering);
                IsDefined = true;
            }
            else if (styling is IIfcSurfaceStyleShading shading)
            {
                SetFromShading(shading);
                IsDefined = true;
            }
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
            // Base surface colour
            SetFromShading(rendering);

            // Diffuse colour override
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

            // Reflection/emissive colour
            if (rendering.ReflectionColour != null)
            {
                if (rendering.ReflectionColour is IIfcColourRgb reflColour)
                {
                    EmissiveColor = new XColourRGB(reflColour.Red, reflColour.Green, reflColour.Blue);
                }
            }

            // Specular colour
            if (rendering.SpecularColour != null)
            {
                if (rendering.SpecularColour is IIfcColourRgb specColour)
                {
                    SpecularColor = new XColourRGB(specColour.Red, specColour.Green, specColour.Blue);
                }
            }

            // Specular highlight → shininess
            if (rendering.SpecularHighlight != null)
            {
                if (rendering.SpecularHighlight is Xbim.Ifc4.PresentationAppearanceResource.IfcSpecularExponent exponent)
                {
                    // Map specular exponent (0-128) to shininess (0.0-1.0)
                    double seVal = (double)exponent.Value;
                    Shininess = seVal > 1.0 ? Math.Clamp((float)(seVal / 128.0), 0f, 1f) : (float)seVal;
                }
                else if (rendering.SpecularHighlight is Xbim.Ifc4.PresentationAppearanceResource.IfcSpecularRoughness roughness)
                {
                    Shininess = (float)(double)roughness.Value;
                }
            }
        }
    }
}
