using System;
using CjsTestAdapter.EventWatchers.EventArgs;

namespace CjsTestAdapter.EventWatchers
{
    public interface ITestFilesUpdateWatcher
    {
        event EventHandler<TestFileChangedEventArgs> FileChangedEvent;
        void AddWatch(string path);
        void RemoveWatch(string path);
    }
}