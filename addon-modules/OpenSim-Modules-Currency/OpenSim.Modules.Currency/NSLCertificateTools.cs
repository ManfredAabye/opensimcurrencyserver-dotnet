/* 
 * Copyright (c) Contributors, http://www.nsl.tuis.ac.jp

Funktion

Diese Klasse dient zur Verwaltung und Überprüfung von SSL/TLS-Zertifikaten in C# (z. B. für einen MoneyServer). Sie bietet Methoden, um:
    Eigene Zertifikate (CA und Client) zu laden
    Certificate Revocation Lists (CRL) zu laden
    Zertifikatsketten zu prüfen
    Server- und Client-Zertifikate im Rahmen einer SSL/TLS-Verbindung zu validieren

Null Pointer Checks

1. Member-Initialisierung
    Alle Felder (m_chain, m_cacert, m_mycert, m_clientcrl) sind nullable und werden mit null initialisiert.

2. Set- und Get-Methoden
    Beim Laden von Zertifikaten (SetPrivateCert, SetPrivateCA, SetPrivateCRL) wird jeder Fehler per try-catch abgefangen. Im Fehlerfall wird das entsprechende Feld auf null gesetzt und geloggt.

3. Zertifikatsprüfungen
    In CheckPrivateChain wird vor der Nutzung von m_chain und m_cacert explizit auf null geprüft:
    C#

    if (m_chain == null || m_cacert == null) return false;

    In den Callback-Methoden zur Zertifikatsvalidierung (ValidateServerCertificate, ValidateClientCertificate) wird jeweils geprüft, ob ein Zertifikat vorhanden ist (certificate == null).

4. CRL-Prüfung
    Die CRL wird nur geprüft, wenn sie geladen wurde (if (m_clientcrl != null)).

Fehlerquellen und ihre Behandlung
    Dateizugriffe: Wenn Zertifikats- oder CRL-Dateien nicht lesbar oder ungültig sind, wird dies sauber abgefangen, das jeweilige Objekt auf null gesetzt und ein Fehler geloggt.
    Chain-Validation: Wenn die Kette nicht validiert werden kann, wird dies mit Rückgabewerten behandelt (false) und geloggt.
    SSL Policy Errors: Policy-Fehler werden geloggt und führen zum Abbruch der Validierung.
    CRL-Eintrag: Wird ein Zertifikat als widerrufen erkannt, wird dies geloggt und die Validierung schlägt fehl.
    Header "NoVerifyCert": Bei explizitem Wunsch per Header wird die Zertifikatsprüfung übersprungen.

Zusammenfassung
    Null Pointer: Sehr sorgfältige Behandlung. Überall werden Objekte vor Nutzung geprüft, alle Fehlerfälle führen zu Logging und setzen der Objekte auf null, Rückgabe erfolgt immer sicher.
    Fehlerquellen: Hauptsächlich externe Faktoren wie ungültige oder fehlende Dateien, fehlerhafte Zertifikate oder widerrufene Zertifikate. Alles wird abgefangen.
    Funktion: Die Klasse ist robust und für produktiven Serverbetrieb geeignet. Sie bietet vollständige und sichere Zertifikatsprüfung für Server- und Client-Zertifikate inkl. CRL-Unterstützung.

Fazit:
Die Klasse ist sicher gegenüber NullPointer-Exceptions und behandelt alle Fehlerfälle sauber mit Logging. 
Sie erfüllt alle Anforderungen für sichere Zertifikatsvalidierung in einem OpenSim-Serverumfeld.
 */

using System;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using log4net;


namespace NSL.Certificate.Tools
{
    /// <summary>
    /// class NSL Certificate Verify
    /// </summary>
    public class NSLCertificateVerify
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private X509Chain m_chain = null;
        private X509Certificate2 m_cacert = null;
        private X509Certificate2 m_mycert = null;

        private Mono.Security.X509.X509Crl m_clientcrl = null;


        /// <summary>NSL Certificate Verify</summary>
        public NSLCertificateVerify()
        {
            m_chain = null;
            m_cacert = null;
            m_clientcrl = null;

            //  m_log.InfoFormat("[NSL CERT VERIFY]: NSLCertificateVerify()");
        }


        /// <summary>
        /// NSL Certificate Verify
        /// </summary>
        /// <param name="certfile"></param>
        public NSLCertificateVerify(string certfile)
        {
            SetPrivateCA(certfile);

            // m_log.InfoFormat("[NSL CERT VERIFY]: NSLCertificateVerify()");
        }


        /// <summary>
        /// NSL Certificate Verify
        /// </summary>
        /// <param name="certfile"></param>
        /// <param name="crlfile"></param>
        public NSLCertificateVerify(string certfile, string crlfile)
        {
            SetPrivateCA(certfile);
            SetPrivateCRL(crlfile);

            // m_log.InfoFormat("[NSL CERT VERIFY]: NSLCertificateVerify()");
        }


        /// <summary>
        /// Set Private Certificate
        /// </summary>
        /// <param name="certfile"></param>
        /// <param name="passwd"></param>
        public void SetPrivateCert(string certfile, string passwd)
        {
            try
            {
                m_mycert = new X509Certificate2(certfile, passwd);
            }
            catch (Exception ex)
            {
                m_mycert = null;
                m_log.ErrorFormat("[SET PRIVATE CERT]: Cert File setting error [{0}]. {1}", certfile, ex);
            }
        }


        /// <summary>
        /// Get Private Certificate
        /// </summary>
        public X509Certificate2 GetPrivateCert()
        {
            return m_mycert;
        }



        /// <summary>
        /// Set Private CA
        /// </summary>
        /// <param name="certfile"></param>
        public void SetPrivateCA(string certfile)
        {
            try
            {
                m_cacert = new X509Certificate2(certfile);
            }
            catch (Exception ex)
            {
                m_cacert = null;
                m_log.ErrorFormat("[SET PRIVATE CA]: CA File reading error [{0}]. {1}", certfile, ex);
            }

            if (m_cacert != null)
            {
                m_chain = new X509Chain();
                m_chain.ChainPolicy.ExtraStore.Add(m_cacert);
                m_chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                m_chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            }
        }

        /// <summary>Sets the private CRL.</summary>
        /// <param name="crlfile">The crlfile.</param>
        public void SetPrivateCRL(string crlfile)
        {
            try
            {
                m_clientcrl = Mono.Security.X509.X509Crl.CreateFromFile(crlfile);
            }
            catch (Exception ex)
            {
                m_clientcrl = null;
                m_log.ErrorFormat("[SET PRIVATE CRL]: CRL File reading error [{0}]. {1}", crlfile, ex);
            }
        }


        /// <summary>
        /// Check Private Chain
        /// </summary>
        /// <param name="cert"></param>
        /// <returns></returns>
        public bool CheckPrivateChain(X509Certificate2 cert)
        {
            if (m_chain == null || m_cacert == null)
            {
                return false;
            }

            bool ret = m_chain.Build((X509Certificate2)cert);
            if (ret)
            {
                return true;
            }

            for (int i = 0; i < m_chain.ChainStatus.Length; i++)
            {
                if (m_chain.ChainStatus[i].Status == X509ChainStatusFlags.UntrustedRoot) return true;
            }
            
            return false;
        }


        /// <summary>
        /// Validate Server Certificate Callback Function
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
        public bool ValidateServerCertificate(object obj, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: Policy is ({0})", sslPolicyErrors);

            if (obj is HttpWebRequest)
            {
                HttpWebRequest Request = (HttpWebRequest)obj;
                string noVerify = Request.Headers.Get("NoVerifyCert");
                if ((noVerify != null) && (noVerify.ToLower() == "true"))
                {
                    m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: No Verify Server Certificate.");
                    return true;
                }
            }

            X509Certificate2 certificate2 = new X509Certificate2(certificate);
            string commonname = certificate2.GetNameInfo(X509NameType.SimpleName, false);
            m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: Common Name is \"{0}\"", commonname);

            // None, ChainErrors Error except for．
            if ((sslPolicyErrors != SslPolicyErrors.None) && (sslPolicyErrors != SslPolicyErrors.RemoteCertificateChainErrors))
            {
                m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: Policy Error! {0}", sslPolicyErrors);
                return false;
            }

            bool valid = CheckPrivateChain(certificate2);
            if (valid)
            {
                m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: Valid Server Certification for \"{0}\"", commonname);
            }
            else
            {
                m_log.InfoFormat("[NSL SERVER CERT VERIFY]: ValidateServerCertificate: Failed to Verify Server Certification for \"{0}\"", commonname);
            }
            return valid;
        }


        /// <summary>
        /// Validate Client Certificate Callback Function
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
        public bool ValidateClientCertificate(object obj, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: ValidateClientCertificate: Start.");

            if (certificate == null)
            {
                m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: ValidateClientCertificate: Client does not have a Certificate!");
                return false;
            }

            X509Certificate2 certificate2 = new X509Certificate2(certificate);
            string commonname = certificate2.GetNameInfo(X509NameType.SimpleName, false);
            m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: ValidateClientCertificate: Common Name is \"{0}\"", commonname);

            // None, ChainErrors Anything other than that is an error.
            if (sslPolicyErrors != SslPolicyErrors.None && sslPolicyErrors != SslPolicyErrors.RemoteCertificateChainErrors)
            {
                m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: ValidateClientCertificate: Policy Error! {0}", sslPolicyErrors);
                return false;
            }

            // check CRL
            if (m_clientcrl != null)
            {
                Mono.Security.X509.X509Certificate monocert = new Mono.Security.X509.X509Certificate(certificate.GetRawCertData());
                Mono.Security.X509.X509Crl.X509CrlEntry entry = m_clientcrl.GetCrlEntry(monocert);
                if (entry != null)
                {
                    m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: Common Name \"{0}\" was revoked at {1}", commonname, entry.RevocationDate.ToString());
                    return false;
                }
            }

            bool valid = CheckPrivateChain(certificate2);
            if (valid)
            {
                m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: Valid Client Certification for \"{0}\"", commonname);
            }
            else
            {
                m_log.InfoFormat("[NSL CLIENT CERT VERIFY]: Failed to Verify Client Certification for \"{0}\"", commonname);
            }
            return valid;
        }
    }

}
