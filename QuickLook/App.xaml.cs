﻿// Copyright © 2017 Paddy Xu
// 
// This file is part of QuickLook program.
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using QuickLook.Helpers;
using QuickLook.Properties;

namespace QuickLook
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static readonly bool IsUWP = ProcessHelper.IsRunningAsUWP();
        public static readonly bool Is64Bit = Environment.Is64BitProcess;
        public static readonly string AppFullPath = Assembly.GetExecutingAssembly().Location;
        public static readonly string AppPath = Path.GetDirectoryName(AppFullPath);

        private bool _isFirstInstance;
        private Mutex _isRunning;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                MessageBox.Show(((Exception) args.ExceptionObject).ToString());

                Shutdown();
            };

            base.OnStartup(e);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            EnsureFirstInstance();

            if (!_isFirstInstance)
            {
                // second instance: preview this file
                if (e.Args.Any() && (Directory.Exists(e.Args.First()) || File.Exists(e.Args.First())))
                    RemoteCallShowPreview(e);
                // second instance: duplicate
                else
                    MessageBox.Show(TranslationHelper.GetString("APP_SECOND"));

                Shutdown();
                return;
            }

            UpgradeSettings();
            CheckUpdate();
            RunListener(e);

            // first instance: run and preview this file
            if (e.Args.Any() && (Directory.Exists(e.Args.First()) || File.Exists(e.Args.First())))
                RemoteCallShowPreview(e);
        }

        private void CheckUpdate()
        {
            if (DateTime.Now - Settings.Default.LastUpdate < TimeSpan.FromDays(7))
                return;

            Task.Delay(120 * 1000).ContinueWith(_ => Updater.CheckForUpdates(true));
            Settings.Default.LastUpdate = DateTime.Now;
            Settings.Default.Save();
        }

        private void UpgradeSettings()
        {
            if (!Settings.Default.Upgraded)
                return;

            Settings.Default.Upgrade();
            Settings.Default.Upgraded = false;
            Settings.Default.Save();
        }

        private void RemoteCallShowPreview(StartupEventArgs e)
        {
            PipeServerManager.SendMessage(e.Args.First());
        }

        private void RunListener(StartupEventArgs e)
        {
            TrayIconManager.GetInstance();
            if (!e.Args.Contains("/autorun") && !IsUWP)
                TrayIconManager.GetInstance()
                    .ShowNotification("", TranslationHelper.GetString("APP_START"));
            if (e.Args.Contains("/first"))
                AutoStartupHelper.CreateAutorunShortcut();

            NativeMethods.QuickLook.Init();

            PluginManager.GetInstance();
            BackgroundListener.GetInstance();
            PipeServerManager.GetInstance().MessageReceived +=
                (msg, ea) => Dispatcher.BeginInvoke(
                    new Action(() => ViewWindowManager.GetInstance().InvokeViewer(msg as string, true)),
                    DispatcherPriority.ApplicationIdle);
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            if (_isFirstInstance)
            {
                _isRunning.ReleaseMutex();

                PipeServerManager.GetInstance().Dispose();
                TrayIconManager.GetInstance().Dispose();
                BackgroundListener.GetInstance().Dispose();
                ViewWindowManager.GetInstance().Dispose();
            }
        }

        private void EnsureFirstInstance()
        {
            _isRunning = new Mutex(true, "QuickLook.App.Mutex", out _isFirstInstance);
        }
    }
}