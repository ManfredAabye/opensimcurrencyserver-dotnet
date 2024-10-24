using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO.Compression;

// V 1.0.10
class Program
{
    static Dictionary<string, Func<XmlRpcRequest, IPEndPoint, XmlRpcResponse>> xmlRpcHandlers = new Dictionary<string, Func<XmlRpcRequest, IPEndPoint, XmlRpcResponse>>();
    static string logFilePath = "XmlRpcLog.txt";  // Default log file

    static void Main(string[] args)
    {
        string iniFilePath = "XmlRpcServer.ini";

        string address = "localhost";
        string port = "8009";

        if (File.Exists(iniFilePath))
        {
            var config = ParseIniFile(iniFilePath);

            if (config.ContainsKey("Connect"))
            {
                if (config["Connect"].ContainsKey("address"))
                    address = config["Connect"]["address"];
                if (config["Connect"].ContainsKey("port"))
                    port = config["Connect"]["port"];
            }

            if (config.ContainsKey("Logging") && config["Logging"].ContainsKey("logFilePath"))
            {
                logFilePath = config["Logging"]["logFilePath"];
            }
        }
        else
        {
            Console.WriteLine("INI-Datei nicht gefunden. Verwende Standardwerte.");
        }

        Console.WriteLine($"Using address: {address}, port: {port}");
        Console.WriteLine($"Logfile path: {logFilePath}");

        HttpListener listener = new HttpListener();
        string url = $"http://{address}:{port}/";

        listener.Prefixes.Add(url + "currency.php/");
        listener.Prefixes.Add(url + "landtool.php/");
        listener.Start();

        Console.WriteLine($"Warte auf eingehende Anfragen unter {url} ...");

        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            HandleXmlRpcRequests(request, response);
        }
    }

    static Dictionary<string, Dictionary<string, string>> ParseIniFile(string filePath)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        string currentSection = "";

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(";"))
                continue;

            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                if (!result.ContainsKey(currentSection))
                {
                    result[currentSection] = new Dictionary<string, string>();
                }
            }
            else
            {
                var keyValue = trimmedLine.Split(new[] { '=' }, 2);
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim();
                    var value = keyValue[1].Trim();
                    if (!string.IsNullOrEmpty(currentSection))
                    {
                        result[currentSection][key] = value;
                    }
                }
            }
        }

        return result;
    }

    static void HandleXmlRpcRequests(HttpListenerRequest request, HttpListenerResponse response)
    {
        Stream requestStream = request.InputStream;
        Stream innerStream = null;
        response.StatusCode = (int)HttpStatusCode.NotFound;
        response.KeepAlive = false;

        try
        {
            if ((request.Headers["Content-Encoding"] == "gzip") || (request.Headers["X-Content-Encoding"] == "gzip"))
            {
                innerStream = requestStream;
                requestStream = new GZipStream(innerStream, CompressionMode.Decompress);
            }

            if (!requestStream.CanRead || requestStream.Length == 0)
                return;
        }
        catch
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        XmlRpcRequest xmlRpcRequest = null;
        try
        {
            using (StreamReader reader = new StreamReader(requestStream, Encoding.UTF8))
            {
                string requestBody = reader.ReadToEnd();
                Log($"Incoming XML-RPC Request: {requestBody}");
                xmlRpcRequest = DeserializeXmlRpcRequest(requestBody);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ERROR]: Failed to decode XML-RPC request: {e.Message}");
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }
        finally
        {
            if (requestStream.CanRead)
                requestStream.Dispose();
            if (innerStream != null && innerStream.CanRead)
                innerStream.Dispose();
        }

        if (xmlRpcRequest == null || string.IsNullOrWhiteSpace(xmlRpcRequest.MethodName))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        XmlRpcResponse xmlRpcResponse = HandleXmlRpcMethod(xmlRpcRequest, request);

        string xmlResponseString = SerializeXmlRpcResponse(xmlRpcResponse);
        Log($"Outgoing XML-RPC Response: {xmlResponseString}");

        SendXmlRpcResponse(response, xmlResponseString);
    }

    static XmlRpcRequest DeserializeXmlRpcRequest(string xmlContent)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xmlContent);

        var methodName = xmlDoc.SelectSingleNode("//methodName")?.InnerText;
        var parameters = new List<object>();

        foreach (XmlNode paramNode in xmlDoc.SelectNodes("//param/value"))
        {
            var valueNode = paramNode.FirstChild;
            parameters.Add(ParseXmlRpcValue(valueNode));
        }

        return new XmlRpcRequest
        {
            MethodName = methodName,
            Params = parameters
        };
    }

    static object ParseXmlRpcValue(XmlNode valueNode)
    {
        switch (valueNode.Name)
        {
            case "string":
                return valueNode.InnerText;
            case "int":
            case "i4":
                return int.Parse(valueNode.InnerText);
            case "boolean":
                return valueNode.InnerText == "1";
            case "double":
                return double.Parse(valueNode.InnerText);
            default:
                return valueNode.InnerText; // Default to string if type is unknown
        }
    }

    static XmlRpcResponse HandleXmlRpcMethod(XmlRpcRequest request, HttpListenerRequest httpRequest)
    {
        if (xmlRpcHandlers.TryGetValue(request.MethodName, out var method))
        {
            try
            {
                request.Params.Add(httpRequest.RemoteEndPoint);
                request.Params.Add(httpRequest.Url);

                return method(request, httpRequest.RemoteEndPoint);
            }
            catch (Exception e)
            {
                return new XmlRpcResponse
                {
                    IsFault = true,
                    FaultCode = -32603,
                    FaultString = $"Error processing method {request.MethodName}: {e.Message}"
                };
            }
        }
        else
        {
            return new XmlRpcResponse
            {
                IsFault = true,
                FaultCode = -32601,
                FaultString = $"Method {request.MethodName} not found"
            };
        }
    }

    static void SendXmlRpcResponse(HttpListenerResponse response, string xmlResponseString)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(xmlResponseString);
        response.ContentLength64 = buffer.Length;
        response.ContentType = "text/xml";
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.KeepAlive = false;
    }

    static string SerializeXmlRpcResponse(XmlRpcResponse response)
    {
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("methodResponse");

            if (response.IsFault)
            {
                writer.WriteStartElement("fault");
                writer.WriteStartElement("value");

                writer.WriteStartElement("struct");
                writer.WriteStartElement("member");
                writer.WriteElementString("name", "faultCode");
                writer.WriteStartElement("value");
                writer.WriteElementString("int", response.FaultCode.ToString());
                writer.WriteEndElement(); // value
                writer.WriteEndElement(); // member

                writer.WriteStartElement("member");
                writer.WriteElementString("name", "faultString");
                writer.WriteStartElement("value");
                writer.WriteElementString("string", response.FaultString);
                writer.WriteEndElement(); // value
                writer.WriteEndElement(); // member

                writer.WriteEndElement(); // struct
                writer.WriteEndElement(); // value
                writer.WriteEndElement(); // fault
            }
            else
            {
                writer.WriteStartElement("params");
                writer.WriteStartElement("param");
                writer.WriteStartElement("value");

                // Example: returning string type response
                writer.WriteStartElement("string");
                writer.WriteString(response.Result?.ToString() ?? string.Empty);
                writer.WriteEndElement(); // string

                writer.WriteEndElement(); // value
                writer.WriteEndElement(); // param
                writer.WriteEndElement(); // params
            }

            writer.WriteEndElement(); // methodResponse
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    static void Log(string message)
    {
        using (StreamWriter writer = new StreamWriter(logFilePath, true))
        {
            writer.WriteLine($"{DateTime.Now}: {message}");
        }
    }
}

class XmlRpcRequest
{
    public string MethodName { get; set; }
    public List<object> Params { get; set; } = new List<object>();
}

class XmlRpcResponse
{
    public bool IsFault { get; set; }
    public int FaultCode { get; set; }
    public string FaultString { get; set; }
    public object Result { get; set; }
}
