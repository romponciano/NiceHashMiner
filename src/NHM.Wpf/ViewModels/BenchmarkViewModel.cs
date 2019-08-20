﻿using System;
using NHM.Common.Enums;
using NiceHashMiner.Benchmarking;
using NiceHashMiner.Configs;
using NiceHashMiner.Mining;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Timers;
using System.Windows;
using NHM.Wpf.ViewModels.Models;
using MessageBox = System.Windows.MessageBox;

namespace NHM.Wpf.ViewModels
{
    public class BenchmarkViewModel : BaseVM
    {
        private readonly Timer _dotTimer = new Timer(1000);

        #region ListView selection

        private IEnumerable<ComputeDevice> _devices;

        public IEnumerable<ComputeDevice> Devices
        {
            get => _devices;
            set
            {
                // Remove old handlers
                if (_devices != null)
                {
                    foreach (var dev in _devices)
                    {
                        dev.PropertyChanged -= UpdateBenchPending;
                        foreach (var algo in dev.AlgorithmSettings)
                        {
                            algo.PropertyChanged -= UpdateBenchPending;
                        }
                    }
                }

                _devices = value;

                // Add new handlers
                if (_devices != null)
                {
                    foreach (var dev in _devices)
                    {
                        dev.PropertyChanged += UpdateBenchPending;
                        foreach (var algo in dev.AlgorithmSettings)
                        {
                            algo.PropertyChanged += UpdateBenchPending;
                        }
                    }
                }

                SelectedDev = null;
                SelectedAlgo = null;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<BenchAlgo> SelectedAlgos { get; }

        /// <summary>
        /// True iff a device is selected and enabled
        /// </summary>
        public bool AlgosEnabled => SelectedDev?.Enabled ?? false;

        public bool SideBarEnabled => SelectedAlgo?.Algo.Enabled ?? false;

        private ComputeDevice _selectedDev;
        public ComputeDevice SelectedDev
        {
            get => _selectedDev;
            set
            {
                if (value == _selectedDev) return;

                // Remove old handler
                if (_selectedDev != null)
                    _selectedDev.PropertyChanged -= SelectedDevOnPropertyChanged;

                _selectedDev = value;

                DisposeBenchAlgos();
                SelectedAlgos.Clear();

                OnPropertyChanged();
                OnPropertyChanged(nameof(AlgosEnabled));

                if (_selectedDev == null) return;

                // Add new handler
                _selectedDev.PropertyChanged += SelectedDevOnPropertyChanged;

                foreach (var algo in _selectedDev.AlgorithmSettings)
                {
                    SelectedAlgos.Add(new BenchAlgo(algo));
                }
            }
        }

        private BenchAlgo _selectedAlgo;

        public BenchAlgo SelectedAlgo
        {
            get => _selectedAlgo;
            set
            {
                if (value == _selectedAlgo) return;

                // Remove old handler
                if (_selectedAlgo != null)
                    _selectedAlgo.Algo.PropertyChanged -= SelectedAlgoOnPropertyChanged;

                _selectedAlgo = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SideBarEnabled));

                if (_selectedAlgo == null) return;

                // Add new handler
                _selectedAlgo.Algo.PropertyChanged += SelectedAlgoOnPropertyChanged;
            }
        }

        private void SelectedAlgoOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_selectedAlgo.Algo.Enabled))
                OnPropertyChanged(nameof(SideBarEnabled));
        }

        private void SelectedDevOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward device enabled change notification to AlgosEnabled
            if (e.PropertyName == nameof(_selectedDev.Enabled))
                OnPropertyChanged(nameof(AlgosEnabled));
        }

        #endregion

        public bool InBenchmark => BenchmarkManager.InBenchmark;

        public bool NotInBenchmark => !InBenchmark;

        public string StartStopButtonLabel =>
            InBenchmark ? Translations.Tr("St_op benchmark") : Translations.Tr("Start _benchmark");

        public bool StartMiningAfterBench
        {
            get => BenchmarkManager.StartMiningOnFinish;
            set => BenchmarkManager.StartMiningOnFinish = value;
        }

        public AlgorithmBenchmarkSettingsType AlgoSelection
        {
            get => BenchmarkManager.Selection;
            set
            {
                BenchmarkManager.Selection = value;
                OnPropertyChanged();
            }
        }

        private BenchmarkPerformanceType _perfType = BenchmarkPerformanceType.Standard;
        public BenchmarkPerformanceType PerfType
        {
            get => _perfType;
            set
            {
                _perfType = value;
                OnPropertyChanged();
            }
        }

        private int _benchesPending;
        public int BenchesPending
        {
            get => _benchesPending;
            set
            {
                _benchesPending = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Progress));
            }
        }

        private int _benchesCompleted;
        public int BenchesCompleted
        {
            get => _benchesCompleted;
            set
            {
                _benchesCompleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Progress));
            }
        }

        public double Progress => (double) BenchesCompleted * 100 / Math.Max(1, BenchesPending);

        public BenchmarkViewModel()
        {
            SelectedAlgos = new ObservableCollection<BenchAlgo>();

            _dotTimer.Elapsed += DotTimerOnElapsed;

            BenchmarkManager.InBenchmarkChanged += BenchmarkManagerOnInBenchmarkChanged;
            BenchmarkManager.OnStepUp += BenchmarkManagerOnOnStepUp;

            UpdateBenchPending();
        }

        private void BenchmarkManagerOnOnStepUp(object sender, StepUpEventArgs e)
        {
            BenchesPending = e.AlgorithmCount;
            BenchesCompleted = e.CurrentIndex;
        }

        private void UpdateBenchPending(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedAlgo.Algo.Enabled) || e.PropertyName == nameof(SelectedDev.Enabled))
                UpdateBenchPending();
        }

        private void UpdateBenchPending()
        {
            BenchesPending = BenchmarkManager.CalcBenchDevAlgoQueue();
        }

        private void BenchmarkManagerOnInBenchmarkChanged(object sender, bool e)
        {
            OnPropertyChanged(nameof(InBenchmark));
            OnPropertyChanged(nameof(NotInBenchmark));
            OnPropertyChanged(nameof(StartStopButtonLabel));
        }

        private void DotTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            foreach (var (_, algo) in BenchmarkManager.GetStatusCheckAlgos())
            {
                SelectedAlgos?.FirstOrDefault(a => a.Algo == algo)?.IncrementTicker();
            }
        }

        public void CommitBenchmarks()
        {
            ConfigManager.CommitBenchmarks();
        }

        public void StartBenchmark()
        {
            if (Devices?.All(d => !d.Enabled) ?? true)
            {
                MessageBox.Show(NiceHashMiner.Translations.Tr("No device has been selected there is nothing to benchmark"),
                    NiceHashMiner.Translations.Tr("No device selected"),
                    MessageBoxButton.OK);
                return;
            }

            BenchmarkManager.CalcBenchDevAlgoQueue();

            if (!BenchmarkManager.HasWork)
            {
                MessageBox.Show(Translations.Tr("Current benchmark settings are already executed. There is nothing to do."),
                    Translations.Tr("Nothing to benchmark"),
                    MessageBoxButton.OK);
                return;
            }

            // Set pending status
            foreach (var devAlgoTuple in BenchmarkManager.BenchDevAlgoQueue)
            {
                foreach (var algo in devAlgoTuple.Item2) algo.SetBenchmarkPending();
            }

            BenchmarkManager.Start(PerfType);
            _dotTimer.Start();
        }

        public void StopBenchmark()
        {
            BenchmarkManager.Stop();
            _dotTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            BenchmarkManager.InBenchmarkChanged -= BenchmarkManagerOnInBenchmarkChanged;
            BenchmarkManager.OnStepUp -= BenchmarkManagerOnOnStepUp;

            DisposeBenchAlgos();

            if (Devices != null)
            {
                foreach (var dev in Devices)
                {
                    dev.PropertyChanged -= UpdateBenchPending;
                    foreach (var algo in dev.AlgorithmSettings)
                    {
                        algo.PropertyChanged -= UpdateBenchPending;
                    }
                }
            }
        }

        private void DisposeBenchAlgos()
        {
            if (SelectedAlgos == null) return;
            foreach (var algo in SelectedAlgos)
            {
                algo.Dispose();
            }
        }
    }
}