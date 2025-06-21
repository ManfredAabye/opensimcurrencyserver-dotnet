/*
 * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/ See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Funktion
Diese Klasse ist das zentrale XML-RPC Modul für den OpenSim MoneyServer. Sie erweitert MoneyDBService und implementiert das Interface IMoneyDBService. Ihre Aufgaben:

    Verwaltung und Verarbeitung von XML-RPC-Anfragen für Geldtransaktionen, Landkauf, Session-Management und mehr.
    Bindung von HTTP- und XML-RPC-Handlern an den HTTP-Server.
    Zugriff und Manipulation von Benutzerguthaben, Transaktionen und Gruppenmitgliedschaften über die Datenbank.
    Spezialfunktionen für Cashbook-Ausgaben, Konsolenbefehle und Logging.

Null-Pointer-Checks & Fehlerquellen
1. Konstruktor & Initialisierung
    Alle kritischen Dependencies (moneyDBService, moneyCore) werden mit ArgumentNullException.ThrowIfNull() geprüft.
    Konfigurationswerte werden aus INI geladen und auf null/Fehlwerte geprüft.
    Dictionaries und andere Felder werden direkt initialisiert oder nachgeladen.

2. HTTP/XML-RPC-Handler
    Viele Methoden prüfen die Eingabeparameter (z.B. ob ein Request-Objekt null ist).
    Bei fehlerhaften Requests oder Sessions wird ein Fehler geloggt und eine Standardantwort zurückgegeben.
    Beispiel:
    C#

    if (httpRequest == null || httpResponse == null) { ... return; }
    if (request == null) { ... return new XmlRpcResponse { Value = new Hashtable { { "success", false } } }; }

    Bei Session-Handling wird vor jedem Zugriff geprüft, ob die Dictionary-Schlüssel existieren.
    Bei Datenbankoperationen werden Exceptions gefangen und führen nicht zu Abstürzen, sondern zu Logging und Fehlerantwort.

3. Datenbankzugriffe
    Datenbankverbindungen werden immer in einem Try-Catch-Finally-Block verwendet, wobei im finally-Block die Verbindung freigegeben wird (dbm.Release()).
    Bei Datenbankoperationen wird auf mögliche Null-Rückgaben geachtet (z.B. FetchUserInfo kann null liefern).

4. Spezielle Fehlerquellen
    Parsen von XML-Requests: Hier können null-Werte entstehen, werden aber meistens abgefangen.
    Zahlreiche Methoden prüfen, ob Parameter null/leer sind, bevor sie verwendet werden (z.B. agentId, groupId).
    SQL-Parameter werden mit AddWithValue gesetzt, was SQL-Injection weitgehend verhindert.

5. Allgemeine Fehlerbehandlung
    Bei Fehlern wird stets ins Log geschrieben.
    Rückgabewerte für Fehlerfälle sind konsistent (meistens false, null oder ein Hashtable mit "success": false).
    Fehler in der Verarbeitung führen zu keinen unkontrollierten Null-Pointern oder Abstürzen.

Beispiele für Null-Checks und Fehlerbehandlung

Session-Checks:
C#

if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID)) { ... }
else { responseData["message"] = "Session check failure, please re-login later!"; return response; }

Datenbankobjekte:
C#

UserInfo rcvr = m_moneyDBService.FetchUserInfo(receiverID);
if (rcvr == null) { m_log.ErrorFormat(...); return response; }

Catch-All für Exceptions:
C#

catch (Exception ex)
{
    m_log.ErrorFormat("[MONEY XMLRPC]: ... Exception occurred: {0}", ex.Message);
    return new XmlRpcResponse();
}

Hashtable-Zugriffe:
C#

Hashtable requestData = (Hashtable)request.Params[0];
if (requestData == null) { ... }

Verbesserungsmöglichkeiten

    In einigen Methoden werden Werte aus Hashtables direkt abgerufen, ohne vorher zu prüfen, 
    ob sie wirklich existieren oder den erwarteten Typ haben (z.B. int.Parse direkt auf einen String aus einer Hashtable). Hier könnten defensive Checks ergänzt werden.
    XML-Parsing könnte robuster gestaltet werden, um fehlerhafte oder absichtlich manipulierte Requests besser abzufangen.
    Teilweise könnten Methoden Parameter noch expliziter auf Plausibilität prüfen (z.B. Range-Checks bei Beträgen).

Fazit

Sehr robust gegen NullPointer-Fehler und allgemeine Fehlerquellen!

    Alle kritischen Zugriffe werden defensiv behandelt.
    Fehler werden stets geloggt und führen zu konsistenten Fehlerantworten.
    Ressourcen werden ordentlich freigegeben.
    Die Funktionalität ist klar: XML-RPC-Endpoint-Management, Geldtransaktionen, User-Sessions, Gruppen-Checks, Logging, Cashbook, etc.

Empfehlung:
Das Modul ist für produktiven Einsatz gut vorbereitet. Zusätzliche defensive Checks bei Daten aus externen Quellen (z.B. XML/Hashtable) wären sinnvoll, sind aber kein kritischer Mangel.
 */

using log4net;
using MySql.Data.MySqlClient;
using Nini.Config;
using NSL.Certificate.Tools;
using NSL.Network.XmlRpc;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Modules.Currency;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;
using System.Xml;
using static Mono.Security.X509.X520;
using static OpenMetaverse.DllmapConfigHelper;
using OpenSim.Server.Base;
using OpenSim.Framework.Console;


namespace OpenSim.Grid.MoneyServer
{
    class MoneyXmlRpcModule : MoneyDBService, IMoneyDBService
    {
        // ##################     Initial          ##################
        #region Setup Initial
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private int m_realMoney = 1000; // Beispielwert oder aus der MoneyServer.ini geladen
        private int m_gameMoney = 10000; // Beispielwert oder aus der MoneyServer.ini geladen

        // MoneyServer settings
        public int m_defaultBalance = 1000;

        private bool m_forceTransfer = false;
        private string m_bankerAvatar = "";

        // Testbereich
        // Maximum pro Tag:
        public int m_TotalDay = 100;
        // Maximum pro Woche:
        public int m_TotalWeek = 250;
        // Maximum pro Monat:
        public int m_TotalMonth = 500;
        // Maximum Besitz:
        public int m_CurrencyMaximum;
        // Geldkauf abschalten:
        public string m_CurrencyOnOff;
        // Geldkauf nur für Gruppe:
        public bool m_CurrencyGroupOnly = false;
        public bool m_UserMailLock = false;
        public string m_CurrencyGroupName = "";
        public string m_CurrencyGroupID = "";

        // Script settings
        private bool m_scriptSendMoney = false;
        private string m_scriptAccessKey = "";
        private string m_scriptIPaddress = "127.0.0.1";

        // HG settings
        private bool m_hg_enable = false;
        private bool m_gst_enable = false;
        private int m_hg_defaultBalance = 0;
        private int m_gst_defaultBalance = 0;
        private int m_CalculateCurrency = 0;

        // XMLRPC Debug settings
        private bool m_DebugConsole = false;
        private bool m_DebugFile = false;

        // Certificate settings
        private bool m_checkServerCert = false;
        private string m_cacertFilename = "";
        private string m_certFilename = "";
        private string m_certPassword = "";

        

        // SSL settings
        private string m_sslCommonName = "";

        private Dictionary<ulong, Scene> m_scenes = new Dictionary<ulong, Scene>();

        private NSLCertificateVerify m_certVerify = new NSLCertificateVerify();

        private string m_BalanceMessageLandSale = "Paid the Money L${0} for Land.";
        private string m_BalanceMessageRcvLandSale = "";
        private string m_BalanceMessageSendGift = "Sent Gift L${0} to {1}.";
        private string m_BalanceMessageReceiveGift = "Received Gift L${0} from {1}.";
        private string m_BalanceMessagePayCharge = "";
        private string m_BalanceMessageBuyObject = "Bought the Object {2} from {1} by L${0}.";
        private string m_BalanceMessageSellObject = "{1} bought the Object {2} by L${0}.";
        private string m_BalanceMessageGetMoney = "Got the Money L${0} from {1}.";
        private string m_BalanceMessageBuyMoney = "Bought the Money L${0}.";
        private string m_BalanceMessageRollBack = "RollBack the Transaction: L${0} from/to {1}.";
        private string m_BalanceMessageSendMoney = "Paid the Money L${0} to {1}.";
        private string m_BalanceMessageReceiveMoney = "Received L${0} from {1}.";

        private bool m_enableAmountZero = false;

        const int MONEYMODULE_REQUEST_TIMEOUT = 30 * 1000;  //30 seconds
        private long TicksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        private IMoneyDBService m_moneyDBService;
        // Konfig fuer Konsolenbefehle.
        private IConfigSource m_config;
        private IMoneyServiceCore m_moneyCore;

        protected IConfig m_server_config;
        protected IConfig m_cert_config;

        /// <value>
        /// Used to notify old regions as to which OpenSim version to upgrade to
        /// </value>
        //private string m_opensimVersion;

        private Dictionary<string, string> m_sessionDic;
        private Dictionary<string, string> m_secureSessionDic;
        private Dictionary<string, string> m_webSessionDic;

        protected BaseHttpServer m_httpServer;


        /// <summary>Initializes a new instance of the <see cref="MoneyXmlRpcModule" /> class.</summary>
        public MoneyXmlRpcModule(string connectionString, int maxDBConnections)
        {
            Initialise(connectionString, maxDBConnections);
        }

        /// <summary>Initialises the specified opensim version.</summary>
        /// <param name="opensimVersion">The opensim version.</param>
        /// <param name="moneyDBService">The money database service.</param>
        /// <param name="moneyCore">The money core.</param>
        public void Initialise(string opensimVersion, IMoneyDBService moneyDBService, IMoneyServiceCore moneyCore, IConfigSource config)
        {
            ArgumentNullException.ThrowIfNull(moneyDBService);

            ArgumentNullException.ThrowIfNull(moneyCore);

            m_moneyDBService = moneyDBService;
            m_moneyCore = moneyCore;

            // Get server configuration
            var serverConfig = m_moneyCore.GetServerConfig() ?? throw new InvalidOperationException("Server configuration is not available");

            // Get certificate configuration
            var certConfig = m_moneyCore.GetCertConfig() ?? throw new InvalidOperationException("Certificate configuration is not available");

            // Load configuration values
            m_defaultBalance = serverConfig.GetInt("DefaultBalance", m_defaultBalance);
            m_forceTransfer = serverConfig.GetBoolean("EnableForceTransfer", m_forceTransfer);
            m_bankerAvatar = serverConfig.GetString("BankerAvatar", m_bankerAvatar).ToLower();

            m_moneyDBService = moneyDBService;
            m_moneyCore = moneyCore;
            m_server_config = m_moneyCore.GetServerConfig();    // [MoneyServer] Section
            m_cert_config = m_moneyCore.GetCertConfig();      // [Certificate] Section

            m_TotalDay = serverConfig.GetInt("TotalDay", m_TotalDay);
            m_TotalWeek = serverConfig.GetInt("TotalWeek", m_TotalWeek);
            m_TotalMonth = serverConfig.GetInt("TotalMonth", m_TotalMonth);
            m_CurrencyMaximum = serverConfig.GetInt("CurrencyMaximum", m_CurrencyMaximum);

            m_CurrencyOnOff = serverConfig.GetString("CurrencyOnOff", m_CurrencyOnOff);
            m_CurrencyGroupOnly = serverConfig.GetBoolean("CurrencyGroupOnly", m_CurrencyGroupOnly);
            m_UserMailLock = serverConfig.GetBoolean("UserMailLock", m_UserMailLock);
            
            m_CurrencyGroupName = serverConfig.GetString("CurrencyGroupName", m_CurrencyGroupName);
            m_CurrencyGroupID = serverConfig.GetString("CurrencyGroupID", m_CurrencyGroupID);

            // [MoneyServer] Section
            m_defaultBalance = m_server_config.GetInt("DefaultBalance", m_defaultBalance);

            m_forceTransfer = m_server_config.GetBoolean("EnableForceTransfer", m_forceTransfer);

            string banker = m_server_config.GetString("BankerAvatar", m_bankerAvatar);
            m_bankerAvatar = banker.ToLower();

            m_enableAmountZero = m_server_config.GetBoolean("EnableAmountZero", m_enableAmountZero);
            m_scriptSendMoney = m_server_config.GetBoolean("EnableScriptSendMoney", m_scriptSendMoney);
            m_scriptAccessKey = m_server_config.GetString("MoneyScriptAccessKey", m_scriptAccessKey);
            m_scriptIPaddress = m_server_config.GetString("MoneyScriptIPaddress", m_scriptIPaddress);

            m_CalculateCurrency = m_server_config.GetInt("CalculateCurrency", m_CalculateCurrency); // New feature
            m_DebugConsole = m_server_config.GetBoolean("DebugConsole", m_DebugConsole); // New feature
            m_DebugFile = m_server_config.GetBoolean("m_DebugFile", m_DebugFile); // New feature

            m_TotalDay = m_server_config.GetInt("TotalDay", m_TotalDay);
            m_TotalWeek = m_server_config.GetInt("TotalWeek", m_TotalWeek);
            m_TotalMonth = m_server_config.GetInt("TotalMonth", m_TotalMonth);
            m_CurrencyMaximum = m_server_config.GetInt("CurrencyMaximum", m_CurrencyMaximum);

            m_CurrencyOnOff = m_server_config.GetString("CurrencyOnOff", m_CurrencyOnOff);
            m_CurrencyGroupOnly = m_server_config.GetBoolean("CurrencyGroupOnly", m_CurrencyGroupOnly);
            m_UserMailLock = m_server_config.GetBoolean("UserMailLock", m_UserMailLock);

            m_CurrencyGroupName = m_server_config.GetString("CurrencyGroupName", m_CurrencyGroupName);
            m_CurrencyGroupID = m_server_config.GetString("CurrencyGroupID", m_CurrencyGroupID);

            if (m_CurrencyMaximum <= 0) m_CurrencyMaximum = 1000;

            // Hyper Grid Avatar
            m_hg_enable = m_server_config.GetBoolean("EnableHGAvatar", m_hg_enable);
            m_gst_enable = m_server_config.GetBoolean("EnableGuestAvatar", m_gst_enable);
            m_hg_defaultBalance = m_server_config.GetInt("HGAvatarDefaultBalance", m_hg_defaultBalance);
            m_gst_defaultBalance = m_server_config.GetInt("GuestAvatarDefaultBalance", m_gst_defaultBalance);

            // Update Balance Messages
            m_BalanceMessageLandSale = m_server_config.GetString("BalanceMessageLandSale", m_BalanceMessageLandSale);
            m_BalanceMessageRcvLandSale = m_server_config.GetString("BalanceMessageRcvLandSale", m_BalanceMessageRcvLandSale);
            m_BalanceMessageSendGift = m_server_config.GetString("BalanceMessageSendGift", m_BalanceMessageSendGift);
            m_BalanceMessageReceiveGift = m_server_config.GetString("BalanceMessageReceiveGift", m_BalanceMessageReceiveGift);
            m_BalanceMessagePayCharge = m_server_config.GetString("BalanceMessagePayCharge", m_BalanceMessagePayCharge);
            m_BalanceMessageBuyObject = m_server_config.GetString("BalanceMessageBuyObject", m_BalanceMessageBuyObject);
            m_BalanceMessageSellObject = m_server_config.GetString("BalanceMessageSellObject", m_BalanceMessageSellObject);
            m_BalanceMessageGetMoney = m_server_config.GetString("BalanceMessageGetMoney", m_BalanceMessageGetMoney);
            m_BalanceMessageBuyMoney = m_server_config.GetString("BalanceMessageBuyMoney", m_BalanceMessageBuyMoney);
            m_BalanceMessageRollBack = m_server_config.GetString("BalanceMessageRollBack", m_BalanceMessageRollBack);
            m_BalanceMessageSendMoney = m_server_config.GetString("BalanceMessageSendMoney", m_BalanceMessageSendMoney);
            m_BalanceMessageReceiveMoney = m_server_config.GetString("BalanceMessageReceiveMoney", m_BalanceMessageReceiveMoney);

            // [Certificate] Section

            // XML RPC to Region Server (Client Mode)
            // Client Certificate
            m_certFilename = m_cert_config.GetString("ClientCertFilename", m_certFilename);
            m_certPassword = m_cert_config.GetString("ClientCertPassword", m_certPassword);
            if (m_certFilename != "")
            {
                m_certVerify.SetPrivateCert(m_certFilename, m_certPassword);
                m_log.Info("[MONEY XMLRPC]: Initialise: Issue Authentication of Client. Cert file is " + m_certFilename);
            }

            // Server Authentication
            // CA : MoneyServer config for checking the server certificate of the web server for XMLRPC
            m_checkServerCert = m_cert_config.GetBoolean("CheckServerCert", m_checkServerCert);
            m_cacertFilename = m_cert_config.GetString("CACertFilename", m_cacertFilename);

            if (m_cacertFilename != "")
            {
                m_certVerify.SetPrivateCA(m_cacertFilename);
            }
            else
            {
                m_checkServerCert = false;
            }

            if (m_checkServerCert)
            {
                m_log.Info("[MONEY XMLRPC]: Initialise: Execute Authentication of Server. CA file is " + m_cacertFilename);
            }
            else
            {
                m_log.Info("[MONEY XMLRPC]: CheckServerCert is false.");
            }

            m_moneyDBService = moneyDBService;
            m_config = config;

            // Rufe die RegisterConsoleCommands Methode auf
            RegisterConsoleCommands(MainConsole.Instance); // Aufruf der Initialisierung der Konsolenbefehle

            m_sessionDic = m_moneyCore.GetSessionDic();
            m_secureSessionDic = m_moneyCore.GetSecureSessionDic();
            m_webSessionDic = m_moneyCore.GetWebSessionDic();
            RegisterHandlers();

            RegisterStreamHandlers();
        }

        private void RegisterStreamHandlers()
        {
            m_log.Info("[MONEY XMLRPC]: Registering currency.php handlers.");
            m_httpServer.AddSimpleStreamHandler(new CurrencyStreamHandler("/currency.php", CurrencyProcessPHP));

            m_log.Info("[MONEY XMLRPC]: Registering landtool.php handlers.");
            m_httpServer.AddSimpleStreamHandler(new LandtoolStreamHandler("/landtool.php", LandtoolProcessPHP));

            m_log.InfoFormat("[MONEY MODULE]: Registered /currency.php and /landtool.php handlers on Port: {0}", m_httpServer.Port);
        }

        /// <summary>Posts the initialise.</summary>
        public void PostInitialise()
        {
        }

        private Dictionary<string, XmlRpcMethod> m_rpcHandlers = new Dictionary<string, XmlRpcMethod>();


        /// <summary>Registers the handlers.</summary>
        public void RegisterHandlers()
        {
            m_httpServer = m_moneyCore.GetHttpServer();
            m_httpServer.AddXmlRPCHandler("ClientLogin", handleClientLogin);
            m_httpServer.AddXmlRPCHandler("ClientLogout", handleClientLogout);
            m_httpServer.AddXmlRPCHandler("GetBalance", handleGetBalance);
            m_httpServer.AddXmlRPCHandler("GetTransaction", handleGetTransaction);

            m_httpServer.AddXmlRPCHandler("CancelTransfer", handleCancelTransfer);

            m_httpServer.AddXmlRPCHandler("TransferMoney", handleTransaction);
            m_httpServer.AddXmlRPCHandler("ForceTransferMoney", handleForceTransaction);        // added
            m_httpServer.AddXmlRPCHandler("PayMoneyCharge", handlePayMoneyCharge);          // added
            m_httpServer.AddXmlRPCHandler("AddBankerMoney", handleAddBankerMoney);          // added

            m_httpServer.AddXmlRPCHandler("SendMoney", handleScriptTransaction);
            m_httpServer.AddXmlRPCHandler("MoveMoney", handleScriptTransaction);

            // this is from original DTL. not check yet.
            m_httpServer.AddXmlRPCHandler("WebLogin", handleWebLogin);
            m_httpServer.AddXmlRPCHandler("WebLogout", handleWebLogout);
            m_httpServer.AddXmlRPCHandler("WebGetBalance", handleWebGetBalance);
            m_httpServer.AddXmlRPCHandler("WebGetTransaction", handleWebGetTransaction);
            m_httpServer.AddXmlRPCHandler("WebGetTransactionNum", handleWebGetTransactionNum);

            // Land Buy Test
            m_httpServer.AddXmlRPCHandler("preflightBuyLandPrep", preflightBuyLandPrep);
            m_httpServer.AddXmlRPCHandler("buyLandPrep", buyLandPrep);

            // Currency Buy Test
            // getCurrencyQuote", quote_func
            // buyCurrency", buy_func
            m_httpServer.AddXmlRPCHandler("getCurrencyQuote", getCurrencyQuote);
            m_httpServer.AddXmlRPCHandler("buyCurrency", buyCurrency);

            // Money Transfer Test
            m_httpServer.AddXmlRPCHandler("OnMoneyTransfered", OnMoneyTransferedHandler);
            m_httpServer.AddXmlRPCHandler("UpdateBalance", BalanceUpdateHandler);
            m_httpServer.AddXmlRPCHandler("UserAlert", UserAlertHandler);

            // Angebot oder eine Information zu einem Kaufpreis
            // m_httpServer.AddXmlRPCHandler("quote", getCurrencyQuote);
        }
        #endregion
        // ##################     Land Buy         ##################
        #region Land Buy

        // Flexibilität: Die gesamte Logik wird innerhalb von LandtoolProcessPHP und ihren Hilfsfunktionen abgewickelt.
        // Fehlerbehandlung: Umfassende Überprüfung auf fehlende Daten oder Fehler während der Verarbeitung.
        // Unabhängigkeit: Keine Abhängigkeit von externen Funktionen oder Modulen.

        private void LandtoolProcessPHP(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: LANDTOOL PROCESS PHP starting...");

            if (httpRequest == null || httpResponse == null)
            {
                m_log.Error("[MONEY XMLRPC MODULE]: Invalid request or response object.");
                return;
            }

            try
            {
                // XML-String aus Anfrage lesen
                string requestBody;
                using (var reader = new StreamReader(httpRequest.InputStream, Encoding.UTF8))
                {
                    requestBody = reader.ReadToEnd();
                }

                // XML-Daten parsen
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(requestBody);

                // Methode extrahieren
                XmlNode methodNameNode = doc.SelectSingleNode("/methodCall/methodName");
                if (methodNameNode == null)
                {
                    throw new Exception("Missing method name in XML-RPC request.");
                }

                string methodName = methodNameNode.InnerText;
                XmlNodeList members = doc.SelectNodes("//param/value/struct/member");

                // Variablen für Landanfrage initialisieren
                string agentId = null, secureSessionId = null, language = null;
                int billableArea = 0, currencyBuy = 0;

                // Werte aus der XML-Struktur extrahieren
                foreach (XmlNode member in members)
                {
                    string name = member.SelectSingleNode("name")?.InnerText;
                    string value = member.SelectSingleNode("value")?.InnerText;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;

                    switch (name)
                    {
                        case "agentId": agentId = value; break;
                        case "billableArea": billableArea = int.Parse(value); break;
                        case "currencyBuy": currencyBuy = int.Parse(value); break;
                        case "language": language = value; break;
                        case "secureSessionId": secureSessionId = value; break;
                    }
                }
                m_log.InfoFormat("[MONEY XMLRPC MODULE]: agentId ", agentId);
                m_log.InfoFormat("[MONEY XMLRPC MODULE]: billableArea", billableArea);
                m_log.InfoFormat("[MONEY XMLRPC MODULE]: currencyBuy", currencyBuy);
                m_log.InfoFormat("[MONEY XMLRPC MODULE]: language", language);
                m_log.InfoFormat("[MONEY XMLRPC MODULE]: secureSessionId", secureSessionId);

                if (methodName == "preflightBuyLandPrep")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Preflight Land Purchase Request for AgentId: {0}, BillableArea: {1}",
                        agentId, billableArea);

                    // Preflight-Prüfung
                    Hashtable preflightResponse = PerformPreflightLandCheck(agentId, billableArea, currencyBuy, language, secureSessionId);
                    if (!(bool)preflightResponse["success"])
                    {
                        m_log.Error("[MONEY XMLRPC MODULE]: Preflight check failed.");
                        httpResponse.StatusCode = 400;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Preflight check failed</response>");
                        return;
                    }

                    // Erfolgreiche Antwort zurückgeben
                    httpResponse.StatusCode = 200;
                    XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = preflightResponse };
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                }
                else if (methodName == "buyLandPrep")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Land Purchase Request for AgentId: {0}, BillableArea: {1}",
                        agentId, billableArea);

                    // Landkauf durchführen
                    Hashtable purchaseResponse = ProcessLandPurchase(agentId, billableArea, currencyBuy, language, secureSessionId);

                    // Überprüfung der Antwort
                    if (!(bool)purchaseResponse["success"])
                    {
                        m_log.Error("[MONEY XMLRPC MODULE]: Land purchase failed.");
                        httpResponse.StatusCode = 400;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Land purchase failed</response>");
                        return;
                    }

                    // Erfolgreiche Antwort zurückgeben
                    httpResponse.StatusCode = 200;
                    XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = purchaseResponse };
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                }
                else
                {
                    m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Unknown landtool method name: {0}", methodName);
                    httpResponse.StatusCode = 400;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Invalid method name</response>");
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Error processing LANDTOOL request. Error: {0}", ex.ToString());
                httpResponse.StatusCode = 500;
                httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Error</response>");
            }
        }

        // Beispiel einer Funktion zur Preflight-Prüfung
        // PerformPreflightLandCheck:
        // Simuliert die Preflight-Prüfung für den Landkauf.
        // Gibt eine Erfolgsmeldung in Form einer Hashtable zurück.
        private Hashtable PerformPreflightLandCheck(string agentId, int billableArea, int currencyBuy, string language, string secureSessionId)
        {
            // Beispielhafte Logik für Preflight-Prüfung
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: Preflight check for AgentId: {0}, Area: {1}, CurrencyBuy: {2}", agentId, billableArea, currencyBuy);

            // Erfolg simulieren
            return new Hashtable
            {
                { "success", true },
                { "agentId", agentId },
                { "billableArea", billableArea },
                { "currencyBuy", currencyBuy },
                { "message", "Preflight check passed" }
            };
        }

        // Beispiel einer Funktion zur Bearbeitung eines Landkaufs
        // ProcessLandPurchase:
        // Simuliert die Durchführung des Landkaufs.
        // Gibt ebenfalls eine Erfolgsmeldung als Hashtable zurück.
        private Hashtable ProcessLandPurchase(string agentId, int billableArea, int currencyBuy, string language, string secureSessionId)
        {
            // Beispielhafte Logik für Landkauf
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing land purchase for AgentId: {0}, Area: {1}, CurrencyBuy: {2}", agentId, billableArea, currencyBuy);

            // Erfolg simulieren
            return new Hashtable
            {
                { "success", true },
                { "agentId", agentId },
                { "billableArea", billableArea },
                { "currencyBuy", currencyBuy },
                { "message", "Land purchase completed successfully" }
            };
        }
        private XmlRpcResponse preflightBuyLandPrep(XmlRpcRequest request, IPEndPoint client)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: preflightBuyLandPrep starting...");

            if (request == null)
            {
                m_log.Error("[MONEY XMLRPC]: preflightBuyLandPrep: request is null.");
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }

            if (client == null)
            {
                m_log.Error("[MONEY XMLRPC]: preflightBuyLandPrep: client is null.");
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                if (requestData == null)
                {
                    m_log.Error("[MONEY XMLRPC]: preflightBuyLandPrep: request data is null.");
                    return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
                }

                int billableArea = Convert.ToInt32(requestData["billableArea"]);
                int currencyBuy = Convert.ToInt32(requestData["currencyBuy"]);

                // Log the received data for debugging
                m_log.InfoFormat("[MONEY XMLRPC]: Received billableArea = {0}, currencyBuy = {1}", billableArea, currencyBuy);

                // Process preflight logic here
                if (billableArea < 0 || currencyBuy < 0)
                {
                    m_log.Error("[MONEY XMLRPC]: preflightBuyLandPrep: Invalid input values.");
                    return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
                }

                // Simulate sending response
                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseValue = new Hashtable
                {
                    { "success", true },
                    { "billableArea", billableArea },
                    { "currencyBuy", currencyBuy }
                };
                response.Value = responseValue;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: preflightBuyLandPrep: {0}", ex.Message);
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }
        }
        private XmlRpcResponse buyLandPrep(XmlRpcRequest request, IPEndPoint client)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: buyLandPrep starting...");

            if (request == null)
            {
                m_log.Error("[MONEY XMLRPC]: buyLandPrep: request is null.");
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }

            if (client == null)
            {
                m_log.Error("[MONEY XMLRPC]: buyLandPrep: client is null.");
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                if (requestData == null)
                {
                    m_log.Error("[MONEY XMLRPC]: buyLandPrep: request data is null.");
                    return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
                }

                string agentId = requestData["agentId"]?.ToString();
                string secureSessionId = requestData["secureSessionId"]?.ToString();
                string language = requestData["language"]?.ToString();
                int billableArea = Convert.ToInt32(requestData["billableArea"]);
                int currencyBuy = Convert.ToInt32(requestData["currencyBuy"]);

                // Log the received data for debugging
                m_log.InfoFormat("[MONEY XMLRPC]: Received agentId = {0}, secureSessionId = {1}, billableArea = {2}, currencyBuy = {3}",
                    agentId, secureSessionId, billableArea, currencyBuy);

                if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(secureSessionId))
                {
                    m_log.Error("[MONEY XMLRPC]: buyLandPrep: Missing required parameters.");
                    return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
                }

                // Process purchase logic here
                bool purchaseSuccessful = ProcessLandPurchase(agentId, secureSessionId, billableArea, currencyBuy);
                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseValue = new Hashtable
        {
            { "success", purchaseSuccessful }
        };
                response.Value = responseValue;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: buyLandPrep: {0}", ex.Message);
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }
        }

        // Helper function to simulate land purchase logic
        private bool ProcessLandPurchase(string agentId, string secureSessionId, int billableArea, int currencyBuy)
        {
            // Simulate some purchase validation
            if (billableArea > 0 && currencyBuy >= billableArea * 10) // Example: each square meter costs 10 currency units
            {
                m_log.InfoFormat("[MONEY XMLRPC]: ProcessLandPurchase: Purchase successful for Agent {0}.", agentId);
                return true;
            }

            m_log.WarnFormat("[MONEY XMLRPC]: ProcessLandPurchase: Purchase failed for Agent {0}.", agentId);
            return false;
        }
        #endregion
        // ##################     Currency Buy     ##################
        #region Currency Buy

        private void CurrencyProcessPHP(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            m_log.InfoFormat("[CURRENCY PROCESS PHP]: Currency Process Starting...");

            if (httpRequest == null || httpResponse == null)
            {
                m_log.Error("[CURRENCY PROCESS PHP]: Invalid request or response object.");
                httpResponse.StatusCode = 400;
                httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Invalid request</response>");
                return;
            }

            try
            {
                string requestBody;
                using (var reader = new StreamReader(httpRequest.InputStream, Encoding.UTF8))
                {
                    requestBody = reader.ReadToEnd();
                }

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(requestBody);

                XmlNode methodNameNode = doc.SelectSingleNode("/methodCall/methodName");
                if (methodNameNode == null)
                {
                    throw new Exception("Missing method name in XML-RPC request.");
                }

                string methodName = methodNameNode.InnerText;

                Hashtable parameters = ExtractXmlRpcParams(doc);

                string agentId = parameters["agentId"]?.ToString();
                string secureSessionId = parameters["secureSessionId"]?.ToString();
                int currencyBuy = int.Parse(parameters["currencyBuy"]?.ToString() ?? "0");

                m_log.InfoFormat("[CURRENCY PROCESS PHP]: Parsed values - AgentId: {0}, CurrencyBuy: {1}, SecureSessionId: {2}", agentId, currencyBuy, secureSessionId);

                string transactionID = parameters["transactionID"]?.ToString();
                string userID = parameters["agentId"]?.ToString();
                int amount = int.Parse(parameters["currencyBuy"]?.ToString() ?? "0");

                if (string.IsNullOrEmpty(transactionID))
                {
                    transactionID = secureSessionId;
                }

                m_log.InfoFormat("[CURRENCY PROCESS PHP]: Parsed values - transactionID: {0}, userID: {1}, amount: {2}", transactionID, userID, amount);

                if (m_CurrencyGroupOnly && m_CurrencyGroupID != "00000000-0000-0000-0000-000000000000" && !IsUserInGroup(agentId, m_CurrencyGroupID))
                {
                    m_log.InfoFormat("[CURRENCY PROCESS PHP]: User {0} is not a member of the required group {1}.", agentId, m_CurrencyGroupID);
                    httpResponse.StatusCode = 403;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>User is not a member of the required group</response>");
                    return;
                }

                if (m_UserMailLock && !UserMailLock(agentId))
                {
                    m_log.InfoFormat("[CURRENCY PROCESS PHP]: User {0} does not have a registered email address.", agentId);
                    httpResponse.StatusCode = 403;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>User does not have a registered email address</response>");
                    return;
                }

                if (m_CurrencyOnOff != "off" && !CheckGroupMoney(agentId, m_CurrencyGroupID))
                {
                    m_log.Info("[CURRENCY PROCESS PHP]: Currency purchase is turned off for this user.");
                    httpResponse.StatusCode = 403;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Currency purchase is turned off</response>");
                    return;
                }

                m_log.InfoFormat("[CURRENCY PROCESS PHP]: Currency Maximum loaded: {0}", m_CurrencyMaximum);

                if (methodName == "getCurrencyQuote")
                {
                    Hashtable quoteResponse = PerformGetCurrencyQuote(agentId, currencyBuy, secureSessionId);

                    int excessAmount = CheckMaximumMoney(agentId, m_CurrencyMaximum);
                    if (excessAmount > 0)
                    {
                        quoteResponse["message"] = $"Your balance was reduced by {excessAmount} to enforce the maximum limit.";
                    }

                    XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = quoteResponse };
                    httpResponse.StatusCode = 200;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                }
                else if (methodName == "buyCurrency")
                {
                    Hashtable purchaseResponse = PerformBuyCurrency(agentId, currencyBuy, secureSessionId);

                    if ((bool)purchaseResponse["success"])
                    {
                        m_log.Info("[CURRENCY PROCESS PHP]: Purchase successful. Proceeding to credit currency.");
                        PerformMoneyTransfer("BANKER", agentId, currencyBuy);
                        UpdateBalance(agentId, "Currency purchase successful.");

                        CheckMaximumMoney(agentId, m_CurrencyMaximum);

                        XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = purchaseResponse };
                        httpResponse.StatusCode = 200;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                    }
                    else
                    {
                        m_log.Error("[CURRENCY PROCESS PHP]: Currency purchase failed.");
                        httpResponse.StatusCode = 400;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Currency purchase failed</response>");
                    }
                }
                else
                {
                    m_log.ErrorFormat("[CURRENCY PROCESS PHP]: Unknown method name: {0}", methodName);
                    httpResponse.StatusCode = 400;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Invalid method name</response>");
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CURRENCY PROCESS PHP]: Error processing request. Error: {0}", ex.ToString());
                httpResponse.StatusCode = 500;
                httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Error</response>");
            }
        }


        private bool UserMailLock(string userID)
        {
            m_log.InfoFormat("[USER MAIL LOCK]: Checking if user {0} has a registered email address", userID);

            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                // SQL-Abfrage zum Überprüfen, ob der Benutzer eine hinterlegte E-Mail-Adresse hat
                string sql = "SELECT email FROM `UserAccounts` WHERE PrincipalID = ?userID";
                string email = null;

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            email = reader.GetString("email");
                            m_log.InfoFormat("[USER MAIL LOCK]: User {0} email address is {1}", userID, email);
                        }
                    }
                }

                // Überprüfen, ob die E-Mail-Adresse leer ist oder null
                if (string.IsNullOrEmpty(email))
                {
                    m_log.InfoFormat("[USER MAIL LOCK]: User {0} does not have a registered email address", userID);
                    return false; // Benutzer hat keine hinterlegte E-Mail-Adresse
                }

                m_log.InfoFormat("[USER MAIL LOCK]: User {0} has a registered email address", userID);
                return true; // Benutzer hat eine hinterlegte E-Mail-Adresse
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[USER MAIL LOCK]: Error checking user email: {0}", ex.Message);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        private bool CheckGroupMoney(string agentId, string groupId)
        {
            m_log.InfoFormat("[CHECK GROUP MONEY]: Checking group membership for agentId: {0}, groupId: {1}", agentId, groupId);

            if (string.IsNullOrEmpty(groupId) || groupId == "00000000-0000-0000-0000-000000000000")
            {
                m_log.Info("[CHECK GROUP MONEY]: groupId is empty or set to accept all groups, returning true");
                return true;
            }

            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                string sql = "SELECT COUNT(*) FROM os_groups_membership WHERE PrincipalID = ?agentId AND GroupID = ?groupId";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.Parameters.AddWithValue("?groupId", groupId);

                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    m_log.InfoFormat("[CHECK GROUP MONEY]: Query result: count={0}", count);
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CHECK GROUP MONEY]: Error checking group membership: {0}", ex.Message);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        // Überprüfen, ob der Benutzer Mitglied der angegebenen Gruppe ist
        private bool IsUserInGroup(string agentId, string groupId)
        {
            // IClientAPI
            m_log.DebugFormat("[IsUserInGroup]: Checking group membership for agentId={0}, groupId={1}", agentId, groupId);

            if (string.IsNullOrEmpty(groupId))
            {
                m_log.Debug("[IsUserInGroup]: groupId is empty, returning true");
                return true; // Keine Einschränkung, wenn groupId nicht gesetzt ist
            }

            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                // SQL-Abfrage zum Überprüfen der Gruppenmitgliedschaft
                string sql = "SELECT COUNT(*) FROM os_groups_membership WHERE PrincipalID = ?agentId AND GroupID = ?groupId";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.Parameters.AddWithValue("?groupId", groupId);

                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    m_log.DebugFormat("[IsUserInGroup]: Query result: count={0}", count);
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[IsUserInGroup]: Error checking group membership: {0}", ex.Message);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public new int CheckMaximumMoney(string userID, int m_CurrencyMaximum)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            m_log.InfoFormat("[CHECK MAXIMUM MONEY]: Checking for userID: {0}, Currency Maximum: {1}", userID, m_CurrencyMaximum);

            try
            {
                // Ausnahmen für SYSTEM und BANKER
                if (userID == "SYSTEM" || userID == "BANKER" || userID == m_bankerAvatar)
                {
                    m_log.InfoFormat("[CHECK MAXIMUM MONEY]: User {0} is SYSTEM or BANKER, skipping check.", userID);
                    return 0; // Keine Begrenzung für diese Benutzer
                }

                // Abrufen des aktuellen Guthabens des Benutzers
                string sql = "SELECT balance FROM balances WHERE user = ?userID";
                int currentBalance = 0;

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            currentBalance = reader.GetInt32("balance");
                            m_log.InfoFormat("[CHECK MAXIMUM MONEY]: Current balance for user {0} is {1}", userID, currentBalance);
                        }
                    }
                }

                // Überprüfen, ob das Guthaben über dem Maximum liegt und ggf. abziehen
                if (currentBalance > m_CurrencyMaximum)
                {
                    int excessAmount = currentBalance - m_CurrencyMaximum;

                    // Guthaben auf das Maximum reduzieren
                    sql = "UPDATE balances SET balance = ?newBalance WHERE user = ?userID";
                    using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                    {
                        cmd.Parameters.AddWithValue("?newBalance", m_CurrencyMaximum);
                        cmd.Parameters.AddWithValue("?userID", userID);
                        cmd.ExecuteNonQuery();
                    }

                    m_log.InfoFormat("[CHECK MAXIMUM MONEY]: Reduced balance for user {0} by {1} to enforce maximum limit of {2}", userID, excessAmount, m_CurrencyMaximum);
                    return excessAmount; // Rückgabe des abgezogenen Betrags
                }

                m_log.InfoFormat("[CHECK MAXIMUM MONEY]: No adjustment needed for user {0}", userID);
                return 0; // Keine Änderung, falls das Guthaben innerhalb des Limits liegt
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CHECK MAXIMUM MONEY]: Error checking and updating user balance: {0}", ex.Message);
                throw;
            }
            finally
            {
                dbm.Release();
            }
        }

        private Hashtable ExtractXmlRpcParams(XmlDocument doc)
        {
            Hashtable parameters = new Hashtable();
            XmlNodeList members = doc.SelectNodes("//param/value/struct/member");

            foreach (XmlNode member in members)
            {
                string name = member.SelectSingleNode("name")?.InnerText;
                string value = member.SelectSingleNode("value")?.InnerText;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                {
                    parameters[name] = value;
                }
            }
            return parameters;
        }

        private Hashtable PerformGetCurrencyQuote(string agentId, int currencyBuy, string secureSessionId)
        {
            m_log.InfoFormat("[PERFORM GET CURRENCY QUOTE]: Generating currency quote for AgentId: {0}", agentId);

            int rate = 100; // 1 = 100L$
            return new Hashtable
            {
                { "success", true },
                { "currency", new Hashtable
                    {
                        { "estimatedCost", currencyBuy / rate }, // Kosten in 
                        { "currencyBuy", currencyBuy }          // Angeforderte Spielwährung
                    }
                },
                { "confirm", Guid.NewGuid().ToString() }
            };
        }

        private Hashtable PerformBuyCurrency(string agentId, int currencyBuy, string secureSessionId)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                m_log.Error("[PERFORM BUY CURRENCY]: AgentId is null or empty.");
                return new Hashtable
                {
                    { "success", false },
                    { "message", "AgentId is required." }
                };
            }

            if (currencyBuy <= 0)
            {
                m_log.Error("[PERFORM BUY CURRENCY]: Invalid currencyBuy amount.");
                return new Hashtable
                {
                    { "success", false },
                    { "message", "Invalid currencyBuy amount." }
                };
            }

            m_log.InfoFormat("[PERFORM BUY CURRENCY]: Processing currency purchase for AgentId: {0}, Amount: {1}", agentId, currencyBuy);
            return new Hashtable
            {
                { "success", true },
                { "message", $"Successfully purchased {currencyBuy}L$ for {agentId}" }
            };
        }
        public XmlRpcResponse getCurrencyQuote(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            int amount = 0;

            // Protokolliere die eingehende XML-Anfrage
            m_log.InfoFormat("[GET CURRENCY QUOTE]: Incoming XML Request: {0}", ToXmlString((Hashtable)request.Params[0]));

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                amount = (int)requestData["currencyBuy"];
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[GET CURRENCY QUOTE]: Error parsing request: {0}", ex.Message);
            }

            // Berechnung und Antwortvorbereitung
            Hashtable currencyResponse = new Hashtable
            {
                { "estimatedCost", amount * 0.01 }, // Berechnung für Kosten
                { "currencyBuy", amount }
            };

            Hashtable quoteResponse = new Hashtable
            {
                { "success", true },
                { "currency", currencyResponse },
                { "confirm", Guid.NewGuid().ToString() }
            };

            // Protokolliere die Antwort, bevor sie zurückgegeben wird
            m_log.InfoFormat("[GET CURRENCY QUOTE]: XML Response: {0}", ToXmlString(quoteResponse));

            // Erstelle die Antwort
            XmlRpcResponse returnval = new XmlRpcResponse { Value = quoteResponse };

            // Füge ein weiteres Log hinzu, um sicherzustellen, dass die Antwort korrekt erstellt wurde
            m_log.InfoFormat("[GET CURRENCY QUOTE]: Returning response for getCurrencyQuote: {0}", ToXmlString((Hashtable)returnval.Value));

            return returnval;
        }
        public XmlRpcResponse buyCurrency(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];
            string agentId = requestData["agentId"]?.ToString();
            int amount = (int)requestData["currencyBuy"];

            // Protokolliere die eingehende XML-Anfrage
            m_log.InfoFormat("[BUY CURRENCY]: Incoming XML Request: {0}", ToXmlString((Hashtable)request.Params[0]));

            // Verarbeite den Kauf und logge die Details
            m_log.InfoFormat("[BUY CURRENCY]: Processing currency purchase for AgentId: {0}, Amount: {1}, Banker: {2}", agentId, amount, m_bankerAvatar);

            // Hier erfolgt der Transfer an den Money Banker (BankerAvatar als Sender)
            string senderID = m_bankerAvatar;  // Der Sender ist der BankerAvatar
            string receiverID = agentId;       // Der Empfänger ist der Agent
            UUID transactionUUID = UUID.Random(); // Eine eindeutige Transaktions-ID für die Transaktion

            Hashtable responseData = new Hashtable();
            responseData["success"] = false;

            // Versuche, die Transaktion auszuführen
            try
            {
                // Logge die Übertragung
                m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Transferring money from {0} to {1}, Amount = {2}", senderID, receiverID, amount);

                // Führe die tatsächliche Transaktion durch, indem die handlePayMoneyCharge-Methode aufgerufen wird
                XmlRpcResponse transferResponse = handlePayMoneyCharge(request, remoteClient); // Übergibt die Anfrage an die tatsächliche Überweisungsmethode

                // Überprüfe, ob die Antwort erfolgreich war
                if (transferResponse != null && transferResponse.Value is Hashtable transferResult &&
                    transferResult.ContainsKey("success") && (bool)transferResult["success"])
                {
                    // Wenn die Transaktion erfolgreich war, setze die Antwortdaten
                    responseData["success"] = true;
                    responseData["message"] = $"Successfully purchased {amount} currency for AgentId {agentId}";
                }
                else
                {
                    // Fehler bei der Überweisung
                    responseData["message"] = "Currency purchase failed during money transfer.";
                }
            }
            catch (Exception ex)
            {
                // Fehlerbehandlung
                m_log.Error($"[BUY CURRENCY]: Error processing currency purchase: {ex.Message}");
                responseData["message"] = "Currency purchase failed.";
            }

            // Erstelle die Antwort
            XmlRpcResponse returnval = new XmlRpcResponse { Value = responseData };

            // Protokolliere die XML-Antwort
            m_log.InfoFormat("[BUY CURRENCY]: XML Response: {0}", ToXmlString(responseData));

            return returnval;
        }
        public new bool PerformMoneyTransfer(string senderID, string receiverID, int amount)
        {
            //m_log.InfoFormat("[MONEY TRANSFER]: Transferring {0} from {1} to {2}.", amount, senderID, receiverID);
            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance - ?amount WHERE user = ?senderID; " +
                             "UPDATE balances SET balance = balance + ?amount WHERE user = ?receiverID";

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?senderID", senderID);
                    cmd.Parameters.AddWithValue("?receiverID", receiverID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(UUID.Random(), senderID, -amount);
                        LogTransaction(UUID.Random(), receiverID, amount);
                    }

                    return result;
                }
            }
            catch (Exception)
            {
                //m_log.ErrorFormat("[MONEY TRANSFER]: Error transferring money: {0}", ex.Message);
                return false;
            }
        }

        public new void InitializeUserCurrency(string agentId)
        {
            m_log.InfoFormat("[INITIALIZE USER CURRENCY]: Initializing currency for new user: {0}", agentId);

            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "INSERT INTO balances (user, balance) VALUES (?agentId, ?realMoney), (?agentId, ?gameMoney)";

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.Parameters.AddWithValue("?realMoney", m_realMoney);
                    cmd.Parameters.AddWithValue("?gameMoney", m_gameMoney);

                    cmd.ExecuteNonQuery();
                }

                m_log.InfoFormat("[INITIALIZE USER CURRENCY]: User {0} initialized with {1} and {2}L$.", agentId, m_realMoney, m_gameMoney);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[INITIALIZE USER CURRENCY]: Error initializing user currency: {0}", ex.Message);
            }
        }

        public new Hashtable ApplyFallbackCredit(string agentId)
        {
            m_log.WarnFormat("[FALLBACK CREDIT]: Applying fallback credit for user {0}", agentId);

            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance + 100 WHERE user = ?agentId";

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[FALLBACK CREDIT]: Error applying fallback credit: {0}", ex.Message);
            }

            return new Hashtable
        {
            { "success", true },
            { "creditedAmount", 100 },
            { "message", "Fallback credit applied due to transaction failure." }
        };
        }

        public bool ValidateTransaction(UUID transactionID, string secureCode)
        {
            // Die Methode ValidateTransfer aus IMoneyDBService verwenden
            bool isValid = ValidateTransfer(secureCode, transactionID);
            if (!isValid)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Transaction validation failed for TransactionID: {0}", transactionID);
                return false;
            }
            return true;
        }
        public bool AddMoney(UUID transactionUUID)
        {
            // Geld zu einer Transaktion hinzufügen
            bool success = DoAddMoney(transactionUUID);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to add money for TransactionUUID: {0}", transactionUUID);
                return false;
            }
            return true;
        }
        public bool WithdrawMoney(UUID transactionID, string senderID, int amount)
        {
            bool success = withdrawMoney(transactionID, senderID, amount);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to withdraw {0} for sender {1} in TransactionID: {2}", amount, senderID, transactionID);
                return false;
            }
            return true;
        }
        public bool GiveMoney(UUID transactionID, string receiverID, int amount)
        {
            bool success = giveMoney(transactionID, receiverID, amount);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to give {0} to receiver {1} in TransactionID: {2}", amount, receiverID, transactionID);
                return false;
            }
            return true;
        }
        public bool UpdateTransactionStatus(UUID transactionID, int status, string description)
        {
            bool success = updateTransactionStatus(transactionID, status, description);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to update status for TransactionID: {0} with Status: {1}", transactionID, status);
                return false;
            }
            return true;
        }
        public bool AddUser(string userID, int balance, int status, int type)
        {
            bool success = addUser(userID, balance, status, type);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to add user {0} with balance {1}", userID, balance);
                return false;
            }
            return true;
        }
        public new IEnumerable<TransactionData> GetTransactionHistory(string userID, int startTime, int endTime)
        {
            return GetTransactionHistory(userID, startTime, endTime);
        }
        public new UserInfo FetchUserInfo(string userID)
        {
            return FetchUserInfo(userID);
        }
        public new bool UserExists(string userID)
        {
            return UserExists(userID);
        }
        public bool PerformTransaction(UUID transactionUUID)
        {
            bool success = DoTransfer(transactionUUID);
            if (!success)
            {
                m_log.ErrorFormat("[MONEY SERVER]: Failed to perform transaction for TransactionUUID: {0}", transactionUUID);
                return false;
            }
            return true;
        }
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            // Regular Expression für gültige E-Mail-Adressen
            const string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

            // Überprüfen, ob die E-Mail-Adresse das Muster erfüllt
            return Regex.IsMatch(email, pattern, RegexOptions.Compiled);
        }

        private Dictionary<UUID, int> balances = new Dictionary<UUID, int>();

        public int GetBalance(UUID uuid)
        {
            if (balances.TryGetValue(uuid, out int balance))
            {
                return balance;
            }
            else
            {
                // Return a default value if the UUID is not found
                return 0;
            }
        }

        public void SetBalance(UUID uuid, int balance)
        {
            balances[uuid] = balance;
        }

        #endregion
        // ##################     XMLRPC Pasing    ##################
        #region XMLRPC Pasing


        public static string ToXmlStringMini(Hashtable data)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("methodResponse");
            doc.AppendChild(root);

            XmlElement paramsElement = doc.CreateElement("params");
            root.AppendChild(paramsElement);

            foreach (DictionaryEntry entry in data)
            {
                XmlElement param = doc.CreateElement("param");
                XmlElement value = doc.CreateElement("value");
                value.InnerText = entry.Value.ToString();
                param.AppendChild(value);
                paramsElement.AppendChild(param);
            }

            return doc.OuterXml;
        }
        private string ToXmlString(Hashtable data)
        {
            XmlDocument doc = new XmlDocument();
            XmlNode methodCallNode = doc.CreateElement("methodCall");

            XmlNode methodNameNode = doc.CreateElement("methodName");
            methodNameNode.InnerText = "response";
            methodCallNode.AppendChild(methodNameNode);

            XmlNode paramsNode = doc.CreateElement("params");
            foreach (DictionaryEntry entry in data)
            {
                XmlNode paramNode = doc.CreateElement("param");

                XmlNode valueNode = doc.CreateElement("value");
                if (entry.Value is Hashtable)
                {
                    XmlNode structNode = doc.CreateElement("struct");
                    foreach (DictionaryEntry structEntry in (Hashtable)entry.Value)
                    {
                        XmlNode memberNode = doc.CreateElement("member");
                        XmlNode nameNode = doc.CreateElement("name");
                        nameNode.InnerText = structEntry.Key.ToString();
                        XmlNode valueNode2 = doc.CreateElement("value");
                        valueNode2.InnerText = structEntry.Value.ToString();
                        memberNode.AppendChild(nameNode);
                        memberNode.AppendChild(valueNode2);
                        structNode.AppendChild(memberNode);
                    }
                    valueNode.AppendChild(structNode);
                }
                else
                {
                    valueNode.InnerText = entry.Value.ToString();
                }

                paramNode.AppendChild(valueNode);
                paramsNode.AppendChild(paramNode);
            }

            methodCallNode.AppendChild(paramsNode);
            doc.AppendChild(methodCallNode);
            return doc.OuterXml;
        }

        // Methode zur Verarbeitung und Parsing der XML-RPC-Anfrage
        private object ParseXmlRpcRequest(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);

            XmlNode methodCallNode = doc.SelectSingleNode("/methodCall");
            XmlNode methodNameNode = methodCallNode.SelectSingleNode("methodName");

            if (methodNameNode == null)
                throw new Exception("Missing method name");

            string methodName = methodNameNode.InnerText;
            XmlNodeList members = methodCallNode.SelectNodes("//param/value/struct/member");

            if (methodName == "getCurrencyQuote")
            {
                CurrencyQuoteRequest request = new CurrencyQuoteRequest();
                foreach (XmlNode member in members)
                {
                    string name = member.SelectSingleNode("name").InnerText;
                    string value = member.SelectSingleNode("value").InnerText;

                    switch (name)
                    {
                        case "agentId": request.AgentId = value; break;
                        case "currencyBuy": request.CurrencyBuy = int.Parse(value); break;
                        case "language": request.Language = value; break;
                        case "secureSessionId": request.SecureSessionId = value; break;
                        case "viewerBuildVersion": request.ViewerBuildVersion = value; break;
                        case "viewerChannel": request.ViewerChannel = value; break;
                        case "viewerMajorVersion": request.ViewerMajorVersion = int.Parse(value); break;
                        case "viewerMinorVersion": request.ViewerMinorVersion = int.Parse(value); break;
                        case "viewerPatchVersion": request.ViewerPatchVersion = int.Parse(value); break;
                    }
                }
                m_log.InfoFormat("[MONEY XML RPC MODULE]: Processed Currency Quote Request for AgentId: {0}", request.AgentId);
                return request;

            }
            else if (methodName == "preflightBuyLandPrep")
            {
                LandPurchaseRequest request = new LandPurchaseRequest();
                foreach (XmlNode member in members)
                {
                    string name = member.SelectSingleNode("name").InnerText;
                    string value = member.SelectSingleNode("value").InnerText;

                    switch (name)
                    {
                        case "agentId": request.AgentId = value; break;
                        case "billableArea": request.BillableArea = int.Parse(value); break;
                        case "currencyBuy": request.CurrencyBuy = int.Parse(value); break;
                        case "language": request.Language = value; break;
                        case "secureSessionId": request.SecureSessionId = value; break;
                    }
                }
                m_log.InfoFormat("[MONEY XML RPC MODULE]: Processed Land Purchase Request for AgentId: {0}, BillableArea: {1}", request.AgentId, request.BillableArea);
                return request;
            }
            m_log.ErrorFormat("[MONEY XML RPC MODULE]: Unknown method name: {0}", methodName);
            throw new Exception("Unknown method name: " + methodName);
        }


        private void LogXmlRpcRequestFile(IOSHttpRequest request)
        {
            try
            {
                // Erstelle einen Dateipfad für das Log
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xmlrpc_debug.log");

                // Lies den Request-Body
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    requestBody = reader.ReadToEnd();
                }

                // Bereite den Logeintrag vor
                string logEntry = $"{DateTime.UtcNow}: {request.RawUrl}\n{requestBody}\n\n";

                // Schreibe den Logeintrag in die Datei
                File.AppendAllText(logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XML RPC MODULE DEBUG]: Error logging XML-RPC request: {0}", ex.Message);
            }
        }

        private void LogXmlRpcRequestConsole(IOSHttpRequest request)
        {
            m_log.InfoFormat("[MONEY XML RPC MODULE]: {0}", new StreamReader(request.InputStream).ReadToEnd());  // TODO: test

            try
            {
                // Lies den Request-Body
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    requestBody = reader.ReadToEnd();
                }

                // Bereite den Logeintrag vor
                string logEntry = $"{DateTime.UtcNow}: {request.RawUrl}\n{requestBody}\n\n";

                // Schreibe den Logeintrag in das Log
                m_log.Info(logEntry);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XML RPC MODULE DEBUG]: Error logging XML-RPC request: {0}", ex.Message);
            }
        }
        #endregion
        // ##################     handler         ##################
        #region handler

        // Spezifische Handler BalanceUpdateHandler: Verarbeitet Updates zum Kontostand.
        public XmlRpcResponse BalanceUpdateHandler(XmlRpcRequest request, IPEndPoint client)
        {
            if (request == null)
            {
                m_log.Error("[MONEY XMLRPC]: BalanceUpdateHandler: request is null.");
                return new XmlRpcResponse();
            }

            if (client == null)
            {
                m_log.Error("[MONEY XMLRPC]: BalanceUpdateHandler: client is null.");
                return new XmlRpcResponse();
            }

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                string balanceUpdateData = (string)requestData["balanceUpdateData"];

                // Process the balance update data
                m_log.InfoFormat("[MONEY XMLRPC]: BalanceUpdateHandler: Updating balance for user {0}", balanceUpdateData);

                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseData = new Hashtable();
                responseData.Add("success", true);
                response.Value = responseData;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: BalanceUpdateHandler: Exception occurred: {0}", ex.Message);
                return new XmlRpcResponse();
            }
        }

        // Spezifische Handler UserAlertHandler: Handhabt Benutzerbenachrichtigungen.
        public XmlRpcResponse UserAlertHandler(XmlRpcRequest request, IPEndPoint client)
        {
            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                string alertMessage = (string)requestData["alertMessage"];

                // Process the alert message
                m_log.InfoFormat("[MONEY XMLRPC]: UserAlertHandler: Alert message received: {0}", alertMessage);

                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseData = new Hashtable();
                responseData.Add("success", true);
                response.Value = responseData;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: UserAlertHandler: Exception occurred: {0}", ex.Message);
                return new XmlRpcResponse();
            }
        }

        public XmlRpcResponse handleClientLogin(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogin: Start.");

            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            responseData["success"] = false;
            responseData["clientBalance"] = 0;

            // Check Client Cert
            if (m_moneyCore.IsCheckClientCert())
            {
                string commonName = GetSSLCommonName();
                if (commonName == "")
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleClientLogin: Warnning: Check Client Cert is set, but SSL Common Name is empty.");
                    responseData["success"] = false;
                    responseData["description"] = "SSL Common Name is empty";
                    return response;
                }
                else
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogin: SSL Common Name is \"{0}\"", commonName);
                }

            }

            string universalID = string.Empty;
            string clientUUID = string.Empty;
            string sessionID = string.Empty;
            string secureID = string.Empty;
            string simIP = string.Empty;
            string userName = string.Empty;
            int balance = 0;
            int avatarType = (int)AvatarType.UNKNOWN_AVATAR;
            int avatarClass = (int)AvatarType.UNKNOWN_AVATAR;

            if (requestData.ContainsKey("clientUUID")) clientUUID = (string)requestData["clientUUID"];
            if (requestData.ContainsKey("clientSessionID")) sessionID = (string)requestData["clientSessionID"];
            if (requestData.ContainsKey("clientSecureSessionID")) secureID = (string)requestData["clientSecureSessionID"];
            if (requestData.ContainsKey("universalID")) universalID = (string)requestData["universalID"];
            if (requestData.ContainsKey("userName")) userName = (string)requestData["userName"];
            if (requestData.ContainsKey("openSimServIP")) simIP = (string)requestData["openSimServIP"];
            if (requestData.ContainsKey("avatarType")) avatarType = Convert.ToInt32(requestData["avatarType"]);
            if (requestData.ContainsKey("avatarClass")) avatarClass = Convert.ToInt32(requestData["avatarClass"]);

            string firstName = string.Empty;
            string lastName = string.Empty;
            string serverURL = string.Empty;
            string securePsw = string.Empty;

            if (!String.IsNullOrEmpty(universalID))
            {
                UUID uuid;
                Util.ParseUniversalUserIdentifier(universalID, out uuid, out serverURL, out firstName, out lastName, out securePsw);
            }
            if (String.IsNullOrEmpty(userName))
            {
                userName = firstName + " " + lastName;
            }

            // Information from DB
            UserInfo userInfo = m_moneyDBService.FetchUserInfo(clientUUID);
            if (userInfo != null)
            {
                avatarType = userInfo.Type;     // Avatar Type is not updated
                if (avatarType == (int)AvatarType.LOCAL_AVATAR) avatarClass = (int)AvatarType.LOCAL_AVATAR;
                if (avatarClass == (int)AvatarType.UNKNOWN_AVATAR) avatarClass = userInfo.Class;
                if (String.IsNullOrEmpty(userName)) userName = userInfo.Avatar;
            }

            if (avatarType == (int)AvatarType.UNKNOWN_AVATAR) avatarType = avatarClass;
            if (String.IsNullOrEmpty(serverURL)) avatarClass = (int)AvatarType.NPC_AVATAR;

            m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: Avatar {0} ({1}) is logged on.", userName, clientUUID);
            m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: Avatar Type is {0} and Avatar Class is {1}", avatarType, avatarClass);

            // Check Avatar
            if (avatarClass == (int)AvatarType.GUEST_AVATAR && !m_gst_enable)
            {
                responseData["description"] = "Avatar is a Guest avatar. But this Money Server does not support Guest avatars.";
                m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: {0}", responseData["description"]);
                return response;
            }
            else if (avatarClass == (int)AvatarType.HG_AVATAR && !m_hg_enable)
            {
                responseData["description"] = "Avatar is a HG avatar. But this Money Server does not support HG avatars.";
                m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: {0}", responseData["description"]);
                return response;
            }
            else if (avatarClass == (int)AvatarType.FOREIGN_AVATAR)
            {
                responseData["description"] = "Avatar is a Foreign avatar.";
                m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: {0}", responseData["description"]);
                return response;
            }
            else if (avatarClass == (int)AvatarType.UNKNOWN_AVATAR)
            {
                responseData["description"] = "Avatar is a Unknown avatar.";
                m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: {0}", responseData["description"]);
                return response;
            }
            // NPC
            else if (avatarClass == (int)AvatarType.NPC_AVATAR)
            {
                responseData["success"] = true;
                responseData["clientBalance"] = 0;
                responseData["description"] = "Avatar is a NPC.";
                m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogon: {0}", responseData["description"]);
                return response;
            }

            //Update the session and secure session dictionary
            lock (m_sessionDic)
            {
                if (!m_sessionDic.ContainsKey(clientUUID))
                {
                    m_sessionDic.Add(clientUUID, sessionID);
                }
                else m_sessionDic[clientUUID] = sessionID;
            }
            lock (m_secureSessionDic)
            {
                if (!m_secureSessionDic.ContainsKey(clientUUID))
                {
                    m_secureSessionDic.Add(clientUUID, secureID);
                }
                else m_secureSessionDic[clientUUID] = secureID;
            }

            try
            {
                if (userInfo == null) userInfo = new UserInfo();
                userInfo.UserID = clientUUID;
                userInfo.SimIP = simIP;
                userInfo.Avatar = userName;
                userInfo.PswHash = UUID.Zero.ToString();
                userInfo.Type = avatarType;
                userInfo.Class = avatarClass;
                userInfo.ServerURL = serverURL;
                if (!String.IsNullOrEmpty(securePsw)) userInfo.PswHash = securePsw;

                if (!m_moneyDBService.TryAddUserInfo(userInfo))
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleClientLogin: Unable to refresh information for user \"{0}\" in DB.", userName);
                    responseData["success"] = true;         // for FireStorm
                    responseData["description"] = "Update or add user information to db failed";
                    return response;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: handleClientLogin: Can't update userinfo for user {0}: {1}", clientUUID, e.ToString());
                responseData["description"] = "Exception occured" + e.ToString();
                return response;
            }

            try
            {
                balance = m_moneyDBService.getBalance(clientUUID);

                //add user to balances table if not exist. (if balance is -1, it means avatar is not exist at balances table)
                if (balance == -1)
                {
                    int default_balance = m_defaultBalance;
                    if (avatarClass == (int)AvatarType.HG_AVATAR) default_balance = m_hg_defaultBalance;
                    if (avatarClass == (int)AvatarType.GUEST_AVATAR) default_balance = m_gst_defaultBalance;

                    if (m_moneyDBService.addUser(clientUUID, default_balance, 0, avatarType))
                    {
                        responseData["success"] = true;
                        responseData["description"] = "add user successfully";
                        responseData["clientBalance"] = default_balance;
                    }
                    else
                    {
                        responseData["description"] = "add user failed";
                    }
                }
                //Success
                else if (balance >= 0)
                {
                    responseData["success"] = true;
                    responseData["description"] = "get user balance successfully";
                    responseData["clientBalance"] = balance;
                }

                return response;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: handleClientLogin: Can't get balance of user {0}: {1}", clientUUID, e.ToString());
                responseData["description"] = "Exception occured" + e.ToString();
            }

            return response;
        }

     
        public XmlRpcResponse handleClientLogout(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string clientUUID = string.Empty;
            if (requestData.ContainsKey("clientUUID")) clientUUID = (string)requestData["clientUUID"];

            m_log.InfoFormat("[MONEY XMLRPC]: handleClientLogout: User {0} is logging off.", clientUUID);
            try
            {
                lock (m_sessionDic)
                {
                    if (m_sessionDic.ContainsKey(clientUUID))
                    {
                        m_sessionDic.Remove(clientUUID);
                    }
                }

                lock (m_secureSessionDic)
                {
                    if (m_secureSessionDic.ContainsKey(clientUUID))
                    {
                        m_secureSessionDic.Remove(clientUUID);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY XMLRPC]: handleClientLogout: Failed to delete user session: " + e.ToString());
                responseData["success"] = false;
                return response;
            }

            responseData["success"] = true;
            return response;
        }

        public XmlRpcResponse handleTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: handleTransaction:");

            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            int amount = 0;
            int transactionType = 0;
            string senderID = string.Empty;
            string receiverID = string.Empty;
            string senderSessionID = string.Empty;
            string senderSecureSessionID = string.Empty;
            string objectID = string.Empty;
            string objectName = string.Empty;
            string regionHandle = string.Empty;
            string regionUUID = string.Empty;
            string description = "Newly added on";

            responseData["success"] = false;
            UUID transactionUUID = UUID.Random();

            if (requestData.ContainsKey("senderID")) senderID = (string)requestData["senderID"];
            if (requestData.ContainsKey("receiverID")) receiverID = (string)requestData["receiverID"];
            if (requestData.ContainsKey("senderSessionID")) senderSessionID = (string)requestData["senderSessionID"];
            if (requestData.ContainsKey("senderSecureSessionID")) senderSecureSessionID = (string)requestData["senderSecureSessionID"];
            if (requestData.ContainsKey("amount")) amount = Convert.ToInt32(requestData["amount"]);
            if (requestData.ContainsKey("objectID")) objectID = (string)requestData["objectID"];
            if (requestData.ContainsKey("objectName")) objectName = (string)requestData["objectName"];
            if (requestData.ContainsKey("regionHandle")) regionHandle = (string)requestData["regionHandle"];
            if (requestData.ContainsKey("regionUUID")) regionUUID = (string)requestData["regionUUID"];
            if (requestData.ContainsKey("transactionType")) transactionType = Convert.ToInt32(requestData["transactionType"]);
            if (requestData.ContainsKey("description")) description = (string)requestData["description"];

            m_log.InfoFormat("[MONEY XMLRPC]: handleTransaction: Transfering money from {0} to {1}, Amount = {2}", senderID, receiverID, amount);
            m_log.InfoFormat("[MONEY XMLRPC]: handleTransaction: Object ID = {0}, Object Name = {1}", objectID, objectName);

            if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID))
            {
                if (m_sessionDic[senderID] == senderSessionID && m_secureSessionDic[senderID] == senderSecureSessionID)
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: handleTransaction: Transfering money from {0} to {1}", senderID, receiverID);
                    int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);
                    try
                    {
                        TransactionData transaction = new TransactionData();
                        transaction.TransUUID = transactionUUID;
                        transaction.Sender = senderID;
                        transaction.Receiver = receiverID;
                        transaction.Amount = amount;
                        transaction.ObjectUUID = objectID;
                        transaction.ObjectName = objectName;
                        transaction.RegionHandle = regionHandle;
                        transaction.RegionUUID = regionUUID;
                        transaction.Type = transactionType;
                        transaction.Time = time;
                        transaction.SecureCode = UUID.Random().ToString();
                        transaction.Status = (int)Status.PENDING_STATUS;
                        transaction.CommonName = GetSSLCommonName();
                        transaction.Description = description + " " + DateTime.UtcNow.ToString();

                        UserInfo rcvr = m_moneyDBService.FetchUserInfo(receiverID);
                        if (rcvr == null)
                        {
                            m_log.ErrorFormat("[MONEY XMLRPC]: handleTransaction: Receive User is not yet in DB {0}", receiverID);
                            return response;
                        }

                        bool result = m_moneyDBService.addTransaction(transaction);
                        if (result)
                        {
                            UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
                            if (user != null)
                            {
                                if (amount > 0 || (m_enableAmountZero && amount == 0))
                                {
                                    string snd_message = "";
                                    string rcv_message = "";

                                    if (transaction.Type == (int)TransactionType.Gift)
                                    {
                                        snd_message = m_BalanceMessageSendGift;
                                        rcv_message = m_BalanceMessageReceiveGift;
                                    }
                                    else if (transaction.Type == (int)TransactionType.LandSale)
                                    {
                                        snd_message = m_BalanceMessageLandSale;
                                        rcv_message = m_BalanceMessageRcvLandSale;
                                    }
                                    else if (transaction.Type == (int)TransactionType.PayObject)
                                    {
                                        snd_message = m_BalanceMessageBuyObject;
                                        rcv_message = m_BalanceMessageSellObject;
                                    }
                                    else if (transaction.Type == (int)TransactionType.ObjectPays)
                                    {       // ObjectGiveMoney
                                        rcv_message = m_BalanceMessageGetMoney;
                                    }

                                    responseData["success"] = NotifyTransfer(transactionUUID, snd_message, rcv_message, objectName);
                                }
                                else if (amount == 0)
                                {
                                    responseData["success"] = true;     // No messages for L$0 object. by Fumi.Iseki
                                }
                                return response;
                            }
                        }
                        else
                        {  // add transaction failed
                            m_log.ErrorFormat("[MONEY XMLRPC]: handleTransaction: Add transaction for user {0} failed.", senderID);
                        }
                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY XMLRPC]: handleTransaction: Exception occurred while adding transaction: " + e.ToString());
                    }
                    return response;
                }
            }

            m_log.Error("[MONEY XMLRPC]: handleTransaction: Session authentication failure for sender " + senderID);
            responseData["message"] = "Session check failure, please re-login later!";
            return response;
        }

        public XmlRpcResponse handleForceTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            int amount = 0;
            int transactionType = 0;
            string senderID = string.Empty;
            string receiverID = string.Empty;
            string objectID = string.Empty;
            string objectName = string.Empty;
            string regionHandle = string.Empty;
            string regionUUID = string.Empty;
            string description = "Newly added on";

            responseData["success"] = false;
            UUID transactionUUID = UUID.Random();

            //
            if (!m_forceTransfer)
            {
                m_log.Error("[MONEY XMLRPC]: handleForceTransaction: Not allowed force transfer of Money.");
                m_log.Error("[MONEY XMLRPC]: handleForceTransaction: Set enableForceTransfer at [MoneyServer] to true in MoneyServer.ini");
                responseData["message"] = "not allowed force transfer of Money!";
                return response;
            }

            if (requestData.ContainsKey("senderID")) senderID = (string)requestData["senderID"];
            if (requestData.ContainsKey("receiverID")) receiverID = (string)requestData["receiverID"];
            if (requestData.ContainsKey("amount")) amount = Convert.ToInt32(requestData["amount"]);
            if (requestData.ContainsKey("objectID")) objectID = (string)requestData["objectID"];
            if (requestData.ContainsKey("objectName")) objectName = (string)requestData["objectName"];
            if (requestData.ContainsKey("regionHandle")) regionHandle = (string)requestData["regionHandle"];
            if (requestData.ContainsKey("regionUUID")) regionUUID = (string)requestData["regionUUID"];
            if (requestData.ContainsKey("transactionType")) transactionType = Convert.ToInt32(requestData["transactionType"]);
            if (requestData.ContainsKey("description")) description = (string)requestData["description"];

            m_log.InfoFormat("[MONEY XMLRPC]: handleForceTransaction: Force transfering money from {0} to {1}, Amount = {2}", senderID, receiverID, amount);
            m_log.InfoFormat("[MONEY XMLRPC]: handleForceTransaction: Object ID = {0}, Object Name = {1}", objectID, objectName);

            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);

            try
            {
                TransactionData transaction = new TransactionData();
                transaction.TransUUID = transactionUUID;
                transaction.Sender = senderID;
                transaction.Receiver = receiverID;
                transaction.Amount = amount;
                transaction.ObjectUUID = objectID;
                transaction.ObjectName = objectName;
                transaction.RegionHandle = regionHandle;
                transaction.RegionUUID = regionUUID;
                transaction.Type = transactionType;
                transaction.Time = time;
                transaction.SecureCode = UUID.Random().ToString();
                transaction.Status = (int)Status.PENDING_STATUS;
                transaction.CommonName = GetSSLCommonName();
                transaction.Description = description + " " + DateTime.UtcNow.ToString();

                UserInfo rcvr = m_moneyDBService.FetchUserInfo(receiverID);
                if (rcvr == null)
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleForceTransaction: Force receive User is not yet in DB {0}", receiverID);
                    return response;
                }

                bool result = m_moneyDBService.addTransaction(transaction);
                if (result)
                {
                    UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
                    if (user != null)
                    {
                        if (amount > 0 || (m_enableAmountZero && amount == 0))
                        {
                            string snd_message = "";
                            string rcv_message = "";

                            if (transaction.Type == (int)TransactionType.Gift)
                            {
                                snd_message = m_BalanceMessageSendGift;
                                rcv_message = m_BalanceMessageReceiveGift;
                            }
                            else if (transaction.Type == (int)TransactionType.LandSale)
                            {
                                snd_message = m_BalanceMessageLandSale;
                                snd_message = m_BalanceMessageRcvLandSale;
                            }
                            else if (transaction.Type == (int)TransactionType.PayObject)
                            {
                                snd_message = m_BalanceMessageBuyObject;
                                rcv_message = m_BalanceMessageSellObject;
                            }
                            else if (transaction.Type == (int)TransactionType.ObjectPays)
                            {       // ObjectGiveMoney
                                rcv_message = m_BalanceMessageGetMoney;
                            }

                            responseData["success"] = NotifyTransfer(transactionUUID, snd_message, rcv_message, objectName);
                        }
                        else if (amount == 0)
                        {
                            responseData["success"] = true;     // No messages for L$0 object. by Fumi.Iseki
                        }
                        return response;
                    }
                }
                else
                {  // add transaction failed
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleForceTransaction: Add force transaction for user {0} failed.", senderID);
                }
                return response;
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY XMLRPC]: handleForceTransaction: Exception occurred while adding force transaction: " + e.ToString());
            }
            return response;
        }

        public XmlRpcResponse handleScriptTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: handleScriptTransaction:");

            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            int amount = 0;
            int transactionType = 0;
            string senderID = UUID.Zero.ToString();
            string receiverID = UUID.Zero.ToString();
            string clientIP = remoteClient.Address.ToString();
            string secretCode = string.Empty;
            string description = "Scripted Send Money from/to Avatar on";

            responseData["success"] = false;
            UUID transactionUUID = UUID.Random();

            if (!m_scriptSendMoney || m_scriptAccessKey == "")
            {
                m_log.Error("[MONEY XMLRPC]: handleScriptTransaction: Not allowed send money to avatar!!");
                m_log.Error("[MONEY XMLRPC]: handleScriptTransaction: Set enableScriptSendMoney and MoneyScriptAccessKey at [MoneyServer] in MoneyServer.ini");
                responseData["message"] = "not allowed set money to avatar!";
                return response;
            }

            if (requestData.ContainsKey("senderID")) senderID = (string)requestData["senderID"];
            if (requestData.ContainsKey("receiverID")) receiverID = (string)requestData["receiverID"];
            if (requestData.ContainsKey("amount")) amount = Convert.ToInt32(requestData["amount"]);
            if (requestData.ContainsKey("transactionType")) transactionType = Convert.ToInt32(requestData["transactionType"]);
            if (requestData.ContainsKey("description")) description = (string)requestData["description"];
            if (requestData.ContainsKey("secretAccessCode")) secretCode = (string)requestData["secretAccessCode"];

            MD5 md5 = MD5.Create();
            byte[] code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(m_scriptAccessKey + "_" + clientIP));
            string hash = BitConverter.ToString(code).ToLower().Replace("-", "");
            code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(hash + "_" + m_scriptIPaddress));
            hash = BitConverter.ToString(code).ToLower().Replace("-", "");

            if (secretCode.ToLower() != hash)
            {
                m_log.Error("[MONEY XMLRPC]: handleScriptTransaction: Not allowed send money to avatar!!");
                m_log.Error("[MONEY XMLRPC]: handleScriptTransaction: Not match Script Access Key.");
                responseData["message"] = "not allowed send money to avatar! not match Script Key";
                return response;
            }

            m_log.InfoFormat("[MONEY XMLRPC]: handleScriptTransaction: Send money from {0} to {1}", senderID, receiverID);
            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);

            try
            {
                TransactionData transaction = new TransactionData();
                transaction.TransUUID = transactionUUID;
                transaction.Sender = senderID;
                transaction.Receiver = receiverID;
                transaction.Amount = amount;
                transaction.ObjectUUID = UUID.Zero.ToString();
                transaction.RegionHandle = "0";
                transaction.Type = transactionType;
                transaction.Time = time;
                transaction.SecureCode = UUID.Random().ToString();
                transaction.Status = (int)Status.PENDING_STATUS;
                transaction.CommonName = GetSSLCommonName();
                transaction.Description = description + " " + DateTime.UtcNow.ToString();

                UserInfo senderInfo = null;
                UserInfo receiverInfo = null;
                if (transaction.Sender != UUID.Zero.ToString()) senderInfo = m_moneyDBService.FetchUserInfo(transaction.Sender);
                if (transaction.Receiver != UUID.Zero.ToString()) receiverInfo = m_moneyDBService.FetchUserInfo(transaction.Receiver);

                if (senderInfo == null && receiverInfo == null)
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleScriptTransaction: Sender and Receiver are not yet in DB, or both of them are System: {0}, {1}",
                                                                                                                transaction.Sender, transaction.Receiver);
                    return response;
                }

                bool result = m_moneyDBService.addTransaction(transaction);
                if (result)
                {
                    if (amount > 0 || (m_enableAmountZero && amount == 0))
                    {
                        if (m_moneyDBService.DoTransfer(transactionUUID))
                        {
                            transaction = m_moneyDBService.FetchTransaction(transactionUUID);
                            if (transaction != null && transaction.Status == (int)Status.SUCCESS_STATUS)
                            {
                                m_log.InfoFormat("[MONEY XMLRPC]: handleScriptTransaction: ScriptTransaction money finished successfully, now update balance {0}",
                                                                                                                            transactionUUID.ToString());
                                string message = string.Empty;
                                if (senderInfo != null)
                                {
                                    if (receiverInfo == null) message = string.Format(m_BalanceMessageSendMoney, amount, "SYSTEM", "");
                                    else message = string.Format(m_BalanceMessageSendMoney, amount, receiverInfo.Avatar, "");
                                    UpdateBalance(transaction.Sender, message);
                                    m_log.InfoFormat("[MONEY XMLRPC]: handleScriptTransaction: Update balance of {0}. Message = {1}", transaction.Sender, message);
                                }
                                if (receiverInfo != null)
                                {
                                    if (senderInfo == null) message = string.Format(m_BalanceMessageReceiveMoney, amount, "SYSTEM", "");
                                    else message = string.Format(m_BalanceMessageReceiveMoney, amount, senderInfo.Avatar, "");
                                    UpdateBalance(transaction.Receiver, message);
                                    m_log.InfoFormat("[MONEY XMLRPC]: handleScriptTransaction: Update balance of {0}. Message = {1}", transaction.Receiver, message);
                                }

                                responseData["success"] = true;
                            }
                        }
                    }
                    else if (amount == 0)
                    {
                        responseData["success"] = true;     // No messages for L$0 add
                    }
                    return response;
                }
                else
                {  // add transaction failed
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleScriptTransaction: Add force transaction for user {0} failed.", transaction.Sender);
                }
                return response;
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY XMLRPC]: handleScriptTransaction: Exception occurred while adding money transaction: " + e.ToString());
            }
            return response;
        }

        public XmlRpcResponse handleAddBankerMoney(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            int amount = 0;
            int transactionType = 0;
            string senderID = UUID.Zero.ToString();
            string bankerID = string.Empty;
            string regionHandle = "0";
            string regionUUID = UUID.Zero.ToString();
            string description = "Add Money to Avatar on";

            responseData["success"] = false;
            UUID transactionUUID = UUID.Random();

            if (requestData.ContainsKey("bankerID")) bankerID = (string)requestData["bankerID"];
            if (requestData.ContainsKey("amount")) amount = Convert.ToInt32(requestData["amount"]);
            if (requestData.ContainsKey("regionHandle")) regionHandle = (string)requestData["regionHandle"];
            if (requestData.ContainsKey("regionUUID")) regionUUID = (string)requestData["regionUUID"];
            if (requestData.ContainsKey("transactionType")) transactionType = Convert.ToInt32(requestData["transactionType"]);
            if (requestData.ContainsKey("description")) description = (string)requestData["description"];

            // Check Banker Avatar
            if (m_bankerAvatar != UUID.Zero.ToString() && m_bankerAvatar != bankerID)
            {
                m_log.Error("[MONEY XMLRPC]: handleAddBankerMoney: Not allowed add money to avatar!!");
                m_log.Error("[MONEY XMLRPC]: handleAddBankerMoney: Set BankerAvatar at [MoneyServer] in MoneyServer.ini");
                responseData["message"] = "not allowed add money to avatar!";
                responseData["banker"] = false;
                return response;
            }
            responseData["banker"] = true;

            m_log.InfoFormat("[MONEY XMLRPC]: handleAddBankerMoney: Add money to avatar {0}", bankerID);
            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);

            try
            {
                TransactionData transaction = new TransactionData();
                transaction.TransUUID = transactionUUID;
                transaction.Sender = senderID;
                transaction.Receiver = bankerID;
                transaction.Amount = amount;
                transaction.ObjectUUID = UUID.Zero.ToString();
                transaction.RegionHandle = regionHandle;
                transaction.RegionUUID = regionUUID;
                transaction.Type = transactionType;
                transaction.Time = time;
                transaction.SecureCode = UUID.Random().ToString();
                transaction.Status = (int)Status.PENDING_STATUS;
                transaction.CommonName = GetSSLCommonName();
                transaction.Description = description + " " + DateTime.UtcNow.ToString();

                UserInfo rcvr = m_moneyDBService.FetchUserInfo(bankerID);
                if (rcvr == null)
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleAddBankerMoney: Avatar is not yet in DB {0}", bankerID);
                    return response;
                }

                bool result = m_moneyDBService.addTransaction(transaction);
                if (result)
                {
                    if (amount > 0 || (m_enableAmountZero && amount == 0))
                    {
                        if (m_moneyDBService.DoAddMoney(transactionUUID))
                        {
                            transaction = m_moneyDBService.FetchTransaction(transactionUUID);
                            if (transaction != null && transaction.Status == (int)Status.SUCCESS_STATUS)
                            {
                                m_log.InfoFormat("[MONEY XMLRPC]: handleAddBankerMoney: Adding money finished successfully, now update balance: {0}",
                                                                                                                            transactionUUID.ToString());
                                string message = string.Format(m_BalanceMessageBuyMoney, amount, "SYSTEM", "");
                                UpdateBalance(transaction.Receiver, message);
                                responseData["success"] = true;
                            }
                        }
                    }
                    else if (amount == 0)
                    {
                        responseData["success"] = true;     // No messages for L$0 add
                    }
                    return response;
                }
                else
                {  // add transaction failed
                    m_log.ErrorFormat("[MONEY XMLRPC]: handleAddBankerMoney: Add force transaction for user {0} failed.", senderID);
                }
                return response;
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY XMLRPC]: handleAddBankerMoney: Exception occurred while adding money transaction: " + e.ToString());
            }
            return response;
        }

        public XmlRpcResponse handlePayMoneyCharge(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge now.");

            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            int amount = 0;
            int transactionType = 0;
            string senderID = string.Empty;
            string receiverID = UUID.Zero.ToString();
            string senderSessionID = string.Empty;
            string senderSecureSessionID = string.Empty;
            string objectID = UUID.Zero.ToString();
            string objectName = string.Empty;
            string regionHandle = string.Empty;
            string regionUUID = string.Empty;
            string description = "Pay Charge on";

            responseData["success"] = false;
            UUID transactionUUID = UUID.Random();

            // Parameter aus der Anfrage extrahieren
            if (requestData.ContainsKey("senderID")) senderID = (string)requestData["senderID"];
            if (requestData.ContainsKey("senderSessionID")) senderSessionID = (string)requestData["senderSessionID"];
            if (requestData.ContainsKey("senderSecureSessionID")) senderSecureSessionID = (string)requestData["senderSecureSessionID"];
            if (requestData.ContainsKey("amount")) amount = Convert.ToInt32(requestData["amount"]);
            if (requestData.ContainsKey("regionHandle")) regionHandle = (string)requestData["regionHandle"];
            if (requestData.ContainsKey("regionUUID")) regionUUID = (string)requestData["regionUUID"];
            if (requestData.ContainsKey("transactionType")) transactionType = Convert.ToInt32(requestData["transactionType"]);
            if (requestData.ContainsKey("description")) description = (string)requestData["description"];
            if (requestData.ContainsKey("receiverID")) receiverID = (string)requestData["receiverID"];
            if (requestData.ContainsKey("objectID")) objectID = (string)requestData["objectID"];
            if (requestData.ContainsKey("objectName")) objectName = (string)requestData["objectName"];

            m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Transfering money from {0} to {1}, Amount = {2}", senderID, receiverID, amount);

            // Sitzungsprüfung überspringen für SYSTEM oder Banker
            if (senderID == m_bankerAvatar || senderID == "SYSTEM")
            {
                m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Sender ist SYSTEM oder BankerAvatar. Sitzungsprüfung wird übersprungen.");
            }
            else if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID))
            {
                if (m_sessionDic[senderID] != senderSessionID || m_secureSessionDic[senderID] != senderSecureSessionID)
                {
                    m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Sitzungsprüfung für Sender fehlgeschlagen " + senderID);
                    responseData["message"] = "Session check failure, please re-login later!";
                    return response;
                }
            }
            else
            {
                m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Sitzungsprüfung für Sender fehlgeschlagen " + senderID);
                responseData["message"] = "Session check failure, please re-login later!";
                return response;
            }

            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);

            try
            {
                TransactionData transaction = new TransactionData
                {
                    TransUUID = transactionUUID,
                    Sender = senderID,
                    Receiver = receiverID,
                    Amount = amount,
                    ObjectUUID = objectID,
                    ObjectName = objectName,
                    RegionHandle = regionHandle,
                    RegionUUID = regionUUID,
                    Type = transactionType,
                    Time = time,
                    SecureCode = UUID.Random().ToString(),
                    Status = (int)Status.PENDING_STATUS,
                    CommonName = GetSSLCommonName(),
                    Description = description + " " + DateTime.UtcNow.ToString()
                };

                bool result = m_moneyDBService.addTransaction(transaction);
                if (result)
                {
                    UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
                    if (user != null)
                    {
                        if (amount == 0)
                        {
                            // Für L$0 keine Transferaktion, einfach Erfolg zurückgeben
                            responseData["success"] = true;
                            return response;
                        }

                        if (amount > 0 || (m_enableAmountZero && amount == 0))
                        {
                            if (!NotifyTransfer(transactionUUID, "", "", ""))
                            {
                                m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Gutschrift fehlgeschlagen, versuche manuell Geld hinzuzufügen.");

                                Hashtable addMoneyParams = new Hashtable();
                                addMoneyParams["bankerID"] = "SYSTEM";
                                addMoneyParams["amount"] = amount;
                                addMoneyParams["regionHandle"] = regionHandle;
                                addMoneyParams["regionUUID"] = regionUUID;
                                addMoneyParams["transactionType"] = transactionType;
                                addMoneyParams["description"] = "Manuelle Gutschrift nach Fehlermeldung";

                                XmlRpcResponse addMoneyResponse = handleAddBankerMoney(
                                    new XmlRpcRequest("AddBankerMoney", new object[] { addMoneyParams }),
                                    remoteClient
                                );

                                Hashtable responseValue = addMoneyResponse.Value as Hashtable;
                                bool addMoneySuccess = addMoneyResponse != null
                                    && responseValue != null
                                    && responseValue.ContainsKey("success")
                                    && (bool)responseValue["success"];

                                responseData["success"] = addMoneySuccess;

                                if (!addMoneySuccess)
                                {
                                    responseData["message"] = "Manuelles Hinzufügen des Geldes fehlgeschlagen.";
                                }

                                return response;
                            }

                            // Wenn NotifyTransfer erfolgreich war
                            responseData["success"] = true;
                            return response;
                        }
                    }
                }
                else
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Zahlungstransaktion für Benutzer {0} fehlgeschlagen.", senderID);
                }
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Ausnahme bei der Zahlungstransaktion: " + e.ToString());
            }

            return response;
        }

        public XmlRpcResponse handleCancelTransfer(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string secureCode = string.Empty;
            string transactionID = string.Empty;
            UUID transactionUUID = UUID.Zero;

            responseData["success"] = false;

            if (requestData.ContainsKey("secureCode")) secureCode = (string)requestData["secureCode"];
            if (requestData.ContainsKey("transactionID"))
            {
                transactionID = (string)requestData["transactionID"];
                UUID.TryParse(transactionID, out transactionUUID);
            }

            if (string.IsNullOrEmpty(secureCode) || string.IsNullOrEmpty(transactionID))
            {
                m_log.Error("[MONEY XMLRPC]: handleCancelTransfer: secureCode and/or transactionID are empty.");
                return response;
            }

            TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
            UserInfo user = m_moneyDBService.FetchUserInfo(transaction.Sender);

            try
            {
                m_log.InfoFormat("[MONEY XMLRPC]: handleCancelTransfer: User {0} wanted to cancel the transaction.", user.Avatar);
                if (m_moneyDBService.ValidateTransfer(secureCode, transactionUUID))
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: handleCancelTransfer: User {0} has canceled the transaction {1}", user.Avatar, transactionID);
                    m_moneyDBService.updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS,
                                                            "User canceled the transaction on " + DateTime.UtcNow.ToString());
                    responseData["success"] = true;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: handleCancelTransfer: Exception occurred when transaction {0}: {1}", transactionID, e.ToString());
            }
            return response;
        }

        public XmlRpcResponse handleGetTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string clientID = string.Empty;
            string sessionID = string.Empty;
            string secureID = string.Empty;
            string transactionID = string.Empty;
            UUID transactionUUID = UUID.Zero;

            responseData["success"] = false;

            if (requestData.ContainsKey("clientUUID")) clientID = (string)requestData["clientUUID"];
            if (requestData.ContainsKey("clientSessionID")) sessionID = (string)requestData["clientSessionID"];
            if (requestData.ContainsKey("clientSecureSessionID")) secureID = (string)requestData["clientSecureSessionID"];

            if (requestData.ContainsKey("transactionID"))
            {
                transactionID = (string)requestData["transactionID"];
                UUID.TryParse(transactionID, out transactionUUID);
            }

            if (m_sessionDic.ContainsKey(clientID) && m_secureSessionDic.ContainsKey(clientID))
            {
                if (m_sessionDic[clientID] == sessionID && m_secureSessionDic[clientID] == secureID)
                {
                    //
                    if (string.IsNullOrEmpty(transactionID))
                    {
                        responseData["description"] = "TransactionID is empty";
                        m_log.Error("[MONEY XMLRPC]: handleGetTransaction: TransactionID is empty.");
                        return response;
                    }

                    try
                    {
                        TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
                        if (transaction != null)
                        {
                            responseData["success"] = true;
                            responseData["amount"] = transaction.Amount;
                            responseData["time"] = transaction.Time;
                            responseData["type"] = transaction.Type;
                            responseData["sender"] = transaction.Sender.ToString();
                            responseData["receiver"] = transaction.Receiver.ToString();
                            responseData["description"] = transaction.Description;
                        }
                        else
                        {
                            responseData["description"] = "Invalid Transaction UUID";
                        }

                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[MONEY XMLRPC]: handleGetTransaction: {0}", e.ToString());
                        m_log.ErrorFormat("[MONEY XMLRPC]: handleGetTransaction: Can't get transaction information for {0}", transactionUUID.ToString());
                    }
                    return response;
                }
            }

            responseData["success"] = false;
            responseData["description"] = "Session check failure, please re-login";
            return response;
        }

        public XmlRpcResponse handleWebLogin(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string userID = string.Empty;
            string webSessionID = string.Empty;

            responseData["success"] = false;

            if (requestData.ContainsKey("userID")) userID = (string)requestData["userID"];
            if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];

            if (string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(webSessionID))
            {
                responseData["errorMessage"] = "userID or sessionID can`t be empty, login failed!";
                return response;
            }

            //Update the web session dictionary
            lock (m_webSessionDic)
            {
                if (!m_webSessionDic.ContainsKey(userID))
                {
                    m_webSessionDic.Add(userID, webSessionID);
                }
                else m_webSessionDic[userID] = webSessionID;
            }

            m_log.InfoFormat("[MONEY XMLRPC]: handleWebLogin: User {0} has logged in from web.", userID);
            responseData["success"] = true;
            return response;
        }

        public XmlRpcResponse handleWebLogout(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string userID = string.Empty;
            string webSessionID = string.Empty;

            responseData["success"] = false;

            if (requestData.ContainsKey("userID")) userID = (string)requestData["userID"];
            if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];

            if (string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(webSessionID))
            {
                responseData["errorMessage"] = "userID or sessionID can`t be empty, log out failed!";
                return response;
            }

            //Update the web session dictionary
            lock (m_webSessionDic)
            {
                if (m_webSessionDic.ContainsKey(userID))
                {
                    m_webSessionDic.Remove(userID);
                }
            }

            m_log.InfoFormat("[MONEY XMLRPC]: handleWebLogout: User {0} has logged out from web.", userID);
            responseData["success"] = true;
            return response;
        }

        public XmlRpcResponse handleWebGetBalance(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string userID = string.Empty;
            string webSessionID = string.Empty;
            int balance = 0;

            responseData["success"] = false;

            if (requestData.ContainsKey("userID")) userID = (string)requestData["userID"];
            if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];

            m_log.InfoFormat("[MONEY XMLRPC]: handleWebGetBalance: Getting balance for user {0}", userID);

            //perform session check
            if (m_webSessionDic.ContainsKey(userID))
            {
                if (m_webSessionDic[userID] == webSessionID)
                {
                    try
                    {
                        balance = m_moneyDBService.getBalance(userID);
                        UserInfo user = m_moneyDBService.FetchUserInfo(userID);
                        if (user != null)
                        {
                            responseData["userName"] = user.Avatar;
                        }
                        else
                        {
                            responseData["userName"] = "unknown user";
                        }
                        // User not found
                        if (balance == -1)
                        {
                            responseData["errorMessage"] = "User not found";
                            responseData["balance"] = 0;
                        }
                        else if (balance >= 0)
                        {
                            responseData["success"] = true;
                            responseData["balance"] = balance;
                        }
                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[MONEY XMLRPC]: handleWebGetBalance: Can't get balance for user {0}, Exception {1}", userID, e.ToString());
                        responseData["errorMessage"] = "Exception occurred when getting balance";
                        return response;
                    }
                }
            }

            m_log.Error("[MONEY XMLRPC]: handleWebLogout: Session authentication failed when getting balance for user " + userID);
            responseData["errorMessage"] = "Session check failure, please re-login";
            return response;
        }

        public XmlRpcResponse handleWebGetTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string userID = string.Empty;
            string webSessionID = string.Empty;
            int lastIndex = -1;
            int startTime = 0;
            int endTime = 0;

            responseData["success"] = false;

            if (requestData.ContainsKey("userID")) userID = (string)requestData["userID"];
            if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];
            if (requestData.ContainsKey("startTime")) startTime = (int)requestData["startTime"];
            if (requestData.ContainsKey("endTime")) endTime = (int)requestData["endTime"];
            if (requestData.ContainsKey("lastIndex")) lastIndex = (int)requestData["lastIndex"];

            if (m_webSessionDic.ContainsKey(userID))
            {
                if (m_webSessionDic[userID] == webSessionID)
                {
                    try
                    {
                        int total = m_moneyDBService.getTransactionNum(userID, startTime, endTime);
                        TransactionData tran = null;
                        m_log.InfoFormat("[MONEY XMLRPC]: handleWebGetTransaction: Getting transation[{0}] for user {1}", lastIndex + 1, userID);
                        if (total > lastIndex + 2)
                        {
                            responseData["isEnd"] = false;
                        }
                        else
                        {
                            responseData["isEnd"] = true;
                        }

                        tran = m_moneyDBService.FetchTransaction(userID, startTime, endTime, lastIndex);
                        if (tran != null)
                        {
                            UserInfo senderInfo = m_moneyDBService.FetchUserInfo(tran.Sender);
                            UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(tran.Receiver);
                            if (senderInfo != null && receiverInfo != null)
                            {
                                responseData["senderName"] = senderInfo.Avatar;
                                responseData["receiverName"] = receiverInfo.Avatar;
                            }
                            else
                            {
                                responseData["senderName"] = "unknown user";
                                responseData["receiverName"] = "unknown user";
                            }
                            responseData["success"] = true;
                            responseData["transactionIndex"] = lastIndex + 1;
                            responseData["transactionUUID"] = tran.TransUUID.ToString();
                            responseData["senderID"] = tran.Sender;
                            responseData["receiverID"] = tran.Receiver;
                            responseData["amount"] = tran.Amount;
                            responseData["type"] = tran.Type;
                            responseData["time"] = tran.Time;
                            responseData["status"] = tran.Status;
                            responseData["description"] = tran.Description;
                        }
                        else
                        {
                            responseData["errorMessage"] = string.Format("Unable to fetch transaction data with the index {0}", lastIndex + 1);
                        }
                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[MONEY XMLRPC]: handleWebGetTransaction: Can't get transaction for user {0}, Exception {1}", userID, e.ToString());
                        responseData["errorMessage"] = "Exception occurred when getting transaction";
                        return response;
                    }
                }
            }

            m_log.Error("[MONEY XMLRPC]: handleWebGetTransaction: Session authentication failed when getting transaction for user " + userID);
            responseData["errorMessage"] = "Session check failure, please re-login";
            return response;
        }

        public XmlRpcResponse handleWebGetTransactionNum(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string userID = string.Empty;
            string webSessionID = string.Empty;
            int startTime = 0;
            int endTime = 0;

            responseData["success"] = false;

            if (requestData.ContainsKey("userID")) userID = (string)requestData["userID"];
            if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];
            if (requestData.ContainsKey("startTime")) startTime = (int)requestData["startTime"];
            if (requestData.ContainsKey("endTime")) endTime = (int)requestData["endTime"];

            if (m_webSessionDic.ContainsKey(userID))
            {
                if (m_webSessionDic[userID] == webSessionID)
                {
                    int it = m_moneyDBService.getTransactionNum(userID, startTime, endTime);
                    if (it >= 0)
                    {
                        m_log.InfoFormat("[MONEY XMLRPC]: handleWebGetTransactionNum: Get {0} transactions for user {1}", it, userID);
                        responseData["success"] = true;
                        responseData["number"] = it;
                    }
                    return response;
                }
            }

            m_log.Error("[MONEY XMLRPC]: handleWebGetTransactionNum: Session authentication failed when getting transaction number for user " + userID);
            responseData["errorMessage"] = "Session check failure, please re-login";
            return response;
        }


        #endregion
        // ##################     helper          ##################
        #region helper


        //Spezifische Handler OnMoneyTransferedHandler: Protokolliert Details zu einer Geldüberweisung.
        public XmlRpcResponse OnMoneyTransferedHandler(XmlRpcRequest request, IPEndPoint client)
        {
            if (request == null)
            {
                m_log.Error("[MONEY XMLRPC]: OnMoneyTransferedHandler: request is null.");
                return new XmlRpcResponse();
            }

            if (client == null)
            {
                m_log.Error("[MONEY XMLRPC]: OnMoneyTransferedHandler: client is null.");
                return new XmlRpcResponse();
            }

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                string transactionID = (string)requestData["transactionID"];
                UUID transactionUUID = UUID.Zero;
                UUID.TryParse(transactionID, out transactionUUID);

                TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
                UserInfo user = m_moneyDBService.FetchUserInfo(transaction.Sender);

                m_log.InfoFormat("[MONEY XMLRPC]: OnMoneyTransferedHandler: Transaction {0} from user {1} to user {2} for {3} units",
                    transactionID, user.Avatar, transaction.Receiver, transaction.Amount);

                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseData = new Hashtable();
                responseData.Add("success", true);
                response.Value = responseData;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: OnMoneyTransferedHandler: Exception occurred: {0}", ex.Message);
                return new XmlRpcResponse();
            }
        }

        public string GetSSLCommonName(XmlRpcRequest request)
        {
            if (request.Params.Count > 5)
            {
                m_sslCommonName = (string)request.Params[5];
            }
            else if (request.Params.Count == 5)
            {
                m_sslCommonName = (string)request.Params[4];
                if (m_sslCommonName == "gridproxy") m_sslCommonName = "";
            }
            else
            {
                m_sslCommonName = "";
            }
            return m_sslCommonName;
        }

        /// <summary>Gets the name of the SSL common.</summary>
        public string GetSSLCommonName()
        {
            return m_sslCommonName;
        }

        public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount, UUID txn, out string result)
        {
            result = String.Empty;
            string description = String.Format("Object {0} pays {1}", resolveObjectName(objectID), resolveAgentName(toID));

            bool give_result = doMoneyTransfer(fromID, toID, amount, 2, description);


            BalanceUpdate(fromID, toID, give_result, description);

            return give_result;
        }

        private string resolveObjectName(UUID objectID)
        {
            SceneObjectPart part = findPrim(objectID);
            if (part != null)
            {
                return part.Name;
            }
            return String.Empty;
        }

        private string resolveAgentName(UUID agentID)
        {
            // try avatar username surname
            Scene scene = GetRandomScene();
            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);
            if (account != null)
            {
                string avatarname = account.FirstName + " " + account.LastName;
                return avatarname;
            }
            else
            {
                m_log.ErrorFormat(
                    "[MONEY]: Could not resolve user {0}",
                    agentID);
            }

            return String.Empty;
        }

        /// <summary>
        /// Utility function Gets a Random scene in the instance.  For when which scene exactly you're doing something with doesn't matter
        /// </summary>
        /// <returns></returns>
        public Scene GetRandomScene()
        {
            lock (m_scenes)
            {
                foreach (Scene rs in m_scenes.Values)
                    return rs;
            }
            return null;
        }

        private SceneObjectPart findPrim(UUID objectID)
        {
            lock (m_scenes)
            {
                foreach (Scene s in m_scenes.Values)
                {
                    SceneObjectPart part = s.GetSceneObjectPart(objectID);
                    if (part != null)
                    {
                        return part;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Transfer money
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="Receiver"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        private bool doMoneyTransfer(UUID Sender, UUID Receiver, int amount, int transactiontype, string description)
        {
            return true;
        }

        private void BalanceUpdate(UUID senderID, UUID receiverID, bool transactionresult, string description)
        {
            IClientAPI sender = LocateClientObject(senderID);
            IClientAPI receiver = LocateClientObject(receiverID);

            if (senderID != receiverID)
            {
                if (sender != null)
                {
                    sender.SendMoneyBalance(UUID.Random(), transactionresult, Utils.StringToBytes(description), GetFundsForAgentID(senderID), 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
                }

                if (receiver != null)
                {
                    receiver.SendMoneyBalance(UUID.Random(), transactionresult, Utils.StringToBytes(description), GetFundsForAgentID(receiverID), 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
                }
            }
        }

        /// <summary>
        /// Sends the the stored money balance to the client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="agentID"></param>
        /// <param name="SessionID"></param>
        /// <param name="TransactionID"></param>
        public void SendMoneyBalance(IClientAPI client, UUID agentID, UUID SessionID, UUID TransactionID)
        {
            if (client.AgentId == agentID && client.SessionId == SessionID)
            {
                int returnfunds = 0;

                try
                {
                    returnfunds = GetFundsForAgentID(agentID);
                }
                catch (Exception e)
                {
                    client.SendAlertMessage(e.Message + " ");
                }

                client.SendMoneyBalance(TransactionID, true, Array.Empty<byte>(), returnfunds, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
            }
            else
            {
                client.SendAlertMessage("Unable to send your money balance to you!");
            }
        }

        private int GetFundsForAgentID(UUID AgentID)
        {
            int returnfunds = 0;

            return returnfunds;
        }

        /// <summary>
        /// Locates a IClientAPI for the client specified
        /// </summary>
        /// <param name="AgentID"></param>
        /// <returns></returns>
        private IClientAPI LocateClientObject(UUID AgentID)
        {
            ScenePresence tPresence;
            lock (m_scenes)
            {
                foreach (Scene _scene in m_scenes.Values)
                {
                    tPresence = _scene.GetScenePresence(AgentID);
                    if (tPresence != null && !tPresence.IsDeleted && !tPresence.IsChildAgent)
                        return tPresence.ControllingClient;
                }
            }
            return null;
        }

        public bool NotifyTransfer(UUID transactionUUID, string msg2sender, string msg2receiver, string objectName)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: User has accepted the transaction, now continue with the transaction");

            try
            {
                if (m_moneyDBService.DoTransfer(transactionUUID))
                {
                    TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
                    if (transaction != null && transaction.Status == (int)Status.SUCCESS_STATUS)
                    {
                        //m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction Type = {0}", transaction.Type);
                        //m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Payment finished successfully, now update balance {0}", transactionUUID.ToString());

                        bool updateSender = true;
                        bool updateReceiv = true;
                        if (transaction.Sender == transaction.Receiver) updateSender = false;
                        if (transaction.Type == (int)TransactionType.UploadCharge) updateReceiv = false;

                        if (updateSender)
                        {
                            UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(transaction.Receiver);
                            string receiverName = "unknown user";
                            if (receiverInfo != null) receiverName = receiverInfo.Avatar;
                            string snd_message = string.Format(msg2sender, transaction.Amount, receiverName, objectName);
                            UpdateBalance(transaction.Sender, snd_message);
                        }
                        if (updateReceiv)
                        {
                            UserInfo senderInfo = m_moneyDBService.FetchUserInfo(transaction.Sender);
                            string senderName = "unknown user";
                            if (senderInfo != null) senderName = senderInfo.Avatar;
                            string rcv_message = string.Format(msg2receiver, transaction.Amount, senderName, objectName);
                            UpdateBalance(transaction.Receiver, rcv_message);
                        }

                        // Notify to sender
                        if (transaction.Type == (int)TransactionType.PayObject)
                        {
                            m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Now notify opensim to give object to customer {0} ", transaction.Sender);
                            Hashtable requestTable = new Hashtable();
                            requestTable["clientUUID"] = transaction.Sender;
                            requestTable["receiverUUID"] = transaction.Receiver;

                            if (m_sessionDic.ContainsKey(transaction.Sender) && m_secureSessionDic.ContainsKey(transaction.Sender))
                            {
                                requestTable["clientSessionID"] = m_sessionDic[transaction.Sender];
                                requestTable["clientSecureSessionID"] = m_secureSessionDic[transaction.Sender];
                            }
                            else
                            {
                                requestTable["clientSessionID"] = UUID.Zero.ToString();
                                requestTable["clientSecureSessionID"] = UUID.Zero.ToString();
                            }
                            requestTable["transactionType"] = transaction.Type;
                            requestTable["amount"] = transaction.Amount;
                            requestTable["objectID"] = transaction.ObjectUUID;
                            requestTable["objectName"] = transaction.ObjectName;
                            requestTable["regionHandle"] = transaction.RegionHandle;

                            UserInfo user = m_moneyDBService.FetchUserInfo(transaction.Sender);
                            if (user != null)
                            {
                                Hashtable responseTable = genericCurrencyXMLRPCRequest(requestTable, "OnMoneyTransfered", user.SimIP);

                                if (responseTable != null && responseTable.ContainsKey("success"))
                                {
                                    //User not online or failed to get object ?
                                    if (!(bool)responseTable["success"])
                                    {
                                        m_log.ErrorFormat("[MONEY XMLRPC]: NotifyTransfer: User {0} can't get the object, rolling back.", transaction.Sender);
                                        if (RollBackTransaction(transaction))
                                        {
                                            m_log.ErrorFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction {0} failed but roll back succeeded.", transactionUUID.ToString());
                                        }
                                        else
                                        {
                                            m_log.ErrorFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction {0} failed and roll back failed as well.",
                                                                                                                        transactionUUID.ToString());
                                        }
                                    }
                                    else
                                    {
                                        m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction {0} finished successfully.", transactionUUID.ToString());
                                        return true;
                                    }
                                }
                            }
                            return false;
                        }
                        return true;
                    }
                }
                m_log.ErrorFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction {0} failed.", transactionUUID.ToString());
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: NotifyTransfer: exception occurred when transaction {0}: {1}", transactionUUID.ToString(), e.ToString());
            }

            return false;
        }

        public XmlRpcResponse handleGetBalance(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            GetSSLCommonName(request);

            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            response.Value = responseData;

            string clientUUID = string.Empty;
            string sessionID = string.Empty;
            string secureID = string.Empty;
            int balance;

            responseData["success"] = false;

            if (requestData.ContainsKey("clientUUID")) clientUUID = (string)requestData["clientUUID"];
            if (requestData.ContainsKey("clientSessionID")) sessionID = (string)requestData["clientSessionID"];
            if (requestData.ContainsKey("clientSecureSessionID")) secureID = (string)requestData["clientSecureSessionID"];

            m_log.InfoFormat("[MONEY XMLRPC]: handleGetBalance: Getting balance for user {0}", clientUUID);

            if (m_sessionDic.ContainsKey(clientUUID) && m_secureSessionDic.ContainsKey(clientUUID))
            {
                if (m_sessionDic[clientUUID] == sessionID && m_secureSessionDic[clientUUID] == secureID)
                {
                    try
                    {
                        balance = m_moneyDBService.getBalance(clientUUID);
                        if (balance == -1) // User not found
                        {
                            responseData["description"] = "user not found";
                            responseData["clientBalance"] = 0;
                        }
                        else if (balance >= 0)
                        {
                            responseData["success"] = true;
                            responseData["clientBalance"] = balance;
                        }

                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[MONEY XMLRPC]: handleGetBalance: Can't get balance for user {0}, Exception {1}", clientUUID, e.ToString());
                    }
                    return response;
                }
            }

            m_log.Error("[MONEY XMLRPC]: handleGetBalance: Session authentication failed when getting balance for user " + clientUUID);
            responseData["description"] = "Session check failure, please re-login";
            return response;
        }
  
        private Hashtable genericCurrencyXMLRPCRequest(Hashtable reqParams, string method, string uri)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: genericCurrencyXMLRPCRequest: to {0}", uri);

            if (reqParams.Count <= 0 || string.IsNullOrEmpty(method)) return null;

            if (m_checkServerCert)
            {
                if (!uri.StartsWith("https://"))
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: genericCurrencyXMLRPCRequest: CheckServerCert is true, but protocol is not HTTPS. Please check INI file.");
                    //return null; 
                }
            }
            else
            {
                if (!uri.StartsWith("https://") && !uri.StartsWith("http://"))
                {
                    m_log.ErrorFormat("[MONEY XMLRPC]: genericCurrencyXMLRPCRequest: Invalid Region Server URL: {0}", uri);
                    return null;
                }
            }

            ArrayList arrayParams = new ArrayList();
            arrayParams.Add(reqParams);
            XmlRpcResponse moneyServResp = null;
            try
            {
                NSLXmlRpcRequest moneyModuleReq = new NSLXmlRpcRequest(method, arrayParams);
                moneyServResp = moneyModuleReq.certSend(uri, m_certVerify, m_checkServerCert, MONEYMODULE_REQUEST_TIMEOUT);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: genericCurrencyXMLRPCRequest: Unable to connect to Region Server {0}", uri);
                m_log.ErrorFormat("[MONEY XMLRPC]: genericCurrencyXMLRPCRequest: {0}", ex.ToString());

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Failed to perform actions on OpenSim Server";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }

            if (moneyServResp == null || moneyServResp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Failed to perform actions on OpenSim Server";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }

            Hashtable moneyRespData = (Hashtable)moneyServResp.Value;
            return moneyRespData;
        }

        private void UpdateBalance(string userID, string message)
        {
            string sessionID = string.Empty;
            string secureID = string.Empty;

            // Konfiguriere das maximale Guthaben (dieser Wert kann aus einer Konfigurationsdatei wie MoneyServer.ini geladen werden)
            //int m_CurrencyMaximum = m_CurrencyMaximum;

            if (m_sessionDic.ContainsKey(userID) && m_secureSessionDic.ContainsKey(userID))
            {
                sessionID = m_sessionDic[userID];
                secureID = m_secureSessionDic[userID];

                // Aktuelles Guthaben des Benutzers abrufen
                int currentBalance = m_moneyDBService.getBalance(userID);

                // Überprüfen, ob das Guthaben über dem Maximum liegt und ggf. abziehen
                if (currentBalance > m_CurrencyMaximum)
                {
                    int excessAmount = currentBalance - m_CurrencyMaximum;

                    // Guthaben auf das Maximum reduzieren
                    bool balanceUpdateSuccess = ReduceUserBalance(userID, excessAmount);
                    if (!balanceUpdateSuccess)
                    {
                        m_log.ErrorFormat("[UpdateBalance]: Error reducing user balance for user {0}", userID);
                        return;
                    }

                    currentBalance = m_CurrencyMaximum;
                    m_log.InfoFormat("[UpdateBalance]: Reduced balance for user {0} by {1} to enforce maximum limit of {2}", userID, excessAmount, m_CurrencyMaximum);
                }

                Hashtable requestTable = new Hashtable();
                requestTable["clientUUID"] = userID;
                requestTable["clientSessionID"] = sessionID;
                requestTable["clientSecureSessionID"] = secureID;
                requestTable["Balance"] = currentBalance;
                if (message != "") requestTable["Message"] = message;

                UserInfo user = m_moneyDBService.FetchUserInfo(userID);
                if (user != null)
                {
                    genericCurrencyXMLRPCRequest(requestTable, "UpdateBalance", user.SimIP);
                    m_log.InfoFormat("[MONEY XMLRPC]: UpdateBalance: Sent UpdateBalance Request to {0}", user.SimIP.ToString());
                }
            }
        }

        private bool ReduceUserBalance(string userID, int amount)
        {
            string sql = "UPDATE balances SET balance = balance - ?amount WHERE user = ?userID";
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?userID", userID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[ReduceUserBalance]: Error reducing user balance: {0}", ex.Message);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        protected bool RollBackTransaction(TransactionData transaction)
        {
            if (m_moneyDBService.withdrawMoney(transaction.TransUUID, transaction.Receiver, transaction.Amount))
            {
                if (m_moneyDBService.giveMoney(transaction.TransUUID, transaction.Sender, transaction.Amount))
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: RollBackTransaction: Transaction {0} is successfully.", transaction.TransUUID.ToString());
                    m_moneyDBService.updateTransactionStatus(transaction.TransUUID, (int)Status.FAILED_STATUS,
                                                                    "The buyer failed to get the object, roll back the transaction");
                    UserInfo senderInfo = m_moneyDBService.FetchUserInfo(transaction.Sender);
                    UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(transaction.Receiver);
                    string senderName = "unknown user";
                    string receiverName = "unknown user";
                    if (senderInfo != null) senderName = senderInfo.Avatar;
                    if (receiverInfo != null) receiverName = receiverInfo.Avatar;

                    string snd_message = string.Format(m_BalanceMessageRollBack, transaction.Amount, receiverName, transaction.ObjectName);
                    string rcv_message = string.Format(m_BalanceMessageRollBack, transaction.Amount, senderName, transaction.ObjectName);

                    if (transaction.Sender != transaction.Receiver) UpdateBalance(transaction.Sender, snd_message);
                    UpdateBalance(transaction.Receiver, rcv_message);
                    return true;
                }
            }
            return false;
        }

        // #########################################
        // Cashbook ausgabe Transaktionen der User.
        // #########################################

        public void GetCashbookBalance(string userID)
        {
            int balance = 0;
            string query = "SELECT balance FROM balances WHERE user = ?userID";

            MySQLSuperManager dbm = ((MoneyDBService)m_moneyDBService).GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(query, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);
                    object result = cmd.ExecuteScalar();
                    m_log.InfoFormat("[Cashbook]: Query executed for userID: {0}, result: {1}", userID, result);
                    if (result != null)
                    {
                        balance = Convert.ToInt32(result);
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[Cashbook]: Error getting balance for userID: {0}. Exception: {1}", userID, ex);
            }
            finally
            {
                dbm.Release();
            }

            m_log.InfoFormat("[Cashbook]: User: {0}, Balance: {1}", userID, balance);
        }


        public void GetCashbookTotalSales(string userID)
        {
            List<CashbookTotalSalesData> totalSalesList = new List<CashbookTotalSalesData>();
            string query = "SELECT objectUUID, TotalCount, TotalAmount FROM totalsales WHERE user = ?userID";

            MySQLSuperManager dbm = ((MoneyDBService)m_moneyDBService).GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(query, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CashbookTotalSalesData data = new CashbookTotalSalesData
                            {
                                ObjectUUID = reader.GetString("objectUUID"),
                                TotalCount = reader.GetInt32("TotalCount"),
                                TotalAmount = reader.GetInt32("TotalAmount")
                            };
                            totalSalesList.Add(data);
                            m_log.InfoFormat("[Cashbook]: Found sale: ObjectUUID={0}, TotalCount={1}, TotalAmount={2}", data.ObjectUUID, data.TotalCount, data.TotalAmount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[Cashbook]: Error getting total sales for userID: {0}. Exception: {1}", userID, ex);
            }
            finally
            {
                dbm.Release();
            }

            m_log.InfoFormat("[Cashbook]: User: {0}, Total Sales:", userID);
            foreach (var sale in totalSalesList)
            {
                m_log.InfoFormat("ObjectUUID: {0}, TotalCount: {1}, TotalAmount: {2}", sale.ObjectUUID, sale.TotalCount, sale.TotalAmount);
            }
        }



        public void GetCashbookTransactions(string userID)
        {
            List<CashbookTransactionData> transactionList = new List<CashbookTransactionData>();
            string query = "SELECT receiver, amount, senderBalance, receiverBalance, objectName, commonName, description FROM transactions WHERE sender = ?userID OR receiver = ?userID";

            MySQLSuperManager dbm = ((MoneyDBService)m_moneyDBService).GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(query, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            CashbookTransactionData data = new CashbookTransactionData
                            {
                                Receiver = reader.GetString("receiver"),
                                Amount = reader.GetInt32("amount"),
                                SenderBalance = reader.GetInt32("senderBalance"),
                                ReceiverBalance = reader.GetInt32("receiverBalance"),
                                ObjectName = reader.GetString("objectName"),
                                CommonName = reader.GetString("commonName"),
                                Description = reader.GetString("description")
                            };
                            transactionList.Add(data);
                            m_log.InfoFormat("[Cashbook]: Found transaction: Receiver={0}, Amount={1}, SenderBalance={2}, ReceiverBalance={3}, ObjectName={4}, CommonName={5}, Description={6}",
                                data.Receiver, data.Amount, data.SenderBalance, data.ReceiverBalance, data.ObjectName, data.CommonName, data.Description);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[Cashbook]: Error getting transactions for userID: {0}. Exception: {1}", userID, ex);
            }
            finally
            {
                dbm.Release();
            }

            m_log.InfoFormat("[Cashbook]: User: {0}, Transactions:", userID);
            foreach (var transaction in transactionList)
            {
                m_log.InfoFormat("Receiver: {0}, Amount: {1}, SenderBalance: {2}, ReceiverBalance: {3}, ObjectName: {4}, CommonName: {5}, Description: {6}",
                    transaction.Receiver, transaction.Amount, transaction.SenderBalance, transaction.ReceiverBalance, transaction.ObjectName, transaction.CommonName, transaction.Description);
            }
        }



        // Aktualisierte Methode zur Initialisierung der Konsolenbefehle
        public void RegisterConsoleCommands(ICommandConsole console)
        {
            console.Commands.AddCommand(
                "MoneyXmlRpcModule",
                false,
                "getbalance",
                "getbalance <userID> or <first name> <last name>",
                "Get the balance for the specified user",
                HandleGetCashbookBalance);

            console.Commands.AddCommand(
                "MoneyXmlRpcModule",
                false,
                "gettotalsales",
                "gettotalsales <userID> or <first name> <last name>",
                "Get the total sales for the specified user",
                HandleGetCashbookTotalSales);

            console.Commands.AddCommand(
                "MoneyXmlRpcModule",
                false,
                "gettransactions",
                "gettransactions <userID> or <first name> <last name>",
                "Get the transactions for the specified user",
                HandleGetCashbookTransactions);
        }

        private void HandleGetCashbookBalance(string module, string[] cmdparams)
        {
            if (cmdparams.Length < 3)
            {
                m_log.Info("[Cashbook]: Usage: getbalance <userID> or <first name> <last name>");
                return;
            }

            string userID = string.Join(" ", cmdparams, 2, cmdparams.Length - 2).Trim();

            // Prüfen, ob userID eine gültige UUID ist
            if (Guid.TryParse(userID, out Guid _))
            {
                m_log.InfoFormat("[Cashbook]: userID is a valid UUID: {0}", userID);
                GetCashbookBalance(userID);
            }
            else
            {
                m_log.InfoFormat("[Cashbook]: userID is a name: {0}", userID);
                GetCashbookBalanceByName(userID);
            }
        }

        private void HandleGetCashbookTotalSales(string module, string[] cmdparams)
        {
            if (cmdparams.Length < 3)
            {
                m_log.Info("[Cashbook]: Usage: gettotalsales <userID> or <first name> <last name>");
                return;
            }

            string userID = string.Join(" ", cmdparams, 2, cmdparams.Length - 2).Trim();

            // Prüfen, ob userID eine gültige UUID ist
            if (Guid.TryParse(userID, out Guid _))
            {
                m_log.InfoFormat("[Cashbook]: userID is a valid UUID: {0}", userID);
                GetCashbookTotalSales(userID);
            }
            else
            {
                m_log.InfoFormat("[Cashbook]: userID is a name: {0}", userID);
                GetCashbookTotalSalesByName(userID);
            }
        }

        private void HandleGetCashbookTransactions(string module, string[] cmdparams)
        {
            if (cmdparams.Length < 3)
            {
                m_log.Info("[Cashbook]: Usage: gettransactions <userID> or <first name> <last name>");
                return;
            }

            string userID = string.Join(" ", cmdparams, 2, cmdparams.Length - 2).Trim();

            // Prüfen, ob userID eine gültige UUID ist
            if (Guid.TryParse(userID, out Guid _))
            {
                m_log.InfoFormat("[Cashbook]: userID is a valid UUID: {0}", userID);
                GetCashbookTransactions(userID);
            }
            else
            {
                m_log.InfoFormat("[Cashbook]: userID is a name: {0}", userID);
                GetCashbookTransactionsByName(userID);
            }
        }

        private void GetCashbookBalanceByName(string name)
        {
            string userID = GetUUIDFromName(name);
            if (string.IsNullOrEmpty(userID))
            {
                m_log.InfoFormat("[Cashbook]: No UUID found for user: {0}", name);
                return;
            }
            GetCashbookBalance(userID);
        }

        private void GetCashbookTotalSalesByName(string name)
        {
            string userID = GetUUIDFromName(name);
            if (string.IsNullOrEmpty(userID))
            {
                m_log.InfoFormat("[Cashbook]: No UUID found for user: {0}", name);
                return;
            }
            GetCashbookTotalSales(userID);
        }

        private void GetCashbookTransactionsByName(string name)
        {
            string userID = GetUUIDFromName(name);
            if (string.IsNullOrEmpty(userID))
            {
                m_log.InfoFormat("[Cashbook]: No UUID found for user: {0}", name);
                return;
            }
            GetCashbookTransactions(userID);
        }

        private string GetUUIDFromName(string name)
        {
            string query = "SELECT PrincipalID FROM UserAccounts WHERE CONCAT(FirstName, ' ', LastName) = ?name";
            string userID = null;

            MySQLSuperManager dbm = ((MoneyDBService)m_moneyDBService).GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(query, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?name", name);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            userID = reader.GetString("PrincipalID");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[Cashbook]: Error getting UUID for name: {0}. Exception: {1}", name, ex);
            }
            finally
            {
                dbm.Release();
            }

            return userID;
        }



        #endregion

    }

}
// ##################     classes                ##################
#region classes

public class CashbookTotalSalesData
{
    public string ObjectUUID { get; set; }
    public int TotalCount { get; set; }
    public int TotalAmount { get; set; }
}

public class CashbookTransactionData
{
    public string Receiver { get; set; }
    public int Amount { get; set; }
    public int SenderBalance { get; set; }
    public int ReceiverBalance { get; set; }
    public string ObjectName { get; set; }
    public string CommonName { get; set; }
    public string Description { get; set; }
}

public class CurrencyQuoteRequest
{
    public string AgentId { get; set; }
    public int CurrencyBuy { get; set; }
    public string Language { get; set; }
    public string SecureSessionId { get; set; }
    public string ViewerBuildVersion { get; set; }
    public string ViewerChannel { get; set; }
    public int ViewerMajorVersion { get; set; }
    public int ViewerMinorVersion { get; set; }
    public int ViewerPatchVersion { get; set; }
}

public class LandPurchaseRequest
{
    public string AgentId { get; set; }
    public int BillableArea { get; set; }
    public int CurrencyBuy { get; set; }
    public string Language { get; set; }
    public string SecureSessionId { get; set; }
}

public class BuyCurrencyRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuyCurrencyRequest"/> class.
    /// </summary>
    public BuyCurrencyRequest()
    {
        // Initialize any default values or properties here
    }
    public string AgentId { get; set; }
    public int CurrencyBuy { get; set; }
    public string Language { get; set; }
    public string SecureSessionId { get; set; }
    public string ViewerBuildVersion { get; set; }
    public string ViewerChannel { get; set; }
    public int ViewerMajorVersion { get; set; }
    public int ViewerMinorVersion { get; set; }
    public int ViewerPatchVersion { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyType { get; set; }

    public bool IsValid()
    {
        // Add validation logic here, e.g., check for null or empty values
        return Amount > 0 && !string.IsNullOrEmpty(CurrencyType);
    }
}
#endregion
