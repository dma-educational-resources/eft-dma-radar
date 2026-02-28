using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace eft_dma_radar.UI.Misc
{
    public sealed class PanelCoordinator
    {
        private static PanelCoordinator _instance;
        public static PanelCoordinator Instance => _instance ??= new PanelCoordinator();

        private readonly object _lock = new();

        private readonly Dictionary<string, bool> _panelReadyState = new();
        private readonly HashSet<string> _requiredPanels = new();

        private bool _allPanelsReady;
        private bool _allPanelsReadyEventFired;

        private TaskCompletionSource<bool> _allPanelsReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler AllPanelsReady;

        private PanelCoordinator() { }

        // MUST be called before or during panel load
        public void RegisterRequiredPanel(string panelName)
        {
            lock (_lock)
            {
                if (_allPanelsReady)
                    return;

                if (_requiredPanels.Add(panelName))
                    _panelReadyState[panelName] = false;
            }
        }

        public void SetPanelReady(string panelName)
        {
            bool becameReady;

            lock (_lock)
            {
                if (!_panelReadyState.TryGetValue(panelName, out var ready) || ready)
                    return;

                _panelReadyState[panelName] = true;
                becameReady = CheckAllPanelsReady_NoLock();
            }

            if (becameReady)
                FireAllPanelsReadyOnce();
        }

        private bool CheckAllPanelsReady_NoLock()
        {
            if (_allPanelsReady)
                return false;

            // IMPORTANT: zero panels = ready
            if (_requiredPanels.Count == 0)
            {
                _allPanelsReady = true;
                _allPanelsReadyTcs.TrySetResult(true);
                return true;
            }

            foreach (var panel in _requiredPanels)
            {
                if (!_panelReadyState.TryGetValue(panel, out var ready) || !ready)
                    return false;
            }

            _allPanelsReady = true;
            _allPanelsReadyTcs.TrySetResult(true);
            return true;
        }

        private void FireAllPanelsReadyOnce()
        {
            if (_allPanelsReadyEventFired)
                return;

            _allPanelsReadyEventFired = true;

            // Marshal back to UI thread safely if possible
            var dispatcher = Dispatcher.CurrentDispatcher;

            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => AllPanelsReady?.Invoke(this, EventArgs.Empty)));
            }
            else
            {
                Task.Run(() => AllPanelsReady?.Invoke(this, EventArgs.Empty));
            }
        }

        public bool IsPanelReady(string panelName)
        {
            lock (_lock)
                return _panelReadyState.TryGetValue(panelName, out var ready) && ready;
        }

        public async Task WaitForAllPanelsAsync(int timeoutMs = 10000)
        {
            Task waitTask;

            lock (_lock)
            {
                if (_allPanelsReady)
                    return;

                // Zero required panels → complete immediately
                if (_requiredPanels.Count == 0)
                {
                    _allPanelsReady = true;
                    _allPanelsReadyTcs.TrySetResult(true);
                    return;
                }

                waitTask = _allPanelsReadyTcs.Task;
            }

            var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));

            if (completed != waitTask)
            {
                string missing;
                lock (_lock)
                {
                    missing = string.Join(", ",
                        _requiredPanels.Where(p =>
                            !_panelReadyState.TryGetValue(p, out var ready) || !ready));
                }

                throw new TimeoutException(
                    $"Timed out waiting for panels: {missing}");
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _panelReadyState.Clear();
                _requiredPanels.Clear();
                _allPanelsReady = false;
                _allPanelsReadyEventFired = false;
                _allPanelsReadyTcs =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
