using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Mesh tessellation parameters.
    /// </summary>
    internal class MeshFactors : IXMeshFactors
    {
        public MeshFactors(double oneMeter, double tolerance)
        {
            OneMeter = oneMeter;
            Tolerance = tolerance;
            Relative = false;
            SetGranularity(MeshGranularity.Normal);
        }

        public double OneMeter { get; set; }
        public double LinearDefection { get; set; }
        public double AngularDeflection { get; set; }
        public bool Relative { get; set; }
        public double Tolerance { get; set; }

        public IXMeshFactors SetGranularity(MeshGranularity granularity)
        {
            switch (granularity)
            {
                case MeshGranularity.Fine:
                    LinearDefection = 12 * OneMeter / 1000; // 12 mm
                    AngularDeflection = 0.26179938;          // 15 degrees
                    break;
                case MeshGranularity.Course:
                    LinearDefection = 50 * OneMeter / 1000; // 50 mm
                    AngularDeflection = 0.698132;            // 40 degrees
                    break;
                case MeshGranularity.Normal:
                default:
                    LinearDefection = 30 * OneMeter / 1000; // 30 mm
                    AngularDeflection = 0.523598776;         // 30 degrees
                    break;
            }
            return this;
        }
    }
}
