﻿using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Emignatik.NxFileViewer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// The MainWindow implements <see cref="ILoggerProvider"/> in order to be notified on logging events
    /// </summary>
    public partial class MainWindow : Window, ILoggerProvider
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_OnDrop(object sender, DragEventArgs e)
        {
            var dataObject = e.Data;
            if (dataObject == null) return;

            if (!dataObject.GetDataPresent(DataFormats.FileDrop)) return;

            var filePaths = dataObject.GetData(DataFormats.FileDrop) as string[];
            if (filePaths == null || filePaths.Length <= 0) return;

            var filePath = filePaths[0];
            switch (Path.GetExtension(filePath))
            {
                case ".nsp":
                    //TODO refaire marcher le drag n drop
                    //SafeLoadFile(filePath);
                    break;
            }
        }

        private void OnLog<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var dispatcher = Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) // To prevent UI thread InvalidOperationException when log event comes from another thread
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    OnLog(logLevel, eventId, state, exception, formatter);
                }));
                return;
            }

            // TODO: check if inner required
            //var exTemp = args.Exception;
            //while (exTemp != null)
            //{
            //    logEvent += " ==> " + exTemp.Message;
            //    exTemp = exTemp.InnerException;
            //}

            var tr = new TextRange(RichTextBoxLog.Document.ContentEnd, RichTextBoxLog.Document.ContentEnd)
            {
                Text = formatter(state, exception) + Environment.NewLine
            };

            SolidColorBrush color;
            if (logLevel >= LogLevel.Error)
                color = Brushes.Red;
            else if (logLevel >= LogLevel.Warning)
                color = Brushes.Orange;
            else
                color = Brushes.Blue;

            tr.ApplyPropertyValue(TextElement.ForegroundProperty, color);
        }

        private void MenuItemClearLogClick(object sender, RoutedEventArgs e)
        {
            RichTextBoxLog.Document.Blocks.Clear();
        }

        void IDisposable.Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new InternalLogger(this);
        }

        private class InternalLogger : ILogger
        {
            private readonly MainWindow _mainWindow;

            public InternalLogger(MainWindow mainWindow)
            {
                _mainWindow = mainWindow;
            }


            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _mainWindow.OnLog(logLevel, eventId, state, exception, formatter);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }
        }

    }
}