﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signum.Engine.Maps;
using System.Threading;
using Signum.Utilities;
using System.IO;
using System.Threading.Tasks;

namespace Signum.Engine
{
    public delegate bool StopOnExceptionDelegate(string id, string fileName, Exception exception);

    public static class ProgressExtensions
    {
        public static StopOnExceptionDelegate StopOnException = null;

        /// <summary>
        /// Executes an action for each element in the collection transactionally and showing the progress in the Console
        /// </summary>
        public static void ProgressForeach<T>(this IEnumerable<T> collection, Func<T, string> elementID, Action<T> action)
        {
            LogWriter writer = GetLogWriter(null);


            var enumerator = collection.ToProgressEnumerator(out IProgressInfo pi);

            if (!Console.IsOutputRedirected)
                SafeConsole.WriteSameLine(pi.ToString());

            foreach (var item in enumerator)
            {
                using (HeavyProfiler.Log("ProgressForeach", () => elementID(item)))
                    try
                    {
                        using (Transaction tr = Transaction.ForceNew())
                        {
                            action(item);
                            tr.Commit();
                        }
                    }
                    catch (Exception e)
                    {
                        writer(ConsoleColor.Red, "{0:u} Error in {1}: {2}", DateTime.Now, elementID(item), e.Message);
                        writer(ConsoleColor.DarkRed, e.StackTrace.Indent(4));

                        if (StopOnException != null && StopOnException(elementID(item), null, e))
                            throw;
                    }

                if (!Console.IsOutputRedirected)
                    SafeConsole.WriteSameLine(pi.ToString());
            }
            if (!Console.IsOutputRedirected)
                SafeConsole.ClearSameLine();
        }

        /// <summary>
        /// Executes an action for each element in the collection transactionally and showing the progress in the Console.
        /// </summary>
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        public static void ProgressForeach<T>(this IEnumerable<T> collection, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            using (StreamWriter log = TryOpenAutoFlush(fileName))
            {
                if (log != null)
                    log.AutoFlush = true;

                LogWriter writer = GetLogWriter(log);


                var enumerator = collection.ToProgressEnumerator(out IProgressInfo pi);

                if (!Console.IsOutputRedirected)
                    SafeConsole.WriteSameLine(pi.ToString());

                foreach (var item in enumerator)
                {
                    using (HeavyProfiler.Log("ProgressForeach", () => elementID(item)))
                        try
                        {
                            using (Transaction tr = Transaction.ForceNew())
                            {
                                action(item, writer);
                                tr.Commit();
                            }
                        }
                        catch (Exception e)
                        {
                            writer(ConsoleColor.Red, "{0:u} Error in {1}: {2}", DateTime.Now, elementID(item), e.Message);
                            writer(ConsoleColor.DarkRed, e.StackTrace.Indent(4));

                            if (StopOnException != null && StopOnException(elementID(item), fileName, e))
                                throw;
                        }

                    if (!Console.IsOutputRedirected)
                        SafeConsole.WriteSameLine(pi.ToString());
                }
                if (!Console.IsOutputRedirected)
                    SafeConsole.ClearSameLine();
            }
        }

        /// <summary>
        /// Executes an action for each element in the collection WITHOUT transactions, and showing the progress in the Console
        /// </summary>
        public static void ProgressForeachNonTransactional<T>(this IEnumerable<T> collection, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            using (StreamWriter log = TryOpenAutoFlush(fileName))
            {
                if (log != null)
                    log.AutoFlush = true;

                LogWriter writer = GetLogWriter(log);


                var enumerator = collection.ToProgressEnumerator(out IProgressInfo pi);

                if (!Console.IsOutputRedirected)
                    SafeConsole.WriteSameLine(pi.ToString());

                foreach (var item in enumerator)
                {
                    using (HeavyProfiler.Log("ProgressForeach", () => elementID(item)))
                        try
                        {
                            action(item, writer);
                        }
                        catch (Exception e)
                        {
                            writer(ConsoleColor.Red, "{0:u} Error in {1}: {2}", DateTime.Now, elementID(item), e.Message);
                            writer(ConsoleColor.DarkRed, e.StackTrace.Indent(4));

                            if (StopOnException != null && StopOnException(elementID(item), fileName, e))
                                throw;
                        }

                    if (!Console.IsOutputRedirected)
                        SafeConsole.WriteSameLine(pi.ToString());
                }
                if (!Console.IsOutputRedirected)
                    SafeConsole.ClearSameLine();
            }
        }


        /// <summary>
        /// Executes an action for each element in the collection in paralel and transactionally, and showing the progress in the Console.
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        /// </summary>
        public static void ProgressForeachParallel<T>(this IEnumerable<T> collection, Func<T, string> elementID, string fileName, Action<T, LogWriter> action, ParallelOptions paralelOptions = null)
        {
            using (StreamWriter log = TryOpenAutoFlush(fileName))
            {
                try
                {
                    LogWriter writer = GetLogWriter(log);


                    var col = collection.ToProgressEnumerator(out IProgressInfo pi);

                    if (!Console.IsOutputRedirected)
                        lock (SafeConsole.SyncKey)
                            SafeConsole.WriteSameLine(pi.ToString());

                    Exception stopException = null;

                    using (ExecutionContext.SuppressFlow())
                        Parallel.ForEach(col, paralelOptions ?? new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (item, state) =>
                       {
                           using (HeavyProfiler.Log("ProgressForeach", () => elementID(item)))
                               try
                               {
                                   using (Transaction tr = Transaction.ForceNew())
                                   {
                                       action(item, writer);
                                       tr.Commit();
                                   }
                               }
                               catch (Exception e)
                               {
                                   writer(ConsoleColor.Red, "{0:u} Error in {1}: {2}", DateTime.Now, elementID(item), e.Message);
                                   writer(ConsoleColor.DarkRed, e.StackTrace.Indent(4));

                                   if (StopOnException != null && StopOnException(elementID(item), fileName, e))
                                       stopException = e;
                               }

                           if (!Console.IsOutputRedirected)
                               lock (SafeConsole.SyncKey)
                                   SafeConsole.WriteSameLine(pi.ToString());

                           if (stopException != null)
                               state.Break();

                       });

                    if (stopException != null)
                        throw stopException;
                }
                finally
                {
                    if (!Console.IsOutputRedirected)
                        SafeConsole.ClearSameLine();
                }
            }
        }

        /// <summary>
        /// Executes an action for each element in the collection transactionally and showing the progress in the Console.
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        /// </summary>
        public static void ProgressForeach<T>(this IEnumerable<T> collection, bool isParallel, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            if (isParallel)
                collection.ProgressForeachParallel(elementID, fileName, action);
            else
                collection.ProgressForeach(elementID, fileName, action);
        }

        /// <summary>
        /// Executes an action for each element in the collection transactionally and showing the progress in the Console.
        /// Also it disables identity behaviour in the Schema and in Sql Server.
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        /// </summary>
        public static void ProgressForeachDisableIdentity<T>(this IEnumerable<T> collection, Type tableType, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            Table table = Schema.Current.Table(tableType);

            if (!table.IdentityBehaviour)
                throw new InvalidOperationException("Identity is false already");

            table.IdentityBehaviour = false;
            try
            {
                collection.ProgressForeach(elementID, fileName, (item, writer) =>
                {
                    using (table.PrimaryKey.Default != null ? null: Administrator.DisableIdentity(table.Name))
                        action(item, writer);
                });
            }
            finally
            {
                table.IdentityBehaviour = true;
                if (!Console.IsOutputRedirected)
                    SafeConsole.ClearSameLine();
            }
        }

        /// <summary>
        /// Executes an action for each element in the collection in paralel, transactionally and showing the progress in the Console.
        /// Also it disables identity behaviour in the Schema and in Sql Server.
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        /// </summary>
        public static void ProgressForeachParallelDisableIdentity<T>(this IEnumerable<T> collection, Type tableType, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            Table table = Schema.Current.Table(tableType);

            if (!table.IdentityBehaviour)
                throw new InvalidOperationException("Identity is false already");

            table.IdentityBehaviour = false;
            try
            {
                collection.ProgressForeachParallel(elementID, fileName, (item, writer) =>
                {
                    using (table.PrimaryKey.Default != null ? null : Administrator.DisableIdentity(table.Name))
                        action(item, writer);
                });
            }
            finally
            {
                table.IdentityBehaviour = true;
            }
        }

        /// <summary>
        /// Executes an action for each element in the collection transactionally and showing the progress in the Console.
        /// Also it disables identity behaviour in the Schema and in Sql Server. Optionally in Parallel.
        /// <param name="action">Use LogWriter to write in the Console and the file at the same time</param>
        /// </summary>
        public static void ProgressForeachDisableIdentity<T>(this IEnumerable<T> collection, bool isParallel, Type tableType, Func<T, string> elementID, string fileName, Action<T, LogWriter> action)
        {
            if (isParallel)
                collection.ProgressForeachParallelDisableIdentity(tableType, elementID, fileName, action);
            else
                collection.ProgressForeachDisableIdentity(tableType, elementID, fileName, action);
        }

        public static string DefaultLogFolder = "Log";

        public static StreamWriter TryOpenAutoFlush(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            if (!Path.IsPathRooted(fileName))
            {
                if (!Directory.Exists(DefaultLogFolder))
                    Directory.CreateDirectory(DefaultLogFolder);

                fileName = Path.Combine(DefaultLogFolder, fileName);
            }

            var result = new StreamWriter(fileName, append: true)
            {
                AutoFlush = true
            };
            return result;
        }

        public static LogWriter GetLogWriter(StreamWriter logStreamWriter)
        {
            if (logStreamWriter != null)
            {
                return (color, str, parameters) =>
                {
                    string f = parameters.IsNullOrEmpty() ? str : str.FormatWith(parameters);
                    lock (logStreamWriter)
                        logStreamWriter.WriteLine(f);
                    lock (SafeConsole.SyncKey)
                    {
                        if (!Console.IsOutputRedirected)
                            SafeConsole.ClearSameLine();

                        if (parameters.IsNullOrEmpty())
                            SafeConsole.WriteLineColor(color, str);
                        else
                            SafeConsole.WriteLineColor(color, str, parameters);
                    }
                };
            }
            else
            {
                return (color, str, parameters) =>
                {
                    lock (SafeConsole.SyncKey)
                    {
                        if (!Console.IsOutputRedirected)
                            SafeConsole.ClearSameLine();

                        if (parameters.IsNullOrEmpty())
                            SafeConsole.WriteLineColor(color, str);
                        else
                            SafeConsole.WriteLineColor(color, str, parameters);
                    }
                };
            }
        }

        public delegate void LogWriter(ConsoleColor color, string text, params object[] parameters);

    }
}
