using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using CjsTestAdapter.EventWatchers;
using CjsTestAdapter.EventWatchers.EventArgs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace CjsTestAdapter
{
    [Export(typeof(ITestContainerDiscoverer))]
    public class CjsTestContainerDiscoverer : ITestContainerDiscoverer
    {
        public const string ExecutorUriString = "executor://CjsTestAdapter";

        public event EventHandler TestContainersUpdated;
        private IServiceProvider serviceProvider;
        private ILogger logger;
        private ISolutionEventsListener solutionListener;
        private ITestFilesUpdateWatcher testFilesUpdateWatcher;
        private ITestFileAddRemoveListener testFilesAddRemoveListener;
        private bool initialContainerSearch;
        private readonly List<ITestContainer> cachedContainers;
        protected string FileExtension { get { return ".js"; } }
        public Uri ExecutorUri { get { return new System.Uri(ExecutorUriString); } }
        public IEnumerable<ITestContainer> TestContainers { get { return GetTestContainers(); } }

        [ImportingConstructor]
        public CjsTestContainerDiscoverer(
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            ILogger logger,
            ISolutionEventsListener solutionListener,
            ITestFilesUpdateWatcher testFilesUpdateWatcher,
            ITestFileAddRemoveListener testFilesAddRemoveListener)
        {
            initialContainerSearch = true;
            cachedContainers = new List<ITestContainer>();
            this.serviceProvider = serviceProvider;
            this.logger = logger;
            this.solutionListener = solutionListener;
            this.testFilesUpdateWatcher = testFilesUpdateWatcher;
            this.testFilesAddRemoveListener = testFilesAddRemoveListener;


            this.testFilesAddRemoveListener.TestFileChanged += OnProjectItemChanged;
            this.testFilesAddRemoveListener.StartListeningForTestFileChanges();

            this.solutionListener.SolutionUnloaded += SolutionListenerOnSolutionUnloaded;
            this.solutionListener.SolutionProjectChanged += OnSolutionProjectChanged;
            this.solutionListener.StartListeningForChanges();

            this.testFilesUpdateWatcher.FileChangedEvent += OnProjectItemChanged;

            //TODO implement file watcher that works within the visual studio environment
            //IVsDocDataFileChangeControl
            //https://msdn.microsoft.com/en-us/library/microsoft.visualstudio.shell.interop.ivsdocdatafilechangecontrol.aspx
        }

        private void OnTestContainersChanged()
        {
            if (TestContainersUpdated != null && !initialContainerSearch)
            {
                TestContainersUpdated(this, EventArgs.Empty);
            }
        }

        private void SolutionListenerOnSolutionUnloaded(object sender, EventArgs eventArgs)
        {
            initialContainerSearch = true;
        }

        private void OnSolutionProjectChanged(object sender, SolutionEventsListenerEventArgs e)
        {
            if (e != null)
            {
                var files = FindCjsFiles(e.Project);
                if (e.ChangedReason == SolutionChangedReason.Load)
                {
                    UpdateFileWatcher(files, true);
                }
                else if (e.ChangedReason == SolutionChangedReason.Unload)
                {
                    UpdateFileWatcher(files, false);
                }
            }

            // Do not fire OnTestContainersChanged here.
            // This will cause us to fire this event too early before the UTE is ready to process containers and will result in an exception.
            // The UTE will query all the TestContainerDiscoverers once the solution is loaded.
        }

        private void UpdateFileWatcher(IEnumerable<string> files, bool isAdd)
        {
            foreach (var file in files)
            {
                if (isAdd)
                {
                    testFilesUpdateWatcher.AddWatch(file);
                    AddTestContainerIfTestFile(file);
                }
                else
                {
                    testFilesUpdateWatcher.RemoveWatch(file);
                    RemoveTestContainer(file);
                }
            }
        }


        private void OnProjectItemChanged(object sender, TestFileChangedEventArgs e)
        {
            if (e != null)
            {
                // Don't do anything for files we are sure can't be test files
                if (!IsCjsFile(e.File)) return;

                switch (e.ChangedReason)
                {
                    case TestFileChangedReason.Added:
                        testFilesUpdateWatcher.AddWatch(e.File);
                        AddTestContainerIfTestFile(e.File);

                        break;
                    case TestFileChangedReason.Removed:
                        testFilesUpdateWatcher.RemoveWatch(e.File);
                        RemoveTestContainer(e.File);

                        break;
                    //If you're editing from VS this will never fire.
                    //http://stackoverflow.com/questions/680698/why-doesnt-filesystemwatcher-detect-changes-from-visual-studio
                    case TestFileChangedReason.Changed:
                        AddTestContainerIfTestFile(e.File);
                        break;
                }

                OnTestContainersChanged();
            }
        }

        private void AddTestContainerIfTestFile(string file)
        {
            var isTestFile = IsTestFile(file);
            RemoveTestContainer(file); // Remove if there is an existing container

            // If this is a test file
            if (isTestFile)
            {
                var container = new CjsTestContainer(this, file, ExecutorUri);
                cachedContainers.Add(container);
            }
        }

        private void RemoveTestContainer(string file)
        {
            var index = cachedContainers.FindIndex(x => x.Source.Equals(file, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                cachedContainers.RemoveAt(index);
            }
        }

        private IEnumerable<ITestContainer> GetTestContainers()
        {
            if (initialContainerSearch)
            {
                cachedContainers.Clear();
                var cjsFiles = FindCjsFiles();
                UpdateFileWatcher(cjsFiles, true);
                initialContainerSearch = false;
            }

            return cachedContainers;
        }

        private IEnumerable<string> FindCjsFiles()
        {
            var solution = (IVsSolution)serviceProvider.GetService(typeof(SVsSolution));
            var loadedProjects = solution.EnumerateLoadedProjects(__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION).OfType<IVsProject>();

            return loadedProjects.SelectMany(FindCjsFiles).ToList();
        }

        private IEnumerable<string> FindCjsFiles(IVsProject project)
        {
            return from item in VsSolutionHelper.GetProjectItems(project)
                   where IsTestFile(item)
                   select item;
        }

        private static bool IsCjsFile(string path)
        {
            //TODO: coffee and typescript
            return ".js".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        private bool IsTestFile(string path)
        {
            try
            {
                return IsCjsFile(path);
            }
            catch (IOException e)
            {
                logger.Log(MessageLevel.Error, "IO error when detecting a test file during Test Container Discovery" + e.Message);
            }

            return false;
        }


        public void Dispose()
        {
            Dispose(true);
            // Use SupressFinalize in case a subclass
            // of this type implements a finalizer.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (testFilesUpdateWatcher != null)
                {
                    testFilesUpdateWatcher.FileChangedEvent -= OnProjectItemChanged;
                    ((IDisposable)testFilesUpdateWatcher).Dispose();
                    testFilesUpdateWatcher = null;
                }

                if (testFilesAddRemoveListener != null)
                {
                    testFilesAddRemoveListener.TestFileChanged -= OnProjectItemChanged;
                    testFilesAddRemoveListener.StopListeningForTestFileChanges();
                    testFilesAddRemoveListener = null;
                }

                if (solutionListener != null)
                {
                    solutionListener.SolutionProjectChanged -= OnSolutionProjectChanged;
                    solutionListener.StopListeningForChanges();
                    solutionListener = null;
                }
            }
        }


    }
}
