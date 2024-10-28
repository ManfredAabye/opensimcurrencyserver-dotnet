using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Nwc.XmlRpc;

namespace OpenSim.Grid.Helpers
{
    public static class HelperFunctions
    {
        private const bool USE_CURRENCY_SERVER = true;
        private const string SYSURL = "http://example.com"; // Ersetzen Sie dies durch die URL Ihres Systems
        private const string CurrencyScriptKey = "123456789";

        private static HttpClient httpClient = new HttpClient();

        // Konvertiert Betrag in echte Währung
        public static int ConvertToReal(int amount)
        {
            //# Do Credit Card Processing here!  Return False if it fails!
            //# Remember, $amount is stored without decimal places, however it's assumed
            //# that the transaction amount is in Cents and has two decimal places
            //# 5 dollars will be 500
            //# 15 dollars will be 1500

            //if ($avatarID==CURRENCY_BANKER) return true;
            //return false;
            return amount;
        }

        public static bool ProcessTransaction(string avatarID, int cost, string ipAddress)
        {
            // Implementieren Sie die tatsächliche Transaktionslogik hier, falls erforderlich
            return true;
        }

        // Sendet eine Benutzerbenachrichtigung
        public static async Task<bool> UserAlert(string agentID, string message, string secureID = null)
        {
            if (!USE_CURRENCY_SERVER || !IsGuid(agentID) || (secureID != null && !IsGuid(secureID, true)))
                return false;

            var results = await OpenSimGetUserInfo(agentID);
            var server = MakeUrl(results.SimIp, 9000);

            if (string.IsNullOrEmpty(server))
                return false;

            var session = await OpenSimGetAvatarSession(agentID);
            if (session == null)
                return false;

            if (secureID == null)
                secureID = session.SecureID;

            var reqParams = new
            {
                clientUUID = agentID,
                clientSessionID = session.SessionID,
                clientSecureSessionID = secureID,
                Description = message
            };

            var response = await DoCall(server, "UserAlert", reqParams);
            return response?.ContainsKey("success") == true && (bool)response["success"];
        }

        public static async Task<bool> UpdateSimulatorBalance(string agentID, int amount = -1, string secureID = null)
        {
            if (!USE_CURRENCY_SERVER || !IsGuid(agentID) || (secureID != null && !IsGuid(secureID, true)))
                return false;

            if (amount < 0)
            {
                amount = await GetBalance(agentID, secureID);
                if (amount < 0) return false;
            }

            var results = await OpenSimGetUserInfo(agentID);
            var server = MakeUrl(results.SimIp, 9000);
            if (string.IsNullOrEmpty(server)) return false;

            var session = await OpenSimGetAvatarSession(agentID);
            if (session == null) return false;

            if (secureID == null) secureID = session.SecureID;

            var reqParams = new
            {
                clientUUID = agentID,
                clientSessionID = session.SessionID,
                clientSecureSessionID = secureID,
                Balance = amount
            };

            var response = await DoCall(server, "UpdateBalance", reqParams);
            return response?.ContainsKey("success") == true && (bool)response["success"];
        }

        public static async Task<int> GetBalance(string agentID, string secureID = null)
        {
            if (!USE_CURRENCY_SERVER || !IsGuid(agentID) || (secureID != null && !IsGuid(secureID, true)))
                return -1;

            var results = await OpenSimGetUserInfo(agentID);
            var server = MakeUrl(results.SimIp, 9000);
            if (string.IsNullOrEmpty(server)) return -1;

            var session = await OpenSimGetAvatarSession(agentID);
            if (session == null) return -1;

            if (secureID == null) secureID = session.SecureID;

            var reqParams = new
            {
                clientUUID = agentID,
                clientSessionID = session.SessionID,
                clientSecureSessionID = secureID
            };

            var response = await DoCall(server, "GetBalance", reqParams);
            return response?.ContainsKey("balance") == true ? (int)response["balance"] : -1;
        }

        public static async Task<bool> SendMoney(string agentID, int amount, int type = 5003, string serverUri = null, string secretCode = null)
        {
            if (!USE_CURRENCY_SERVER || !IsGuid(agentID))
                return false;

            var server = MakeUrl(serverUri ?? (await OpenSimGetUserInfo(agentID)).SimIp, 9000);

            if (string.IsNullOrEmpty(server))
                return false;

            if (secretCode != null)
                secretCode = GetConfirmValue(server);
            else
                secretCode = await GetConfirmValue(server);

            var reqParams = new
            {
                agentUUID = agentID,
                secretAccessCode = secretCode,
                amount = amount,
                type = type
            };

            var response = await DoCall(server, "SendMoney", reqParams);
            return response?.ContainsKey("success") == true && (bool)response["success"];
        }

        public static string GetConfirmValue(string ipAddress)
        {
            var key = CurrencyScriptKey;
            return Hash.MD5(key + "_" + ipAddress);
        }

        private static async Task<Dictionary<string, object>> DoCall(string serverUri, string method, object reqParams)
        {
            var request = XmlRpcRequest(reqParams, method);
            var content = new StringContent(request, Encoding.UTF8, "text/xml");

            using var response = await httpClient.PostAsync(serverUri, content);
            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsStringAsync();
            return XmlRpcResponse(responseData);
        }

        private static string XmlRpcRequest(object requestData, string method)
        {
            return XmlRpcUtil.ToXmlRpcRequest(method, requestData);
        }

        private static Dictionary<string, object> XmlRpcResponse(string xmlData)
        {
            return XmlRpcUtil.FromXmlRpcResponse(xmlData);
        }
        
        private static bool IsGuid(string value, bool allowNull = false)
        {
            return Guid.TryParse(value, out _) || (allowNull && value == null);
        }

        private static async Task<(string SimIp, string SessionID, string SecureID)> OpenSimGetUserInfo(string agentID)
        {
            // Implementieren Sie die OpenSim-Benutzerinformationsabfrage hier
            return (SimIp: "localhost", SessionID: "session-id", SecureID: "secure-id");
        }

        private static string MakeUrl(string simIp, int port)
        {
            return $"http://{simIp}:{port}";
        }

        private static async Task<(string SimIp, string SessionID, string SecureID)> OpenSimGetAvatarSession(string agentID)
        {
            // Implementieren Sie die Logik zur Abfrage der Avatar-Sitzung hier
            return (SimIp: "localhost", SessionID: "session-id", SecureID: "secure-id");
        }
    }
}
