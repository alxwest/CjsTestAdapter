using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.IO;
using Jint.Parser;
using Jint.Parser.Ast;

namespace CjsTestAdapter
{
    [ExtensionUri(CjsTestContainerDiscoverer.ExecutorUriString)]
    public class CjsTestExecutor : ITestExecutor
    {
        private static string[] extensions = new string[] { "js", "ts", "coffee" };
       private bool cancelled;

        public void RunTests(IEnumerable<string> sources, IRunContext runContext,
            IFrameworkHandle frameworkHandle)
        {
            IEnumerable<TestCase> tests = CjsTestDiscoverer.GetTests(sources, null, frameworkHandle);
            RunTests(tests, runContext, frameworkHandle);
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext,
               IFrameworkHandle frameworkHandle)
        {
            cancelled = false;
            
            foreach (var fileGroup in tests.GroupBy(x => x.CodeFilePath))
            {
                if (cancelled) break;
                try
                {
                    string jsTestFile = fileGroup.Key;
                    var engineArgs = new List<string>();

                    string codeBase = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;
                    UriBuilder uri = new UriBuilder(codeBase);
                    string path = Uri.UnescapeDataString(uri.Path);
                    string exeDir = Path.GetDirectoryName(path);
                    //Create temp folder for tests.
                    var subDir = Path.GetDirectoryName(jsTestFile).Substring(runContext.SolutionDirectory.Length);
                    var tmpGuid = Guid.NewGuid().ToString();
                    var tempDir = Path.Combine(Path.GetTempPath(), tmpGuid);
                    var subTempDir = Path.Combine(tmpGuid, subDir);
                    var absTempPath = Path.Combine(Path.GetTempPath(), subTempDir);
                    copyScriptAndRequired(jsTestFile, absTempPath);
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
                    foreach (string arg in new string[] { "test", Path.Combine(absTempPath, Path.GetFileName(jsTestFile)), "--ignore-ssl-errors=true" })
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

                    psi.FileName = Path.Combine(exeDir, engineExecutable);
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
                                    var testCase = fileGroup.FirstOrDefault(x => x.DisplayName == testName.Result("$1"));
                                    if (testCase != null)
                                    {
                                        //record previous result
                                        if (currentResult != null) frameworkHandle.RecordResult(currentResult);
                                        //create new result
                                        currentResult = new TestResult(testCase);
                                        frameworkHandle.RecordStart(testCase);
                                        currentResult.Outcome = TestOutcome.Passed;
                                    }
                                }
                                //Is it a fail?
                                if (line.StartsWith("FAIL"))
                                {
                                    currentResult.Outcome = TestOutcome.Failed;
                                    currentResult.ErrorMessage = line;
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
                        //Delete the temp dir.
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    } catch (Exception ex)
                    {
                        frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Error, ex.Message);
                        throw ex;
                    }
                }catch(ParserException ex)
                {
                    frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Error, 
                       string.Format("Could not parse file {0} due to syntax error on line {1}", ex.Source, ex.LineNumber));
                    frameworkHandle.SendMessage(Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel.Error, 
                        string.Format("ParserException: {0}", ex.Description));
                }
            }
        }

        public void Cancel()
        {
            cancelled = true;
        }
        public static readonly Uri ExecutorUri = new Uri(CjsTestContainerDiscoverer.ExecutorUriString);

        private void copyScriptAndRequired(string codeFile, string tempDir)
        {
            var fileName = Path.GetFileName(codeFile);
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            File.Copy(codeFile, Path.Combine(tempDir, fileName), true);

            var code = File.ReadAllText(codeFile);
            var parser = new JavaScriptParser();
            var program = parser.Parse(code, new ParserOptions { Tolerant = true });

            //get any "global var x = require('whatever');"
            var required = program.VariableDeclarations.Where(x => x.Declarations.Any()
                && x.Declarations.First().Init.Type == SyntaxNodes.CallExpression
                && x.Declarations.First().Init.As<CallExpression>().Callee.Type == SyntaxNodes.Identifier
                && x.Declarations.First().Init.As<CallExpression>().Callee.As<Identifier>().Name == "require"
            )
            .Select(x => x.Declarations.First().Init.As<CallExpression>().Arguments.First().As<Literal>().Value.ToString());

            foreach (var require in required)
            {
                foreach (var ext in extensions)
                {
                    var file = string.Format("{0}.{1}", Path.Combine(Path.GetDirectoryName(codeFile), require), ext);
                    if (File.Exists(file))
                    {
                        copyScriptAndRequired(file, Path.GetDirectoryName(string.Format("{0}.{1}", Path.Combine(tempDir, require), ext)));
                    }
                }
            }

        }

    }
}
