﻿using Certify.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertifyManager
    {
        private ItemManager siteManager = null;

        private const string SCHEDULED_TASK_NAME = "Certify Maintenance Task";
        private const string SCHEDULED_TASK_EXE = "certify.exe";
        private const string SCHEDULED_TASK_ARGS = "renew";

        public CertifyManager()
        {
            Certify.Management.Util.SetSupportedTLSVersions();

            siteManager = new ItemManager();
            siteManager.LoadSettings();
        }

        /// <summary>
        /// Check if we have one or more managed sites setup
        /// </summary>
        public bool HasManagedSites
        {
            get
            {
                if (siteManager.GetManagedSites().Count > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private VaultManager GetVaultManager()
        {
            return new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);
        }

        public List<ManagedSite> GetManagedSites()
        {
            return this.siteManager.GetManagedSites();
        }

        public List<RegistrationItem> GetRegistrations()
        {
            var vault = GetVaultManager();
            var reg = vault.GetRegistrations(reloadVaultConfig: true);
            var list = new List<RegistrationItem>();

            foreach (var r in reg)
            {
                list.Add(new RegistrationItem { Id = r.Id.ToString(), Name = r.Registration.Contacts.First(), Contacts = r.Registration.Contacts });
            }

            return list;
        }

        public List<IdentifierItem> GetIdentifiers()
        {
            var vault = GetVaultManager();
            var reg = vault.GetIdentifiers(reloadVaultConfig: true);
            var list = new List<IdentifierItem>();

            foreach (var r in reg)
            {
                list.Add(new IdentifierItem { Id = r.Id.ToString(), Name = r.Dns, Dns = r.Dns, Status = r.Authorization?.Status });
            }

            return list;
        }

        public List<CertificateItem> GetCertificates()
        {
            var vault = GetVaultManager();
            var certs = vault.GetCertificates(reloadVaultConfig: true);
            var list = new List<CertificateItem>();

            foreach (var i in certs)
            {
                list.Add(new CertificateItem { Id = i.Id.ToString(), Name = i.IdentifierDns });
            }

            return list;
        }

        public void SetManagedSites(List<ManagedSite> managedSites)
        {
            this.siteManager.UpdatedManagedSites(managedSites);
        }

        public void SaveManagedSites(List<ManagedSite> managedSites)
        {
            this.siteManager.UpdatedManagedSites(managedSites);
            this.siteManager.StoreSettings();
        }

        public bool HasRegisteredContacts()
        {
            var vaultManager = GetVaultManager();
            return vaultManager.HasContacts(true);
        }

        /// <summary>
        /// Test dummy method for async UI testing etc
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedSite"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(VaultManager vaultManager, ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            return await Task<CertificateRequestResult>.Run<CertificateRequestResult>(() =>
            {
                for (var i = 0; i < 6; i++)
                {
                    if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Step " + i });

                    var time = new Random().Next(2000);
                    System.Threading.Thread.Sleep(time);
                }
                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = "Finish" });
                System.Threading.Thread.Sleep(500);
                return new CertificateRequestResult { };
            });
        }

        public void DeleteManagedSite(string id)
        {
            var site = siteManager.GetManagedSite(id);
            if (site != null)
            {
                this.siteManager.DeleteManagedSite(site);
            }
        }

        public bool AddRegisteredContact(ContactRegistration reg)
        {
            var vaultManager = GetVaultManager();
            return vaultManager.AddNewRegistrationAndAcceptTOS("mailto:" + reg.EmailAddress);
        }

        /// <summary>
        /// Remove other contacts which don't match the email address given
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public void RemoveExtraContacts(string email)
        {
            var vaultManager = GetVaultManager();
            var regList = vaultManager.GetRegistrations(true).ToList();
            foreach (var reg in regList)
            {
                if (!reg.Registration.Contacts.Contains("mailto:" + email))
                {
                    vaultManager.DeleteRegistrationInfo(reg.Id);
                }
            }
        }

        public string GetAcmeSummary()
        {
            var vaultManager = GetVaultManager();
            return vaultManager.GetACMEBaseURI();
        }

        public string GetVaultSummary()
        {
            var vaultManager = GetVaultManager();
            return vaultManager.GetVaultPath();
        }

        public async Task<CertificateRequestResult> PerformCertificateRequest(VaultManager vaultManager, ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            // FIXME: refactor into different concerns, there's way to much being done here

            return await Task.Run(async () =>
            {
                try
                {
                    ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralInfo, Message = "Beginning Certificate Request Process: " + managedSite.Name });

                    bool enableIdentifierReuse = false;

                    if (vaultManager == null)
                    {
                        vaultManager = GetVaultManager();
                    }

                    //enable or disable EFS flag on private key certs based on preference
                    vaultManager.UseEFSForSensitiveFiles = Properties.Settings.Default.EnableEFS;

                    //primary domain and each subject alternative name must now be registered as an identifier with LE and validated

                    if (progress != null) progress.Report(new RequestProgressState { IsRunning = true, CurrentState = RequestState.Running, Message = "Registering Domain Identifiers" });

                    await Task.Delay(200); //allow UI update

                    var config = managedSite.RequestConfig;

                    List<string> allDomains = new List<string> { config.PrimaryDomain };

                    if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);

                    bool allIdentifiersValidated = true;

                    if (config.ChallengeType == null) config.ChallengeType = "http-01";

                    List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();
                    var distinctDomains = allDomains.Distinct();

                    foreach (var domain in distinctDomains)
                    {
                        var identifierAlias = vaultManager.ComputeIdentifierAlias(domain);

                        //check if this domain already has an associated identifier registerd with LetsEncrypt which hasn't expired yet
                        //await Task.Delay(200); //allow UI update

                        ACMESharp.Vault.Model.IdentifierInfo existingIdentifier = null;

                        if (enableIdentifierReuse)
                        {
                            existingIdentifier = vaultManager.GetIdentifier(domain.Trim().ToLower());
                        }

                        bool identifierAlreadyValid = false;
                        if (existingIdentifier != null
                            && existingIdentifier.Authorization != null
                            && (existingIdentifier.Authorization.Status == "valid" || existingIdentifier.Authorization.Status == "pending")
                            && existingIdentifier.Authorization.Expires > DateTime.Now.AddDays(1))
                        {
                            //we have an existing validated identifier, reuse that for this certificate request
                            identifierAlias = existingIdentifier.Alias;

                            if (existingIdentifier.Authorization.Status == "valid")
                            {
                                identifierAlreadyValid = true;
                            }

                            // managedSite.AppendLog(new ManagedSiteLogItem { EventDate =
                            // DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted,
                            // Message = "Attempting Certificate Request: " + managedSite.SiteType });
                            System.Diagnostics.Debug.WriteLine("Reusing existing valid non-expired identifier for the domain " + domain);
                        }

                        ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Domain Validation: " + domain });

                        //begin authorization process (register identifier, request authorization if not already given)
                        if (progress != null) progress.Report(new RequestProgressState { Message = "Registering and Validating " + domain });

                        //TODO: make operations async and yeild IO of vault
                        /*var authorization = await Task.Run(() =>
                        {
                            return vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);
                        });*/

                        var authorization = vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);

                        if (authorization != null && authorization.Identifier != null && !identifierAlreadyValid)
                        {
                            if (authorization.Identifier.Authorization.IsPending())
                            {
                                if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
                                {
                                    if (progress != null) progress.Report(new RequestProgressState { Message = "Performing Challenge Response via IIS: " + domain });

                                    //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
                                    //prepare IIS with answer for the LE challenege
                                    authorization = vaultManager.PerformIISAutomatedChallengeResponse(config, authorization);

                                    //if we attempted extensionless config checks, report any errors
                                    if (config.PerformAutoConfig && !authorization.ExtensionlessConfigCheckedOK)
                                    {
                                        ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (" + managedSite.ItemType + ")" });
                                        siteManager.StoreSettings();

                                        var result = new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "Automated configuration checks failed. Authorizations will not be able to complete.\nCheck you have http bindings for your site and ensure you can browse to http://" + domain + "/.well-known/acme-challenge/configcheck before proceeding." };
                                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = result.Message, Result = result });

                                        return result;
                                    }
                                    else
                                    {
                                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Requesting Validation from Lets Encrypt: " + domain });

                                        //ask LE to validate our challenge response
                                        vaultManager.SubmitChallenge(identifierAlias, config.ChallengeType);

                                        bool identifierValidated = vaultManager.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                                        if (!identifierValidated)
                                        {
                                            if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = "Domain validation failed: " + domain });

                                            allIdentifiersValidated = false;
                                        }
                                        else
                                        {
                                            if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Domain validation completed: " + domain });

                                            identifierAuthorizations.Add(authorization);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (authorization.Identifier.Authorization.Status == "valid")
                                {
                                    identifierAuthorizations.Add(new PendingAuthorization { Identifier = authorization.Identifier });
                                }
                            }
                        }
                        else
                        {
                            if (identifierAlreadyValid)
                            {
                                //we have previously validated this identifier and it has not yet expired, so we can just reuse it in our cert request
                                identifierAuthorizations.Add(new PendingAuthorization { Identifier = existingIdentifier });
                            }
                        }
                    }

                    //check if all identifiers validates
                    if (identifierAuthorizations.Count == distinctDomains.Count())
                    {
                        allIdentifiersValidated = true;
                    }

                    if (allIdentifiersValidated)
                    {
                        string primaryDnsIdentifier = identifierAuthorizations.First().Identifier.Alias;
                        string[] alternativeDnsIdentifiers = identifierAuthorizations.Where(i => i.Identifier.Alias != primaryDnsIdentifier).Select(i => i.Identifier.Alias).ToArray();

                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Requesting Certificate via Lets Encrypt" });
                        //await Task.Delay(200); //allow UI update

                        var certRequestResult = vaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);
                        if (certRequestResult.IsSuccess)
                        {
                            if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = "Completed Certificate Request." });

                            string pfxPath = certRequestResult.Result.ToString();

                            if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS && config.PerformAutomatedCertBinding)
                            {
                                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Performing Automated Certificate Binding" });
                                //await Task.Delay(200); //allow UI update

                                var iisManager = new IISManager();

                                //Install certificate into certificate store and bind to IIS site
                                if (iisManager.InstallCertForRequest(managedSite.RequestConfig, pfxPath, cleanupCertStore: true))
                                {
                                    //all done
                                    ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestSuccessful, Message = "Completed certificate request and automated bindings update (IIS)" });

                                    //udpate managed site summary

                                    try
                                    {
                                        var certInfo = new CertificateManager().GetCertificate(pfxPath);
                                        managedSite.DateStart = certInfo.NotBefore;
                                        managedSite.DateExpiry = certInfo.NotAfter;
                                        managedSite.DateRenewed = DateTime.Now;

                                        managedSite.CertificatePath = pfxPath;
                                    }
                                    catch (Exception)
                                    {
                                        ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralWarning, Message = "Failed to parse certificate dates" });
                                    }
                                    siteManager.UpdatedManagedSite(managedSite);

                                    var result = new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = true, Message = "Certificate installed and SSL bindings updated for " + config.PrimaryDomain };
                                    if (progress != null) progress.Report(new RequestProgressState { IsRunning = false, CurrentState = RequestState.Success, Message = result.Message });

                                    return result;
                                }
                                else
                                {
                                    return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "An error occurred installing the certificate. Certificate file may not be valid: " + pfxPath };
                                }
                            }
                            else
                            {
                                //user has opted for manual binding of certificate
                                try
                                {
                                    var certInfo = new CertificateManager().GetCertificate(pfxPath);
                                    managedSite.DateStart = certInfo.NotBefore;
                                    managedSite.DateExpiry = certInfo.NotAfter;
                                    managedSite.DateRenewed = DateTime.Now;

                                    managedSite.CertificatePath = pfxPath;
                                }
                                catch (Exception)
                                {
                                    ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralWarning, Message = "Failed to parse certificate dates" });
                                }
                                siteManager.UpdatedManagedSite(managedSite);

                                return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = true, Message = "Certificate created ready for manual binding: " + pfxPath };
                            }
                        }
                        else
                        {
                            return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "The Let's Encrypt service did not issue a valid certificate in the time allowed. " + (certRequestResult.ErrorMessage ?? "") };
                        }
                    }
                    else
                    {
                        return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "Validation of the required challenges did not complete successfully. Please ensure all domains to be referenced in the Certificate can be used to access this site without redirection. " };
                    }
                }
                catch (Exception exp)
                {
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                    return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = managedSite.Name + ": Request failed - " + exp.Message };
                }
            });
        }

        public List<ManagedSite> ImportManagedSitesFromVault(bool mergeSitesAsSan = false)
        {
            var sites = new List<ManagedSite>();

            //get dns identifiers from vault
            var vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);
            var iisManager = new IISManager();

            var identifiers = vaultManager.GetIdentifiers();
            var iisSites = iisManager.GetSiteBindingList(ignoreStoppedSites: Certify.Properties.Settings.Default.IgnoreStoppedSites);
            foreach (var identifier in identifiers)
            {
                //identify IIS site related to this identifier (if any)
                var iisSite = iisSites.FirstOrDefault(d => d.Host == identifier.Dns);
                var site = new ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    GroupId = iisSite?.SiteId,
                    Name = identifier.Dns + (iisSite != null ? " : " + iisSite.SiteName : ""),
                    IncludeInAutoRenew = true,
                    Comments = "Imported from vault",
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                    TargetHost = "localhost",
                    RequestConfig = new CertRequestConfig
                    {
                        BindingIPAddress = iisSite?.IP,
                        BindingPort = iisSite?.Port.ToString(),
                        ChallengeType = "http-01",
                        EnableFailureNotifications = true,
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        PrimaryDomain = identifier.Dns,
                        SubjectAlternativeNames = new string[] { identifier.Dns },
                        WebsiteRootPath = iisSite?.PhysicalPath
                    },
                    DomainOptions = new List<DomainOption>() { new DomainOption { Domain = identifier.Dns, IsPrimaryDomain = true, IsSelected = true } }
                };

                sites.Add(site);
            }

            if (mergeSitesAsSan)
            {
                foreach (var s in sites)
                {
                    //merge sites with same group (iis site etc) and different primary domain
                    if (sites.Any(m => m.GroupId != null && m.GroupId == s.GroupId && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain))
                    {
                        //existing site to merge into
                        //add san for dns
                        var mergedSite = sites.FirstOrDefault(m =>
                        m.GroupId != null && m.GroupId == s.GroupId
                        && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain
                        && m.RequestConfig.PrimaryDomain != null
                        );
                        if (mergedSite != null)
                        {
                            mergedSite.DomainOptions.Add(new DomainOption { Domain = s.RequestConfig.PrimaryDomain, IsPrimaryDomain = false, IsSelected = true });

                            //use shortest version of domain name as site name
                            if (mergedSite.RequestConfig.PrimaryDomain.Contains(s.RequestConfig.PrimaryDomain))
                            {
                                mergedSite.Name = mergedSite.Name.Replace(mergedSite.RequestConfig.PrimaryDomain, s.RequestConfig.PrimaryDomain);
                            }

                            //flag spare site config to be discar
                            s.RequestConfig.PrimaryDomain = null;
                        }
                    }
                }

                //discard sites which have been merged into other sites
                sites.RemoveAll(s => s.RequestConfig.PrimaryDomain == null);
            }
            return sites;
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            await Task.Delay(200); //allow UI to update
                                   //currently the vault won't let us run parallel requests due to file locks
            bool performRequestsInParallel = false;
            bool testModeOnly = false;

            siteManager.LoadSettings();

            IEnumerable<ManagedSite> sites = siteManager.GetManagedSites();

            if (autoRenewalOnly)
            {
                sites = sites.Where(s => s.IncludeInAutoRenew == true);
            }

            //check site list and examine current certificates. If certificate is less than n days old, don't attempt to renew it
            var sitesToRenew = new List<ManagedSite>();
            var renewalIntervalDays = Properties.Settings.Default.RenewalIntervalDays;

#if DEBUG
            //in debug mode we renew every time instead of skipping based on days old
            renewalIntervalDays = 0;
#endif

            var renewalTasks = new List<Task<CertificateRequestResult>>();
            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                //if we know the last renewal date, check whether we should renew again, otherwise assume it's more than 30 days ago by default and attempt renewal
                var timeSinceLastRenewal = (s.DateRenewed != null ? s.DateRenewed : DateTime.Now.AddDays(-30)) - DateTime.Now;

                bool isRenewalRequired = Math.Abs(timeSinceLastRenewal.Value.TotalDays) > renewalIntervalDays;
                bool isSiteRunning = true;

                //if we care about stopped sites being stopped, check for that
                if (Properties.Settings.Default.IgnoreStoppedSites)
                {
                    isSiteRunning = IsManagedSiteRunning(s.Id);
                }

                if (isRenewalRequired && isSiteRunning)
                {
                    //get matching progress tracker for this site
                    IProgress<RequestProgressState> tracker = null;
                    if (progressTrackers != null)
                    {
                        tracker = progressTrackers[s.Id];
                    }

                    if (testModeOnly)
                    {
                        //simulated request for UI testing
                        renewalTasks.Add(this.PerformDummyCertificateRequest(null, s, tracker));
                    }
                    else
                    {
                        renewalTasks.Add(this.PerformCertificateRequest(null, s, tracker));
                    }
                }
                else
                {
                    var msg = "Skipping Renewal, existing certificate still OK. ";

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = "Site stopped, renewal skipped as domain validation cannot be performed. ";
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[s.Id];
                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = msg });
                    }

                    ManagedSiteLog.AppendLog(s.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralInfo, Message = msg + s.Name });
                }
            }

            if (!renewalTasks.Any())
            {
                //nothing to do
                return new List<CertificateRequestResult>();
            }

            if (performRequestsInParallel)
            {
                var results = await Task.WhenAll(renewalTasks);

                //siteManager.StoreSettings();
                return results.ToList();
            }
            else
            {
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    results.Add(await t);
                }

                return results;
            }
        }

        private bool IsManagedSiteRunning(string id, IISManager iisManager = null)
        {
            var managedSite = siteManager.GetManagedSite(id);
            if (managedSite != null)
            {
                if (iisManager == null) iisManager = new IISManager();
                return iisManager.IsSiteRunning(id);
            }
            else
            {
                //site not identified, assume it is running
                return true;
            }
        }

        public bool IsWindowsScheduledTaskPresent()
        {
            var taskList = Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.GetTasks();
            if (taskList.Any(t => t.Name == SCHEDULED_TASK_NAME))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Creates the windows scheduled task to perform renewals, running as the given userid (who
        /// should be admin level so they can perform cert mgmt and IIS management functions)
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        public bool CreateWindowsScheduledTask(string userId, string pwd)
        {
            // https://taskscheduler.codeplex.com/documentation
            var taskService = Microsoft.Win32.TaskScheduler.TaskService.Instance;
            try
            {
                var cliPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SCHEDULED_TASK_EXE);

                //setup auto renewal task, executing as admin using the given username and password
                var task = taskService.NewTask();

                task.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.Highest;
                task.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(cliPath, SCHEDULED_TASK_ARGS));
                task.Triggers.Add(new Microsoft.Win32.TaskScheduler.DailyTrigger { DaysInterval = 1 });

                //register/update task
                taskService.RootFolder.RegisterTaskDefinition(SCHEDULED_TASK_NAME, task, Microsoft.Win32.TaskScheduler.TaskCreation.CreateOrUpdate, userId, pwd, Microsoft.Win32.TaskScheduler.TaskLogonType.Password);

                return true;
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                //failed to create task
                return false;
            }
        }

        public void DeleteWindowsScheduledTask()
        {
            Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.DeleteTask(SCHEDULED_TASK_NAME, exceptionOnNotExists: false);
        }
    }
}