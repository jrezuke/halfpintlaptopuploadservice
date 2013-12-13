using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Timers;
using System.Web;
using NLog;
using NLog.Targets;

namespace HalfPintLaptopUploadService
{
    partial class HalfPintLaptopUploadService : ServiceBase
    {
        private Timer _timer;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        public HalfPintLaptopUploadService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            _timer = new Timer { Interval = 3600000, Enabled = true, AutoReset = true }; //3600000 1hour
            _timer.Start();
            _timer.Elapsed += TimerElapsed;
            StartAction();
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            StartAction();
        }

        private void StartAction()
        {
            //create the log directory if it doesn't exits
            string logFolder = ConfigurationManager.AppSettings["LogPath"];
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            string computerName = Environment.MachineName;
            string logName = "uploadServiceLog_" + computerName + DateTime.Today.Month + "_" + DateTime.Today.Year + ".txt";
            logName = Path.Combine(logFolder, logName);
            
            //name the log file based on computer name, month and year
            var fileTarget = LogManager.Configuration.AllTargets.First(t => t.Name == "logfile") as FileTarget;
            if (fileTarget != null)
            {
                fileTarget.FileName = logName;
                
            }

            
            string siteCode = DoChecksUploads();
            
            //this means that the Halfpint directory doesn't exist
            if (String.IsNullOrEmpty(siteCode))
                return;
            
            if (DateTime.Now.Hour == 1)
            {
                if (!string.IsNullOrEmpty(siteCode))
                {
                    DoNovanetUploads(siteCode, computerName);
                }
            }

            DoLogUpload(siteCode, computerName);
        }
        
        private string DoChecksUploads()
        {
            //we need the siteCode for the novanet upload
            string siteCode = string.Empty;
            Logger.Info("Starting checks upload service");

            string checksFolder = ConfigurationManager.AppSettings["ChecksPath"];
            var di = new DirectoryInfo(checksFolder);
            if (!di.Exists)
            {
                //this should get created by the CHECKS application
                //if it doesn't exist then there is no work to do so just exit
                Logger.Info("The Halfpint folder does not exist");
                return siteCode;
            }

            //create the archive directory if it doesn't exits
            string archiveFolder = ConfigurationManager.AppSettings["ChecksArchivePath"];
            if (!Directory.Exists(archiveFolder))
            {
                Directory.CreateDirectory(archiveFolder);
                Logger.Info("Created the HalfPintArchive folder");
            }

            //archive old files from the halfpint folder (files with last modified older than 7 days)
            FileInfo[] fis = di.GetFiles();
            foreach (var fi in fis)
            {
                //if the file is older than 1 week then archive
                if (fi.LastWriteTime.CompareTo(DateTime.Today.AddDays(-7)) < 0)
                {
                    fi.CopyTo(Path.Combine(archiveFolder, fi.Name), true);
                    fi.Delete();
                    Logger.Info("Archived file: " + fi.Name);
                }
            }

            //get the files from the copy directory
            string checksCopyFolder = Path.Combine(checksFolder, "Copy");
            di = new DirectoryInfo(checksCopyFolder);
            if (!di.Exists)
            {
                //this should get created by the CHECKS application
                //if it doesn't exist then there is no work to do so just exit
                Logger.Info("The Halfpint\\Copy folder does not exist");
                return siteCode;
            }

            //arcive them first
            fis = di.GetFiles();
            foreach (var fi in fis)
            {
                //if the file is older than 1 week then archive
                if (fi.LastWriteTime.CompareTo(DateTime.Today.AddDays(-7)) < 0)
                {
                    fi.MoveTo(Path.Combine(archiveFolder, fi.Name));
                    Logger.Info("Archived file: " + fi.Name);
                }

            }

            //do upload
            fis = di.GetFiles();
            foreach (var fi in fis)
            {
                if (fi.Name.IndexOf("copy", System.StringComparison.Ordinal) > -1 || fi.Name.IndexOf("Chart", System.StringComparison.Ordinal) > -1)
                {
                    //skip test files
                    if (fi.Name.StartsWith("T"))
                        continue;

                    siteCode = fi.Name.Substring(0, 2);

                    //formulate key
                    //add all the numbers in the file name
                    int key = 0;
                    foreach (var c in fi.Name)
                    {
                        if (char.IsNumber(c))
                            key += int.Parse(c.ToString());
                    }

                    key *= key;

                    int iInstitId = int.Parse(siteCode);


                    key = key * iInstitId;
                    var rnd = new Random();
                    int iRnd = rnd.Next(100000, 999999);
                    string sKey = iRnd.ToString() + key.ToString();

                    UploadFile(fi.FullName, siteCode, sKey, fi.Name);
                }

            }

            return siteCode;
        }

        private void UploadFile(string fullName, string siteCode, string key, string fileName)
        {
            Logger.Info("UploadFile: " + fileName);

            var qsCollection = HttpUtility.ParseQueryString(string.Empty);
            qsCollection["siteCode"] = siteCode;
            qsCollection["key"] = key;
            qsCollection["fileName"] = fileName;
            var queryString = qsCollection.ToString();
            using (var client = new HttpClient())
            using (var content = new MultipartFormDataContent())
            {
                var filestream = File.Open(fullName, FileMode.Open);
                content.Add(new StreamContent(filestream), "file", fileName);

                //var requestUri = "https://halfpintstudy.org/hpUpload/api/upload?" + queryString;
                //var requestUri = "http://asus1/hpuploadapi/api/upload?" + queryString;
                var requestUri = "http://joelaptop4/hpuploadapi/api/upload?" + queryString;
                var result = client.PostAsync(requestUri, content).Result;
            }
        }

        private void DoNovanetUploads(string siteCode, string computerName)
        {
            Logger.Info("Starting novanet upload service");

            //create the archive directory if it doesn't exits
            string archiveFolder = ConfigurationManager.AppSettings["NovaNetArchivesArchive"];
            if (!Directory.Exists(archiveFolder))
            {
                Directory.CreateDirectory(archiveFolder);
                Logger.Info("Created the NovaNetArchivesArchive folder");
            }   

            //check if folder exists
            string novanetFolder = ConfigurationManager.AppSettings["NovaNetArchives"];
            if (!Directory.Exists(novanetFolder))
            {
                Logger.Info("NovaNet archives folder does not exist!");
                return;
            }

            var di = new DirectoryInfo(novanetFolder);
            FileInfo[] fis = di.GetFiles();
            foreach (var fi in fis)
            {
                UploadNovaNetFile(fi.FullName, siteCode, computerName, fi.Name);
                //then archive
                fi.CopyTo(Path.Combine(archiveFolder, fi.Name), true);
                fi.Delete();
                Logger.Info("Archived file: " + fi.Name);
            }
        }

        private void DoLogUpload(string siteCode, string computerName)
        {
            Logger.Info("Starting log upload service");

            //create the archive directory if it doesn't exits
            string logsArchivesPath = ConfigurationManager.AppSettings["LogsArchivesPath"];
            if (!Directory.Exists(logsArchivesPath))
            {
                Directory.CreateDirectory(logsArchivesPath);
                Logger.Info("Created the logs archive folder");
            }

            //check for any files to be archived
            //get the pervious month and year
            var dtPrevious = DateTime.Today.AddMonths(-1);
            var previous = dtPrevious.Month.ToString() + "_" + dtPrevious.Year;
            
            string logFolder = ConfigurationManager.AppSettings["LogPath"];
            var di = new DirectoryInfo(logFolder);
            FileInfo[] fis = di.GetFiles();
            foreach (var fi in fis)
            {
                if (fi.Name.Contains(previous))
                {
                    //archive this file
                    fi.CopyTo(Path.Combine(logsArchivesPath, fi.Name), true);
                    Logger.Info("Archived file: " + fi.Name);
                    fi.Delete();
                }
                else //upload
                {
                    UploadLogFile(fi.FullName, siteCode, computerName, fi.Name);
                }
            }


        }
        
        private void UploadNovaNetFile(string fullName, string siteCode, string computerName, string fileName)
        {
            Logger.Info("Upload NovaNet File: " + fileName);

            var qsCollection = HttpUtility.ParseQueryString(string.Empty);
            qsCollection["siteCode"] = siteCode;
            qsCollection["computerName"] = computerName;
            qsCollection["fileName"] = fileName;
            var queryString = qsCollection.ToString();

            var client = new HttpClient();
            using (var content = new MultipartFormDataContent())
            {
                var filestream = File.Open(fullName, FileMode.Open);
                content.Add(new StreamContent(filestream), "file", fileName);

                //var requestUri = "https://halfpintstudy.org/hpUpload/api/NovanetUpload?" + queryString;
                //var requestUri = "http://asus1/hpuploadapi/api/NovanetUpload?" + queryString;
                var requestUri = "http://joelaptop4/hpuploadapi/api/NovanetUpload?" + queryString;
                var result = client.PostAsync(requestUri, content).Result;

            }
        }

        private void UploadLogFile(string fullName, string siteCode, string computerName, string fileName)
        {
            Logger.Info("Upload Log File: " + fileName);

            var qsCollection = HttpUtility.ParseQueryString(string.Empty);
            qsCollection["siteCode"] = siteCode;
            qsCollection["computerName"] = computerName;
            qsCollection["fileName"] = fileName;
            var queryString = qsCollection.ToString();

            var client = new HttpClient();
            using (var content = new MultipartFormDataContent())
            {
                var filestream = File.Open(fullName, FileMode.Open);
                content.Add(new StreamContent(filestream), "file", fileName);

                //var requestUri = "https://halfpintstudy.org/hpUpload/api/LogUpload?" + queryString;
                //var requestUri = "http://asus1/hpuploadapi/api/LogUpload?" + queryString;
                var requestUri = "http://joelaptop4/hpuploadapi/api/LogUpload?" + queryString;
                var result = client.PostAsync(requestUri, content).Result;

            }
        }
        protected override void OnStop()
        {
            Logger.Info("HalfPintLaptopUploadService stop");
        }


    }
}
