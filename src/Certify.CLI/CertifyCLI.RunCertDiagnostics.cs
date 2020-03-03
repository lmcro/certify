﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        /// <summary>
        /// Run general diagnostics, optionally fixing binding deployment
        /// </summary>
        /// <param name="autoFix">Attempt to re-apply current certificate</param>
        /// <param name="forceAutoDeploy">Change all deployment modes to Auto</param>
        public async Task RunCertDiagnostics(bool autoFix = false, bool forceAutoDeploy = false)
        {
            string stripNonNumericFromString(string input)
            {
                return new string(input.Where(c => char.IsDigit(c)).ToArray());
            }

            bool isNumeric(string input)
            {
                return int.TryParse(input, out _);
            }

            var managedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter());
            Console.ForegroundColor = ConsoleColor.White;
#if BINDING_CHECKS
            Console.WriteLine("Checking existing bindings..");

            var bindingConfig = Certify.Utils.Networking.GetCertificateBindings().Where(b => b.Port == 443);

            foreach (var b in bindingConfig)
            {
                Console.WriteLine($"{b.IP}:{b.Port}");
            }

            var dupeBindings = bindingConfig.GroupBy(x => x.IP + ":" + x.Port)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

            if (dupeBindings.Any())
            {
                foreach (var d in dupeBindings)
                {
                    Console.WriteLine($"Duplicate binding will fail:  {d}");
                }
            }
            else
            {
                Console.WriteLine("No duplicate IP:Port bindings identified.");
            }
#endif
            Console.WriteLine("Running cert diagnostics..");

            var countSiteIdsFixed = 0;
            var countBindingRedeployments = 0;

            foreach (var site in managedCertificates)
            {
                var redeployRequired = false;

                if (autoFix)
                {
                    redeployRequired = true;
                }

                if ((site.GroupId != site.ServerSiteId) || !isNumeric(site.ServerSiteId))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\t WARNING: managed cert has invalid ServerSiteID: " + site.Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    redeployRequired = true;

                    if (autoFix)
                    {

                        site.ServerSiteId = stripNonNumericFromString(site.ServerSiteId);
                        site.GroupId = site.ServerSiteId;
                        //update managed site
                        Console.WriteLine("\t Auto fixing managed cert ServerSiteID: " + site.Name);

                        var update = await _certifyClient.UpdateManagedCertificate(site);
                        
                        countSiteIdsFixed++;
                    }
                }

                if (autoFix && forceAutoDeploy)
                {
                    redeployRequired = true;

                    if (site.RequestConfig.DeploymentSiteOption != DeploymentOption.Auto && site.RequestConfig.DeploymentSiteOption != DeploymentOption.AllSites)
                    {
                        Console.WriteLine("\t Auto fixing managed cert deployment mode: " + site.Name);
                        site.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;

                        var update = await _certifyClient.UpdateManagedCertificate(site);
                    }
                }

                if (!string.IsNullOrEmpty(site.CertificatePath) && System.IO.File.Exists(site.CertificatePath))
                {
                    Console.WriteLine($"{site.Name}");
                    var fileCert = CertificateManager.LoadCertificate(site.CertificatePath);

                    if (fileCert != null)
                    {
                        try
                        {
                            var storedCert = CertificateManager.GetCertificateByThumbprint(site.CertificateThumbprintHash);
                            if (storedCert != null)
                            {
                                // cert in store, check permissions
                                Console.WriteLine($"Stored cert :: " + storedCert.FriendlyName);
                                var test = fileCert.PrivateKey.KeyExchangeAlgorithm;
                                Console.WriteLine(test.ToString());

                                var access = CertificateManager.GetUserAccessInfoForCertificatePrivateKey(storedCert);
                                foreach (System.Security.AccessControl.AuthorizationRule a in access.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount)))
                                {
                                    Console.WriteLine("\t Access: " + a.IdentityReference.Value.ToString());
                                }
                            }

                            // re-deploy certificate if possible
                            if (redeployRequired && autoFix)
                            {

                                //re-apply current certificate file to store and bindings
                                if (!string.IsNullOrEmpty(site.CertificateThumbprintHash))
                                {
                                    var result = await _certifyClient.ReapplyCertificateBindings(site.Id, false);
                                    
                                    countBindingRedeployments++;

                                    if (!result.IsSuccess)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("\t Error: Failed to re-applying certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("\t Info: re-applied certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }

                                    System.Threading.Thread.Sleep(5000);
                                }
                                else
                                {

                                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                                    Console.WriteLine($"Warning: {site.Name} :: No certificate information, bindings cannot be redeployed");
                                    Console.ForegroundColor = ConsoleColor.White;

                                }
                            }

                        }
                        catch (Exception exp)
                        {
                            Console.WriteLine(exp.ToString());
                        }

                    }
                    else
                    {
                        //Console.WriteLine($"{site.Name} certificate file does not exist: {site.CertificatePath}");
                        if (redeployRequired)
                        {
                            Console.WriteLine($"{site.Name} has no current certificate and requires manual verification/redeploy of cert.");
                        }
                    }
                }
            }

            // TODO: get refresh of managed certs and for each current cert thumbprint, verify binding thumbprint match

            Console.WriteLine("-----------");
        }

        public async Task<bool> PerformDeployment(string managedCertName, string taskName)
        {

            var managedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter { Name = managedCertName });

            if (managedCertificates.Count == 1)
            {
                if (!string.IsNullOrEmpty(taskName))
                {
                    var managedCert = managedCertificates.Single();

                    // identify specific task
                    Console.WriteLine("Performing deployment task '" + taskName + "'..");
                    var task = managedCert.DeploymentTasks.FirstOrDefault(t => t.TaskName.ToLowerInvariant().Trim() == taskName.ToLowerInvariant().Trim());

                    if (task != null)
                    {
                        var results = await _certifyClient.PerformDeployment(managedCert.Id, task.Id, isPreviewOnly: false);

                        if (results.Any(r => r.HasError == true))
                        {
                            var err = results.First(f => f.HasError == true);
                            Console.WriteLine("One or more task steps failed: " + err.Description);
                            return false;
                        }
                        else
                        {
                            Console.WriteLine("Deployment task completed.");
                            return true;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Task '" + taskName + "' not found. Deployment failed.");
                        return false;
                    }
                }
                else
                {
                    // perform all deployment tasks
                    Console.WriteLine("Performing all deployment tasks for managed certificate '" + managedCertName + "'..");
                }
            }
            else
            {
                if (managedCertificates.Count > 1)
                {
                    // too many matches
                    Console.WriteLine("Managed Certificate name matched more than one item. Deployment failed.");
                    return false;
                }
                else
                {
                    // no matches
                    Console.WriteLine("Managed Certificate name has no matches. Deployment failed.");
                    return false;
                }
            }
            throw new NotImplementedException();
        }
    }
}
