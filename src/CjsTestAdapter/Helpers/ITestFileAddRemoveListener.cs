using System;
using CjsTestAdapter.EventWatchers.EventArgs;

namespace CjsTestAdapter.EventWatchers
{
    public interface ITestFileAddRemoveListener
    {
        event EventHandler<TestFileChangedEventArgs> TestFileChanged;
        void StartListeningForTestFileChanges();
        void StopListeningForTestFileChanges();
    }
}