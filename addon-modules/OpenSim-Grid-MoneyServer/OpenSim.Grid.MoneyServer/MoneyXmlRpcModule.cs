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
 */

using log4net;

using Nini.Config;

using NSL.Certificate.Tools;
using NSL.Network.XmlRpc;

using Nwc.XmlRpc;

using OpenMetaverse;

using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Modules.Currency;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;


namespace OpenSim.Grid.MoneyServer
{
    class MoneyXmlRpcModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private int m_defaultBalance = 1000;

        private bool m_forceTransfer = false;
        private string m_bankerAvatar = "";

        private bool m_scriptSendMoney = false;
        private string m_scriptAccessKey = "";
        private string m_scriptIPaddress = "127.0.0.1";        

        private bool m_hg_enable = false;
        private bool m_gst_enable = false;
        private int m_hg_defaultBalance = 0;
        private int m_gst_defaultBalance = 0;
        private int m_CalculateCurrency = 0;

        // XMLRPC Debug settings
        private bool m_DebugConsole = false;
        private bool m_DebugFile = false;

        private bool m_checkServerCert = false;
        private string m_cacertFilename = "";

        private string m_certFilename = "";
        private string m_certPassword = "";

        private string m_sslCommonName = "";

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
        public MoneyXmlRpcModule()
        {
        }

        /// <summary>Initialises the specified opensim version.</summary>
        /// <param name="opensimVersion">The opensim version.</param>
        /// <param name="moneyDBService">The money database service.</param>
        /// <param name="moneyCore">The money core.</param>
        public void Initialise(string opensimVersion, IMoneyDBService moneyDBService, IMoneyServiceCore moneyCore)
        {
            //m_opensimVersion = opensimVersion;
            m_moneyDBService = moneyDBService;
            m_moneyCore = moneyCore;
            m_server_config = m_moneyCore.GetServerConfig();    // [MoneyServer] Section
            m_cert_config = m_moneyCore.GetCertConfig();      // [Certificate] Section

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
                m_log.Info("[MONEY XMLRPC]: Initialise: No check XMLRPC Server or CACertFilename is empty. CheckServerCert is false.");
            }

            m_sessionDic = m_moneyCore.GetSessionDic();
            m_secureSessionDic = m_moneyCore.GetSecureSessionDic();
            m_webSessionDic = m_moneyCore.GetWebSessionDic();
            RegisterHandlers();

            RegisterStreamHandlers();
        }

        /// <summary>Registers stream handlers for PHP scripts.</summary>
        private void RegisterStreamHandlers()
        {
            m_log.Info("[MONEY XMLRPC]: Registering currency.php  handlers.");
            m_httpServer.AddSimpleStreamHandler(new SimpleStreamHandler("/currency.php", CurrencyProcessPHP));

            m_log.Info("[MONEY XMLRPC]: Registering landtool.php  handlers.");
            m_httpServer.AddSimpleStreamHandler(new SimpleStreamHandler("/landtool.php", LandtoolProcessPHP));

            m_log.InfoFormat("[MONEY MODULE]: Registered /currency.php and /landtool.php handlers.");
        }
                
        /// <summary>Posts the initialise.</summary>
        public void PostInitialise()
        {
        }

        private Dictionary<string, XmlRpcMethod> m_rpcHandlers = new Dictionary<string, XmlRpcMethod>();

        /*
        RegisterHandlers

        Die Funktion RegisterHandlers verkn�pft verschiedene Methoden mit bestimmten XML-RPC-Endpunkten. 
        Diese Methoden sind Handler, die ausgef�hrt werden, wenn ein bestimmter XML-RPC-Request eingeht. 
        Die Registrierung erfolgt mit AddXmlRPCHandler.

            Beispiel:

        m_httpServer.AddXmlRPCHandler("ClientLogin", handleClientLogin);

        Hier wird die Methode handleClientLogin mit dem Endpunkt ClientLogin registriert. 
        Wenn ein XML-RPC-Request an diesen Endpunkt gesendet wird, verarbeitet handleClientLogin die Anfrage.

        Anwendungsf�lle:

            ClientLogin: Verarbeitung von Benutzeranmeldungen.
            GetBalance: Abfrage des Kontostands.
            TransferMoney: Durchf�hrung von Geldtransaktionen.
            buyLandPrep: Vorbereitung eines Landkaufs.
            getCurrencyQuote: Abruf von W�hrungsumrechnungskursen.

        M�gliche fehlende Funktionen:
        1. Benutzerverwaltung

            CreateUser: Zum Erstellen neuer Benutzerkonten.
            DeleteUser: Zum L�schen eines bestehenden Benutzerkontos.
            UpdateUserDetails: Um Benutzerdetails zu aktualisieren, wie z. B. Namen, E-Mail-Adresse oder Kontoinformationen.

        2. Erweiterte Kontostandsverwaltung

            FreezeAccount: Zum Einfrieren eines Kontos bei verd�chtigen Aktivit�ten.
            UnfreezeAccount: Um ein eingefrorenes Konto wieder zu aktivieren.
            AdjustBalance: Manuelle Anpassung des Kontostands (z. B. f�r Admins).

        3. Detaillierte Transaktionsverwaltung

            GetTransactionHistory: Abfrage der vollst�ndigen Transaktionshistorie eines Benutzers.
            RefundTransaction: R�ckerstattung einer Transaktion.
            ValidateTransaction: Validierung einer Transaktion vor ihrer Ausf�hrung.

        4. Benachrichtigungen

            SendUserNotification: Senden von spezifischen Benachrichtigungen an Benutzer.
            GetPendingNotifications: Abrufen ausstehender Benachrichtigungen.

        5. W�hrungsverwaltung

            SetExchangeRate: Festlegen eines Wechselkurses f�r eine virtuelle W�hrung.
            GetExchangeRate: Abruf des aktuellen Wechselkurses.
            ConvertCurrency: Konvertierung einer W�hrungseinheit in eine andere.

        6. Sicherheitsfunktionen

            AuthenticateSession: Authentifizierung von Benutzer-Sessions, um unautorisierte Zugriffe zu verhindern.
            InvalidateSession: Ung�ltigmachen von Sitzungen (z. B. nach Logout oder Timeout).
            VerifyTransactionSignature: �berpr�fung der Signatur einer Transaktion zur Sicherheitsgew�hrleistung.

        7. System- und Debugging-Funktionen

            Ping: Einfacher Test, um sicherzustellen, dass der Server erreichbar ist.
            HealthCheck: �berpr�fung des Serverstatus und der Systemressourcen.
            LogTransactionDetails: Aufzeichnen detaillierter Transaktionsprotokolle f�r Debugging-Zwecke.

        8. Land- und Immobilienmanagement

            SellLand: Verf�gbarmachen von Land f�r den Verkauf.
            CancelLandSale: Abbrechen eines laufenden Landverkaufs.
            GetLandDetails: Abrufen von Details zu einem Grundst�ck.

        9. Erweiterte Zahlungsabwicklung

            SchedulePayment: Planen von zuk�nftigen Zahlungen.
            CancelScheduledPayment: Stornieren einer geplanten Zahlung.
            SplitPayment: Aufteilen einer Zahlung auf mehrere Empf�nger.

        10. Reporting

            GenerateAccountStatement: Erstellen eines Kontoauszugs f�r einen bestimmten Zeitraum.
            GetSystemStatistics: Abrufen von Systemstatistiken, wie z. B. die Anzahl aktiver Benutzer oder die Summe durchgef�hrter Transaktionen.

        Um die RegisterHandlers-Methode zu vervollst�ndigen, sollten zus�tzliche Funktionen hinzugef�gt werden, 
        die Benutzerverwaltung, erweiterte Zahlungsabwicklungen, Sicherheitsma�nahmen und Reporting umfassen. 
        Dies stellt sicher, dass das System robust, sicher und flexibel ist.
        */
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
            m_httpServer.AddXmlRPCHandler("getCurrencyQuote", getCurrencyQuote);
            m_httpServer.AddXmlRPCHandler("buyCurrency", buyCurrency);

            // Money Transfer Test
            m_httpServer.AddXmlRPCHandler("OnMoneyTransfered", OnMoneyTransferedHandler);
            m_httpServer.AddXmlRPCHandler("UpdateBalance", BalanceUpdateHandler);
            m_httpServer.AddXmlRPCHandler("UserAlert", UserAlertHandler);

            // Angebot oder eine Information zu einem Kaufpreis
            m_httpServer.AddXmlRPCHandler("quote", quote);
        }

        // ##################     Land Buy     ##################

        // Flexibilit�t: Die gesamte Logik wird innerhalb von LandtoolProcessPHP und ihren Hilfsfunktionen abgewickelt.
        // Fehlerbehandlung: Umfassende �berpr�fung auf fehlende Daten oder Fehler w�hrend der Verarbeitung.
        // Unabh�ngigkeit: Keine Abh�ngigkeit von externen Funktionen oder Modulen.

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

                // Variablen f�r Landanfrage initialisieren
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

                    // Preflight-Pr�fung
                    Hashtable preflightResponse = PerformPreflightLandCheck(agentId, billableArea, currencyBuy, language, secureSessionId);
                    if (!(bool)preflightResponse["success"])
                    {
                        m_log.Error("[MONEY XMLRPC MODULE]: Preflight check failed.");
                        httpResponse.StatusCode = 400;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Preflight check failed</response>");
                        return;
                    }

                    // Erfolgreiche Antwort zur�ckgeben
                    httpResponse.StatusCode = 200;
                    XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = preflightResponse };
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                }
                else if (methodName == "buyLandPrep")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Land Purchase Request for AgentId: {0}, BillableArea: {1}",
                        agentId, billableArea);

                    // Landkauf durchf�hren
                    Hashtable purchaseResponse = ProcessLandPurchase(agentId, billableArea, currencyBuy, language, secureSessionId);

                    // �berpr�fung der Antwort
                    if (!(bool)purchaseResponse["success"])
                    {
                        m_log.Error("[MONEY XMLRPC MODULE]: Land purchase failed.");
                        httpResponse.StatusCode = 400;
                        httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Land purchase failed</response>");
                        return;
                    }

                    // Erfolgreiche Antwort zur�ckgeben
                    httpResponse.StatusCode = 200;
                    XmlRpcResponse xmlResponse = new XmlRpcResponse { Value = purchaseResponse };
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(xmlResponse.ToString());
                }
                else
                {
                    m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Unknown method name: {0}", methodName);
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

        // Beispiel einer Funktion zur Preflight-Pr�fung
        // PerformPreflightLandCheck:
        // Simuliert die Preflight-Pr�fung f�r den Landkauf.
        // Gibt eine Erfolgsmeldung in Form einer Hashtable zur�ck.
        private Hashtable PerformPreflightLandCheck(string agentId, int billableArea, int currencyBuy, string language, string secureSessionId)
        {
            // Beispielhafte Logik f�r Preflight-Pr�fung
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
        // Simuliert die Durchf�hrung des Landkaufs.
        // Gibt ebenfalls eine Erfolgsmeldung als Hashtable zur�ck.
        private Hashtable ProcessLandPurchase(string agentId, int billableArea, int currencyBuy, string language, string secureSessionId)
        {
            // Beispielhafte Logik f�r Landkauf
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


        // ##################     Currency Buy     ##################
        /*
        private void CurrencyProcessPHP(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: CURRENCY PROCESS PHP starting...");

            try
            {
                // XML-String aus der Anfrage lesen
                string requestBody;
                using (StreamReader reader = new StreamReader(httpRequest.InputStream, Encoding.UTF8))
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

                // Variablen f�r Anfragedaten
                string agentId = null, secureSessionId = null, language = null, viewerBuildVersion = null, viewerChannel = null;
                int currencyBuy = 0, viewerMajorVersion = 0, viewerMinorVersion = 0, viewerPatchVersion = 0;

                // Werte aus der XML-Struktur extrahieren
                foreach (XmlNode member in members)
                {
                    string name = member.SelectSingleNode("name")?.InnerText;
                    string value = member.SelectSingleNode("value")?.InnerText;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;

                    switch (name)
                    {
                        case "agentId": agentId = value; break;
                        case "currencyBuy": currencyBuy = int.Parse(value); break;
                        case "language": language = value; break;
                        case "secureSessionId": secureSessionId = value; break;
                        case "viewerBuildVersion": viewerBuildVersion = value; break;
                        case "viewerChannel": viewerChannel = value; break;
                        case "viewerMajorVersion": viewerMajorVersion = int.Parse(value); break;
                        case "viewerMinorVersion": viewerMinorVersion = int.Parse(value); break;
                        case "viewerPatchVersion": viewerPatchVersion = int.Parse(value); break;
                    }
                }

                // Methode verarbeiten
                if (methodName == "getCurrencyQuote")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Currency Quote Request for AgentId: {0}", agentId);

                    // Implementierung der getCurrencyQuote-Logik direkt in dieser Methode
                    Hashtable responseValue = new Hashtable
                    {
                        { "success", true },
                        { "agentId", agentId },
                        { "currencyBuy", currencyBuy },
                        { "language", language },
                        { "quote", GenerateCurrencyQuote(currencyBuy) }, // Generiere ein W�hrungsangebot
                        { "viewerBuildVersion", viewerBuildVersion },
                        { "viewerChannel", viewerChannel },
                        { "viewerMajorVersion", viewerMajorVersion },
                        { "viewerMinorVersion", viewerMinorVersion },
                        { "viewerPatchVersion", viewerPatchVersion }
                    };
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: agentId ", agentId);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: currencyBuy", currencyBuy);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: language", language);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: quote", GenerateCurrencyQuote(currencyBuy));
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: viewerBuildVersion", viewerBuildVersion);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: viewerChannel", viewerChannel);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: viewerMajorVersion", viewerMajorVersion);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: viewerMinorVersion", viewerMinorVersion);
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: viewerPatchVersion", viewerPatchVersion);



                    XmlRpcResponse quoteResponse = new XmlRpcResponse
                    {
                        Value = responseValue
                    };

                    // Erfolgreiche Antwort zur�ckgeben
                    httpResponse.StatusCode = 200;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(quoteResponse.ToString());
                }
                else
                {
                    m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Unknown method name: {0}", methodName);
                    httpResponse.StatusCode = 400;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Invalid method name</response>");
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Error processing CURRENCY request. Error: {0}", ex.ToString());
                httpResponse.StatusCode = 500;
                httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Error</response>");
            }
        }

        // Beispiel einer Funktion, um einen W�hrungswert zu generieren
        private int GenerateCurrencyQuote(int currencyBuy)
        {
            // Logik, um den W�hrungswert zu berechnen
            // Dies k�nnte durch eine externe API oder interne Berechnungen erfolgen
            const double exchangeRate = 1.5; // Beispielkurs
            return (int)(currencyBuy * exchangeRate);
        }
        */

        // NEU 16. 11.2024 14:39

        private void CurrencyProcessPHP(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: CURRENCY PROCESS PHP starting...");

            try
            {
                // XML-String aus Anfrage lesen
                string requestBody;
                using (StreamReader reader = new StreamReader(httpRequest.InputStream, Encoding.UTF8))
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

                // Variablen f�r Anfragedaten
                string agentId = null, secureSessionId = null;
                int currencyBuy = 0;

                // Werte aus der XML-Struktur extrahieren
                foreach (XmlNode member in members)
                {
                    string name = member.SelectSingleNode("name")?.InnerText;
                    string value = member.SelectSingleNode("value")?.InnerText;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;

                    switch (name)
                    {
                        case "agentId": agentId = value; break;
                        case "currencyBuy": currencyBuy = int.Parse(value); break;
                        case "secureSessionId": secureSessionId = value; break;
                    }
                }

                // W�hrungsangebot oder Kauf basierend auf der Methode verarbeiten
                if (methodName == "getCurrencyQuote")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Currency Quote for AgentId: {0}", agentId);

                    Hashtable responseValue = GetCurrencyQuote(agentId, secureSessionId, currencyBuy);

                    XmlRpcResponse quoteResponse = new XmlRpcResponse { Value = responseValue };

                    // Erfolgreiche Antwort zur�ckgeben
                    httpResponse.StatusCode = 200;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(quoteResponse.ToString());
                }
                else if (methodName == "buyCurrency")
                {
                    m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processing Buy Currency for AgentId: {0}", agentId);

                    Hashtable responseValue = BuyCurrency(agentId, secureSessionId, currencyBuy);

                    XmlRpcResponse buyResponse = new XmlRpcResponse { Value = responseValue };

                    // Erfolgreiche Antwort zur�ckgeben
                    httpResponse.StatusCode = 200;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes(buyResponse.ToString());
                }
                else
                {
                    m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Unknown method name: {0}", methodName);
                    httpResponse.StatusCode = 400;
                    httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Invalid method name</response>");
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC MODULE]: Error processing CURRENCY request. Error: {0}", ex.ToString());
                httpResponse.StatusCode = 500;
                httpResponse.RawBuffer = Encoding.UTF8.GetBytes("<response>Error</response>");
            }
        }














        private Hashtable GetCurrencyQuote(string agentId, string secureSessionId, int currencyBuy)
        {
            // Datenbankabfrage, um die Sitzung zu validieren (Dummy-Datenbank-Check)
            bool sessionValid = ValidateSession(agentId, secureSessionId);

            if (!sessionValid)
            {
                return new Hashtable
                {
                    { "success", false },
                    { "errorMessage", "Unable to authenticate user. Click URL for more info." },
                    { "errorURI", "https://example.com" } // Beispiel-URL
                };
            }

            // Angebot generieren
            int estimatedCost = ConvertToReal(currencyBuy);

            Hashtable currencyData = new Hashtable
            {
                { "estimatedCost", estimatedCost },
                { "currencyBuy", currencyBuy }
            };

            return new Hashtable
            {
                { "success", true },
                { "currency", currencyData },
                { "confirm", "1234567883789" } // Beispiel-Best�tigungscode
            };
        }




        private object BuyCurrency(object request, IPEndPoint client = null)
        {
            // BankerAvatar Pr�fung
            if (string.IsNullOrEmpty(m_bankerAvatar) || m_bankerAvatar == "00000000-0000-0000-0000-00000000000")
            {
                // Fehlerantwort zur�ckgeben, wenn der BankerAvatar ung�ltig ist
                Hashtable responseData = new Hashtable
        {
            { "success", false },
            { "errorMessage", "Currency transactions are currently disabled or all avatars can buy." },
            { "errorURI", "https://example.com" }
        };
                return responseData; // F�r Hashtable-basierte R�ckgabe
            }

            try
            {
                // Unterscheidung zwischen XML-RPC und Hashtable
                if (request is XmlRpcRequest xmlRequest)
                {
                    // XML-RPC Request bearbeiten
                    if (xmlRequest.Params.Count == 0)
                    {
                        m_log.Error("[MONEY XMLRPC]: buyCurrency: No parameters in request.");
                        return new XmlRpcResponse();
                    }

                    Hashtable requestData = (Hashtable)xmlRequest.Params[0];
                    string currencyAmount = (string)requestData["currencyAmount"];
                    int amount = int.Parse(currencyAmount);

                    int cost = CalculateCost(amount);
                    m_log.InfoFormat("[MONEY XMLRPC]: buyCurrency: Cost for {0} currency units: {1}", amount, cost);

                    // Transaktion durchf�hren (DB-Operation)
                    bool transactionSuccess = m_moneyDBService.BuyCurrency(amount, cost, m_bankerAvatar);

                    XmlRpcResponse xmlResponse = new XmlRpcResponse();
                    Hashtable responseData = new Hashtable();
                    responseData.Add("success", transactionSuccess);
                    if (!transactionSuccess)
                    {
                        responseData.Add("errorMessage", "Transaction failed. Please try again later.");
                    }
                    xmlResponse.Value = responseData;
                    return xmlResponse; // R�ckgabe f�r XML-RPC

                }
                else if (request is Hashtable requestData)
                {
                    // Hashtable Request bearbeiten
                    string agentId = (string)requestData["agentId"];
                    string secureSessionId = (string)requestData["secureSessionId"];
                    int currencyBuy = (int)requestData["currencyBuy"];

                    // Sitzung validieren
                    bool sessionValid = ValidateSession(agentId, secureSessionId);
                    if (!sessionValid)
                    {
                        return new Hashtable
                        {
                            { "success", false },
                            { "errorMessage", "Unable to authenticate user. Click URL for more info." },
                            { "errorURI", "https://example.com" }
                        };
                    }

                    // Kosten berechnen
                    int cost = ConvertToReal(currencyBuy);

                    // Minimaler Kaufbetrag
                    const int minimumReal = 10;
                    if (cost < minimumReal)
                    {
                        return new Hashtable
                        {
                            { "success", false },
                            { "errorMessage", $"Minimum purchase amount is {minimumReal}." },
                            { "errorURI", "https://example.com" }
                        };
                    }

                    // Transaktion verarbeiten
                    bool transactionSuccess = ProcessTransaction(agentId, cost, m_bankerAvatar);

                    if (!transactionSuccess)
                    {
                        return new Hashtable
                        {
                            { "success", false },
                            { "errorMessage", "Transaction failed. Please try again later." }
                        };
                    }

                    // Erfolgreich W�hrung �bertragen
                    MoveMoney(m_bankerAvatar, agentId, currencyBuy, "Currency purchase");

                    return new Hashtable
                    {
                        { "success", true }
                    };
                }
                else
                {
                    m_log.Error("[MONEY]: Invalid request type.");
                    return new Hashtable
                    {
                        { "success", false },
                        { "errorMessage", "Invalid request format." }
                    };
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY]: Exception occurred in BuyCurrency: {0}", ex.Message);
                return new Hashtable
                {
                    { "success", false },
                    { "errorMessage", "An error occurred during the transaction." }
                };
            }
        }

        private void MoveMoney(string m_bankerAvatar, string agentId, int currencyBuy, string v)
        {
            throw new NotImplementedException();
        }



        // BuyCurrency 1 BuyCurrency(string agentId, string secureSessionId, int currencyBuy)

        // , m_bankerAvatar


        private Hashtable BuyCurrency(string agentId, string secureSessionId, int currencyBuy)
        { //002
            // Datenbankabfrage, um die Sitzung zu validieren (Dummy-Datenbank-Check)
            bool sessionValid = ValidateSession(agentId, secureSessionId);

            if (!sessionValid)
            {
                return new Hashtable
                {
                    { "success", false },
                    { "errorMessage", "Unable to authenticate user. Click URL for more info." },
                    { "errorURI", "https://example.com" } // Beispiel-URL
                };
            }

            // Berechnung der Kosten
            int cost = ConvertToReal(currencyBuy);

            // Minimaler Kaufbetrag (Beispiel)
            const int minimumReal = 10;
            if (cost < minimumReal)
            {
                return new Hashtable
                {
                    { "success", false },
                    { "errorMessage", $"Minimum purchase amount is {minimumReal}." },
                    { "errorURI", "https://example.com" }
                };
            }


















            // Transaktion verarbeiten (Dummy-Prozess)
            bool transactionSuccessful = ProcessTransaction(agentId, cost);

            if (!transactionSuccessful)
            {
                return new Hashtable
                {
                    { "success", false },
                    { "errorMessage", "Transaction failed. Gateway denied your charge." },
                    { "errorURI", "https://example.com" }
                };
            }

            // Erfolgreiche Antwort
            return new Hashtable
            {
                { "success", true }
            };
        }

        private bool ProcessTransaction(string agentId, int cost)
        {
            throw new NotImplementedException();
        }

        /*
        Dummy-Funktionen
        ValidateSession: Simuliert die Sitzungvalidierung.
        ConvertToReal: Konvertiert die virtuelle W�hrung in reale Kosten.
        ProcessTransaction: Simuliert eine Zahlungsabwicklung.
        */

        private bool ValidateSession(string agentId, string secureSessionId)
        {
            // Beispielhafte Validierung (immer erfolgreich f�r Testzwecke)
            return !string.IsNullOrEmpty(agentId) && !string.IsNullOrEmpty(secureSessionId);
        }

        private int ConvertToReal(int currencyBuy)
        {
            const double exchangeRate = 1.5; // Beispielkurs
            return (int)(currencyBuy * exchangeRate);
        }

        private bool ProcessTransaction(string agentId, int cost, string m_bankerAvatar)
        {
            // Simuliert eine erfolgreiche Transaktion
            m_log.InfoFormat("[MONEY XMLRPC MODULE]: Processed transaction for AgentId: {0}, Cost: {1}", agentId, cost);
            return true;
        }




        // ##################     XMLRPC Pasing     ##################


        /*
        OSParseXmlRpcRequest

        Dient zum Parsen und Verarbeiten des XML-RPC-Anfrageinhalts. 
        Basierend auf der Methode im XML-Dokument wird ein entsprechendes Objekt (z. B. CurrencyQuoteRequest oder LandPurchaseRequest) erstellt.

            Wichtige Schritte:
                Analysiert die methodCall-Struktur der XML-Daten.
                Verarbeitet verschiedene Methoden wie getCurrencyQuote oder preflightBuyLandPrep.
                Gibt ein stark typisiertes Objekt zur�ck, das die spezifischen Anfragedaten enth�lt.
        */
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

        /*
        LogXmlRpcRequestFile und LogXmlRpcRequestConsole
        Schreiben Debug-Informationen zu XML-RPC-Anfragen entweder in eine Datei oder in die Konsole. Diese Funktionen helfen, Probleme zu diagnostizieren.
            Beispiel:
        string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xmlrpc_debug.log");
        File.AppendAllText(logFilePath, logEntry);
        */
        private void LogXmlRpcRequestFile(IOSHttpRequest request)
        {
            try
            {
                // Erstelle einen Dateipfad f�r das Log
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

        /*
        LogXmlRpcRequestFile und LogXmlRpcRequestConsole
        Schreiben Debug-Informationen zu XML-RPC-Anfragen entweder in eine Datei oder in die Konsole. Diese Funktionen helfen, Probleme zu diagnostizieren.
            Beispiel:
        string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xmlrpc_debug.log");
        File.AppendAllText(logFilePath, logEntry);
        */
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

        /*
        CalculateCost
        Berechnet die Kosten basierend auf einer W�hrungsmenge.
            Beispiel:

        return currencyAmount * m_CalculateCurrency;
        */
        /// <summary>
        /// Calculates the cost based on the given currency amount.
        /// </summary>
        /// <param name="currencyAmount">The amount of currency.</param>
        /// <returns>The calculated cost.</returns>
        private int CalculateCost(int currencyAmount)
        {
            m_log.InfoFormat("[MONEY XML RPC MODULE]: Cost for {0} currency units: {1}", currencyAmount, currencyAmount * m_CalculateCurrency);
            // The cost of each currency unit is calculated by multiplying the currency amount by the calculate currency value.
            // The calculate currency value is a private field of the class.
            // The commented line is an example of how the price per unit can be set.
            return currencyAmount * m_CalculateCurrency;
            
            //return 0;
        }

        /*
        Spezifische Handler (z. B. OnMoneyTransferedHandler, BalanceUpdateHandler, UserAlertHandler)
        Diese Funktionen verarbeiten bestimmte Anfragen:

            OnMoneyTransferedHandler: Protokolliert Details zu einer Geld�berweisung.
            BalanceUpdateHandler: Verarbeitet Updates zum Kontostand.
            UserAlertHandler: Handhabt Benutzerbenachrichtigungen.
        */
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

        /*
        Spezifische Handler (z. B. OnMoneyTransferedHandler, BalanceUpdateHandler, UserAlertHandler)
        Diese Funktionen verarbeiten bestimmte Anfragen:

            OnMoneyTransferedHandler: Protokolliert Details zu einer Geld�berweisung.
            BalanceUpdateHandler: Verarbeitet Updates zum Kontostand.
            UserAlertHandler: Handhabt Benutzerbenachrichtigungen.
        */
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

        /*
        Spezifische Handler (z. B. OnMoneyTransferedHandler, BalanceUpdateHandler, UserAlertHandler)
        Diese Funktionen verarbeiten bestimmte Anfragen:

            OnMoneyTransferedHandler: Protokolliert Details zu einer Geld�berweisung.
            BalanceUpdateHandler: Verarbeitet Updates zum Kontostand.
            UserAlertHandler: Handhabt Benutzerbenachrichtigungen.
        */
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

        /**/
        private XmlRpcResponse getCurrencyQuote(XmlRpcRequest request, IPEndPoint client)
        {
            // Log the request for auditing purposes
            m_log.InfoFormat("[MONEY XMLRPC]: handleClient getCurrencyQuote.");

            // Create a response object to store the quote details
            Hashtable quoteResponse = new Hashtable();

            // Set the success flag to true
            quoteResponse.Add("success", true);

            // Add a placeholder for currency details (to be implemented)
            quoteResponse.Add("currency", new Hashtable()); // TODO: Add currency details here

            // Add a confirmation code (to be implemented)
            quoteResponse.Add("confirm", "asdfad9fj39ma9fj"); // TODO: Generate a unique confirmation code

            // Create an XML-RPC response object
            XmlRpcResponse returnval = new XmlRpcResponse();

            // Set the response value to the quote response object
            returnval.Value = quoteResponse;

            // Return the response to the client
            return returnval;
        }

        /*
        buyCurrency(XmlRpcRequest request, IPEndPoint client)
        Zweck: Verarbeitet eine Anfrage zum Kauf von virtueller W�hrung.
        Details:
            Liest die angeforderte W�hrungsmenge aus der Anfrage.
            Berechnet die Kosten (vermutlich mit einer Funktion CalculateCost).
            F�hrt eine Kaufoperation in einer Datenbank durch (m_moneyDBService.BuyCurrency).
            Gibt eine Erfolgsantwort zur�ck.
        Anwendung: Diese Funktion wird aufgerufen, wenn ein Benutzer virtuelle W�hrung erwerben m�chte.
        */

        // BuyCurrency 1 BuyCurrency(string agentId, string secureSessionId, int currencyBuy)
        // BuyCurrency 2 buyCurrency(XmlRpcRequest request, IPEndPoint client)

        private XmlRpcResponse buyCurrency(XmlRpcRequest request, IPEndPoint client)
        { //002
            if (request == null)
            {
                m_log.Error("[MONEY XMLRPC]: buyCurrency: request is null.");
                return new XmlRpcResponse();
            }

            if (client == null)
            {
                m_log.Error("[MONEY XMLRPC]: buyCurrency: client is null.");
                return new XmlRpcResponse();
            }

            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                string currencyAmount = (string)requestData["currencyAmount"];
                int amount = int.Parse(currencyAmount);

                int cost = CalculateCost(amount);
                m_log.InfoFormat("[MONEY XMLRPC]: buyCurrency: Cost for {0} currency units: {1}", amount, cost);

                // Assuming m_moneyDBService is an instance of a class that handles database operations
                m_moneyDBService.BuyCurrency(amount, cost);

                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable responseData = new Hashtable();
                responseData.Add("success", true);
                response.Value = responseData;
                return response;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: buyCurrency: Exception occurred: {0}", ex.Message);
                return new XmlRpcResponse();
            }
        }

        /*
        buy_func(XmlRpcRequest request, IPEndPoint client)
        Zweck: Handhabt den Kaufvorgang.
        Details:
            Eine sehr einfache Funktion, die lediglich eine Erfolgsmeldung zur�ckgibt.
            Wird vermutlich f�r grundlegende Tests oder als Platzhalter verwendet.
        Anwendung: Einsatz f�r grundlegende Kaufoperationen oder Tests.
        */
        /// <summary>Handles the buy function.</summary>
        /// <param name="request">The XML-RPC request.</param>
        /// <param name="client">The client endpoint.</param>
        /// <returns>An XML-RPC response indicating success.</returns>
        private XmlRpcResponse buy_func(XmlRpcRequest request, IPEndPoint client)
        {
            // Log the XML-RPC request
            m_log.InfoFormat("[MONEY XMLRPC]: handleClient buyCurrency.");

            // Create the XML-RPC response
            XmlRpcResponse returnval = new XmlRpcResponse();
            Hashtable returnresp = new Hashtable();
            returnresp.Add("success", true);
            returnval.Value = returnresp;

            // Return the XML-RPC response
            return returnval;
        }

        /*
        quote(XmlRpcRequest request, IPEndPoint client)
        Zweck: Bietet ein Angebot oder eine Information zu einem Kaufpreis.
        Details:
            Liefert eine Erfolgsantwort mit Platzhaltern f�r W�hrungsdetails und eine Best�tigung.
            TODO-Kommentare deuten darauf hin, dass die Details und der Best�tigungscode noch implementiert werden m�ssen.
        Anwendung: Bereitstellung von Informationen zu W�hrungskursen oder Transaktionen.
        */
        /// <summary> Handles the get currency quote request.</summary>
        /// <param name="request">The incoming XML-RPC request.</param>
        /// <param name="client">The client that made the request.</param>
        /// <returns>An XML-RPC response with the currency quote.</returns>
        private XmlRpcResponse quote(XmlRpcRequest request, IPEndPoint client)
        {
            // Log the request for auditing purposes
            m_log.InfoFormat("[MONEY XMLRPC]: handleClient getCurrencyQuote.");

            // Create a response object to store the quote details
            Hashtable quoteResponse = new Hashtable();

            // Set the success flag to true
            quoteResponse.Add("success", true);

            // Add a placeholder for currency details (to be implemented)
            quoteResponse.Add("currency", new Hashtable()); // TODO: Add currency details here

            // Add a confirmation code (to be implemented)
            quoteResponse.Add("confirm", "asdfad9fj39ma9fj"); // TODO: Generate a unique confirmation code

            // Create an XML-RPC response object
            XmlRpcResponse returnval = new XmlRpcResponse();

            // Set the response value to the quote response object
            returnval.Value = quoteResponse;

            // Return the response to the client
            return returnval;
        }






        // Neu II 15.11.2024






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







        // Neu II 15.11.2024 Ende

        /*
        buyLandPrep(XmlRpcRequest request, IPEndPoint client)
        Zweck: Handhabt den Kauf von virtuellem Land.
        Details:
            �berpr�ft die Anfrage und den Client.
            Gibt standardm��ig eine Erfolgsantwort zur�ck.
            Kann erweitert werden, um den Kaufvorgang tats�chlich zu verarbeiten.
        Anwendung: Wird verwendet, um Landk�ufe zu erm�glichen oder zu validieren.
        */
        /// <summary>Lands the buy function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="client">The client.</param>
        /*
        private XmlRpcResponse buyLandPrep(XmlRpcRequest request, IPEndPoint client)
        {
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
                m_log.InfoFormat("[MONEY XMLRPC]: handleClient buyLandPrep.");
                XmlRpcResponse returnval = new XmlRpcResponse();
                Hashtable returnresp = new Hashtable();
                returnresp.Add("success", true);
                returnval.Value = returnresp;
                return returnval;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: buyLandPrep: {0}", ex.Message);
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }
        }
        */

        /*
        preflightBuyLandPrep(XmlRpcRequest request, IPEndPoint client)
        Zweck: F�hrt Vorabpr�fungen f�r Landk�ufe durch.
        Details:
            �hnlich wie landBuy, pr�ft diese Funktion die G�ltigkeit von Anfrage und Client.
            Kann f�r Sicherheitspr�fungen oder Vorbereitungsvorg�nge verwendet werden.
        Anwendung: Vorbereitung vor der Ausf�hrung eines Landkaufs.
        */
        /// <summary>Preflights the buy land prep function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="client">The client.</param>
        /*
        private XmlRpcResponse preflightBuyLandPrep(XmlRpcRequest request, IPEndPoint client)
        {
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
                m_log.InfoFormat("[MONEY XMLRPC]: handleClient preflightBuyLandPrep.");
                XmlRpcResponse returnval = new XmlRpcResponse();
                Hashtable returnresp = new Hashtable();
                returnresp.Add("success", true);
                returnval.Value = returnresp;
                return returnval;
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY XMLRPC]: preflightBuyLandPrep: {0}", ex.Message);
                return new XmlRpcResponse { Value = new Hashtable { { "success", false } } };
            }
        }
        */

        /*
        GetSSLCommonName(XmlRpcRequest request) und GetSSLCommonName()
        Zweck: Extrahiert den SSL Common Name aus einer Anfrage oder gibt den gespeicherten Wert zur�ck.
        Details:
            Verwendet, um Client-Zertifikate zu validieren.
            Sicherstellt, dass nur autorisierte Clients zugreifen k�nnen.
        Anwendung: Wichtiger Bestandteil der Sicherheits�berpr�fung im System.
        */
        /// <summary>Gets the name of the SSL common.</summary>
        /// <param name="request">The request.</param>
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
        /**/
        /// <summary>Gets the name of the SSL common.</summary>
        public string GetSSLCommonName()
        {
            return m_sslCommonName;
        }

        /*
        handleClientLogin(XmlRpcRequest request, IPEndPoint remoteClient)
        Zweck: Handhabt die Anmeldung eines Clients und �berpr�ft dessen Berechtigungen.
        Details:
            �berpr�ft den SSL Common Name und liest Client-Daten aus der Anfrage.
            F�hrt Validierungen f�r verschiedene Avatar-Typen durch (z. B. Gast, NPC, Fremd-Avatar).
            Speichert Sitzungsdaten und initialisiert ein Benutzerkonto in der Datenbank, falls erforderlich.
            Gibt die W�hrungsbilanz des Benutzers zur�ck.
        Anwendung: Wird beim Einloggen eines Benutzers in das System verwendet.
        */
        /// <summary>
        /// Get the user balance when user entering a parcel.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
        handleClientLogout(XmlRpcRequest request, IPEndPoint remoteClient)
        Zweck: Beendet die Sitzung eines Benutzers.
        Details:
            Entfernt Sitzungs- und sichere Sitzungsdaten aus internen Dictionaries.
            Gibt eine Erfolgsantwort zur�ck.
        Anwendung: Aufgerufen, wenn ein Benutzer sich abmeldet.
        */
        /// <summary>
        /// handle incoming transaction
        /// </summary>
        /// <param name="request"></param>
        /// <param name="remoteClient"></param>
        /// <returns></returns>
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


        /*
        handleTransaction
        Zweck:
        Verarbeitet normale Geldtransaktionen zwischen zwei Nutzern.
        Wichtige Schritte:
            Authentifiziert den Absender basierend auf Sitzungsinformationen.
            Validiert Eingabedaten wie senderID, receiverID, amount usw.
            Erstellt eine Transaktion und speichert sie in der Datenbank.
            �bertr�gt das Geld und benachrichtigt beide Parteien, falls erfolgreich.
        Verwendung:
        Diese Funktion wird f�r regul�re Geldtransfers genutzt, wie das Bezahlen eines anderen Benutzers, 
        beispielsweise f�r Dienste oder virtuelle Objekte.
        */
        /// <summary>
        /// handle incoming transaction
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
         handleForceTransaction
        Zweck:
        Erm�glicht die Durchf�hrung einer "erzwungenen" Transaktion, bei der Authentifizierungspr�fungen ignoriert werden k�nnen.
        Wichtige Schritte:
            Pr�ft, ob erzwungene Transaktionen aktiviert sind.
            Validiert die Eingabedaten.
            F�gt die Transaktion direkt in die Datenbank ein und f�hrt sie durch.
        Verwendung:
        Diese Funktion wird in Ausnahmef�llen eingesetzt, z. B. wenn ein Administrator Gelder zwischen Konten verschieben muss, ohne die �blichen Pr�fungen.
         */
        // added by Fumi.Iseki
        /// <summary>
        /// handle incoming force transaction. no check senderSessionID and senderSecureSessionID
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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
        /*
        handleScriptTransaction
        Zweck:
        Erm�glicht Skripten das Senden von Geldtransaktionen, indem sie eine vorher festgelegte Zugriffsmethode verwenden.
        Wichtige Schritte:
            Verifiziert die Zugriffsbefugnis anhand eines geheimen Codes.
            Erstellt eine Transaktion und f�hrt sie aus.
            Benachrichtigt die betroffenen Benutzer �ber die Transaktion.
        Verwendung:
        Wird genutzt, um Geldtransaktionen durch externe oder serverseitige Skripte durchzuf�hren, z. B. f�r automatisierte Zahlungen.
        */
        // added by Fumi.Iseki
        /// <summary>
        /// handle scripted sending money transaction.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
        handleAddBankerMoney
        Zweck:
        Erlaubt einem als "Banker" definierten Benutzer, Geld auf ein Konto hinzuzuf�gen.
        Wichtige Schritte:
            Pr�ft, ob der anfragende Benutzer der autorisierte "Banker" ist.
            F�gt die Transaktion in die Datenbank ein.
            F�hrt die Gutschrift auf das Zielkonto aus.
        Verwendung:
        Wird verwendet, um Guthaben auf Benutzerkonten hinzuzuf�gen, z. B. durch einen Admin oder Banker in einem virtuellen W�hrungssystem.
        */
        // added by Fumi.Iseki
        /// <summary>
        /// handle adding money transaction.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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


        /*
        handlePayMoneyCharge
        Zweck:
            F�hrt eine Transaktion aus, bei der Geld von einem Benutzer (senderID) an einen anderen Benutzer (receiverID) �bertragen wird.
            Pr�ft dabei die Authentizit�t der Sitzung (Session-IDs) und speichert Transaktionsdetails in einer Datenbank.
        Hauptablauf:
            Parameterpr�fung: Die Funktion pr�ft, ob alle notwendigen Parameter (z. B. senderID, amount, transactionType) in der Anforderung enthalten sind.
            Authentifizierung: Vergleicht die Sitzungs-IDs und sichert die Sitzung ab.
            Transaktionserstellung: Erstellt einen Transaktionsdatensatz mit Status PENDING und speichert ihn in der Datenbank.
            Benachrichtigung: Sendet eine Nachricht �ber die erfolgte Transaktion und f�hrt die eigentliche �berweisung durch (NotifyTransfer).
            Fehlerbehandlung: Verarbeitet m�gliche Ausf�lle, etwa durch Rollback der Transaktion.
        Anwendung:
        Diese Funktion wird von einem Client aufgerufen, der eine Zahlung oder Geb�hr ausl�sen m�chte. Beispielaufruf:

        XmlRpcRequest request = new XmlRpcRequest();
        request.Params = new Hashtable
        {
            {"senderID", "uuid-of-sender"},
            {"receiverID", "uuid-of-receiver"},
            {"amount", 100},
            {"description", "Payment for services"}
        };

        XmlRpcResponse response = handlePayMoneyCharge(request, remoteClient);

        */
        // added by Fumi.Iseki
        /// <summary>
        /// handle pay charge transaction. no check receiver information.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public XmlRpcResponse handlePayMoneyCharge(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge:");

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
            m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Object ID = {0}, Object Name = {1}", objectID, objectName);

            if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID))
            {
                if (m_sessionDic[senderID] == senderSessionID && m_secureSessionDic[senderID] == senderSecureSessionID)
                {
                    m_log.InfoFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Pay from {0}", senderID);
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

                        bool result = m_moneyDBService.addTransaction(transaction);
                        if (result)
                        {
                            UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
                            if (user != null)
                            {
                                if (amount > 0 || (m_enableAmountZero && amount == 0))
                                {
                                    string message = string.Format(m_BalanceMessagePayCharge, amount, "SYSTEM", "");
                                    responseData["success"] = NotifyTransfer(transactionUUID, message, "", "");
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
                            m_log.ErrorFormat("[MONEY XMLRPC]: handlePayMoneyCharge: Pay money transaction for user {0} failed.", senderID);
                        }
                        return response;
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Exception occurred while pay money transaction: " + e.ToString());
                    }
                    return response;
                }
            }

            m_log.Error("[MONEY XMLRPC]: handlePayMoneyCharge: Session authentication failure for sender " + senderID);
            responseData["message"] = "Session check failure, please re-login later!";
            return response;
        }

        /*
        NotifyTransfer
        Zweck:
            Setzt eine begonnene Transaktion fort, nachdem sie vom Benutzer best�tigt wurde.
            F�hrt die eigentliche �bertragung der Mittel zwischen Konten durch und aktualisiert den Kontostand.
        Hauptablauf:
            Transaktionsvalidierung: Pr�ft, ob die Transaktion in der Datenbank abgeschlossen ist (Status.SUCCESS_STATUS).
            Kontostandaktualisierung: Aktualisiert den Kontostand von Sender und Empf�nger.
            Objekt�bergabe: Benachrichtigt andere Dienste, wenn ein virtueller Gegenstand Teil der Transaktion ist.
        Anwendung:
        Wird intern von der Anwendung aufgerufen, nachdem eine Transaktion erfolgreich autorisiert wurde.
        */
        //  added by Fumi.Iseki
        /// <summary>
        /// Continue transaction with no confirm.
        /// </summary>
        /// <param name="transactionUUID"></param>
        /// <returns></returns>
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
                        m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Transaction Type = {0}", transaction.Type);
                        m_log.InfoFormat("[MONEY XMLRPC]: NotifyTransfer: Payment finished successfully, now update balance {0}", transactionUUID.ToString());

                        bool updateSender = true;
                        bool updateReceiv = true;
                        if (transaction.Sender == transaction.Receiver) updateSender = false;
                        //if (transaction.Type==(int)TransactionType.UploadCharge) return true;
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

        /*
        handleGetBalance
        Zweck:
            Ermittelt den Kontostand eines Benutzers.
        Hauptablauf:
            Parameterpr�fung: Die Funktion pr�ft, ob alle notwendigen Parameter (clientUUID, clientSessionID, clientSecureSessionID) bereitgestellt werden.
            Authentifizierung: Verifiziert die Sitzung anhand der bereitgestellten IDs.
            Kontostandabfrage: Ruft den Kontostand des Benutzers aus der Datenbank ab und gibt diesen zur�ck.
        Anwendung:
        Wird aufgerufen, wenn ein Client den aktuellen Kontostand abfragen m�chte. Beispielaufruf:

        XmlRpcRequest request = new XmlRpcRequest();
        request.Params = new Hashtable
        {
            {"clientUUID", "uuid-of-client"},
            {"clientSessionID", "session-id"},
            {"clientSecureSessionID", "secure-session-id"}
        };

        XmlRpcResponse response = handleGetBalance(request, remoteClient);

        */
        /// <summary>
        /// Get the user balance.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
        genericCurrencyXMLRPCRequest
        Zweck:
            Generische Funktion zur Kommunikation mit externen Diensten �ber XML-RPC.
        Hauptablauf:
            Parameterpr�fung: Pr�ft, ob die Anforderung g�ltig ist.
            Senden der Anforderung: Erstellt eine XML-RPC-Anfrage und sendet sie an die angegebene URI.
            Fehlerbehandlung: Gibt bei Fehlschl�gen einen Fehler-Hash zur�ck.
        Anwendung:
        Diese Funktion wird von anderen Funktionen verwendet, um mit entfernten Diensten zu kommunizieren.
        */
        /// <summary>   
        /// Generic XMLRPC client abstraction
        /// </summary>   
        /// <param name="ReqParams">Hashtable containing parameters to the method</param>   
        /// <param name="method">Method to invoke</param>   
        /// <returns>Hashtable with success=>bool and other values</returns>   
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

        /*
        UpdateBalance
        Zweck:
            Aktualisiert den Kontostand eines Benutzers und benachrichtigt ihn optional mit einer Nachricht.
        Hauptablauf:
            Sitzungsinformationen abrufen: Sammelt Sitzungsinformationen (clientSessionID, clientSecureSessionID).
            Benachrichtigung: Sendet eine XML-RPC-Anforderung an den entsprechenden Simulator.
        Anwendung:
        Wird intern aufgerufen, wenn der Kontostand nach einer Transaktion ge�ndert wurde.
        */
        /// <summary>
        /// Update the client balance.We don't care about the result.
        /// </summary>
        /// <param name="userID"></param>
        private void UpdateBalance(string userID, string message)
        {
            string sessionID = string.Empty;
            string secureID = string.Empty;

            if (m_sessionDic.ContainsKey(userID) && m_secureSessionDic.ContainsKey(userID))
            {
                sessionID = m_sessionDic[userID];
                secureID = m_secureSessionDic[userID];

                Hashtable requestTable = new Hashtable();
                requestTable["clientUUID"] = userID;
                requestTable["clientSessionID"] = sessionID;
                requestTable["clientSecureSessionID"] = secureID;
                requestTable["Balance"] = m_moneyDBService.getBalance(userID);
                if (message != "") requestTable["Message"] = message;

                UserInfo user = m_moneyDBService.FetchUserInfo(userID);
                if (user != null)
                {
                    genericCurrencyXMLRPCRequest(requestTable, "UpdateBalance", user.SimIP);
                    m_log.InfoFormat("[MONEY XMLRPC]: UpdateBalance: Sended UpdateBalance Request to {0}", user.SimIP.ToString());
                }
            }
        }

        /*
        RollBackTransaction
        Zweck:
        Diese Funktion f�hrt einen "Rollback" (R�ckg�ngig machen) einer fehlgeschlagenen Transaktion durch. 
        Wenn ein Fehler bei der Abwicklung auftritt, wird das Geld vom Empf�nger zur�ck zum Absender �berwiesen.
        Ablauf:
            Pr�ft, ob der Betrag vom Empf�ngerkonto erfolgreich abgezogen wurde.
            �berweist den Betrag zur�ck auf das Absenderkonto.
            Aktualisiert den Transaktionsstatus auf "FAILED_STATUS".
            Sendet Benachrichtigungen an beide Parteien.
            Gibt true zur�ck, wenn der Rollback erfolgreich war, sonst false.
        Anwendung:
        Diese Funktion wird aufgerufen, wenn eine Transaktion abgebrochen werden muss, 
        z. B. bei technischen Fehlern oder wenn der K�ufer das gekaufte Objekt nicht erh�lt.
        */
        /// <summary>
        /// RollBack the transaction if user failed to get the object paid
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
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

        /*
        handleCancelTransfer
        Zweck:
        Erm�glicht Benutzern, eine Transaktion aktiv zu stornieren.
        Ablauf:
            Liest Transaktions-ID und Sicherheitscode aus der Anfrage aus.
            Validiert den Sicherheitscode und die Transaktion.
            Aktualisiert den Status der Transaktion auf "FAILED_STATUS".
            Gibt eine XML-RPC-Antwort zur�ck, die angibt, ob die Stornierung erfolgreich war.
        Anwendung:
        Diese Methode wird von einem Benutzer ausgel�st, der eine Transaktion abbrechen m�chte. 
                Sie wird typischerweise �ber eine XML-RPC-Schnittstelle von einem Client aufgerufen.
        */
        /// <summary>Handles the cancel transfer.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
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

        /*
        handleGetTransaction
        Zweck:
        Stellt Details zu einer spezifischen Transaktion bereit.
        Ablauf:
            �berpr�ft die Sitzung des Clients.
            Sucht die Transaktion in der Datenbank anhand der Transaktions-ID.
            Gibt die Transaktionsdaten (Betrag, Zeit, Typ, Sender, Empf�nger, Beschreibung) zur�ck.
        Anwendung:
        Wird verwendet, um Transaktionsdetails anzuzeigen, beispielsweise auf einer Benutzeroberfl�che oder in einer App.
        */
        /// <summary>Handles the get transaction.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
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

        // In development
        /*
        handleWebLogin
        Zweck:
        Verarbeitet das Einloggen eines Benutzers �ber eine Weboberfl�che.
        Ablauf:
            Pr�ft, ob userID und sessionID angegeben sind.
            Aktualisiert oder speichert die Sitzung im m_webSessionDic.
            Gibt Erfolg zur�ck, wenn die Anmeldung erfolgreich war.
        Anwendung:
        Wird aufgerufen, wenn ein Benutzer sich �ber das Web einloggt.
        */
        /// <summary>Handles the web login.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
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

        /*
        handleWebLogout
        Zweck:
        Verarbeitet das Ausloggen eines Benutzers �ber eine Weboberfl�che.
        Ablauf:
            Entfernt die Sitzung des Benutzers aus m_webSessionDic.
            Gibt Erfolg zur�ck, wenn das Ausloggen erfolgreich war.
        Anwendung:
        Wird aufgerufen, wenn ein Benutzer sich �ber das Web ausloggt.
        */
        /// <summary>Handles the web logout.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
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

        /*
        handleWebGetBalance
        Zweck:
        Liefert den Kontostand eines Benutzers �ber eine Webschnittstelle.
        Ablauf:
            �berpr�ft die Sitzung des Benutzers.
            Ruft den Kontostand aus der Datenbank ab.
            Gibt Erfolg und den Kontostand zur�ck oder einen Fehler, falls etwas schiefgeht.
        Anwendung:
        Erm�glicht Benutzern, ihren aktuellen Kontostand einzusehen.
        */
        /// <summary>
        /// Get balance method for web pages.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
        handleWebGetTransaction
        Zweck:
        Ruft Transaktionsdaten f�r eine Webschnittstelle ab.
        Ablauf:
            �berpr�ft die Sitzung des Benutzers.
            Ruft Transaktionen nach Index, Zeit oder anderen Parametern aus der Datenbank ab.
            Gibt die Transaktionsdaten zur�ck.
        Anwendung:
        Wird verwendet, um Transaktionsverl�ufe oder Details auf einer Weboberfl�che anzuzeigen.
        */
        /// <summary>
        /// Get transaction for web pages
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

        /*
        handleWebGetTransactionNum
        Zweck:
        Ermittelt die Anzahl der Transaktionen eines Benutzers f�r einen bestimmten Zeitraum.
        Ablauf:
            �berpr�ft die Sitzung des Benutzers.
            Ermittelt die Anzahl der Transaktionen in der Datenbank.
            Gibt die Anzahl oder eine Fehlermeldung zur�ck.
        Anwendung:
        Wird zur Anzeige der Anzahl von Transaktionen in einem bestimmten Zeitraum genutzt, z. B. auf Dashboards.
        */
        /// <summary>
        /// Get total number of transactions for web pages.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

    }


}
// Anfrage - Klassen f�r spezifische XML-RPC-Methoden
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

public abstract class XmlRpcBaseRequest
{
    // Gemeinsame Eigenschaften oder Methoden k�nnen hier definiert werden
    string[] AcceptTypes { get; }
    Encoding ContentEncoding { get; }
    long ContentLength { get; }
    long ContentLength64 { get; }
    string ContentType { get; }
    bool HasEntityBody { get; }
    NameValueCollection Headers { get; }
    string HttpMethod { get; }
    Stream InputStream { get; }
    bool IsSecured { get; }
    bool KeepAlive { get; }
    NameValueCollection QueryString { get; }
    Hashtable Query { get; }
    HashSet<string> QueryFlags { get; }
    Dictionary<string, string> QueryAsDictionary { get; }
    string RawUrl { get; }
    IPEndPoint RemoteIPEndPoint { get; }
    IPEndPoint LocalIPEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
    Uri Url { get; }
    string UriPath { get; }
    string UserAgent { get; }
    double ArrivalTS { get; }
}

/// <summary>
/// Represents a request to buy currency.
/// </summary>
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

    /// <summary>
    /// Gets or sets the amount of currency to buy.
    /// </summary>
    /// <value>The amount of currency to buy.</value>
    public decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets the currency type.
    /// </summary>
    /// <value>The currency type.</value>
    public string CurrencyType { get; set; }

    /// <summary>
    /// Validates the request.
    /// </summary>
    /// <returns><c>true</c> if the request is valid; otherwise, <c>false</c>.</returns>
    public bool IsValid()
    {
        // Add validation logic here, e.g., check for null or empty values
        return Amount > 0 && !string.IsNullOrEmpty(CurrencyType);
    }
}
