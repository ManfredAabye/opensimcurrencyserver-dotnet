/* 
 * Copyright (c) Contributors, http://www.nsl.tuis.ac.jp
 *
 */


using System;
using System.Collections;
using System.IO;
using System.Xml;
using System.Net;
using System.Text;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using log4net;
using Nwc.XmlRpc;
using System.Net.Security;
using NSL.Certificate.Tools;


namespace NSL.Network.XmlRpc
{
    public class NSLXmlRpcRequest : XmlRpcRequest
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // The encoding
        private Encoding _encoding = new UTF8Encoding();
        // The serializer
        private XmlRpcRequestSerializer _serializer = new XmlRpcRequestSerializer();
        // The deserializer
        private XmlRpcResponseDeserializer _deserializer = new XmlRpcResponseDeserializer();


        /// <summary>Initializes a new instance of the <see cref="NSLXmlRpcRequest" /> class.</summary>
        public NSLXmlRpcRequest()
        {
            _params = new ArrayList();
        }


        /// <summary>Initializes a new instance of the <see cref="NSLXmlRpcRequest" /> class.</summary>
        /// <param name="methodName">Name of the method.</param>
        /// <param name="parameters">The parameters.</param>
        public NSLXmlRpcRequest(String methodName, IList parameters)
        {
            MethodName = methodName;
            _params = parameters;
        }


        //public XmlRpcResponse certSend(String url, X509Certificate2 myClientCert, bool checkServerCert, Int32 timeout)
        /// <summary>Certs the send.</summary>
        /// <param name="url">The URL.</param>
        /// <param name="certVerify">The cert verify.</param>
        /// <param name="checkServerCert">if set to <c>true</c> [check server cert].</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        /// <exception cref="Nwc.XmlRpc.XmlRpcException"></exception>
        public XmlRpcResponse certSend(String url, NSLCertificateVerify certVerify, bool checkServerCert, Int32 timeout)
        {
            m_log.InfoFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSend: connect to {0}", url);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            if (request == null)
            {
                throw new XmlRpcException(XmlRpcErrorCodes.TRANSPORT_ERROR, XmlRpcErrorCodes.TRANSPORT_ERROR_MSG + ": Could not create request with " + url);
            }

            X509Certificate2 clientCert = null;

            request.Method = "POST";
            request.ContentType = "text/xml";
            request.AllowWriteStreamBuffering = true;
            request.Timeout = timeout;
            request.UserAgent = "NSLXmlRpcRequest";

            if (certVerify != null)
            {
                clientCert = certVerify.GetPrivateCert();
                if (clientCert != null) request.ClientCertificates.Add(clientCert);  // Own certificate   // 自身の証明書
                request.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(certVerify.ValidateServerCertificate);
            }
            else
            {
                checkServerCert = false;
            }

            if (!checkServerCert)
            {
                request.Headers.Add("NoVerifyCert", "true");   // Do not verify the certificate of the other party  // 相手の証明書を検証しない
            }

            Stream stream = null;
            try
            {
                stream = request.GetRequestStream();
            }

            catch (Exception ex)
            {
                ex.ToString();
                m_log.ErrorFormat("[MONEY NSL XMLRPC]: GetRequestStream Error: {0}", ex);
                stream = null;
            }
            if (stream == null) return null;

            XmlTextWriter xml = new XmlTextWriter(stream, _encoding);
            _serializer.Serialize(xml, this);
            xml.Flush();
            xml.Close();

            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY NSL XMLRPC]: XmlRpcResponse certSend: GetResponse Error: {0}", ex.ToString());
            }
            StreamReader input = new StreamReader(response.GetResponseStream());

            string inputXml = input.ReadToEnd();
            XmlRpcResponse resp = (XmlRpcResponse)_deserializer.Deserialize(inputXml);

            input.Close();
            response.Close();
            return resp;
        }
    }
}
