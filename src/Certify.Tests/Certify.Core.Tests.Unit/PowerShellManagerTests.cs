﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class PowerShellManagerTests
    {

        [TestMethod, Description("Test Script runs OK")]
        public async Task TestLoadManagedCertificates()
        {
            var path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            await PowerShellManager.RunScript(new CertificateRequestResult {}, path+"\\Assets\\Powershell\\Simple.ps1");


            var transcriptLogExists = System.IO.File.Exists(@"C:\Temp\Certify\TestOutput\TestTranscript.txt");
            var outputExists = System.IO.File.Exists(@"C:\Temp\Certify\TestOutput\TestPSOutput.txt");
            Assert.IsTrue(outputExists, "Powershell output file should exist");
            Assert.IsTrue(transcriptLogExists, "Powershell transcript log file should exist");

            System.IO.File.Delete(@"C:\Temp\Certify\TestOutput\TestPSOutput.txt");
            System.IO.File.Delete(@"C:\Temp\Certify\TestOutput\TestTranscript.txt");

        }

      
    }
}
