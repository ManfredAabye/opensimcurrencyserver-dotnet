/*
Funktion des Codes

    Die Klasse CustomSimpleStreamHandler erweitert SimpleBaseRequestHandler und implementiert ISimpleStreamHandler.
    Sie dient als generischer HTTP-Handler, der verschiedene Arten von Authentifizierung und Verarbeitungslogik unterstützt.
    Konstruktoren erlauben flexible Kombinationen aus Pfad, Name, Auth-Objekt, Prozess-Methoden und Callbacks.
    Die zentrale Methode ist Handle, die einen HTTP-Request verarbeitet: Authentifizierung, dann die eigentliche Verarbeitung durch Callback, Delegate oder Fallback-Logik.

Verbesserungsvorschläge

    Logging im Catch-Block für bessere Fehlerdiagnose.
    Optional: Warnung oder Exception, wenn weder m_processAction, m_processRequest gesetzt ist noch ProcessRequest überschrieben wurde.
    Optional: Dokumentation, dass für sinnvolle Funktionalität eine dieser Methoden/Callbacks gesetzt sein muss.

Fazit:
Die Klasse ist robust gegen NullPointer-Exceptions, weil überall sauber geprüft wird. Die Fehlerbehandlung ist sehr generisch – für Produktionseinsatz wäre Logging sinnvoll. 
Die eigentliche Funktion ist flexibel: Sie ermöglicht Authentifizierung und verschiedene Arten von Request-Handling in HTTP-Servern.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenSim.Framework.Servers.HttpServer;
using System.Net;
using OpenSim.Framework.ServiceAuth;

namespace OpenSim.Grid.MoneyServer
{
    public class CustomSimpleStreamHandler : SimpleBaseRequestHandler, ISimpleStreamHandler
    {
        protected IServiceAuth m_Auth;
        protected SimpleStreamMethod m_processRequest;
        private Action<IOSHttpRequest, IOSHttpResponse> m_processAction;

        public CustomSimpleStreamHandler(string path) : base(path) { }

        public CustomSimpleStreamHandler(string path, string name) : base(path, name) { }

        public CustomSimpleStreamHandler(string path, SimpleStreamMethod processRequest) : base(path)
        {
            m_processRequest = processRequest;
        }

        public CustomSimpleStreamHandler(string path, SimpleStreamMethod processRequest, string name) : base(path, name)
        {
            m_processRequest = processRequest;
        }

        public CustomSimpleStreamHandler(string path, IServiceAuth auth) : base(path)
        {
            m_Auth = auth;
        }

        public CustomSimpleStreamHandler(string path, IServiceAuth auth, SimpleStreamMethod processRequest) : base(path)
        {
            m_Auth = auth;
            m_processRequest = processRequest;
        }

        public CustomSimpleStreamHandler(string path, IServiceAuth auth, SimpleStreamMethod processRequest, string name) : base(path, name)
        {
            m_Auth = auth;
            m_processRequest = processRequest;
        }

        public CustomSimpleStreamHandler(string path, Action<IOSHttpRequest, IOSHttpResponse> processAction) : base(path)
        {
            m_processAction = processAction;
        }

        public virtual void Handle(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            RequestsReceived++;

            if (m_Auth != null)
            {
                HttpStatusCode statusCode;

                if (!m_Auth.Authenticate(httpRequest.Headers, httpResponse.AddHeader, out statusCode))
                {
                    httpResponse.StatusCode = (int)statusCode;
                    return;
                }
            }

            try
            {
                if (m_processAction != null)
                    m_processAction(httpRequest, httpResponse);
                else if (m_processRequest != null)
                    m_processRequest(httpRequest, httpResponse);
                else
                    ProcessRequest(httpRequest, httpResponse);
            }
            catch
            {
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

            RequestsHandled++;
        }

        protected virtual void ProcessRequest(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            // Override in derived classes to implement the request handling logic.
        }
    }
}
