using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using Jint.Parser;
using System.Diagnostics;
using Jint.Parser.Ast;

namespace CjsTestAdapter
{

    [DefaultExecutorUri(CjsTestContainerDiscoverer.ExecutorUriString)]
    [FileExtension(".js")]
    public class CjsTestDiscoverer : ITestDiscoverer
    {

        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext,
            IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            GetTests(sources, discoverySink, logger);
        }

        public static List<TestCase> GetTests(IEnumerable<string> sources, ITestCaseDiscoverySink discoverySink, IMessageLogger logger)
        {

            List<TestCase> testCases = new List<TestCase>();
            foreach (string source in sources)
            {
                var script = File.ReadAllText(source);
                try
                {
                    var parser = new JavaScriptParser();
                    var program = parser.Parse(script, new ParserOptions { Tolerant = true });

                    var tests = program.Body.Where(x => x.Type == Jint.Parser.Ast.SyntaxNodes.ExpressionStatement
                                                        && x.As<ExpressionStatement>().Expression.Type == SyntaxNodes.CallExpression

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee != null
                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.Type == SyntaxNodes.MemberExpression

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Property != null
                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Property.Type == SyntaxNodes.Identifier

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Property.As<Identifier>().Name == "begin"

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object != null
                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.Type == SyntaxNodes.MemberExpression

                                                         && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Property != null
                                                         && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Property.Type == SyntaxNodes.Identifier

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Property.As<Identifier>().Name == "test"

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Object != null
                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Object.Type == SyntaxNodes.Identifier

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Callee.As<MemberExpression>().Object.As<MemberExpression>().Object.As<Identifier>().Name == "casper"

                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Arguments.Any()
                                                        && x.As<ExpressionStatement>().Expression.As<CallExpression>().Arguments.First().Type == SyntaxNodes.Literal

                                                        ).Select
                                                        (x => new
                                                        {
                                                            Name = x.As<ExpressionStatement>().Expression.As<CallExpression>().Arguments.First().As<Literal>().Value.ToString(),
                                                            Column = x.Location.Start.Column,
                                                            Line = x.Location.Start.Line
                                                        });

                    foreach (var test in tests)
                    {
                        var testName = test.Name;
                        if (!string.IsNullOrWhiteSpace(testName))
                        {
                            var testcase = new TestCase(testName, CjsTestExecutor.ExecutorUri, source)
                            {
                                CodeFilePath = source,
                                LineNumber = test.Line,
                            };
                            if (discoverySink != null)
                            {
                                discoverySink.SendTestCase(testcase);
                            }
                            testCases.Add(testcase);
                        }
                    }
                }
                catch (ParserException ex)
                {
                    logger.SendMessage(TestMessageLevel.Error,
                       string.Format("Could not parse file {0} due to syntax error on line {1}", source, ex.LineNumber));
                    logger.SendMessage(TestMessageLevel.Error,
                        string.Format("ParserException: {0}", ex.Description));
                }
            }

            return testCases;
        }

    }
}