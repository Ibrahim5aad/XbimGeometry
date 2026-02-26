using Moq;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Ifc4;

namespace Xbim.Geometry.Engine.Tests.Helpers;

/// <summary>
/// Default value provider for Moq that produces IItemSet implementations
/// instead of null when IFC entity properties are accessed.
/// Copied from the main test project's MoqCreators.
/// </summary>
public class MoqDefaultBehaviourProvider : LookupOrFallbackDefaultValueProvider
{
    public MoqDefaultBehaviourProvider()
    {
        base.Register(typeof(IItemSet<>), (type, mock) =>
        {
            Type lType = typeof(ItemListMoq<>);
            var genType = type.GetGenericArguments()[0];
            Type constructed = lType.MakeGenericType(genType);
            return Activator.CreateInstance(constructed)!;
        });
    }
}
