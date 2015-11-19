using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.IO;

namespace CjsTestAdapter
{
    [ExtensionUri(CjsTestContainerDiscoverer.ExecutorUriString)]
    public class CjsTestExecutor : ITestExecutor
    {
        private const string temp = "tmp";
        private bool cancelled;

        public void RunTests(IEnumerable<string> sources, IRunContext runContext,
            IFrameworkHandle frameworkHandle)
        {
            IEnumerable<TestCase> tests = CjsTestDiscoverer.GetTests(sources, null);
            RunTests(tests, runContext, frameworkHandle);
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext,
               IFrameworkHandle frameworkHandle)
        {
            cancelled = false;

            foreach (var sourceGroup in tests.GroupBy(x => x.Source))
            {
                if (cancelled) break;
                string jsTestFile = sourceGroup.Key;           
                var engineArgs = new List<string>();

                string codeBase = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                string exeDir = Path.GetDirectoryName(path);
                //Create temp folder for tests.
                var temppath = Path.Combine(exeDir, temp);
                if (!Directory.Exists(temppath)) Directory.CreateDirectory(temppath);
                //Copy the test js file to a temporary file.
                var tempFileName = string.Format("{0}.js", Guid.NewGuid());
                var tempFile = Path.Combine(temppath, tempFileName);
                File.Copy(jsTestFile, tempFile,true);
                var casperArgs = new List<string>();
                //TODO: Allow for SlimerJs or TrifleJS
                string[] engineNativeArgs = new[] {
                                                "cookies-file",
                                                "config",
                                                "debug",
                                                "disk-cache",
                                                "ignore-ssl-errors",
                                                "load-images",
                                                "load-plugins",
                                                "local-storage-path",
                                                "local-storage-quota",
                                                "local-to-remote-url-access",
                                                "max-disk-cache-size",
                                                "output-encoding",
                                                "proxy",
                                                "proxy-auth",
                                                "proxy-type",
                                                "remote-debugger-port",
                                                "remote-debugger-autorun",
                                                "script-encoding",
                                                "ssl-protocol",
                                                "ssl-certificates-path",
                                                "web-security",
                                                "webdriver",
                                                "webdriver-logfile",
                                                "webdriver-loglevel",
                                                "webdriver-selenium-grid-hub",
                                                "wd",
                                                "w",
                                            };
                var engineExecutable = @"PhantomJs\phantomjs.exe";
                //TODO Put casper/phantom options into a settings file (EG --ignore-ssl-errors=true)
                foreach (string arg in new string[] { "test", Path.Combine(temp, tempFileName), "--ignore-ssl-errors=true" })
                {
                    bool found = false;
                    foreach (string native in engineNativeArgs)
                    {
                        if (arg.StartsWith("--" + native))
                        {
                            engineArgs.Add(arg);
                            found = true;
                        }
                    }

                    if (!found)
                        if (!arg.StartsWith("--engine="))
                            casperArgs.Add(arg);
                }              

                var casperCommand = new List<string>();
                casperCommand.AddRange(engineArgs);
                casperCommand.AddRange(new[] {
                        @"CasperJs\bin\bootstrap.js",
                        "--casper-path=CasperJs",
                        "--cli"
                    });
                casperCommand.AddRange(casperArgs);

                ProcessStartInfo psi = new ProcessStartInfo();

                psi.FileName = engineExecutable;
                psi.WorkingDirectory = exeDir;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.Arguments = String.Join(" ", casperCommand.ToArray());
                
                TestResult currentResult = null;
                try
                {
                    Process p = Process.Start(psi);
                    string error = null;
                    while (!p.StandardOutput.EndOfStream)
                    {
                        string line = p.StandardOutput.ReadLine();
                        if (!string.IsNullOrEmpty(line)) frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Informational, line);
                        //Is it an error?
                        if (line.Contains("CasperError"))
                        {
                            error = line;
                        }
                        if (error != null)
                        {
                            error += line;
                        }
                        else
                        {
                            //Is it a test name?
                            var testName = Regex.Match(line, "^# (.+?)$");
                            if (testName.Success)
                            {
                                //record previous result
                                if (currentResult != null) frameworkHandle.RecordResult(currentResult);
                                var testCase = sourceGroup.FirstOrDefault(x => x.DisplayName == testName.Result("$1"));
                                if (testCase != null)
                                {
                                    currentResult = new TestResult(testCase);
                                    frameworkHandle.RecordStart(testCase);
                                    currentResult.Outcome = TestOutcome.Passed;
                                }
                            }
                            //Is it a fail?
                            if (line.StartsWith("FAIL"))
                            {
                                currentResult.Outcome = TestOutcome.Failed;
                            }
                        }
                    }
                    if (error != null)
                    {
                        frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Error, error);
                    }
                    //record last result
                    if (currentResult != null) frameworkHandle.RecordResult(currentResult);
                    p.WaitForExit();
                    if(File.Exists(tempFile)) File.Delete(tempFile);
                }catch(Exception ex)
                {
                    frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Error, ex.Message);
                    throw ex;
                }
            }
        }

        public void Cancel()
        {
            cancelled = true;
        }
        public static readonly Uri ExecutorUri = new Uri(CjsTestContainerDiscoverer.ExecutorUriString);

    }
}
