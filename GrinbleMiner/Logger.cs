/*
   Copyright 2019 Lip Wee Yeo Amano

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrinbleMiner
{
    public static class Logger
    {

        #region Enums and Declarations

        public enum Level
        {
            Verbose,
            Debug,
            Info,
            Warn,
            Error,
            None
        }

        public static Level MinimumLogLevel;
        public static Level MinimumFileLogLevel;

        public static bool IsLogToFile = false;

        private static readonly object _lockConsole = new object();
        private static readonly object _lockFile = new object();

        #endregion

        #region Public methods

        public static void Log(Exception ex, string prefix = null)
        {
            StackFrame[] stackFrames = null;
            try
            {
                stackFrames =
                    new StackTrace().GetFrames().Where(f =>
                    f.HasMethod() ? !f.GetMethod().Name.StartsWith("log", StringComparison.OrdinalIgnoreCase) : true).
                    ToArray();
            }
            catch { }

            var exLevel = 0;
            var currentEx = ex;
            var error = string.IsNullOrWhiteSpace(prefix)
                ? new StringBuilder("An exception has occured:")
                : new StringBuilder($"{prefix} An exception has occured:");
            try
            {
                while (currentEx != null)
                {
                    var exTypeName = currentEx.GetType().Name;
                    var stackFrame = (exLevel > (stackFrames?.Length ?? 0) - 1) ? null : stackFrames[exLevel];

                    exLevel++; error.AppendLine();

                    if (stackFrame == null) error.AppendFormat("({0}) {1}", exTypeName, currentEx.Message);
                    else
                    {
                        var methodPath = stackFrame.GetMethod().DeclaringType.FullName + "." + stackFrame.GetMethod().Name;
                        error.AppendFormat("({0}) {1} [from {2}]", exTypeName, currentEx.Message, methodPath);
                    }

                    currentEx = currentEx.InnerException;
                }
            }
            catch { }
#if DEBUG
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                error.AppendLine(ex.StackTrace);
#endif
            Log(Level.Error, error.ToString());
        }

        public static void Log(Level level, string message, params object[] args)
        {
            if (args.Length > 0)
                message = string.Format(message, args);

            if (level != Level.None)
                message = string.Format("[{0}] [{1}] {2}", level.ToString(), GetCurrentTimestamp(), message);

            if (level >= MinimumLogLevel)
                Task.Factory.StartNew(() => LogConsole(level, message), TaskCreationOptions.PreferFairness);

            if (IsLogToFile && level >= MinimumFileLogLevel)
                Task.Factory.StartNew(() => LogFile(message), TaskCreationOptions.PreferFairness);
        }

        public static void Log(ConsoleColor customColor, Level level, string message, params object[] args)
        {
            if (args.Length > 0)
                message = string.Format(message, args);

            if (level != Level.None)
                message = string.Format("[{0}] [{1}] {2}", level.ToString(), GetCurrentTimestamp(), message);

            if (level >= MinimumLogLevel)
                Task.Factory.StartNew(() => LogConsole(customColor, message), TaskCreationOptions.PreferFairness);

            if (IsLogToFile && level >= MinimumFileLogLevel)
                Task.Factory.StartNew(() => LogFile(message), TaskCreationOptions.PreferFairness);
        }

        #endregion

        #region Private methods

        private static string GetCurrentTimestamp() => string.Format("{0:s}", DateTime.Now);

        private static string LogFileFormat() => $"{DateTime.Today:yyyy-MM-dd}.log";

        private static string LogFilePath() => Path.Combine(Program.GetAppDirPath(), "Log", LogFileFormat());

        private static void LogConsole(Level level, string message)
        {
            lock (_lockConsole)
            {
                switch (level)
                {
                    case Level.Verbose:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;
                    case Level.Debug:
                        Console.ForegroundColor = ConsoleColor.Blue;
                        break;
                    case Level.Warn:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case Level.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        Console.ResetColor();
                        break;
                }
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        private static void LogConsole(ConsoleColor customColor, string message)
        {
            lock (_lockConsole)
            {
                Console.ForegroundColor = customColor;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        private static void LogFile(string message)
        {
            lock (_lockFile)
            {
                var logFilePath = LogFilePath();
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(logFilePath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

                    using (var logStream = File.AppendText(logFilePath))
                    {
                        logStream.WriteLine(message);
                        logStream.Close();
                    }
                }
                catch
                {
                    Console.WriteLine(string.Format("[ERROR] Failed writing to log file '{0}'", logFilePath));
                }
            }
        }

        #endregion

    }
}
