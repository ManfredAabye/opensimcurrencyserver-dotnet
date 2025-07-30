# OpenSimCurrencyServer DOTNET 2025

<img src="https://ci.appveyor.com/api/projects/status/32r7s2skrgm9ubva?svg=true" alt="Project Badge" width="150">


<img src="https://github.com/ManfredAabye/opensimcurrencyserver-dotnet/blob/main/OpenSimCurrencyServerSmal.png" alt="Project Badge">


### Alle Bestandteile wurden auf Stabilität getestet, soweit es mit meinem Server möglich war. Zusätzlich wurden deutsche Informationen eingefügt, die den jeweiligen Code analysieren.
### All components have been tested for stability as far as possible with my server. In addition, German information has been added that analyzes the corresponding code.

### Buying land without landtool.php is now possible.

### Buying currency without currency.php is now possible.

## OpenSim Currency Server for OpenSim 0.9.3.1 Dev
Windows Linux IOS DotNet 6.0 & Windows Linux IOS DotNet 8.0

(Ubuntu 18 = DOTNET 6.0 - Ubuntu 20+ DOTNET 8.0)

Everything works so far.

The MoneyServer intercepts the helper messages and processes them internally.

     ; helper uri (for currency.php and landtool.php):
     economy = ${Const|BaseURL}:8008/
     
Testing Firestorm Viewer for OpenSim 7.1.11.76496 works.

### From the original DTL/NSL Money Server
by Fumi.Iseki and NSL http://www.nsl.tuis.ac.jp , here is a test revision for DotNet6 & DotNet8.

## New:

     ; General maximum:
     CurrencyMaximum = 20000;
     ; Turn on money purchase = on turn off = off.
     CurrencyOnOff = on;
     
     ; Buy money only for group true/false:
     CurrencyGroupOnly = true;
     ; Buy money only for group the group ID
     CurrencyGroupID = "00000000-0000-0000-0000-000000000000";
     
     ; Verify the email address and anyone without it cannot buy money.
     UserMailLock = true;

## TODO:
Various functions are still missing.

     ; Maximum per day:
     TotalDay = 100;
     ; Maximum per week:
     TotalWeek = 250;
     ; Maximum per month:
     TotalMonth = 500;
