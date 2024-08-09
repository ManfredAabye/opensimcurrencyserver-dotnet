/* Modified by Fumi.Iseki for Unix/Linix  http://www.nsl.tuis.ac.jp
 *
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Net;
using System.Security.Cryptography;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using Mono.Addins;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using NSL.Certificate.Tools;
using NSL.Network.XmlRpc;

[assembly: Addin("DTLNSLMoneyModule", "1.0")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace OpenSim.Modules.Currency
{
    /// <summary>
    /// Transaction Type
    /// </summary>
    public enum TransactionType : int
    {
        None = 0,
        // Extend
        BirthGift = 900,
        AwardPoints = 901,
        // One-Time Charges
        ObjectClaim = 1000,
        LandClaim = 1001,
        GroupCreate = 1002,
        GroupJoin = 1004,
        TeleportCharge = 1100,
        UploadCharge = 1101,
        LandAuction = 1102,
        ClassifiedCharge = 1103,
        // Recurrent Charges
        ObjectTax = 2000,
        LandTax = 2001,
        LightTax = 2002,
        ParcelDirFee = 2003,
        GroupTax = 2004,
        ClassifiedRenew = 2005,
        ScheduledFee = 2900,
        // Inventory Transactions
        GiveInventory = 3000,
        // Transfers Between Users
        ObjectSale = 5000,
        Gift = 5001,
        LandSale = 5002,
        ReferBonus = 5003,
        InvntorySale = 5004,
        RefundPurchase = 5005,
        LandPassSale = 5006,
        DwellBonus = 5007,
        PayObject = 5008,
        ObjectPays = 5009,
        BuyMoney = 5010,
        MoveMoney = 5011,
        SendMoney = 5012,
        // Group Transactions
        GroupLandDeed = 6001,
        GroupObjectDeed = 6002,
        GroupLiability = 6003,
        GroupDividend = 6004,
        GroupMembershipDues = 6005,
        // Stipend Credits
        StipendBasic = 10000
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "DTLNSLMoneyModule")]
    public class DTLNSLMoneyModule : IMoneyModule, ISharedRegionModule
    {
        // Constant memebers   
        private const int MONEYMODULE_REQUEST_TIMEOUT = 10000;

        // Private data members.   
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        //private bool  m_enabled = true;
        private bool m_sellEnabled = false;
        private bool m_enable_server = true;   // enable Money Server

        private IConfigSource m_config;

        private string m_moneyServURL = string.Empty;
        public BaseHttpServer HttpServer;

        private string m_certFilename = "";
        private string m_certPassword = "";
        private bool m_checkServerCert = false;
        private string m_cacertFilename = "";
        //private X509Certificate2 m_cert  = null;

        private bool m_use_web_settle = false;
        private string m_settle_url = "";
        private string m_settle_message = "";
        private bool m_settle_user = false;

        private int m_hg_avatarClass = (int)AvatarType.HG_AVATAR;

        private NSLCertificateVerify m_certVerify = new NSLCertificateVerify(); // For server authentication


        /// <summary>   
        /// Scene dictionary indexed by Region Handle   
        /// </summary>   
        private Dictionary<ulong, Scene> m_sceneList = new Dictionary<ulong, Scene>();

        /// <summary>   
        /// To cache the balance data while the money server is not available.   
        /// </summary>   
        private Dictionary<UUID, int> m_moneyServer = new Dictionary<UUID, int>();

        // Events  
        public event ObjectPaid OnObjectPaid;

        // Price
        private int ObjectCount = 0;
        private int PriceEnergyUnit = 100;
        private int PriceObjectClaim = 10;
        private int PricePublicObjectDecay = 4;
        private int PricePublicObjectDelete = 4;
        private int PriceParcelClaim = 1;
        private float PriceParcelClaimFactor = 1.0f;
        private int PriceUpload = 0;
        private int PriceRentLight = 5;
        private float PriceObjectRent = 1.0f;
        private float PriceObjectScaleFactor = 10.0f;
        private int PriceParcelRent = 1;
        private int PriceGroupCreate = 0;
        private int TeleportMinPrice = 2;
        private float TeleportPriceExponent = 2.0f;
        private float EnergyEfficiency = 1.0f;



        /// <summary>Initialises the specified scene.</summary>
        /// <param name="scene">The scene.</param>
        /// <param name="source">The source.</param>
        public void Initialise(Scene scene, IConfigSource source)
        {
            Initialise(source);
            if (string.IsNullOrEmpty(m_moneyServURL)) m_enable_server = false;
            //
            AddRegion(scene);
        }


        /// <summary>
        /// This is called to initialize the region module. For shared modules, this is called
        /// exactly once, after creating the single (shared) instance. For non-shared modules,
        /// this is called once on each instance, after the instace for the region has been created.
        /// </summary>
        /// <param name="source">A <see cref="T:Nini.Config.IConfigSource" /></param>
        public void Initialise(IConfigSource source)
        {
            //m_log.InfoFormat("[MONEY MODULE]: Initialise:");

            // Handle the parameters errors.
            if (source == null) return;

            try
            {
                m_config = source;

                // [Economy] section
                IConfig economyConfig = m_config.Configs["Economy"];

                if (economyConfig.GetString("EconomyModule") != Name)
                {
                    //m_enabled = false;
                    m_log.InfoFormat("[MONEY MODULE]: Initialise: The DTL/NSL MoneyModule is disabled");
                    return;
                }
                else
                {
                    m_log.InfoFormat("[MONEY MODULE]: Initialise: The DTL/NSL MoneyModule is enabled");
                }

                m_sellEnabled = economyConfig.GetBoolean("SellEnabled", m_sellEnabled);
                m_moneyServURL = economyConfig.GetString("CurrencyServer", m_moneyServURL);

                // Client Certification
                m_certFilename = economyConfig.GetString("ClientCertFilename", m_certFilename);
                m_certPassword = economyConfig.GetString("ClientCertPassword", m_certPassword);
                if (m_certFilename != "")
                {
                    m_certVerify.SetPrivateCert(m_certFilename, m_certPassword);
                    m_log.Info("[MONEY MODULE]: Initialise: Issue Authentication of Client. Cert File is " + m_certFilename);
                }

                // Server Authentication  MoneyServer server certificate check
                m_checkServerCert = economyConfig.GetBoolean("CheckServerCert", m_checkServerCert);
                m_cacertFilename = economyConfig.GetString("CACertFilename", m_cacertFilename);

                if (m_cacertFilename != "")
                {
                    m_certVerify.SetPrivateCA(m_cacertFilename);
                }
                else
                {
                    m_checkServerCert = false;
                }

                if (m_checkServerCert)
                {
                    m_log.Info("[MONEY MODULE]: Initialise: Execute Authentication of Server. CA Cert File is " + m_cacertFilename);
                }
                else
                {
                    m_log.Info("[MONEY MODULE]: Initialise: No check Money Server or CACertFilename is empty. CheckServerCert is false.");
                }

                // Settlement
                m_use_web_settle = economyConfig.GetBoolean("SettlementByWeb", m_use_web_settle);
                m_settle_url = economyConfig.GetString("SettlementURL", m_settle_url);
                m_settle_message = economyConfig.GetString("SettlementMessage", m_settle_message);

                // Price
                PriceEnergyUnit = economyConfig.GetInt("PriceEnergyUnit", PriceEnergyUnit);
                PriceObjectClaim = economyConfig.GetInt("PriceObjectClaim", PriceObjectClaim);
                PricePublicObjectDecay = economyConfig.GetInt("PricePublicObjectDecay", PricePublicObjectDecay);
                PricePublicObjectDelete = economyConfig.GetInt("PricePublicObjectDelete", PricePublicObjectDelete);
                PriceParcelClaim = economyConfig.GetInt("PriceParcelClaim", PriceParcelClaim);
                PriceParcelClaimFactor = economyConfig.GetFloat("PriceParcelClaimFactor", PriceParcelClaimFactor);
                PriceUpload = economyConfig.GetInt("PriceUpload", PriceUpload);
                PriceRentLight = economyConfig.GetInt("PriceRentLight", PriceRentLight);
                PriceObjectRent = economyConfig.GetFloat("PriceObjectRent", PriceObjectRent);
                PriceObjectScaleFactor = economyConfig.GetFloat("PriceObjectScaleFactor", PriceObjectScaleFactor);
                PriceParcelRent = economyConfig.GetInt("PriceParcelRent", PriceParcelRent);
                PriceGroupCreate = economyConfig.GetInt("PriceGroupCreate", PriceGroupCreate);
                TeleportMinPrice = economyConfig.GetInt("TeleportMinPrice", TeleportMinPrice);
                TeleportPriceExponent = economyConfig.GetFloat("TeleportPriceExponent", TeleportPriceExponent);
                EnergyEfficiency = economyConfig.GetFloat("EnergyEfficiency", EnergyEfficiency);

                // for HG Avatar
                string avatar_class = economyConfig.GetString("HGAvatarAs", "HGAvatar").ToLower();
                if (avatar_class == "localavatar") m_hg_avatarClass = (int)AvatarType.LOCAL_AVATAR;
                else if (avatar_class == "guestavatar") m_hg_avatarClass = (int)AvatarType.GUEST_AVATAR;
                else if (avatar_class == "hgavatar") m_hg_avatarClass = (int)AvatarType.HG_AVATAR;
                else if (avatar_class == "foreignavatar") m_hg_avatarClass = (int)AvatarType.FOREIGN_AVATAR;
                else m_hg_avatarClass = (int)AvatarType.UNKNOWN_AVATAR;

            }
            catch
            {
                m_log.ErrorFormat("[MONEY MODULE]: Initialise: Faile to read configuration file");
            }
        }


        // Test 2023

        /// <summary>The m RPC handlers</summary>
        private Dictionary<string, XmlRpcMethod> m_rpcHandlers;

        /// <summary>Processes the PHP.</summary>
        /// <param name="request">The request.</param>
        /// <param name="response">The response.</param>
        public void processPHP(IOSHttpRequest request, IOSHttpResponse response)
        {
            MainServer.Instance.HandleXmlRpcRequests((OSHttpRequest)request, (OSHttpResponse)response, m_rpcHandlers);
        }

        /// <summary>Quotes the function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        public XmlRpcResponse quote_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {

            // UUID agentId = UUID.Zero;
            int amount = 0;
            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                amount = (int)requestData["currencyBuy"];
            }
            catch { }

            Hashtable currencyResponse = new Hashtable();
            currencyResponse.Add("estimatedCost", 0);
            //currencyResponse.Add("estimatedLocalCost", " 0 Euros");

            currencyResponse.Add("currencyBuy", amount);
            m_log.InfoFormat("[MONEY MODULE] quote_func currencyBuy: {0}", amount);

            Hashtable quoteResponse = new Hashtable();
            quoteResponse.Add("success", true);
            quoteResponse.Add("currency", currencyResponse);
            quoteResponse.Add("confirm", "asdfad9fj39ma9fj");

            //quoteResponse.Add("success", false);
            //quoteResponse.Add("errorMessage", "There is currency");
            //quoteResponse.Add("errorURI", "http://opensimulator.org");
            XmlRpcResponse returnval = new XmlRpcResponse();
            returnval.Value = quoteResponse;          

            return returnval;
        }

        /// <summary>Preflights the buy land prep function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        public XmlRpcResponse preflightBuyLandPrep_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            Hashtable membershiplevels = new Hashtable();
            ArrayList levels = new ArrayList();
            Hashtable level = new Hashtable();
            level.Add("id", "00000000-0000-0000-0000-000000000000");
            level.Add("description", "some level");
            levels.Add(level);
            //membershiplevels.Add("levels",levels);

            Hashtable landuse = new Hashtable();
            landuse.Add("upgrade", false);
            landuse.Add("action", "http://invaliddomaininvalid.com/");

            Hashtable currency = new Hashtable();
            currency.Add("estimatedCost", 0);

            Hashtable membership = new Hashtable();
            membershiplevels.Add("upgrade", false);
            membershiplevels.Add("action", "http://invaliddomaininvalid.com/");
            membershiplevels.Add("levels", membershiplevels);

            retparam.Add("success", true);
            retparam.Add("currency", currency);
            retparam.Add("membership", membership);
            retparam.Add("landuse", landuse);
            retparam.Add("confirm", "asdfajsdkfjasdkfjalsdfjasdf");

            ret.Value = retparam;

            return ret;
        }

        /// <summary>Lands the buy function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        public XmlRpcResponse landBuy_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            // Hashtable requestData = (Hashtable) request.Params[0];

            // UUID agentId = UUID.Zero;
            // int amount = 0;

            retparam.Add("success", true);
            ret.Value = retparam;

            m_log.InfoFormat("[MONEY MODULE]: land buy", retparam.ToString());

            return ret;
        }

        /// <summary>Buys the function.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        public XmlRpcResponse buy_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            // Hashtable requestData = (Hashtable) request.Params[0];
            // UUID agentId = UUID.Zero;
            // int amount = 0;

            XmlRpcResponse returnval = new XmlRpcResponse();
            Hashtable returnresp = new Hashtable();
            returnresp.Add("success", true);
            returnval.Value = returnresp;
            m_log.InfoFormat("[MONEY MODULE]: money buy", returnval.ToString());
            return returnval;
        }
        // Test End 2023


        /// <summary>
        /// This is called whenever a <see cref="T:OpenSim.Region.Framework.Scenes.Scene" /> is added. For shared modules, this can happen several times.
        /// For non-shared modules, this happens exactly once, after <see cref="M:OpenSim.Region.Framework.Interfaces.IRegionModuleBase.Initialise(Nini.Config.IConfigSource)" /> has been called.
        /// </summary>
        /// <param name="scene">A <see cref="T:OpenSim.Region.Framework.Scenes.Scene" /></param>
        public void AddRegion(Scene scene)
        {
            //m_log.InfoFormat("[MONEY MODULE]: AddRegion:");

            if (scene == null) return;

            scene.RegisterModuleInterface<IMoneyModule>(this);  // Eliminate conflicting modules

            lock (m_sceneList)
            {
                if (m_sceneList.Count == 0)
                {
                    if (m_enable_server)
                    {
                        HttpServer = new BaseHttpServer(9000);
                        HttpServer.AddStreamHandler(new Region.Framework.Scenes.RegionStatsHandler(scene.RegionInfo));

                        HttpServer.AddXmlRPCHandler("OnMoneyTransfered", OnMoneyTransferedHandler);
                        HttpServer.AddXmlRPCHandler("UpdateBalance", BalanceUpdateHandler);
                        HttpServer.AddXmlRPCHandler("UserAlert", UserAlertHandler);
                        HttpServer.AddXmlRPCHandler("GetBalance", GetBalanceHandler);                       // added
                        HttpServer.AddXmlRPCHandler("AddBankerMoney", AddBankerMoneyHandler);               // added
                        HttpServer.AddXmlRPCHandler("SendMoney", SendMoneyHandler);                         // added
                        HttpServer.AddXmlRPCHandler("MoveMoney", MoveMoneyHandler);                         // added

                        // OS Version > 0.9.2 ???
                        m_rpcHandlers = new Dictionary<string, XmlRpcMethod>(); // add php 2023
                        m_rpcHandlers.Add("getCurrencyQuote", quote_func); // add php 2023
                        m_rpcHandlers.Add("buyCurrency", buy_func); // add php 2023
                        m_rpcHandlers.Add("preflightBuyLandPrep", preflightBuyLandPrep_func); // add php 2023
                        m_rpcHandlers.Add("buyLandPrep", landBuy_func); // add php 2023

                        MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler("/currency.php", processPHP)); // add php 2023
                        MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler("/landtool.php", processPHP)); // add php 2023

                        // OS Version < 0.9.2 ???
                        // HttpServer.AddXmlRPCHandler("getCurrencyQuote", quote_func); // add php 2023
                        // HttpServer.AddXmlRPCHandler("buyCurrency", buy_func);
                        // HttpServer.AddXmlRPCHandler("preflightBuyLandPrep", preflightBuyLandPrep_func); // add php 2023
                        // HttpServer.AddXmlRPCHandler("buyLandPrep", landBuy_func); // add php 2023

                        MainServer.Instance.AddXmlRPCHandler("OnMoneyTransfered", OnMoneyTransferedHandler);
                        MainServer.Instance.AddXmlRPCHandler("UpdateBalance", BalanceUpdateHandler);
                        MainServer.Instance.AddXmlRPCHandler("UserAlert", UserAlertHandler);
                        MainServer.Instance.AddXmlRPCHandler("GetBalance", GetBalanceHandler);              // added
                        MainServer.Instance.AddXmlRPCHandler("AddBankerMoney", AddBankerMoneyHandler);      // added
                        MainServer.Instance.AddXmlRPCHandler("SendMoney", SendMoneyHandler);                // added
                        MainServer.Instance.AddXmlRPCHandler("MoveMoney", MoveMoneyHandler);                // added

                        MainServer.Instance.AddXmlRPCHandler("getCurrencyQuote", quote_func); // add php 2023
                        MainServer.Instance.AddXmlRPCHandler("preflightBuyLandPrep", preflightBuyLandPrep_func); // add php 2023
                        MainServer.Instance.AddXmlRPCHandler("buyLandPrep", landBuy_func); // add php 2023
                    }
                }

                if (m_sceneList.ContainsKey(scene.RegionInfo.RegionHandle))
                {
                    m_sceneList[scene.RegionInfo.RegionHandle] = scene;
                }
                else
                {
                    m_sceneList.Add(scene.RegionInfo.RegionHandle, scene);
                }
            }

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;
            scene.EventManager.OnMakeChildAgent += MakeChildAgent;

            // for OpenSim
            scene.EventManager.OnMoneyTransfer += MoneyTransferAction;
            scene.EventManager.OnValidateLandBuy += ValidateLandBuy;
            scene.EventManager.OnLandBuy += processLandBuy;

        }


        /// <summary>
        /// This is called whenever a <see cref="T:OpenSim.Region.Framework.Scenes.Scene" /> is removed. For shared modules, this can happen several times.
        /// For non-shared modules, this happens exactly once, if the scene this instance is associated with is removed.
        /// </summary>
        /// <param name="scene">A <see cref="T:OpenSim.Region.Framework.Scenes.Scene" /></param>
        public void RemoveRegion(Scene scene)
        {
            if (scene == null) return;

            lock (m_sceneList)
            {
                scene.EventManager.OnNewClient -= OnNewClient;
                scene.EventManager.OnMakeRootAgent -= OnMakeRootAgent;
                scene.EventManager.OnMakeChildAgent -= MakeChildAgent;

                // for OpenSim
                scene.EventManager.OnMoneyTransfer -= MoneyTransferAction;
                scene.EventManager.OnValidateLandBuy -= ValidateLandBuy;
                scene.EventManager.OnLandBuy -= processLandBuy;
            }
        }


        /// <summary>
        /// This will be called once for every scene loaded. In a shared module
        /// this will be multiple times in one instance, while a nonshared
        /// module instance will only be called once.
        /// This method is called after AddRegion has been called in all
        /// modules for that scene, providing an opportunity to request
        /// another module's interface, or hook an event from another module.
        /// </summary>
        /// <param name="scene">A <see cref="T:OpenSim.Region.Framework.Scenes.Scene" /></param>
        public void RegionLoaded(Scene scene)
        {
            m_log.InfoFormat("[MONEY MODULE] region loaded {0}", scene.RegionInfo.RegionID.ToString());
        }


        /// <summary>
        /// If this returns non-null, it is the type of an interface that
        /// this module intends to register.
        /// This will cause the loader to defer loading of this module
        /// until all other modules have been loaded. If no other module
        /// has registered the interface by then, this module will be
        /// activated, else it will remain inactive, letting the other module
        /// take over. This should return non-null ONLY in modules that are
        /// intended to be easily replaceable, e.g. stub implementations
        /// that the developer expects to be replaced by third party provided
        /// modules.
        /// </summary>
        public Type ReplaceableInterface
        {
            get { return null; }
        }


        /// <summary>Gets a value indicating whether this instance is shared module.</summary>
        /// <value>
        ///   <c>true</c> if this instance is shared module; otherwise, <c>false</c>.</value>
        public bool IsSharedModule
        {
            get { return true; }
        }


        /// <summary>
        ///   <br />
        /// </summary>
        /// <value>The name of the module</value>
        public string Name
        {
            get { return "DTLNSLMoneyModule"; }
        }


        /// <summary>
        /// This is called exactly once after all the shared region-modules have been instanciated and
        /// <see cref="M:OpenSim.Region.Framework.Interfaces.IRegionModuleBase.Initialise(Nini.Config.IConfigSource)" />d.
        /// </summary>
        public void PostInitialise()
        {

        }


        /// <summary>
        /// This is the inverse to <see cref="M:OpenSim.Region.Framework.Interfaces.IRegionModuleBase.Initialise(Nini.Config.IConfigSource)" />. After a Close(), this instance won't be usable anymore.
        /// </summary>
        public void Close()
        {

        }

        /// <summary>Objects the give money.</summary>
        /// <param name="objectID">The object identifier.</param>
        /// <param name="fromID">From identifier.</param>
        /// <param name="toID">To identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="txn">The TXN.</param>
        /// <param name="result">The result.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount, UUID txn, out string result)
        {
            result = string.Empty;
            if (!m_sellEnabled)
            {
                result = "LINDENDOLLAR_INSUFFICIENTFUNDS";
                return false;
            }

            string objName = string.Empty;
            string avatarName = string.Empty;

            SceneObjectPart sceneObj = GetLocatePrim(objectID);
            if (sceneObj == null)
            {
                result = "LINDENDOLLAR_INSUFFICIENTFUNDS";
                return false;
            }
            objName = sceneObj.Name;

            Scene scene = GetLocateScene(toID);
            if (scene != null)
            {
                UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, toID);
                if (account != null)
                {
                    avatarName = account.FirstName + " " + account.LastName;
                }
            }

            bool ret = false;
            string description = String.Format("Object {0} pays {1}", objName, avatarName);

            if (sceneObj.OwnerID == fromID)
            {
                ulong regionHandle = sceneObj.RegionHandle;
                UUID regionUUID = sceneObj.RegionID;
                if (GetLocateClient(fromID) != null)
                {
                    ret = TransferMoney(fromID, toID, amount, (int)TransactionType.ObjectPays, objectID, regionHandle, regionUUID, description);
                }
                else
                {
                    ret = ForceTransferMoney(fromID, toID, amount, (int)TransactionType.ObjectPays, objectID, regionHandle, regionUUID, description);
                }
            }

            if (!ret) result = "LINDENDOLLAR_INSUFFICIENTFUNDS";
            return ret;
        }


        //
        /// <summary>Gets the upload charge.</summary>
        /// <value>The upload charge.</value>
        public int UploadCharge
        {
            get { return PriceUpload; }
        }


        //
        /// <summary>Gets the group creation charge.</summary>
        /// <value>The group creation charge.</value>
        public int GroupCreationCharge
        {
            get { return PriceGroupCreate; }
        }


        /// <summary>Gets the balance.</summary>
        /// <param name="agentID">The agent identifier.</param>
        public int GetBalance(UUID agentID)
        {
            IClientAPI client = GetLocateClient(agentID);
            return QueryBalanceFromMoneyServer(client);
        }


        /// <summary>Uploads the covered.</summary>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="amount">The amount.</param>
        public bool UploadCovered(UUID agentID, int amount)
        {
            IClientAPI client = GetLocateClient(agentID);

            if (m_enable_server || string.IsNullOrEmpty(m_moneyServURL))
            {
                int balance = QueryBalanceFromMoneyServer(client);
                if (balance >= amount) return true;
            }
            return false;
        }


        /// <summary>Amounts the covered.</summary>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="amount">The amount.</param>
        public bool AmountCovered(UUID agentID, int amount)
        {
            IClientAPI client = GetLocateClient(agentID);

            if (m_enable_server || string.IsNullOrEmpty(m_moneyServURL))
            {
                int balance = QueryBalanceFromMoneyServer(client);
                if (balance >= amount) return true;
            }
            return false;
        }


        /// <summary>Applies the upload charge.</summary>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="text">The text.</param>
        public void ApplyUploadCharge(UUID agentID, int amount, string text)
        {
            ulong regionHandle = GetLocateScene(agentID).RegionInfo.RegionHandle;
            UUID regionUUID = GetLocateScene(agentID).RegionInfo.RegionID;
            PayMoneyCharge(agentID, amount, (int)TransactionType.UploadCharge, regionHandle, regionUUID, text);
        }


        /// <summary>Applies the charge.</summary>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="type">The type.</param>
        public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type)
        {
            ApplyCharge(agentID, amount, type, string.Empty);
        }


        /// <summary>Applies the charge.</summary>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="type">The type.</param>
        /// <param name="text">The text.</param>
        public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type, string text)
        {
            ulong regionHandle = GetLocateScene(agentID).RegionInfo.RegionHandle;
            UUID regionUUID = GetLocateScene(agentID).RegionInfo.RegionID;
            PayMoneyCharge(agentID, amount, (int)type, regionHandle, regionUUID, text);
        }


        /// <summary>Transfers the specified from identifier.</summary>
        /// <param name="fromID">From identifier.</param>
        /// <param name="toID">To identifier.</param>
        /// <param name="regionHandle">The region handle.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="type">The type.</param>
        /// <param name="text">The text.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public bool Transfer(UUID fromID, UUID toID, int regionHandle, int amount, MoneyTransactionType type, string text)
        {
            return TransferMoney(fromID, toID, amount, (int)type, UUID.Zero, (ulong)regionHandle, UUID.Zero, text);
        }


        /// <summary>Transfers the specified from identifier.</summary>
        /// <param name="fromID">From identifier.</param>
        /// <param name="toID">To identifier.</param>
        /// <param name="objectID">The object identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="type">The type.</param>
        /// <param name="text">The text.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public bool Transfer(UUID fromID, UUID toID, UUID objectID, int amount, MoneyTransactionType type, string text)
        {
            SceneObjectPart sceneObj = GetLocatePrim(objectID);
            if (sceneObj == null) return false;

            ulong regionHandle = sceneObj.ParentGroup.Scene.RegionInfo.RegionHandle;
            UUID regionUUID = sceneObj.ParentGroup.Scene.RegionInfo.RegionID;
            return TransferMoney(fromID, toID, amount, (int)type, objectID, (ulong)regionHandle, regionUUID, text);
        }


        // for 0.8.3 over
        /// <summary>Moves the money.</summary>
        /// <param name="fromAgentID">From agent identifier.</param>
        /// <param name="toAgentID">To agent identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="text">The text.</param>
        public void MoveMoney(UUID fromAgentID, UUID toAgentID, int amount, string text)
        {
            ForceTransferMoney(fromAgentID, toAgentID, amount, (int)TransactionType.MoveMoney, UUID.Zero, (ulong)0, UUID.Zero, text);
        }

        // for 0.9.1 over
        /// <summary>Moves the money.</summary>
        /// <param name="fromAgentID">From agent identifier.</param>
        /// <param name="toAgentID">To agent identifier.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="type">The type.</param>
        /// <param name="text">The text.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public bool MoveMoney(UUID fromAgentID, UUID toAgentID, int amount, MoneyTransactionType type, string text)
        {
            bool ret = ForceTransferMoney(fromAgentID, toAgentID, amount, (int)type, UUID.Zero, (ulong)0, UUID.Zero, text);
            return ret;
        }



        /// <summary>Called when [new client].</summary>
        /// <param name="client">The client.</param>
        private void OnNewClient(IClientAPI client)
        {
            client.OnEconomyDataRequest += OnEconomyDataRequest;
            client.OnLogout += ClientClosed;

            client.OnMoneyBalanceRequest += OnMoneyBalanceRequest;
            client.OnRequestPayPrice += OnRequestPayPrice;
            client.OnObjectBuy += OnObjectBuy;
        }


        /// <summary>Called when [make root agent].</summary>
        /// <param name="agent">The agent.</param>
        public void OnMakeRootAgent(ScenePresence agent)
        {
            int balance = 0;
            IClientAPI client = agent.ControllingClient;

            m_enable_server = LoginMoneyServer(agent, out balance);
            client.SendMoneyBalance(UUID.Zero, true, new byte[0], balance, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);

        }


        // for OnClientClosed event
        /// <summary>Clients the closed.</summary>
        /// <param name="client">The client.</param>
        private void ClientClosed(IClientAPI client)
        {
            if (m_enable_server && client != null)
            {
                LogoffMoneyServer(client);
            }
        }


        // for OnMakeChildAgent event
        /// <summary>Makes the child agent.</summary>
        /// <param name="avatar">The avatar.</param>
        private void MakeChildAgent(ScenePresence avatar)
        {
        }


        // for OnMoneyTransfer event 
        /// <summary>Moneys the transfer action.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="moneyEvent">The money event.</param>
        private void MoneyTransferAction(Object sender, EventManager.MoneyTransferArgs moneyEvent)
        {
            if (!m_sellEnabled) return;

            // Check the money transaction is necessary.   
            if (moneyEvent.sender == moneyEvent.receiver)
            {
                return;
            }

            UUID receiver = moneyEvent.receiver;
            // Pay for the object.   
            if (moneyEvent.transactiontype == (int)TransactionType.PayObject)
            {
                SceneObjectPart sceneObj = GetLocatePrim(moneyEvent.receiver);
                if (sceneObj != null)
                {
                    receiver = sceneObj.OwnerID;
                }
                else
                {
                    return;
                }
            }

            // Before paying for the object, save the object local ID for current transaction.
            UUID objectID = UUID.Zero;
            ulong regionHandle = 0;
            UUID regionUUID = UUID.Zero;

            if (sender is Scene)
            {
                Scene scene = (Scene)sender;
                regionHandle = scene.RegionInfo.RegionHandle;
                regionUUID = scene.RegionInfo.RegionID;

                if (moneyEvent.transactiontype == (int)TransactionType.PayObject)
                {
                    objectID = scene.GetSceneObjectPart(moneyEvent.receiver).UUID;
                }
            }

            TransferMoney(moneyEvent.sender, receiver, moneyEvent.amount, moneyEvent.transactiontype, objectID, regionHandle, regionUUID, "OnMoneyTransfer event");
            return;
        }


        // for OnValidateLandBuy event
        /// <summary>Validates the land buy.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="landBuyEvent">The land buy event.</param>
        private void ValidateLandBuy(Object sender, EventManager.LandBuyArgs landBuyEvent)
        {
            IClientAPI senderClient = GetLocateClient(landBuyEvent.agentId);
            if (senderClient != null)
            {
                int balance = QueryBalanceFromMoneyServer(senderClient);
                if (balance >= landBuyEvent.parcelPrice)
                {
                    lock (landBuyEvent)
                    {
                        landBuyEvent.economyValidated = true;
                    }
                }
            }
            return;
        }


        // for LandBuy even
        /// <summary>Processes the land buy.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="landBuyEvent">The land buy event.</param>
        private void processLandBuy(Object sender, EventManager.LandBuyArgs landBuyEvent)
        {
            if (!m_sellEnabled) return;

            lock (landBuyEvent)
            {
                if (landBuyEvent.economyValidated == true && landBuyEvent.transactionID == 0)
                {
                    landBuyEvent.transactionID = Util.UnixTimeSinceEpoch();

                    ulong parcelID = (ulong)landBuyEvent.parcelLocalID;
                    UUID regionUUID = UUID.Zero;
                    if (sender is Scene) regionUUID = ((Scene)sender).RegionInfo.RegionID;

                    if (TransferMoney(landBuyEvent.agentId, landBuyEvent.parcelOwnerID,
                                      landBuyEvent.parcelPrice, (int)TransactionType.LandSale, regionUUID, parcelID, regionUUID, "Land Purchase"))
                    {
                        landBuyEvent.amountDebited = landBuyEvent.parcelPrice;
                    }
                }
            }
            return;
        }


        // for OnObjectBuy event
        /// <summary>Called when [object buy].</summary>
        /// <param name="remoteClient">The remote client.</param>
        /// <param name="agentID">The agent identifier.</param>
        /// <param name="sessionID">The session identifier.</param>
        /// <param name="groupID">The group identifier.</param>
        /// <param name="categoryID">The category identifier.</param>
        /// <param name="localID">The local identifier.</param>
        /// <param name="saleType">Type of the sale.</param>
        /// <param name="salePrice">The sale price.</param>
        public void OnObjectBuy(IClientAPI remoteClient, UUID agentID, UUID sessionID,
                                UUID groupID, UUID categoryID, uint localID, byte saleType, int salePrice)
        {
            m_log.InfoFormat("[MONEY MODULE]: OnObjectBuy: agent = {0}, {1}", agentID, remoteClient.AgentId);

            // Handle the parameters error.   
            if (!m_sellEnabled) return;
            if (remoteClient == null || salePrice < 0) return;

            // Get the balance from money server.   
            int balance = QueryBalanceFromMoneyServer(remoteClient);
            if (balance < salePrice)
            {
                remoteClient.SendAgentAlertMessage("Unable to buy now. You don't have sufficient funds", false);
                return;
            }

            Scene scene = GetLocateScene(remoteClient.AgentId);
            if (scene != null)
            {
                SceneObjectPart sceneObj = scene.GetSceneObjectPart(localID);
                if (sceneObj != null)
                {
                    IBuySellModule mod = scene.RequestModuleInterface<IBuySellModule>();
                    if (mod != null)
                    {
                        UUID receiverId = sceneObj.OwnerID;
                        ulong regionHandle = sceneObj.RegionHandle;
                        UUID regionUUID = sceneObj.RegionID;
                        bool ret = false;
                        //
                        if (salePrice >= 0)
                        {
                            if (!string.IsNullOrEmpty(m_moneyServURL))
                            {
                                ret = TransferMoney(remoteClient.AgentId, receiverId, salePrice,
                                                (int)TransactionType.PayObject, sceneObj.UUID, regionHandle, regionUUID, "Object Buy");
                            }
                            else if (salePrice == 0)
                            {    // amount is 0 with No Money Server
                                ret = true;
                            }
                        }
                        if (ret)
                        {
                            mod.BuyObject(remoteClient, categoryID, localID, saleType, salePrice);
                        }
                    }
                }
                else
                {
                    remoteClient.SendAgentAlertMessage("Unable to buy now. The object was not found", false);
                    return;
                }
            }
            return;
        }


        /// <summary>   
        /// Sends the the stored money balance to the client   
        /// </summary>   
        /// <param name="client"></param>   
        /// <param name="agentID"></param>   
        /// <param name="SessionID"></param>   
        /// <param name="TransactionID"></param>   
        private void OnMoneyBalanceRequest(IClientAPI client, UUID agentID, UUID SessionID, UUID TransactionID)
        {
            m_log.InfoFormat("[MONEY MODULE]: OnMoneyBalanceRequest:");

            if (client.AgentId == agentID && client.SessionId == SessionID)
            {
                int balance = 0;
                //
                if (m_enable_server)
                {
                    balance = QueryBalanceFromMoneyServer(client);
                }

                client.SendMoneyBalance(TransactionID, true, new byte[0], balance, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
            }
            else
            {
                client.SendAlertMessage("Unable to send your money balance");
            }
        }


        /// <summary>Called when [request pay price].</summary>
        /// <param name="client">The client.</param>
        /// <param name="objectID">The object identifier.</param>
        private void OnRequestPayPrice(IClientAPI client, UUID objectID)
        {
            m_log.InfoFormat("[MONEY MODULE]: OnRequestPayPrice:");

            Scene scene = GetLocateScene(client.AgentId);
            if (scene == null) return;
            SceneObjectPart sceneObj = scene.GetSceneObjectPart(objectID);
            if (sceneObj == null) return;
            SceneObjectGroup group = sceneObj.ParentGroup;
            SceneObjectPart root = group.RootPart;

            client.SendPayPrice(objectID, root.PayPrice);
        }


        //
        //private void OnEconomyDataRequest(UUID agentId)
        /// <summary>Called when [economy data request].</summary>
        /// <param name="user">The user.</param>
        private void OnEconomyDataRequest(IClientAPI user)
        {
            if (user != null)
            {
                if (m_enable_server || string.IsNullOrEmpty(m_moneyServURL))
                {
                    //Scene s = GetLocateScene(user.AgentId);
                    Scene s = (Scene)user.Scene;
                    user.SendEconomyData(EnergyEfficiency, s.RegionInfo.ObjectCapacity, ObjectCount, PriceEnergyUnit, PriceGroupCreate,
                                     PriceObjectClaim, PriceObjectRent, PriceObjectScaleFactor, PriceParcelClaim, PriceParcelClaimFactor,
                                     PriceParcelRent, PricePublicObjectDecay, PricePublicObjectDelete, PriceRentLight, PriceUpload,
                                     TeleportMinPrice, TeleportPriceExponent);
                }
            }
        }



        // "OnMoneyTransfered" RPC from MoneyServer
        /// <summary>Called when [money transfered handler].</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse OnMoneyTransferedHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY MODULE]: OnMoneyTransferedHandler:");

            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if (requestParam.Contains("clientUUID") && requestParam.Contains("clientSessionID") && requestParam.Contains("clientSecureSessionID"))
                {
                    UUID clientUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);

                    if (clientUUID != UUID.Zero)
                    {
                        IClientAPI client = GetLocateClient(clientUUID);
                        string sessionid = (string)requestParam["clientSessionID"];
                        string secureid = (string)requestParam["clientSecureSessionID"];
                        if (client != null && secureid == client.SecureSessionId.ToString() && (sessionid == UUID.Zero.ToString() || sessionid == client.SessionId.ToString()))
                        {
                            if (requestParam.Contains("transactionType") && requestParam.Contains("objectID") && requestParam.Contains("amount"))
                            {
                                m_log.InfoFormat("[MONEY MODULE]: OnMoneyTransferedHandler: type = {0}", requestParam["transactionType"]);

                                // Pay for the object.
                                if ((int)requestParam["transactionType"] == (int)TransactionType.PayObject)
                                {
                                    // Send notify to the client(viewer) for Money Event Trigger.   
                                    ObjectPaid handlerOnObjectPaid = OnObjectPaid;
                                    if (handlerOnObjectPaid != null)
                                    {
                                        UUID objectID = UUID.Zero;
                                        UUID.TryParse((string)requestParam["objectID"], out objectID);
                                        handlerOnObjectPaid(objectID, clientUUID, (int)requestParam["amount"]); // call Script Engine for LSL money()
                                    }
                                    ret = true;
                                }
                            }
                        }
                    }
                }
            }

            // Send the response to money server.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;

            if (!ret)
            {
                m_log.ErrorFormat("[MONEY MODULE]: OnMoneyTransferedHandler: Transaction is failed. MoneyServer will rollback");
            }
            resp.Value = paramTable;

            return resp;
        }


        // "UpdateBalance" RPC from MoneyServer or Script
        /// <summary>Balances the update handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse BalanceUpdateHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {

            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if (requestParam.Contains("clientUUID") && requestParam.Contains("clientSessionID") && requestParam.Contains("clientSecureSessionID"))
                {
                    UUID clientUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
                    //
                    if (clientUUID != UUID.Zero)
                    {
                        IClientAPI client = GetLocateClient(clientUUID);
                        string sessionid = (string)requestParam["clientSessionID"];
                        string secureid = (string)requestParam["clientSecureSessionID"];
                        if (client != null && secureid == client.SecureSessionId.ToString() && (sessionid == UUID.Zero.ToString() || sessionid == client.SessionId.ToString()))
                        {
                            //
                            if (requestParam.Contains("Balance"))
                            {
                                // Send notify to the client.   
                                string msg = "";
                                if (requestParam.Contains("Message")) msg = (string)requestParam["Message"];
                                client.SendMoneyBalance(UUID.Random(), true, Utils.StringToBytes(msg), (int)requestParam["Balance"],
                                                                                    0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
                                // Dialog
                                if (msg != "")
                                {
                                    Scene scene = (Scene)client.Scene;
                                    IDialogModule dlg = scene.RequestModuleInterface<IDialogModule>();
                                    dlg.SendAlertToUser(client.AgentId, msg);
                                }
                                ret = true;
                            }
                        }
                    }
                }
            }


            // Send the response to money server.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;

            if (!ret)
            {
                m_log.ErrorFormat("[MONEY MODULE]: BalanceUpdateHandler: Cannot update client balance from MoneyServer");
            }
            resp.Value = paramTable;

            return resp;
        }


        // "UserAlert" RPC from Script
        /// <summary>Users the alert handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse UserAlertHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY MODULE]: UserAlertHandler:");

            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if (requestParam.Contains("clientUUID") && requestParam.Contains("clientSessionID") && requestParam.Contains("clientSecureSessionID"))
                {
                    UUID clientUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
                    //
                    if (clientUUID != UUID.Zero)
                    {
                        IClientAPI client = GetLocateClient(clientUUID);
                        string sessionid = (string)requestParam["clientSessionID"];
                        string secureid = (string)requestParam["clientSecureSessionID"];
                        if (client != null && secureid == client.SecureSessionId.ToString() && (sessionid == UUID.Zero.ToString() || sessionid == client.SessionId.ToString()))
                        {
                            if (requestParam.Contains("Description"))
                            {
                                string description = (string)requestParam["Description"];
                                // Show the notice dialog with money server message.
                                GridInstantMessage gridMsg = new GridInstantMessage(null, UUID.Zero, "MonyServer", new UUID(clientUUID.ToString()),
                                                                    (byte)InstantMessageDialog.MessageFromAgent, description, false, new Vector3());
                                client.SendInstantMessage(gridMsg);
                                ret = true;
                            }
                        }
                    }
                }
            }

            // Send the response to money server.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;

            resp.Value = paramTable;
            return resp;
        }


        // "GetBalance" RPC from Script
        /// <summary>Gets the balance handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse GetBalanceHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {

            bool ret = false;
            int balance = -1;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if (requestParam.Contains("clientUUID") && requestParam.Contains("clientSessionID") && requestParam.Contains("clientSecureSessionID"))
                {
                    UUID clientUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
                    //
                    if (clientUUID != UUID.Zero)
                    {
                        IClientAPI client = GetLocateClient(clientUUID);
                        string sessionid = (string)requestParam["clientSessionID"];
                        string secureid = (string)requestParam["clientSecureSessionID"];
                        if (client != null && secureid == client.SecureSessionId.ToString() && (sessionid == UUID.Zero.ToString() || sessionid == client.SessionId.ToString()))
                        {
                            balance = QueryBalanceFromMoneyServer(client);
                        }
                    }
                }
            }

            // Send the response to caller.
            if (balance < 0)
            {
                m_log.ErrorFormat("[MONEY MODULE]: GetBalanceHandler: GetBalance transaction is failed");
                ret = false;
            }

            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;
            paramTable["balance"] = balance;
            resp.Value = paramTable;

            return resp;
        }


        // "AddBankerMoney" RPC from Script
        /// <summary>Adds the banker money handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse AddBankerMoneyHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            m_log.InfoFormat("[MONEY MODULE]: AddBankerMoneyHandler:");

            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];

                if (requestParam.Contains("clientUUID") && requestParam.Contains("clientSessionID") && requestParam.Contains("clientSecureSessionID"))
                {
                    UUID bankerUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["clientUUID"], out bankerUUID);
                    //
                    if (bankerUUID != UUID.Zero)
                    {
                        IClientAPI client = GetLocateClient(bankerUUID);
                        string sessionid = (string)requestParam["clientSessionID"];
                        string secureid = (string)requestParam["clientSecureSessionID"];
                        if (client != null && secureid == client.SecureSessionId.ToString() && (sessionid == UUID.Zero.ToString() || sessionid == client.SessionId.ToString()))
                        {
                            if (requestParam.Contains("amount"))
                            {
                                Scene scene = (Scene)client.Scene;
                                int amount = (int)requestParam["amount"];
                                ulong regionHandle = scene.RegionInfo.RegionHandle;
                                UUID regionUUID = scene.RegionInfo.RegionID;
                                ret = AddBankerMoney(bankerUUID, amount, regionHandle, regionUUID);

                                if (m_use_web_settle && m_settle_user)
                                {
                                    ret = true;
                                    IDialogModule dlg = scene.RequestModuleInterface<IDialogModule>();
                                    if (dlg != null)
                                    {
                                        dlg.SendUrlToUser(bankerUUID, "SYSTEM", UUID.Zero, UUID.Zero, false, m_settle_message, m_settle_url);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!ret) m_log.ErrorFormat("[MONEY MODULE]: AddBankerMoneyHandler: Add Banker Money transaction is failed");

            // Send the response to caller.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["settle"] = false;
            paramTable["success"] = ret;

            if (m_use_web_settle && m_settle_user) paramTable["settle"] = true;
            resp.Value = paramTable;

            return resp;
        }


        // "SendMoney" RPC from Script
        /// <summary>Sends the money handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse SendMoneyHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if (requestParam.Contains("agentUUID") && requestParam.Contains("secretAccessCode"))
                {
                    UUID agentUUID = UUID.Zero;
                    UUID.TryParse((string)requestParam["agentUUID"], out agentUUID);

                    if (agentUUID != UUID.Zero)
                    {
                        if (requestParam.Contains("amount"))
                        {
                            int amount = (int)requestParam["amount"];
                            int type = -1;
                            if (requestParam.Contains("type")) type = (int)requestParam["type"];
                            string secretCode = (string)requestParam["secretAccessCode"];
                            string scriptIP = remoteClient.Address.ToString();

                            MD5 md5 = MD5.Create();
                            byte[] code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(secretCode + "_" + scriptIP));
                            string hash = BitConverter.ToString(code).ToLower().Replace("-", "");
                            //m_log.InfoFormat("[MONEY MODULE]: SendMoneyHandler: SecretCode: {0} + {1} = {2}", secretCode, scriptIP, hash);
                            ret = SendMoneyTo(agentUUID, amount, type, hash);
                        }
                    }
                    else
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: SendMoneyHandler: amount is missed");
                    }
                }
                else
                {
                    if (!requestParam.Contains("agentUUID"))
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: SendMoneyHandler: agentUUID is missed");
                    }
                    if (!requestParam.Contains("secretAccessCode"))
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: SendMoneyHandler: secretAccessCode is missed");
                    }
                }
            }
            else
            {
                m_log.ErrorFormat("[MONEY MODULE]: SendMoneyHandler: Params count is under 0");
            }

            if (!ret) m_log.ErrorFormat("[MONEY MODULE]: SendMoneyHandler: Send Money transaction is failed");

            // Send the response to caller.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;

            resp.Value = paramTable;

            return resp;
        }


        // "MoveMoney" RPC from Script
        /// <summary>Moves the money handler.</summary>
        /// <param name="request">The request.</param>
        /// <param name="remoteClient">The remote client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        public XmlRpcResponse MoveMoneyHandler(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            bool ret = false;

            if (request.Params.Count > 0)
            {
                Hashtable requestParam = (Hashtable)request.Params[0];
                if ((requestParam.Contains("fromUUID") || requestParam.Contains("toUUID")) && requestParam.Contains("secretAccessCode"))
                {
                    UUID fromUUID = UUID.Zero;
                    UUID toUUID = UUID.Zero;  // UUID.Zero means System
                    if (requestParam.Contains("fromUUID")) UUID.TryParse((string)requestParam["fromUUID"], out fromUUID);
                    if (requestParam.Contains("toUUID")) UUID.TryParse((string)requestParam["toUUID"], out toUUID);

                    if (requestParam.Contains("amount"))
                    {
                        int amount = (int)requestParam["amount"];
                        string secretCode = (string)requestParam["secretAccessCode"];
                        string scriptIP = remoteClient.Address.ToString();

                        MD5 md5 = MD5.Create();
                        byte[] code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(secretCode + "_" + scriptIP));
                        string hash = BitConverter.ToString(code).ToLower().Replace("-", "");
                        //m_log.InfoFormat("[MONEY MODULE]: MoveMoneyHandler: SecretCode: {0} + {1} = {2}", secretCode, scriptIP, hash);
                        ret = MoveMoneyFromTo(fromUUID, toUUID, amount, hash);
                    }
                    else
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyHandler: amount is missed");
                    }
                }
                else
                {
                    if (!requestParam.Contains("fromUUID") && !requestParam.Contains("toUUID"))
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyHandler: fromUUID and toUUID are missed");
                    }
                    if (!requestParam.Contains("secretAccessCode"))
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyHandler: secretAccessCode is missed");
                    }
                }
            }
            else
            {
                m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyHandler: Params count is under 0");
            }

            if (!ret) m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyHandler: Move Money transaction is failed");

            // Send the response to caller.
            XmlRpcResponse resp = new XmlRpcResponse();
            Hashtable paramTable = new Hashtable();
            paramTable["success"] = ret;

            resp.Value = paramTable;

            return resp;
        }


        /// <summary>   
        /// Transfer the money from one user to another. Need to notify money server to update.   
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.   
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool TransferMoney(UUID sender, UUID receiver, int amount, int type, UUID objectID, ulong regionHandle, UUID regionUUID, string description)
        {
            bool ret = false;
            IClientAPI senderClient = GetLocateClient(sender);

            // Handle the illegal transaction.   
            // receiverClient could be null.
            if (senderClient == null)
            {
                m_log.InfoFormat("[MONEY MODULE]: TransferMoney: Client {0} not found", sender.ToString());
                return false;
            }

            if (QueryBalanceFromMoneyServer(senderClient) < amount)
            {
                m_log.InfoFormat("[MONEY MODULE]: TransferMoney: No insufficient balance in client [{0}]", sender.ToString());
                return false;
            }

            if (m_enable_server)
            {
                string objName = string.Empty;
                SceneObjectPart sceneObj = GetLocatePrim(objectID);
                if (sceneObj != null) objName = sceneObj.Name;

                // Fill parameters for money transfer XML-RPC.   
                Hashtable paramTable = new Hashtable();
                paramTable["senderID"] = sender.ToString();
                paramTable["receiverID"] = receiver.ToString();
                paramTable["senderSessionID"] = senderClient.SessionId.ToString();
                paramTable["senderSecureSessionID"] = senderClient.SecureSessionId.ToString();
                paramTable["transactionType"] = type;
                paramTable["objectID"] = objectID.ToString();
                paramTable["objectName"] = objName;
                paramTable["regionHandle"] = regionHandle.ToString();
                paramTable["regionUUID"] = regionUUID.ToString();
                paramTable["amount"] = amount;
                paramTable["description"] = description;

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "TransferMoney");

                // Handle the return values from Money Server.  
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                }
                else m_log.ErrorFormat("[MONEY MODULE]: TransferMoney: Can not money transfer request from [{0}] to [{1}]", sender.ToString(), receiver.ToString());
            }

            return ret;
        }


        /// <summary>   
        /// Force transfer the money from one user to another. 
        /// This function does not check sender login.
        /// Need to notify money server to update.   
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.   
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool ForceTransferMoney(UUID sender, UUID receiver, int amount, int type, UUID objectID, ulong regionHandle, UUID regionUUID, string description)
        {
            //m_log.InfoFormat("[MONEY MODULE]: ForceTransferMoney:");

            bool ret = false;

            if (m_enable_server)
            {
                string objName = string.Empty;
                SceneObjectPart sceneObj = GetLocatePrim(objectID);
                if (sceneObj != null) objName = sceneObj.Name;

                // Fill parameters for money transfer XML-RPC.   
                Hashtable paramTable = new Hashtable();
                paramTable["senderID"] = sender.ToString();
                paramTable["receiverID"] = receiver.ToString();
                paramTable["transactionType"] = type;
                paramTable["objectID"] = objectID.ToString();
                paramTable["objectName"] = objName;
                paramTable["regionHandle"] = regionHandle.ToString();
                paramTable["regionUUID"] = regionUUID.ToString();
                paramTable["amount"] = amount;
                paramTable["description"] = description;

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ForceTransferMoney");

                // Handle the return values from Money Server.  
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                }
                else m_log.ErrorFormat("[MONEY MODULE]: ForceTransferMoney: Can not money force transfer request from [{0}] to [{1}]", sender.ToString(), receiver.ToString());
            }

            return ret;
        }


        /// <summary>   
        /// Send the money to avatar. Need to notify money server to update.   
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.  
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool SendMoneyTo(UUID avatarID, int amount, int type, string secretCode)
        {
            bool ret = false;

            if (m_enable_server)
            {
                // Fill parameters for money transfer XML-RPC.   
                if (type < 0) type = (int)TransactionType.ReferBonus;
                Hashtable paramTable = new Hashtable();
                paramTable["receiverID"] = avatarID.ToString();
                paramTable["transactionType"] = type;
                paramTable["amount"] = amount;
                paramTable["secretAccessCode"] = secretCode;
                paramTable["description"] = "Bonus to Avatar";

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "SendMoney");

                // Handle the return values from Money Server.  
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                    else m_log.ErrorFormat("[MONEY MODULE]: SendMoneyTo: Fail Message is {0}", resultTable["message"]);
                }
                else m_log.ErrorFormat("[MONEY MODULE]: SendMoneyTo: Money Server is not responce");
            }

            return ret;
        }


        /// <summary>   
        /// Move the money from avatar to other avatar. Need to notify money server to update.   
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.  
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool MoveMoneyFromTo(UUID senderID, UUID receiverID, int amount, string secretCode)
        {
            bool ret = false;

            if (m_enable_server)
            {
                // Fill parameters for money transfer XML-RPC.   
                Hashtable paramTable = new Hashtable();
                paramTable["senderID"] = senderID.ToString();
                paramTable["receiverID"] = receiverID.ToString();
                paramTable["transactionType"] = (int)TransactionType.MoveMoney;
                paramTable["amount"] = amount;
                paramTable["secretAccessCode"] = secretCode;
                paramTable["description"] = "Move Money";

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "MoveMoney");

                // Handle the return values from Money Server.  
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                    else m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyFromTo: Fail Message is {0}", resultTable["message"]);
                }
                else m_log.ErrorFormat("[MONEY MODULE]: MoveMoneyFromTo: Money Server is not responce");
            }

            return ret;
        }


        /// <summary>   
        /// Add the money to banker avatar. Need to notify money server to update.   
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.  
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool AddBankerMoney(UUID bankerID, int amount, ulong regionHandle, UUID regionUUID)
        {
            bool ret = false;
            m_settle_user = false;

            if (m_enable_server)
            {
                // Fill parameters for money transfer XML-RPC.   
                Hashtable paramTable = new Hashtable();
                paramTable["bankerID"] = bankerID.ToString();
                paramTable["transactionType"] = (int)TransactionType.BuyMoney;
                paramTable["amount"] = amount;
                paramTable["regionHandle"] = regionHandle.ToString();
                paramTable["regionUUID"] = regionUUID.ToString();
                paramTable["description"] = "Add Money to Avatar";

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "AddBankerMoney");

                // Handle the return values from Money Server.  
                if (resultTable != null)
                {
                    if (resultTable.Contains("success") && (bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                    else
                    {
                        if (resultTable.Contains("banker"))
                        {
                            m_settle_user = !(bool)resultTable["banker"]; // If avatar is not banker, Web Settlement is used.
                            if (m_settle_user && m_use_web_settle) m_log.ErrorFormat("[MONEY MODULE]: AddBankerMoney: Avatar is not Banker. Web Settlemrnt is used.");
                        }
                        else m_log.ErrorFormat("[MONEY MODULE]: AddBankerMoney: Fail Message {0}", resultTable["message"]);
                    }
                }
                else m_log.ErrorFormat("[MONEY MODULE]: AddBankerMoney: Money Server is not responce");
            }

            return ret;
        }


        /// <summary>   
        /// Pay the money of charge.
        /// </summary>   
        /// <param name="amount">   
        /// The amount of money.   
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool PayMoneyCharge(UUID sender, int amount, int type, ulong regionHandle, UUID regionUUID, string description)
        {
            bool ret = false;
            IClientAPI senderClient = GetLocateClient(sender);

            // Handle the illegal transaction.   
            // receiverClient could be null.
            if (senderClient == null)
            {
                m_log.InfoFormat("[MONEY MODULE]: PayMoneyCharge: Client {0} is not found", sender.ToString());
                return false;
            }

            if (QueryBalanceFromMoneyServer(senderClient) < amount)
            {
                m_log.InfoFormat("[MONEY MODULE]: PayMoneyCharge: No insufficient balance in client [{0}]", sender.ToString());
                return false;
            }

            if (m_enable_server)
            {
                // Fill parameters for money transfer XML-RPC.   
                Hashtable paramTable = new Hashtable();
                paramTable["senderID"] = sender.ToString();
                paramTable["senderSessionID"] = senderClient.SessionId.ToString();
                paramTable["senderSecureSessionID"] = senderClient.SecureSessionId.ToString();
                paramTable["transactionType"] = type;
                paramTable["amount"] = amount;
                paramTable["regionHandle"] = regionHandle.ToString();
                paramTable["regionUUID"] = regionUUID.ToString();
                paramTable["description"] = description;

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "PayMoneyCharge");

                // Handle the return values from Money Server.  
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                }
                else m_log.ErrorFormat("[MONEY MODULE]: PayMoneyCharge: Can not pay money of charge request from [{0}]", sender.ToString());
            }

            return ret;
        }


        /// <summary>Queries the balance from money server.</summary>
        /// <param name="client">The client.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        private int QueryBalanceFromMoneyServer(IClientAPI client)
        {
            //m_log.InfoFormat("[MONEY MODULE]: QueryBalanceFromMoneyServer:");

            int balance = 0;

            if (client != null)
            {
                if (m_enable_server)
                {
                    Hashtable paramTable = new Hashtable();
                    paramTable["clientUUID"] = client.AgentId.ToString();
                    paramTable["clientSessionID"] = client.SessionId.ToString();
                    paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

                    // Generate the request for transfer.   
                    Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "GetBalance");

                    // Handle the return result
                    if (resultTable != null && resultTable.Contains("success"))
                    {
                        if ((bool)resultTable["success"] == true)
                        {
                            balance = (int)resultTable["clientBalance"];
                        }
                    }
                }
                else
                {
                    if (m_moneyServer.ContainsKey(client.AgentId))
                    {
                        balance = m_moneyServer[client.AgentId];
                    }
                }
            }

            return balance;
        }


        /// <summary>   
        /// Login the money server when the new client login.
        /// </summary>   
        /// <param name="userID">   
        /// Indicate user ID of the new client.   
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool LoginMoneyServer(ScenePresence avatar, out int balance)
        {
            balance = 0;
            bool ret = false;
            bool isNpc = avatar.IsNPC;

            IClientAPI client = avatar.ControllingClient;

            if (!string.IsNullOrEmpty(m_moneyServURL))
            {
                Scene scene = (Scene)client.Scene;
                string userName = string.Empty;

                // Get the username for the login user.
                if (client.Scene is Scene)
                {
                    if (scene != null)
                    {
                        UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);
                        if (account != null)
                        {
                            userName = account.FirstName + " " + account.LastName;
                        }
                    }
                }


                // User Universal Identifer for Grid Avatar, HG Avatar or NPC
                string universalID = string.Empty;
                string firstName = string.Empty;
                string lastName = string.Empty;
                string serverURL = string.Empty;
                int avatarType = (int)AvatarType.LOCAL_AVATAR;
                int avatarClass = (int)AvatarType.LOCAL_AVATAR;

                AgentCircuitData agent = scene.AuthenticateHandler.GetAgentCircuitData(client.AgentId);

                if (agent != null)
                {
                    universalID = Util.ProduceUserUniversalIdentifier(agent);
                    if (!String.IsNullOrEmpty(universalID))
                    {
                        UUID uuid;
                        string tmp;
                        Util.ParseUniversalUserIdentifier(universalID, out uuid, out serverURL, out firstName, out lastName, out tmp);
                    }
                    // if serverURL is empty, avatar is a NPC
                    if (isNpc || String.IsNullOrEmpty(serverURL))
                    {
                        avatarType = (int)AvatarType.NPC_AVATAR;
                    }
                    //
                    if ((agent.teleportFlags & (uint)Constants.TeleportFlags.ViaHGLogin) != 0 || String.IsNullOrEmpty(userName))
                    {
                        avatarType = (int)AvatarType.HG_AVATAR;
                    }
                }
                if (String.IsNullOrEmpty(userName))
                {
                    userName = firstName + " " + lastName;
                }

                //
                avatarClass = avatarType;
                if (avatarType == (int)AvatarType.NPC_AVATAR) return false;
                if (avatarType == (int)AvatarType.HG_AVATAR) avatarClass = m_hg_avatarClass;

                //
                // Login the Money Server.   
                Hashtable paramTable = new Hashtable();
                paramTable["openSimServIP"] = scene.RegionInfo.ServerURI.Replace(scene.RegionInfo.InternalEndPoint.Port.ToString(),
                                                                                         scene.RegionInfo.HttpPort.ToString());
                paramTable["avatarType"] = avatarType.ToString();
                paramTable["avatarClass"] = avatarClass.ToString();
                paramTable["userName"] = userName;
                paramTable["universalID"] = universalID;
                paramTable["clientUUID"] = client.AgentId.ToString();
                paramTable["clientSessionID"] = client.SessionId.ToString();
                paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ClientLogin");

                // Handle the return result 
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        balance = (int)resultTable["clientBalance"];
                        m_log.InfoFormat("[MONEY MODULE]: LoginMoneyServer: Client [{0}] login Money Server {1}", client.AgentId.ToString(), m_moneyServURL);
                        ret = true;
                    }
                }
                else m_log.ErrorFormat("[MONEY MODULE]: LoginMoneyServer: Unable to login Money Server {0} for client [{1}]", m_moneyServURL, client.AgentId.ToString());
            }
            else m_log.ErrorFormat("[MONEY MODULE]: LoginMoneyServer: Money Server is not available!!");


            // Notifies the Viewer of the setting.
            if (ret || string.IsNullOrEmpty(m_moneyServURL))
            {
                OnEconomyDataRequest(client);
            }

            return ret;
        }


        /// <summary>   
        /// Log off from the money server.   
        /// </summary>   
        /// <param name="userID">   
        /// Indicate user ID of the new client.   
        /// </param>   
        /// <returns>   
        /// return true, if successfully.   
        /// </returns>   
        private bool LogoffMoneyServer(IClientAPI client)
        {
            bool ret = false;

            if (!string.IsNullOrEmpty(m_moneyServURL))
            {
                // Log off from the Money Server.   
                Hashtable paramTable = new Hashtable();
                paramTable["clientUUID"] = client.AgentId.ToString();
                paramTable["clientSessionID"] = client.SessionId.ToString();
                paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ClientLogout");
                // Handle the return result
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        ret = true;
                    }
                }
            }

            return ret;
        }


        //
        /// <summary>Gets the transaction information.</summary>
        /// <param name="client">The client.</param>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        private EventManager.MoneyTransferArgs GetTransactionInfo(IClientAPI client, string transactionID)
        {
            EventManager.MoneyTransferArgs args = null;

            if (m_enable_server)
            {
                Hashtable paramTable = new Hashtable();
                paramTable["clientUUID"] = client.AgentId.ToString();
                paramTable["clientSessionID"] = client.SessionId.ToString();
                paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();
                paramTable["transactionID"] = transactionID;

                // Generate the request for transfer.   
                Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "GetTransaction");

                // Handle the return result
                if (resultTable != null && resultTable.Contains("success"))
                {
                    if ((bool)resultTable["success"] == true)
                    {
                        int amount = (int)resultTable["amount"];
                        int type = (int)resultTable["type"];
                        string desc = (string)resultTable["description"];
                        UUID sender = UUID.Zero;
                        UUID recver = UUID.Zero;
                        UUID.TryParse((string)resultTable["sender"], out sender);
                        UUID.TryParse((string)resultTable["receiver"], out recver);
                        args = new EventManager.MoneyTransferArgs(sender, recver, amount, type, desc);
                    }
                    else
                    {
                        m_log.ErrorFormat("[MONEY MODULE]: GetTransactionInfo: GetTransactionInfo: Fail to Request. {0}", (string)resultTable["description"]);
                    }
                }
                else
                {
                    m_log.ErrorFormat("[MONEY MODULE]: GetTransactionInfo: Invalid Response");
                }
            }
            else
            {
                m_log.ErrorFormat("[MONEY MODULE]: GetTransactionInfo: Invalid Money Server URL");
            }

            return args;
        }


        /// <summary>   
        /// Generic XMLRPC client abstraction   
        /// </summary>   
        /// <param name="reqParams">Hashtable containing parameters to the method</param>   
        /// <param name="method">Method to invoke</param>   
        /// <returns>Hashtable with success=>bool and other values</returns>   
        private Hashtable genericCurrencyXMLRPCRequest(Hashtable reqParams, string method)
        {
            if (reqParams.Count <= 0 || string.IsNullOrEmpty(method)) return null;

            if (m_checkServerCert)
            {
                if (!m_moneyServURL.StartsWith("https://"))
                {
                    m_log.InfoFormat("[MONEY MODULE]: genericCurrencyXMLRPCRequest: CheckServerCert is true, but protocol is not HTTPS. Please check INI file");
                    //return null;
                }
            }
            else
            {
                if (!m_moneyServURL.StartsWith("https://") && !m_moneyServURL.StartsWith("http://"))
                {
                    m_log.ErrorFormat("[MONEY MODULE]: genericCurrencyXMLRPCRequest: Invalid Money Server URL: {0}", m_moneyServURL);
                    return null;
                }
            }

            ArrayList arrayParams = new ArrayList();
            arrayParams.Add(reqParams);
            XmlRpcResponse moneyServResp = null;
            try
            {
                NSLXmlRpcRequest moneyModuleReq = new NSLXmlRpcRequest(method, arrayParams);
                //moneyServResp = moneyModuleReq.certSend(m_moneyServURL, m_cert, m_checkServerCert, MONEYMODULE_REQUEST_TIMEOUT);
                moneyServResp = moneyModuleReq.certSend(m_moneyServURL, m_certVerify, m_checkServerCert, MONEYMODULE_REQUEST_TIMEOUT);
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[MONEY MODULE]: genericCurrencyXMLRPCRequest: Unable to connect to Money Server {0}", m_moneyServURL);
                m_log.ErrorFormat("[MONEY MODULE]: genericCurrencyXMLRPCRequest: {0}", ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }

            if (moneyServResp == null || moneyServResp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }

            Hashtable moneyRespData = (Hashtable)moneyServResp.Value;
            return moneyRespData;
        }


        /// Locates a IClientAPI for the client specified   
        /// </summary>   
        /// <param name="AgentID"></param>   
        /// <returns></returns>   
        private IClientAPI GetLocateClient(UUID AgentID)
        {
            IClientAPI client = null;

            lock (m_sceneList)
            {
                if (m_sceneList.Count > 0)
                {
                    foreach (Scene _scene in m_sceneList.Values)
                    {
                        ScenePresence tPresence = (ScenePresence)_scene.GetScenePresence(AgentID);
                        if (tPresence != null && !tPresence.IsChildAgent)
                        {
                            IClientAPI rclient = tPresence.ControllingClient;
                            if (rclient != null)
                            {
                                client = rclient;
                                break;
                            }
                        }
                    }
                }
            }

            return client;
        }


        /// <summary>Gets the locate scene.</summary>
        /// <param name="AgentId">The agent identifier.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        private Scene GetLocateScene(UUID AgentId)
        {
            Scene scene = null;

            lock (m_sceneList)
            {
                if (m_sceneList.Count > 0)
                {
                    foreach (Scene _scene in m_sceneList.Values)
                    {
                        ScenePresence tPresence = (ScenePresence)_scene.GetScenePresence(AgentId);
                        if (tPresence != null && !tPresence.IsChildAgent)
                        {
                            scene = _scene;
                            break;
                        }
                    }
                }
            }

            return scene;
        }


        /// <summary>Gets the locate prim.</summary>
        /// <param name="objectID">The object identifier.</param>
        /// <returns>
        ///   <br />
        /// </returns>
        private SceneObjectPart GetLocatePrim(UUID objectID)
        {
            SceneObjectPart sceneObj = null;

            lock (m_sceneList)
            {
                if (m_sceneList.Count > 0)
                {
                    foreach (Scene _scene in m_sceneList.Values)
                    {
                        SceneObjectPart part = (SceneObjectPart)_scene.GetSceneObjectPart(objectID);
                        if (part != null)
                        {
                            sceneObj = part;
                            break;
                        }
                    }
                }
            }

            return sceneObj;
        }

    }

}
