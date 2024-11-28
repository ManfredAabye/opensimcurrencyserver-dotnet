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

using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Grid.MoneyServer;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Reflection;
using System.Timers;

using Timer = System.Timers.Timer;


/// <summary>
/// OpenSim Grid MoneyServer
/// </summary>
internal class MoneyServerBase : BaseOpenSimServer, IMoneyServiceCore
{
    private MoneyDBService dbService;

    private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

    private string connectionString = string.Empty;
    private uint m_moneyServerPort = 8008;         // 8008 is default server port

    private string m_certFilename = "";
    private string m_certPassword = "";
    private string m_cacertFilename = "";
    private string m_clcrlFilename = "";
    private bool m_checkClientCert = false;

    private int DEAD_TIME = 120;
    private int MAX_DB_CONNECTION = 10; // 10 is default

    private MoneyXmlRpcModule m_moneyXmlRpcModule;
    private MoneyDBService m_moneyDBService;

    private NSLCertificateVerify m_certVerify = new NSLCertificateVerify(); // Client Certificate

    private Dictionary<string, string> m_sessionDic = new Dictionary<string, string>();
    private Dictionary<string, string> m_secureSessionDic = new Dictionary<string, string>();
    private Dictionary<string, string> m_webSessionDic = new Dictionary<string, string>();

    IConfig m_server_config;
    IConfig m_cert_config;



    /// <summary>
    /// Initializes a new instance of the MoneyServerBase class.
    /// </summary>
    /// <remarks>
    /// This constructor initializes the MoneyServerBase object and sets up the console and logging.
    /// </remarks>
    public MoneyServerBase()
    {
        // Initialize the console for the Money Server
        m_console = new LocalConsole("MoneyServer ");

        // Set the main console instance to the Money Server console
        MainConsole.Instance = m_console;

        // Log a message to indicate that the Money Server is initializing
        m_log.Info("[MONEY SERVER]: Initializing Money Server module and loading configurations...");
    }

    /// <summary>
    /// Work
    /// </summary>
    public void Work()
    {
        // Create a new timer to check transactions every 60 seconds
        Timer checkTimer = new Timer
        {
            Interval = 60 * 1000,
            Enabled = true
        };


        // Add event handler to check transactions
        checkTimer.Elapsed += CheckTransaction;

        try
        {
            // Start the timer
            checkTimer.Start();

            // Run the console prompt loop
            while (true)
            {
                m_console.Prompt();
            }
        }
        catch (Exception ex)
        {
            // Log any exceptions that occur
            m_log.ErrorFormat("Error in Work: {0}", ex.Message);
        }
        finally
        {
            // Stop the timer if it's still running
            if (checkTimer != null && checkTimer.Enabled)
            {
                checkTimer.Stop();
                checkTimer.Dispose();
            }
        }
    }

    /// <summary>
    /// Check the transactions table, set expired transaction state to failed
    /// </summary>
    private void CheckTransaction(object sender, ElapsedEventArgs e)
    {
        if (m_moneyDBService == null)
        {
            m_log.Error("m_moneyDBService is null, cannot check transactions.");
            return;
        }

        try
        {
            long ticksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            int unixEpochTime = (int)((DateTime.UtcNow.Ticks - ticksToEpoch) / 10000000);
            int deadTime = unixEpochTime - DEAD_TIME;
            m_moneyDBService.SetTransExpired(deadTime);
        }
        catch (Exception ex)
        {
            m_log.ErrorFormat("Error in CheckTransaction: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Startup Specific
    /// </summary>
    protected override void StartupSpecific()
    {
        m_log.Info("[MONEY SERVER]: Setup HTTP Server process");

        ReadIniConfig();

        try
        {
            if (m_certFilename != "")
            {
                m_httpServer = new BaseHttpServer(m_moneyServerPort, true, m_certFilename, m_certPassword);
                m_httpServer.CertificateValidationCallback = null;
                //
                if (m_checkClientCert)
                {
                    m_httpServer.CertificateValidationCallback = (RemoteCertificateValidationCallback)m_certVerify.ValidateClientCertificate;
                    m_log.Info("[MONEY SERVER]: Set RemoteCertificateValidationCallback");
                }
            }
            else
            {
                m_httpServer = new BaseHttpServer(m_moneyServerPort);
            }

            SetupMoneyServices();
            m_httpServer.Start();
            base.StartupSpecific();         // OpenSim/Framework/Servers/BaseOpenSimServer.cs 
        }

        catch (Exception e)
        {
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Fail to start HTTPS process");
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Please Check Certificate File or Password. Exit");
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: {0}", e);
            Environment.Exit(1);
        }
    }

    public void ReadIniConfig()
    {
        MoneyServerConfigSource moneyConfig = new MoneyServerConfigSource();
        Config = moneyConfig.m_config;

        try
        {
            // [Startup]
            IConfig st_config = moneyConfig.m_config.Configs["Startup"];
            string PIDFile = st_config.GetString("PIDFile", "");
            if (PIDFile != "") Create_PIDFile(PIDFile);

            // [MySql]
            IConfig db_config = moneyConfig.m_config.Configs["MySql"];
            string sqlserver = db_config.GetString("hostname", "localhost");
            string database = db_config.GetString("database", "OpenSim");
            string username = db_config.GetString("username", "root");
            string password = db_config.GetString("password", "password");
            string pooling = db_config.GetString("pooling", "false");
            string port = db_config.GetString("port", "3306");
            MAX_DB_CONNECTION = db_config.GetInt("MaxConnection", MAX_DB_CONNECTION);

            connectionString = "Server=" + sqlserver + ";Port=" + port + ";Database=" + database + ";User ID=" +
                                        username + ";Password=" + password + ";Pooling=" + pooling + ";";

            // [MoneyServer]
            m_server_config = moneyConfig.m_config.Configs["MoneyServer"];
            DEAD_TIME = m_server_config.GetInt("ExpiredTime", DEAD_TIME);
            m_moneyServerPort = (uint)m_server_config.GetInt("ServerPort", (int)m_moneyServerPort);

            //
            // [Certificate]
            m_cert_config = moneyConfig.m_config.Configs["Certificate"];
            if (m_cert_config == null)
            {
                m_log.Info("[MONEY SERVER]: [Certificate] section is not found. Using [MoneyServer] section instead");
                m_cert_config = m_server_config;
            }

            // HTTPS Server Cert (Server Mode)
            m_certFilename = m_cert_config.GetString("ServerCertFilename", m_certFilename);
            m_certPassword = m_cert_config.GetString("ServerCertPassword", m_certPassword);
            if (m_certFilename != "")
            {
                m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute HTTPS comunication. Server Cert file is " + m_certFilename);
            }

            // Client Certificate
            m_checkClientCert = m_cert_config.GetBoolean("CheckClientCert", m_checkClientCert);
            m_cacertFilename = m_cert_config.GetString("CACertFilename", m_cacertFilename);
            m_clcrlFilename = m_cert_config.GetString("ClientCrlFilename", m_clcrlFilename);
            if (m_checkClientCert && (m_cacertFilename != ""))
            {
                m_certVerify.SetPrivateCA(m_cacertFilename);
                m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CA file is " + m_cacertFilename);
            }
            else
            {
                m_checkClientCert = false;
            }
            if (m_checkClientCert)
            {
                if (m_clcrlFilename != "")
                {
                    m_certVerify.SetPrivateCRL(m_clcrlFilename);
                    m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CRL file is " + m_clcrlFilename);
                }
            }

            // Initialisiere die MoneyDBService mit der Verbindungszeichenkette und der maxDBConnections
            dbService = new MoneyDBService();
            dbService.Initialise(connectionString, MAX_DB_CONNECTION);
        }
        catch (Exception ex)
        {
            m_log.Error("[MONEY SERVER]: ReadIniConfig: Fail to setup configure. Please check MoneyServer.ini. Exit", ex);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Create PID File added by skidz
    /// </summary>
    protected void Create_PIDFile(string path)
        {
        try
        {
            string pidstring = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            FileStream fs = File.Create(path);
            System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
            Byte[] buf = enc.GetBytes(pidstring);
            fs.Write(buf, 0, buf.Length);
            fs.Close();
            m_pidFile = path;
        }

        catch (Exception) { }

    }

    protected virtual void SetupMoneyServices()
    {
        m_log.Info("[MONEY SERVER]: Connecting to Money Storage Server");

        m_moneyDBService = new MoneyDBService();
        m_moneyDBService.Initialise(connectionString, MAX_DB_CONNECTION);

        m_moneyXmlRpcModule = new MoneyXmlRpcModule(connectionString, MAX_DB_CONNECTION);
        m_moneyXmlRpcModule.Initialise(m_version, m_moneyDBService, this);
        m_moneyXmlRpcModule.PostInitialise();
    }

    public bool IsCheckClientCert()
    {
        return m_checkClientCert;
    }

    public IConfig GetServerConfig()
    {
        return m_server_config;
    }

    public IConfig GetCertConfig()
    {
        return m_cert_config;
    }

    public BaseHttpServer GetHttpServer()
    {
        return m_httpServer;
    }

    public Dictionary<string, string> GetSessionDic()
    {
        return m_sessionDic;
    }

    public Dictionary<string, string> GetSecureSessionDic()
    {
        return m_secureSessionDic;
    }

    public Dictionary<string, string> GetWebSessionDic()
    {
        return m_webSessionDic;
    }

    class MoneyServerConfigSource
    {

        public IniConfigSource m_config;

        public MoneyServerConfigSource()
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "MoneyServer.ini");
            if (File.Exists(configPath))
            {
                m_config = new IniConfigSource(configPath);
            }
        }

        public void Save(string path)
        {
            m_config.Save(path);
        }

    }
}

