# OpenSimCurrencyServer-DOTNET

<img src="https://ci.appveyor.com/api/projects/status/32r7s2skrgm9ubva?svg=true" alt="Project Badge" width="150">

.

<img src="https://i.pinimg.com/originals/34/2e/6d/342e6d8b1ef0a4ff9ae8853284047266.jpg" alt="Project Badge" width="250">

.

OpenSim Currency Server for OpenSim 0.9.3.x Dev (X64/Unix/DotNet6) & (X64/Unix/DotNet8)

Everything works except the landtool.php currency.php test.

Ubuntu 18 = DOTNET 6.0 - Ubuntu 20+ DOTNET 8.0

opensim.currency - modified

From Original DTL/NSL Money Server for X64/Unix/DotNet and Windows 10/11 64bit with XAMPP/MariaDB, X64 DotNet6 & X64 DotNet8

by Fumi.Iseki and NSL http://www.nsl.tuis.ac.jp , here is a test revision for DotNet6 & DotNet8.

## TODO: 
test landtool.php currency.php
Debug and test...

The MoneyServer now writes many errors to the console
MoneyServer.ini

     ; Log XML-RPC request to file or console based on debug settings
     DebugConsole = true
     DebugFile = false

helpers Robust.ini:

     economy = ${Const|BaseURL}:8008/

Further communication information is available in the console.
