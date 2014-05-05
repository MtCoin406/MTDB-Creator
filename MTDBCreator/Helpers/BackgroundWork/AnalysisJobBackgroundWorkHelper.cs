﻿#region Namespaces

using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MTDBCreator.ViewModels;
using MTDBCreator.Windows;
using MTDBFramework.Data;
using MTDBFramework.IO;
using MTDBFramework.UI;

#endregion

namespace MTDBCreator.Helpers.BackgroundWork
{
    public class AnalysisJobBackgroundWorkHelper : IBackgroundWorkHelper
    {
        public AnalysisJobBackgroundWorkHelper(AnalysisJobViewModel analysisJobViewModel)
        {
            m_AnalysisJobViewModel = analysisJobViewModel;
        }

        public void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var analysisJobProcessor = new AnalysisJobProcessor(m_AnalysisJobViewModel.Options);
            analysisJobProcessor.ProgressChanged += analysisJobProcessor_ProgressChanged;

            try
            {
                e.Result = analysisJobProcessor.Process(m_AnalysisJobViewModel.AnalysisJobItems);
            }
            catch (Exception ex)
            {
                e.Result = ex;
            }
        }

        public void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            HostProcessWindow.MainProgressBar.IsIndeterminate = false;

            var userStates = e.UserState as object[];

            if (userStates != null)
            {
                var total = Convert.ToInt32(userStates[0].ToString());
                var status = userStates[1].ToString();

                HostProcessWindow.MainProgressBar.Value = e.ProgressPercentage;
                HostProcessWindow.MainProgressBar.Maximum = total;
                HostProcessWindow.Status = status;
            }
        }

        public void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result is Exception)
            {
                var ex = e.Result as Exception;

                ErrorHelper.WriteExceptionTraceInformation(ex);

                MessageBox.Show(String.Format("The following exception has occurred:{0}{0}{1}", Environment.NewLine, ex.Message), Application.Current.MainWindow.Tag.ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }

            HostProcessWindow.DialogResult = !(e.Result is Exception);
            HostProcessWindow.Close();
        }

        public object Argument
        {
            get { return m_AnalysisJobViewModel; }
        }
        public object Result
        {
            get { return m_AnalysisJobViewModel.AnalysisJobItems; }
        }

        public ProcessWindow HostProcessWindow { get; set; }

        private void analysisJobProcessor_ProgressChanged(object sender, MtdbProgressChangedEventArgs e)
        {
            var analysisJobItem = e.UserObject as AnalysisJobItem;

            if (analysisJobItem != null)
            {
                HostProcessWindow.MainBackgroundWorker.ReportProgress(e.Current, new object[] { e.Total.ToString(), String.Concat("Processing analysis file: ", Path.GetFileName(analysisJobItem.FilePath)) });
            }
        }

        private AnalysisJobViewModel m_AnalysisJobViewModel { get; set; }
    }
}