using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Nwc.XmlRpc;

namespace OpenSim.Grid.Currency
{
    public class CurrencyServer
    {
        private const string SYSURL = "http://example.com"; // Beispiel-URL für Systemmeldungen oder Fehler

        private readonly XmlRpcServer _xmlRpcServer;

        public CurrencyServer()
        {
            _xmlRpcServer = new XmlRpcServer();
            _xmlRpcServer.Add("getCurrencyQuote", GetCurrencyQuote);
            _xmlRpcServer.Add("buyCurrency", BuyCurrency);
            _xmlRpcServer.Add("simulatorUserBalanceRequest", BalanceRequest);
            _xmlRpcServer.Add("regionMoveMoney", RegionMoveMoney);
            _xmlRpcServer.Add("simulatorClaimUserRequest", ClaimUserRequest);
        }

        public XmlRpcResponse GetCurrencyQuote(XmlRpcRequest request)
        {
            var req = (Dictionary<string, object>)request.Params[0];
            string agentId = req["agentId"].ToString();
            string secureSessionId = req["secureSessionId"].ToString();
            int currencyBuy = Convert.ToInt32(req["currencyBuy"]);
            string ipAddress = GetIpAddress();

            bool isSessionValid = OpenSimCheckSecureSession(agentId, secureSessionId);

            XmlRpcResponse response = new XmlRpcResponse();
            if (isSessionValid)
            {
                string confirmValue = GetConfirmValue(ipAddress);
                int cost = ConvertToReal(currencyBuy);

                response.Value = new Dictionary<string, object>
                {
                    { "success", true },
                    { "currency", new Dictionary<string, object> { { "estimatedCost", cost }, { "currencyBuy", currencyBuy } } },
                    { "confirm", confirmValue }
                };
            }
            else
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "Unable to Authenticate\n\nClick URL for more info." },
                    { "errorURI", SYSURL }
                };
            }

            return response;
        }

        public XmlRpcResponse BuyCurrency(XmlRpcRequest request)
        {
            var req = (Dictionary<string, object>)request.Params[0];
            string agentId = req["agentId"].ToString();
            string secureSessionId = req["secureSessionId"].ToString();
            int currencyBuy = Convert.ToInt32(req["currencyBuy"]);
            string confirm = req["confirm"].ToString();
            string ipAddress = GetIpAddress();

            XmlRpcResponse response = new XmlRpcResponse();
            if (confirm != GetConfirmValue(ipAddress))
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "\n\nMismatch Confirm Value!!" },
                    { "errorURI", SYSURL }
                };
                return response;
            }

            bool isSessionValid = OpenSimCheckSecureSession(agentId, secureSessionId);

            if (isSessionValid)
            {
                int cost = ConvertToReal(currencyBuy);
                bool transactionPermit = ProcessTransaction(agentId, cost, ipAddress);

                if (transactionPermit && AddMoney(agentId, currencyBuy, secureSessionId))
                {
                    response.Value = new Dictionary<string, object> { { "success", true } };
                }
                else
                {
                    response.Value = new Dictionary<string, object>
                    {
                        { "success", false },
                        { "errorMessage", "\n\nUnable to process the transaction. The gateway denied your charge" },
                        { "errorURI", SYSURL }
                    };
                }
            }
            else
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "\n\nMismatch Secure Session ID!!" },
                    { "errorURI", SYSURL }
                };
            }

            return response;
        }

        public XmlRpcResponse BalanceRequest(XmlRpcRequest request)
        {
            var req = (Dictionary<string, object>)request.Params[0];
            string agentId = req["agentId"].ToString();
            string secureSessionId = req["secureSessionId"].ToString();

            int balance = GetBalance(agentId, secureSessionId);

            XmlRpcResponse response = new XmlRpcResponse();
            if (balance >= 0)
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", true },
                    { "agentId", agentId },
                    { "funds", balance }
                };
            }
            else
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "Could not authenticate your avatar. Money operations may be unavailable" },
                    { "errorURI", " " }
                };
            }

            return response;
        }

        public XmlRpcResponse RegionMoveMoney(XmlRpcRequest request)
        {
            var req = (Dictionary<string, object>)request.Params[0];
            string agentId = req["agentId"].ToString();
            string destId = req["destId"].ToString();
            string secureSessionId = req["secureSessionId"].ToString();
            int cash = Convert.ToInt32(req["cash"]);
            string ipAddress = GetIpAddress();

            XmlRpcResponse response = new XmlRpcResponse();
            bool regionAuthorized = OpenSimCheckRegionSecret(req["regionId"].ToString(), req["secret"].ToString());

            if (regionAuthorized && OpenSimCheckSecureSession(agentId, secureSessionId))
            {
                int balance = GetBalance(agentId, secureSessionId);

                if (balance >= cash)
                {
                    MoveMoney(agentId, destId, cash, "Transfer", 0, "Description", 0, 0, ipAddress);
                    int newBalance = GetBalance(agentId, secureSessionId);

                    response.Value = new Dictionary<string, object>
                    {
                        { "success", true },
                        { "agentId", agentId },
                        { "funds", balance },
                        { "funds2", newBalance },
                        { "currencySecret", " " }
                    };

                    UpdateSimulatorBalance(agentId, newBalance, secureSessionId);
                    UpdateSimulatorBalance(destId, GetBalance(destId));
                }
                else
                {
                    response.Value = new Dictionary<string, object>
                    {
                        { "success", false },
                        { "errorMessage", "You do not have sufficient funds for this purchase" },
                        { "errorURI", " " }
                    };
                }
            }
            else
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "This region is not authorized to manage your money." },
                    { "errorURI", " " }
                };
            }

            return response;
        }

        public XmlRpcResponse ClaimUserRequest(XmlRpcRequest request)
        {
            var req = (Dictionary<string, object>)request.Params[0];
            string agentId = req["agentId"].ToString();
            string secureSessionId = req["secureSessionId"].ToString();
            string regionId = req["regionId"].ToString();

            XmlRpcResponse response = new XmlRpcResponse();
            bool regionAuthorized = OpenSimCheckRegionSecret(regionId, req["secret"].ToString());

            if (regionAuthorized && OpenSimCheckSecureSession(agentId, secureSessionId))
            {
                bool updateRegion = OpenSimSetCurrentRegion(agentId, regionId);

                if (updateRegion)
                {
                    int balance = GetBalance(agentId, secureSessionId);
                    response.Value = new Dictionary<string, object>
                    {
                        { "success", true },
                        { "agentId", agentId },
                        { "funds", balance },
                        { "currencySecret", " " }
                    };
                }
                else
                {
                    response.Value = new Dictionary<string, object>
                    {
                        { "success", false },
                        { "errorMessage", "Error occurred, when DB was updated." },
                        { "errorURI", " " }
                    };
                }
            }
            else
            {
                response.Value = new Dictionary<string, object>
                {
                    { "success", false },
                    { "errorMessage", "Unable to authenticate avatar. Money operations may be unavailable." },
                    { "errorURI", " " }
                };
            }

            return response;
        }

        private bool OpenSimCheckSecureSession(string agentId, string secureSessionId) { /* Implement secure session check */ return true; }
        private int ConvertToReal(int amount) { /* Currency conversion */ return amount; }
        private bool ProcessTransaction(string agentId, int cost, string ipAddress) { /* Process transaction */ return true; }
        private bool AddMoney(string agentId, int amount, string secureSessionId) { /* Add money to agent account */ return true; }
        private int GetBalance(string agentId, string secureSessionId = null) { /* Fetch balance from database */ return 1000; }
        private void MoveMoney(string fromId, string toId, int amount, string transactionType, int flags, string description, int inventoryPerm, int nextOwnerPerm, string ipAddress) { /* Move money */ }
        private void UpdateSimulatorBalance(string agentId, int balance, string secureSessionId = null) { /* Update simulator balance */ }
        private bool OpenSimCheckRegionSecret(string regionId, string secret) { /* Check region secret */ return true; }
        private bool OpenSimSetCurrentRegion(string agentId, string regionId) { /* Set current region for agent */ return true; }
        private string GetConfirmValue(string ipAddress) { /* Generate confirm value based on IP address */ return "confirm123"; }
        private string GetIpAddress() { return "127.0.0.1"; /* Get IP address */ }

        public void HandleRequest()
        {
            byte[] requestData = Encoding.UTF8.GetBytes(Console.ReadLine());
            string requestString = Encoding.UTF8.GetString(requestData);
            XmlRpcRequest request = new XmlRpcRequest(requestString);
            XmlRpcResponse response = _xmlRpcServer.Execute(request);

            Console.WriteLine(response.ToString());
        }
    }
}
