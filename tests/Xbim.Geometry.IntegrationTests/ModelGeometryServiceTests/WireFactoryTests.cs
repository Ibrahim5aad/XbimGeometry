using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{
    public class WireFactoryTests
    {
        #region Setup
  
        
        private readonly IXModelGeometryService _modelSvc;
        #endregion

        public WireFactoryTests(IXGeometryConverterFactory factory, ILoggerFactory loggerFactory)
        {
            _modelSvc = factory.CreateModelGeometryService(new MemoryModel(new EntityFactoryIfc4()), loggerFactory);
        }
    }
}
