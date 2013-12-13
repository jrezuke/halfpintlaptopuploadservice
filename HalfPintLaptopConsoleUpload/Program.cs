using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using NLog.Targets;

namespace HalfPintLaptopConsoleUpload
{
    class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        static void Main(string[] args)
        {
            string computerName = Environment.MachineName;
            var dt = new DateTime(2013, 1, 1);
            var dtPrevious = dt.AddMonths(-1);
            string logName = "uploadLog_" + computerName + DateTime.Today.Month + "_" + DateTime.Today.Year + ".txt";

            var fileTarget = LogManager.Configuration.AllTargets.First(t => t.Name == "logfile") as FileTarget;
            if (fileTarget != null) fileTarget.FileName = logName;

            Logger.Info("HalfPintLaptopUploadService start");
        }
    }
}
