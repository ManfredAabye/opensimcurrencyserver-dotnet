# OpenSimCurrencyServer-DOTNET

<img src="https://ci.appveyor.com/api/projects/status/32r7s2skrgm9ubva?svg=true" alt="Project Badge" width="150">

.

<img src="https://i.pinimg.com/originals/34/2e/6d/342e6d8b1ef0a4ff9ae8853284047266.jpg" alt="Project Badge" width="250">

.

OpenSim Currency Server for OpenSim 0.9.3.x Dev (X64/Unix/DotNet6) & (X64/Unix/DotNet8)

Everything works except the landtool.php currency.php test.

Ubuntu 18 = DOTNET 6.0 - Ubuntu 20+ DOTNET 8.0

opensim.currency - modified

From Original DTL/NSL Money Server for X64/Unix/DotNet and Windows 10/11 64bit with XAMPP/MariaDB 

by Fumi.Iseki and NSL http://www.nsl.tuis.ac.jp , here is a test revision for DotNet6 & DotNet8.

## TODO: 
test landtool.php currency.php

helpers Robust.ini:

     economy = ${Const|BaseURL}:8008/
     
## Errors
'''
2024-10-27 17:22:19 - URL: /landtool.php - Payload: <?xml version="1.0"?><methodCall><methodName>preflightBuyLandPrep</methodName><params><param><value><struct><member><name>agentId</name><value><string>134e495d-0b1e-48b5-b10b-b009a600cdca</string></value></member><member><name>billableArea</name><value><int>0</int></value></member><member><name>currencyBuy</name><value><int>0</int></value></member><member><name>language</name><value><string>de</string></value></member><member><name>secureSessionId</name><value><string>ae09697b-58c4-437c-b83c-76ea7bdcd77f</string></value></member></struct></value></param></params></methodCall>
'''

'''
2024-10-27 17:22:24 - URL: /currency.php - Payload: <?xml version="1.0"?><methodCall><methodName>getCurrencyQuote</methodName><params><param><value><struct><member><name>agentId</name><value><string>134e495d-0b1e-48b5-b10b-b009a600cdca</string></value></member><member><name>currencyBuy</name><value><int>100</int></value></member><member><name>language</name><value><string>de</string></value></member><member><name>secureSessionId</name><value><string>ae09697b-58c4-437c-b83c-76ea7bdcd77f</string></value></member><member><name>viewerBuildVersion</name><value><string>76496</string></value></member><member><name>viewerChannel</name><value><string>Firestorm-Releasex64</string></value></member><member><name>viewerMajorVersion</name><value><int>7</int></value></member><member><name>viewerMinorVersion</name><value><int>1</int></value></member><member><name>viewerPatchVersion</name><value><int>11</int></value></member></struct></value></param></params></methodCall>
'''

2024-10-27 18:22:19,877 INFO  - OpenSim.Grid.MoneyServer.MoneyXmlRpcModule [MONEY MODULE]: Received request at /landtool.php

2024-10-27 18:22:19,878 ERROR - OpenSim.Grid.MoneyServer.MoneyXmlRpcModule [MONEY MODULE]: Error processing request: Object reference not set to an instance of an object.

2024-10-27 18:22:24,751 INFO  - OpenSim.Grid.MoneyServer.MoneyXmlRpcModule [MONEY MODULE]: Received request at /currency.php

2024-10-27 18:22:24,751 ERROR - OpenSim.Grid.MoneyServer.MoneyXmlRpcModule [MONEY MODULE]: Error processing request: Object reference not set to an instance of an object.
