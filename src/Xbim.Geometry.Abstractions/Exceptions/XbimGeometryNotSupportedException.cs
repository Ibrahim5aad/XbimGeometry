using System;

namespace Xbim.Geometry.Exceptions
{
    public class XbimGeometryNotSupportedException : Exception
    {
        public XbimGeometryNotSupportedException() { }
        public XbimGeometryNotSupportedException(string message) : base(message) { }
        public XbimGeometryNotSupportedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
