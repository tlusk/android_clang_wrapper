using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ClangWrapper
{
    internal static class Program
    {
        [MTAThread]
        public static int Main(string[] args)
        {
            using (var tempFile = new TempFile())
            {
                string fileName = Assembly.GetEntryAssembly().Location;
                fileName = fileName.Remove(fileName.Length - 4) + "2.exe";

                if (args.Length > 0 && args[0].StartsWith("@"))
                {

                    string optionsFile = args[0].Substring(1);

                    List<string> lines = new List<string>();
                    foreach (string line in File.ReadLines(optionsFile))
                    {
                        if (line.StartsWith("-"))
                            lines.Add(line);
                        else
                            lines[lines.Count - 1] += " " + line;
                    }

                    System.IO.File.WriteAllLines(tempFile.Path, lines);
                    args[0] = "@" + tempFile.Path;
                }

                // Fires up a new process to run inside this one
                var process = Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = false,

                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,

                    FileName = fileName,
                    Arguments = String.Join(" ", args)
                });

                // Depending on your application you may either prioritize the IO or the exact opposite
                const ThreadPriority ioPriority = ThreadPriority.Highest;
                var outputThread = new Thread(outputReader) { Name = "ChildIO Output", Priority = ioPriority };
                var errorThread = new Thread(errorReader) { Name = "ChildIO Error", Priority = ioPriority };
                var inputThread = new Thread(inputReader) { Name = "ChildIO Input", Priority = ioPriority };

                // Set as background threads (will automatically stop when application ends)
                outputThread.IsBackground = errorThread.IsBackground
                    = inputThread.IsBackground = true;

                // Start the IO threads
                outputThread.Start(process);
                errorThread.Start(process);
                inputThread.Start(process);

                // Signal to end the application
                ManualResetEvent stopApp = new ManualResetEvent(false);

                // Enables the exited event and set the stopApp signal on exited
                process.EnableRaisingEvents = true;
                process.Exited += (e, sender) => { stopApp.Set(); };

                if (args.Length > 0 && args[args.Length - 1] == "-")
                    process.StandardInput.BaseStream.Close();

                // Wait for the child app to stop
                stopApp.WaitOne();

                return process.ExitCode;
            }            
        }

        /// <summary>
        /// Continuously copies data from one stream to the other.
        /// </summary>
        /// <param name="instream">The input stream.</param>
        /// <param name="outstream">The output stream.</param>
        private static void passThrough(Stream instream, Stream outstream)
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                int len;
                while ((len = instream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (outstream.CanWrite)
                    {
                        outstream.Write(buffer, 0, len);
                        outstream.Flush();
                    }                    
                }

                Thread.Sleep(100);
            }
        }

        private static void outputReader(object p)
        {
            var process = (Process)p;
            // Pass the standard output of the child to our standard output
            passThrough(process.StandardOutput.BaseStream, Console.OpenStandardOutput());
        }

        private static void errorReader(object p)
        {
            var process = (Process)p;
            // Pass the standard error of the child to our standard error
            passThrough(process.StandardError.BaseStream, Console.OpenStandardError());
        }

        private static void inputReader(object p)
        {
            var process = (Process)p;
            // Pass our standard input into the standard input of the child
            passThrough(Console.OpenStandardInput(), process.StandardInput.BaseStream);
        }
    }

    sealed class TempFile : IDisposable
    {
        string path;
        public TempFile() : this(System.IO.Path.GetTempFileName()) { }

        public TempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            this.path = path;
        }
        public string Path
        {
            get
            {
                if (path == null) throw new ObjectDisposedException(GetType().Name);
                return path;
            }
        }
        ~TempFile() { Dispose(false); }
        public void Dispose() { Dispose(true); }
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
            if (path != null)
            {
                try { File.Delete(path); }
                catch { } // best effort
                path = null;
            }
        }
    }
}