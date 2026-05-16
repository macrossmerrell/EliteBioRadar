using System;
using System.IO;

namespace EliteBioRadar
{
    public static class Log
    {
        private static readonly string _path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EliteBioRadar.log");

        private static readonly object _lock = new();

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff}  {message}\r\n");
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try { File.Delete(_path); } catch { }
        }
    }
}
