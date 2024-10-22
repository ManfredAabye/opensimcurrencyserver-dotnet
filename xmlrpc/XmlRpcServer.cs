using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;

// XmlRpcServer V 1.0.3

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

        // Starten des HttpListener mit den konfigurierten Werten
        HttpListener listener = new HttpListener();
        string url = $"http://{address}:{port}/";
        listener.Prefixes.Add(url + "currency.php/");
        listener.Prefixes.Add(url + "landtool.php/");
        listener.Start();

        Console.WriteLine($"Warte auf eingehende XML-RPC-Anfragen unter {url} ...");

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
                Console.WriteLine("Empfangene Anfrage:");
                Console.WriteLine(requestBody);

                // Schreibe die Anfrage in unterschiedliche Dateien je nach Ziel-URL.
                if (request.Url.AbsolutePath.EndsWith("/currency.php/"))
                {
                    File.AppendAllText("xmlrpc_currency.log", requestBody + Environment.NewLine);
                }
                else if (request.Url.AbsolutePath.EndsWith("/landtool.php/"))
                {
                    File.AppendAllText("xmlrpc_landtool.log", requestBody + Environment.NewLine);
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
