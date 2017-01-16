﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Monitorian.Models;
using Monitorian.Models.Monitor;
using Monitorian.Models.Watcher;
using Monitorian.ViewModels;
using Monitorian.Views;

namespace Monitorian
{
	public class MainController
	{
		private readonly Application _current = Application.Current;

		public Settings Settings { get; }

		private readonly SettingsChangeWatcher _settingsWatcher;
		private readonly PowerChangeWatcher _powerWatcher;
		private readonly BrightnessChangeWatcher _brightnessWatcher;

		public ObservableCollection<MonitorViewModel> Monitors { get; } = new ObservableCollection<MonitorViewModel>();
		
		public NotifyIconComponent NotifyIconComponent { get; }

		public MainController()
		{
			Settings = new Settings();

			NotifyIconComponent = new NotifyIconComponent();
			NotifyIconComponent.MouseLeftButtonClick += OnMouseLeftButtonClick;
			NotifyIconComponent.MouseRightButtonClick += OnMouseRightButtonClick;

			_settingsWatcher = new SettingsChangeWatcher();
			_powerWatcher = new PowerChangeWatcher();
			_brightnessWatcher = new BrightnessChangeWatcher();
		}

		public async Task Initiate(RemotingAgent agent)
		{
			if (agent == null)
				throw new ArgumentNullException(nameof(agent));

			Settings.Load();

			var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
			LanguageService.Switch(args);

			var dpi = VisualTreeHelperAddition.GetNotificationAreaDpi();
			NotifyIconComponent.ShowIcon("pack://application:,,,/Resources/Brightness.ico", dpi, ProductInfo.Title);

			var window = new MainWindow(this);
			_current.MainWindow = window;
			_current.MainWindow.DpiChanged += OnDpiChanged;

			if (!args.Contains(RegistryService.Arguments))
				_current.MainWindow.Show();

			agent.ShowRequested += OnShowRequested;

			await ScanAsync();

			_settingsWatcher.Start(async () => await ScanAsync());
			_powerWatcher.Start(async () => await ScanAsync());
			_brightnessWatcher.Start((instanceName, brightness) => Update(instanceName, brightness));
		}

		private void OnDpiChanged(object sender, DpiChangedEventArgs e)
		{
			NotifyIconComponent.AdjustIcon(e.NewDpi);
		}

		public void End()
		{
			foreach (var monitor in Monitors)
				monitor.Dispose();

			NotifyIconComponent.Dispose();

			Settings.Save();

			_settingsWatcher.Stop();
			_powerWatcher.Stop();
			_brightnessWatcher.Stop();
		}

		private void OnMouseLeftButtonClick(object sender, EventArgs e)
		{
			ShowMainWindow();
		}

		private void OnMouseRightButtonClick(object sender, Point e)
		{
			ShowMenuWindow(e);
		}

		private void OnShowRequested(object sender, EventArgs e)
		{
			_current.Dispatcher.Invoke(() => ShowMainWindow());
		}

		private async void ShowMainWindow()
		{
			if (!((MainWindow)_current.MainWindow).IsReady)
				return;

			if (_current.MainWindow.Visibility != Visibility.Visible)
			{
				_current.MainWindow.Show();
				_current.MainWindow.Activate();
			}
			await UpdateAsync();
		}

		private void ShowMenuWindow(Point e)
		{
			var window = new MenuWindow(e);
			window.Show();
		}

		private readonly int _largestCount = 4;

		public async Task ScanAsync()
		{
			var scanTime = DateTime.Now;

			var oldMonitors = Monitors.ToList();

			foreach (var item in await Task.Run(() => MonitorManager.EnumerateMonitors()))
			{
				var oldMonitor = Monitors.FirstOrDefault(x =>
					string.Equals(x.DeviceInstanceId, item.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
				if (oldMonitor != null)
				{
					oldMonitors.Remove(oldMonitor);
					item.Dispose();
					continue;
				}

				var newMonitor = new MonitorViewModel(item);
				if (Monitors.Count < _largestCount)
				{
					newMonitor.UpdateBrightness();
					newMonitor.IsTarget = true;
				}
				Monitors.Add(newMonitor);
			}

			foreach (var oldMonitor in oldMonitors)
			{
				oldMonitor.Dispose();
				Monitors.Remove(oldMonitor);
			}

			await Task.WhenAll(Monitors
				.Take(_largestCount)
				.Where(x => x.UpdateTime < scanTime)
				.Select(x => Task.Run(() =>
				{
					x.UpdateBrightness();
					x.IsTarget = true;
				})));
		}

		public async Task UpdateAsync()
		{
			await Task.WhenAll(Monitors.Select(async x => await Task.Run(() => x.UpdateBrightness())));
		}

		public void Update(string instanceName, int brightness)
		{
			var monitor = Monitors.FirstOrDefault(x => instanceName.StartsWith(x.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
			if (monitor != null)
			{
				monitor.UpdateBrightness(brightness);
			}
		}
	}
}