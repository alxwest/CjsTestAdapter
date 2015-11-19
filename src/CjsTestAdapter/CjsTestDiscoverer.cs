using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;

namespace CjsTestAdapter
{

    [DefaultExecutorUri(CjsTestContainerDiscoverer.ExecutorUriString)]
    [FileExtension(".js")]
    public class CjsTestDiscoverer : ITestDiscoverer
    {
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext,
            IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            GetTests(sources, discoverySink);
        }

        public static List<TestCase> GetTests(IEnumerable<string> sources, ITestCaseDiscoverySink discoverySink)
        {
            List<TestCase> tests = new List<TestCase>();
            foreach (string source in sources)
            {
                var script = File.ReadAllText(source);
                var testNodes = Regex.Matches(script, @"casper\.test\.begin\('(.+?)',");

                foreach (Match testNode in testNodes)
                {
                    var testName = testNode.Result("$1");
                    if (!string.IsNullOrWhiteSpace(testName))
                    {
                        var testcase = new TestCase(testName, CjsTestExecutor.ExecutorUri, source)
                            {
                                CodeFilePath = source,
                            };
                        if (discoverySink != null)
                        {
                            discoverySink.SendTestCase(testcase);
                        }
                        tests.Add(testcase);
                    }

                }

            }
            return tests;
        }
    }
}