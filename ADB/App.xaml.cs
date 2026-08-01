using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using ADB_Tool_Automation_Post_FB.Helpers;

namespace ADB
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Assembly assembly = Assembly.GetExecutingAssembly();
            string executablePath = assembly.Location;
            string configurationPath = ConfigurationManager.OpenExeConfiguration(
                ConfigurationUserLevel.None).FilePath;
            string commit = ConfigurationManager.AppSettings["Build.CommitSha"] ?? "unknown";
            string timestamp = File.Exists(executablePath)
                ? File.GetLastWriteTimeUtc(executablePath).ToString("O") : "unknown";
            Logger.LogInfo($"[Build Provenance] BuildCommitSha='{commit}', BuildTimestampUtc='{timestamp}', AssemblyVersion='{assembly.GetName().Version}', ExecutablePath='{executablePath}', ConfigurationPath='{configurationPath}'");
        }
    }
}
