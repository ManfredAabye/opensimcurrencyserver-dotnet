using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

// XmlRpcServer

class Program
{
    static void Main(string[] args)
    {
        // Erstelle einen HttpListener, der auf Port 8080 hört.
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8008/currency.php/");
        listener.Start();
        Console.WriteLine("Warte auf eingehende XML-RPC-Anfragen...");

        while (true)
        {
            // Warte auf eine eingehende Anfrage.
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Lese den Inhalt der Anfrage.
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string requestBody = reader.ReadToEnd();
                Console.WriteLine("Empfangene Anfrage: ");
                Console.WriteLine(requestBody);

                // Schreibe die Anfrage in die Datei xmlrpcdebug.txt.
                File.AppendAllText("xmlrpcdebug.txt", requestBody + Environment.NewLine);
            }

            // Setze die Antwort zurück.
            string responseString = "<response>Request received</response>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            using (var output = response.OutputStream)
            {
                output.Write(buffer, 0, buffer.Length);
            }
        }
    }
}
