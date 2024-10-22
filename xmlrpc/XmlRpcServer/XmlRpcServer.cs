using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;

// XmlRpcServer V 1.0.7

class Program
{
    static void Main(string[] args)
    {
        // Pfad zur INI-Datei
        string iniFilePath = "XmlRpcServer.ini";

        // Standardwerte für Adresse und Port
        string address = "localhost";
        string port = "8009";

        // Lese die INI-Datei und setze die Konfiguration
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
        }
        else
        {
            Console.WriteLine("INI-Datei nicht gefunden. Verwende Standardwerte.");
        }

        // Ausgabe der verwendeten Adresse und des Ports
        Console.WriteLine($"Using address: {address}, port: {port}");

        // Starten des HttpListener mit den konfigurierten Werten
        HttpListener listener = new HttpListener();
        string url = $"http://{address}:{port}/";

        // Sicherstellen, dass alle Präfixe mit '/' enden
        listener.Prefixes.Add(url + "currency.php/"); // Endet mit /
        listener.Prefixes.Add(url + "landtool.php/"); // Endet mit /
        listener.Start();

        Console.WriteLine($"Warte auf eingehende Anfragen unter {url} ...");

        while (true)
        {
            // Warte auf eine eingehende Anfrage
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Lese den Inhalt der Anfrage
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string requestBody = reader.ReadToEnd();
                
                // Überprüfe, ob die Anfrage XML-RPC enthält
                bool isXmlRpcRequest = requestBody.Contains("xmlrpc");

                // Prüfe die URL der Anfrage
                if (request.Url.AbsolutePath.EndsWith("/currency.php") || request.Url.AbsolutePath.EndsWith("/currency.php/"))
                {
                    File.AppendAllText("xmlrpc_currency.log", requestBody + Environment.NewLine);
                    
                    // Ausgabe in der Konsole je nach Anfrageart
                    if (isXmlRpcRequest)
                    {
                        Console.WriteLine("XMLRPC empfangene Anfrage (currency.php):");
                    }
                    else
                    {
                        Console.WriteLine("Unbekannte empfangene Anfrage (currency.php):");
                    }
                    Console.WriteLine(requestBody);
                }
                else if (request.Url.AbsolutePath.EndsWith("/landtool.php") || request.Url.AbsolutePath.EndsWith("/landtool.php/"))
                {
                    File.AppendAllText("xmlrpc_landtool.log", requestBody + Environment.NewLine);
                    
                    // Ausgabe in der Konsole je nach Anfrageart
                    if (isXmlRpcRequest)
                    {
                        Console.WriteLine("XMLRPC empfangene Anfrage (landtool.php):");
                    }
                    else
                    {
                        Console.WriteLine("Unbekannte empfangene Anfrage (landtool.php):");
                    }
                    Console.WriteLine(requestBody);
                }
                else
                {
                    // Alle anderen Anfragen in die allgemeine Logdatei
                    File.AppendAllText("xmlrpc_general.log", requestBody + Environment.NewLine);
                    Console.WriteLine("Unbekannte empfangene Anfrage:");
                    Console.WriteLine(requestBody);
                }
            }

            // Bereite die Antwort vor
            string responseString = "<response>Request received</response>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;

            // Sende die Antwort zurück
            using (var output = response.OutputStream)
            {
                output.Write(buffer, 0, buffer.Length);
            }
        }
    }

    // Funktion zum Parsen einer INI-Datei
    static Dictionary<string, Dictionary<string, string>> ParseIniFile(string filePath)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        string currentSection = "";

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmedLine = line.Trim();

            // Ignoriere leere Zeilen und Kommentare
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(";"))
                continue;

            // Überprüfe, ob es sich um eine neue Sektion handelt
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
                // Lese Schlüssel und Wert
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
}
