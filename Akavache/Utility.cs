﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NLog;
using ReactiveUI;

namespace Akavache
{
    static class Utility
    {
        static readonly Logger log = LogManager.GetCurrentClassLogger();

        public static string GetMd5Hash(string input)
        {
            using (var md5Hasher = MD5.Create())
            {
                // Convert the input string to a byte array and compute the hash.
                var data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(input));
                var sBuilder = new StringBuilder();
                foreach (var item in data)
                {
                    sBuilder.Append(item.ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }

        public static IObservable<FileStream> SafeOpenFileAsync(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return Observable.Create<FileStream>(subj =>
            {
                try
                {
                    var createModes = new[]
                    {
                        FileMode.Create,
                        FileMode.CreateNew,
                        FileMode.OpenOrCreate,
                    };
    
                    if (!createModes.Contains(mode) && !File.Exists(path))
                    {
                        subj.OnError(new FileNotFoundException());
                    } else
                    {
                        subj.OnNext(new FileStream(path, mode, access, share, 4096, true));
                        subj.OnCompleted();
                    }
                }
                catch (Exception ex)
                {
                    subj.OnError(new FileNotFoundException());
                }
                        
                return Disposable.Empty;
            });
        }

        public static void CreateRecursive(this DirectoryInfo This)
        {
            This.FullName.Split(Path.DirectorySeparatorChar).Scan("", (acc, x) =>
            {
                var path = Path.Combine(acc, x);

                if (path[path.Length - 1] == Path.VolumeSeparatorChar)
                {
                    path += Path.DirectorySeparatorChar;
                }

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return (new DirectoryInfo(path)).FullName;
            });
        }

        public static TAcc Scan<T, TAcc>(this IEnumerable<T> This, TAcc initialValue, Func<TAcc, T, TAcc> accFunc)
        {
            TAcc acc = initialValue;

            foreach (var x in This)
            {
                acc = accFunc(acc, x);
            }

            return acc;
        }

        public static IObservable<T> LogErrors<T>(this IObservable<T> This, string message = null)
        {
            return Observable.Create<T>(subj =>
            {
                return This.Subscribe(subj.OnNext,
                    ex =>
                    {
                        var msg = message ?? "0x" + This.GetHashCode().ToString("x");
                        log.Info("{0} failed with {1}:\n{2}", msg, ex.Message, ex.ToString());
                        subj.OnError(ex);
                    }, subj.OnCompleted);
            });
        }

        public static IObservable<Unit> CopyToAsync(this Stream This, Stream destination, IScheduler scheduler = null)
        {
            return Observable.Start(() =>
            {
                try
                {
                    This.CopyTo(destination);
                    This.Dispose();
                    destination.Dispose();
                }
                catch(Exception ex)
                {
                    log.Warn("CopyToAsync failed", ex);
                }
            }, scheduler ?? RxApp.TaskpoolScheduler);

#if FALSE
            var reader = Observable.FromAsyncPattern<byte[], int, int, int>(This.BeginRead, This.EndRead);
            var writer = Observable.FromAsyncPattern<byte[], int, int>(destination.BeginWrite, destination.EndWrite);

            //var bufs = new ThreadLocal<byte[]>(() => new byte[4096]);
            var bufs = new Lazy<byte[]>(() => new byte[4096]);

            var readStream = Observable.Defer(() => reader(bufs.Value, 0, 4096))
                .Repeat()
                .TakeWhile(x => x > 0);

            var ret = readStream
                .Select(x => writer(bufs.Value, 0, x))
                .Concat()
                .Aggregate(Unit.Default, (acc, _) => Unit.Default)
                .Finally(() => { This.Dispose(); destination.Dispose(); })
                .Multicast(new ReplaySubject<Unit>());

            ret.Connect();
            return ret;
#endif
        }

        public static void Retry(this Action block, int retries = 3)
        {
            while (true)
            {
                try
                {
                    block();
                    return;
                }
                catch (Exception)
                {
                    retries--;
                    if (retries == 0)
                    {
                        Thread.Sleep(10);
                        throw;
                    }
                }
            }
        }

        public static T Retry<T>(this Func<T> block, int retries = 3)
        {
            while (true)
            {
                try
                {
                    T ret = block();
                    return ret;
                }
                catch (Exception)
                {
                    retries--;
                    if (retries == 0)
                    {
                        Thread.Sleep(10);
                        throw;
                    }
                }
            }
        }
    }
}
