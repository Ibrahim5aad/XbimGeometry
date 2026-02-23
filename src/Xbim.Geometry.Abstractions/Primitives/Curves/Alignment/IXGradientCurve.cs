namespace Xbim.Geometry.Abstractions
{
    public interface IXGradientCurve : IXCurve
    {
        IXWire ToWire();
    }
}
