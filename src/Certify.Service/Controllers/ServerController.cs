﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using Certify.Management;
using Certify.Models;

namespace Certify.Service
{
    [RoutePrefix("api/server")]
    public class ServerController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public ServerController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("isavailable/{serverType}")]
        public async Task<bool> IsServerAvailable(StandardServerTypes serverType)
        {
            DebugLog();

            if (serverType == StandardServerTypes.IIS)
            {
                return await _certifyManager.IsServerTypeAvailable(serverType);
            }
            else
            {
                return false;
            }
        }

        [HttpGet, Route("sitelist/{serverType}")]
        public async Task<List<BindingInfo>> GetServerSiteList(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return await _certifyManager.GetPrimaryWebSites(Management.CoreAppSettings.Current.IgnoreStoppedSites);
            }
            else
            {
                return new List<BindingInfo>();
            }
        }

        [HttpGet, Route("sitedomains/{serverType}/{serverSiteId}")]
        public async Task<List<DomainOption>> GetServerSiteDomainOptions(StandardServerTypes serverType, string serverSiteId)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return await _certifyManager.GetDomainOptionsFromSite(serverSiteId);
            }
            else
            {
                return new List<DomainOption>();
            }
        }

        [HttpGet, Route("version/{serverType}")]
        public async Task<string> GetServerVersion(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                var version = await _certifyManager.GetServerTypeVersion(serverType);
                return version.ToString();
            }
            else
            {
                return null;
            }
        }

        [HttpGet, Route("diagnostics/{serverType}/{siteId?}")]
        public async Task<List<ActionStep>> RunServerDiagnostics(StandardServerTypes serverType, string siteId)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return await _certifyManager.RunServerDiagnostics(serverType, siteId);
            }
            else
            {
                return null;
            }
        }
    }
}
