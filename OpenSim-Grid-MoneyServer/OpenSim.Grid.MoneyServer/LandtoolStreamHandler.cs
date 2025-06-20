using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenSim.Framework.Servers.HttpServer;

namespace OpenSim.Grid.MoneyServer
{
    public class LandtoolStreamHandler : CustomSimpleStreamHandler
    {
        public LandtoolStreamHandler(string path, Action<IOSHttpRequest, IOSHttpResponse> processAction)
            : base(path, processAction)
        {
        }
    }
}