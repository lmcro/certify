﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Management
{
    /// <summary>
    /// SiteManager encapsulates settings and operations on the list of Sites we manage certificates for using Certify and is additional to the ACMESharp Vault. These could be Local IIS, Manually Configured, DNS driven etc
    /// </summary>
    public class ItemManager
    {
        public const string ITEMMANAGERCONFIG = "manageditems.json";

        /// <summary>
        /// If true, one or more of our managed sites are hosted within a Local IIS server on the same machine
        /// </summary>
        public bool EnableLocalIISMode { get; set; } //TODO: driven by config

        private List<ManagedSite> ManagedSites { get; set; }

        public ItemManager()
        {
            EnableLocalIISMode = true;
            this.ManagedSites = new List<ManagedSite>(); // this.Preview();
        }

        internal void UpdatedManagedSites(List<ManagedSite> managedSites)
        {
            this.ManagedSites = managedSites;
            this.StoreSettings();
        }

        public void StoreSettings()
        {
            string appDataPath = Util.GetAppDataFolder();
            string siteManagerConfig = Newtonsoft.Json.JsonConvert.SerializeObject(this.ManagedSites, Newtonsoft.Json.Formatting.Indented);

            lock (ITEMMANAGERCONFIG)
            {
                System.IO.File.WriteAllText(appDataPath + "\\" + ITEMMANAGERCONFIG, siteManagerConfig);
            }
        }

        public void LoadSettings()
        {
            string appDataPath = Util.GetAppDataFolder();
            var path = appDataPath + "\\" + ITEMMANAGERCONFIG;
            if (System.IO.File.Exists(path))
            {
                lock (ITEMMANAGERCONFIG)
                {
                    string configData = System.IO.File.ReadAllText(path);
                    this.ManagedSites = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ManagedSite>>(configData);
                }
            }
            else
            {
                this.ManagedSites = new List<ManagedSite>();
            }

            foreach (var s in this.ManagedSites)
            {
                s.IsChanged = false;
            }
        }

        /// <summary>
        /// For current configured environment, show preview of recommended site management (for local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public List<ManagedSite> Preview()
        {
            List<ManagedSite> sites = new List<ManagedSite>();

            if (EnableLocalIISMode)
            {
                try
                {
                    var iisSites = new IISManager().GetSiteBindingList(ignoreStoppedSites: Certify.Properties.Settings.Default.IgnoreStoppedSites).OrderBy(s => s.SiteId).ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        ManagedSite managedSite = new ManagedSite { Id = s.Key };
                        managedSite.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                        managedSite.TargetHost = "localhost";
                        managedSite.Name = iisSites.First(i => i.SiteId == s.Key).SiteName;

                        //TODO: replace site binding with domain options
                        //managedSite.SiteBindings = new List<ManagedSiteBinding>();

                        foreach (var binding in s)
                        {
                            var managedBinding = new ManagedSiteBinding { Hostname = binding.Host, IP = binding.IP, Port = binding.Port, UseSNI = true, CertName = "Certify_" + binding.Host };
                            // managedSite.SiteBindings.Add(managedBinding);
                        }
                        sites.Add(managedSite);
                    }
                }
                catch (Exception)
                {
                    //can't read sites
                    System.Diagnostics.Debug.WriteLine("Can't get IIS site list.");
                }
            }
            return sites;
        }

        public ManagedSite GetManagedSite(string siteId, string domain = null)
        {
            var site = this.ManagedSites.FirstOrDefault(s => (siteId != null && s.Id == siteId) || (domain != null && s.DomainOptions.Any(bind => bind.Domain == domain)));
            return site;
        }

        public List<ManagedSite> GetManagedSites()
        {
            this.LoadSettings();

            if (this.ManagedSites == null) this.ManagedSites = new List<ManagedSite>();

            return this.ManagedSites;
        }

        public void UpdatedManagedSite(ManagedSite managedSite)
        {
            this.LoadSettings();

            var existingSite = this.ManagedSites.FirstOrDefault(s => s.Id == managedSite.Id);
            if (existingSite != null)
            {
                this.ManagedSites.Remove(existingSite);
            }

            this.ManagedSites.Add(managedSite);
            this.StoreSettings();
        }

        public void DeleteManagedSite(ManagedSite site)
        {
            this.LoadSettings();

            var existingSite = this.ManagedSites.FirstOrDefault(s => s.Id == site.Id);
            if (existingSite != null)
            {
                this.ManagedSites.Remove(existingSite);
            }
            this.StoreSettings();
        }
    }
}