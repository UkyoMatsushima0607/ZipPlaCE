using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Common
{
    public abstract class Configuration
    {
        //public static readonly string XmlPath = Directory.GetCurrentDirectory() + @"\config.xml";
        private static readonly string XmlPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "config.xml");
        private static readonly string XmlBack = Path.ChangeExtension(XmlPath, "bak");
        private static readonly string RootName =
            ((AssemblyProductAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyProductAttribute))).Product.Replace(" ", "_") + "Config";
        // xmlFile・xmlNode はプロセス全体で共有するキャッシュ（cachedRoot）の木を指すことがあるので、書き換えてはいけない。
        // 保存するときは、その時点のファイルを複製して自分の要素だけ差し替える（Save）
        private XElement xmlFile = null;
        private XElement xmlNode = null;

        // 再入防止（XmlSerializer の逆シリアル化がこのコンストラクタを呼ぶため）。
        // スレッドごとに持つ。static だと、別スレッドで読み込み中に構築したものが既定値のまま返っていた（B2）
        [ThreadStatic]
        private static bool loaded;
        private Exception lastException = null;

        // 解析済みの config.xml を、更新日時とサイズをキーにプロセス全体で持つ（K1）。中身は読み取り専用として扱う。
        // 読み書きは cacheLocker の中で行う（LINQ to XML の木はスレッド安全ではないため、読むときも lock する）
        private static readonly object cacheLocker = new object();
        private static XElement cachedRoot = null;
        private static DateTime cachedLastWriteTimeUtc;
        private static long cachedLength = -1;

        /// <summary>
        /// config.xml を解析した木を返す。ファイルが変わっていなければキャッシュを返す。
        /// ファイルがないか、ルート要素の名前が違うときは空の木を返す（キャッシュしない）。cacheLocker の中で呼ぶこと。
        /// </summary>
        private static XElement GetCachedRoot()
        {
            var info = new FileInfo(XmlPath);
            if (!info.Exists) return new XElement(RootName);
            var time = info.LastWriteTimeUtc;
            var length = info.Length;
            if (cachedRoot != null && time == cachedLastWriteTimeUtc && length == cachedLength) return cachedRoot;
            cachedRoot = null;
            var root = XElement.Load(XmlPath);
            if (root.Name != RootName) return new XElement(RootName);
            cachedRoot = root;
            cachedLastWriteTimeUtc = time;
            cachedLength = length;
            return root;
        }

        public Configuration()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                var prepared = null as Dictionary<Type, Configuration>;
                if (prepareMode)
                {
                    // 準備タスクの完了前に別スレッドから構築された場合は、完了を待って準備済みの値を使う
                    PrepareXmlSerializer_Task?.Wait();
                    prepared = GetPreparedConfigration_Dictionary;
                }
                if (prepared != null)
                {
                    var thisType = GetType();
                    SetXElementToThis(thisType, GetPreparedConfigration(prepared, thisType));
                }
                else
                {
                    var thisType = GetType();
                    lock (cacheLocker)
                    {
                        xmlFile = null;
                        var root = GetCachedRoot();
                        // 同じ名前の要素が複数あるときは最初の 1 つを使う（残りは Save で消す）
                        var xElement = root.Element(thisType.Name);
                        if (xElement != null)
                        {
                            try
                            {
                                SetXElementToThis(thisType, xElement);
                            }
                            catch { }
                            xmlNode = xElement;
                        }
                        xmlFile = root;
                    }
                }
            }
            catch (NotPreparedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                xmlFile = null;
            }
            finally
            {
                loaded = false;
            }
        }
        
        public static bool ConfigFileExists() => File.Exists(XmlPath);
        public static bool ConfigDataProtection() => File.Exists(XmlBack);

        /// <summary>
        /// 先に ConfigFileExists() を行って下さい
        /// </summary>
        /// <returns></returns>
        public static DateTime GetLastWriteTimeWithoutFileCheckWithException() => File.GetLastWriteTime(XmlPath);

        public Exception GetLastException()
        {
            return lastException;
        }

        public Exception Save()
        {
#if DEBUG
            if (prepareMode) throw new Exception("Save can not be called in prepareMode.");
#else
            if (prepareMode) return new Exception("Save can not be called in prepareMode.");
#endif
            if (xmlFile != null)
            {
                try
                {
                    // シリアル化はプロパティの getter から別の設定を構築することがあるので lock の外で行う
                    var newNode = GetXElementFromThis();
                    // カタログとビューアのプロセスが同時に保存すると、片方の書き込みが共有違反で失敗したり、
                    // 読み直してから書くまでの間に他方の変更を古い内容で上書きしたりして設定が失われていた（B3）。
                    // 読み直し・差し替え・書き込みをプロセス間のミューテックスで直列にする
                    using (var interProcess = new System.Threading.Mutex(false, GetSaveMutexName()))
                    {
                    var ownsMutex = false;
                    try
                    {
                        try { ownsMutex = interProcess.WaitOne(5000); } catch (System.Threading.AbandonedMutexException) { ownsMutex = true; }
                    lock (cacheLocker)
                    {
                        // 読み込んだときから内容が変わっていなければ書き込まない（K1）
                        if (xmlNode != null && XNode.DeepEquals(xmlNode, newNode)) return null;

                        // その時点のファイルを複製して自分の要素だけ差し替える。
                        // 構築したときの木をそのまま書くと、そのあと他のウインドウやプロセスが保存した別の節を古い内容で上書きしてしまう
                        XElement file;
                        try
                        {
                            file = new XElement(GetCachedRoot());
                        }
                        catch
                        {
                            // 今のファイルが読めないときは、従来どおり構築したときの内容に自分の要素を入れて書く
                            file = new XElement(xmlFile);
                        }
                        var olds = file.Elements(GetType().Name).ToArray();
                        if (olds.Length == 0)
                        {
                            file.Add(newNode);
                        }
                        else
                        {
                            olds[0].ReplaceWith(newNode);
                            for (var i = 1; i < olds.Length; i++) olds[i].Remove();
                        }

                        // 書き込み中に他のプロセスに変えられた場合に備え、キャッシュは捨てて次の読み込みで読み直す
                        cachedRoot = null;
                        // ミューテックスを取れなかった・ほかのアプリが開いている場合に備え、共有違反は少し待って数回やり直す（B3）
                        for (var attempt = 0; ; attempt++)
                        {
                            try
                            {
                                WriteFile(file);
                                break;
                            }
                            catch (IOException) when (attempt < 4 && !File.Exists(XmlBack))
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        xmlFile = file;
                        xmlNode = newNode;
                    }
                    }
                    finally
                    {
                        if (ownsMutex) interProcess.ReleaseMutex();
                    }
                    }

                    return null;
                }
                catch (Exception exception)
                {
                    return lastException = exception;
                }
            }
            else
            {
                return lastException;
            }
        }

        // 設定ファイルのパスごとのプロセス間のミューテックスの名前（B3）。名前に \ は使えないので、パスのハッシュにする
        private static string GetSaveMutexName()
        {
            unchecked
            {
                var hash = 2166136261u; // FNV-1a（string.GetHashCode はプロセスによって変わりうるので使わない）
                foreach (var c in XmlPath.ToUpperInvariant())
                {
                    hash = (hash ^ c) * 16777619u;
                }
                return "ZipPlaCE.ConfigSave." + hash.ToString("x8");
            }
        }

        private static void WriteFile(XElement xmlFile)
        {
                    // 設定ファイルの保存
                    using (var ms = new MemoryStream())
                    {
                        xmlFile.Save(ms);
                        ms.Seek(0, SeekOrigin.Begin);
                        if (File.Exists(XmlPath))
                        {
                            using (var fs = new FileStream(XmlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                using (var fsb = new FileStream(XmlBack, FileMode.CreateNew, FileAccess.Write, FileShare.None)) fs.CopyTo(fsb);
                                fs.SetLength(0);
                                ms.CopyTo(fs);
                            }
                            File.Delete(XmlBack);

                            /*
                            File.Copy(XmlPath, XmlBack, overwrite: false); // 移動では一時的に設定ファイルが存在しない状態になるため危険
                            try
                            {
                                using (var fs = new FileStream(XmlPath, FileMode.Truncate, FileAccess.Write)) ms.CopyTo(fs);
                            }
                            catch
                            {
                                // Truncate したがその後の書き込みに失敗した、等のパターンで XmlBack からの復旧処理を行うか？

                                throw;
                            }
                            File.Delete(XmlBack);
                            */
                        }
                        else
                        {
                            try
                            {
                                using (var fs = new FileStream(XmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                {
                                    if (File.Exists(XmlBack)) throw new IOException();
                                    ms.CopyTo(fs);
                                }
                            }
                            catch
                            {
                                try
                                {
                                    File.Delete(XmlPath);
                                }
                                catch { }
                                throw;
                            }

                            // バックアップが残される可能性があり危険
                            //using (var fs = new FileStream(XmlBack, FileMode.CreateNew, FileAccess.Write)) ms.CopyTo(fs);
                            //File.Move(XmlBack, XmlPath);
                        }
                    }

                    //xmlFile.Save(XmlPath);
        }

        public static DateTime GetLastWriteTime()
        {
            try
            {
                if (File.Exists(XmlPath))
                {
                    return GetLastWriteTimeWithoutFileCheckWithException();
                }
                else
                {
                    return new DateTime();
                }
            }
            catch
            {
                return new DateTime();
            }
        }

        private XElement GetXElementFromThis()
        {
            using (var stream = new MemoryStream())
            {
                var serializer = GetXmlSerializer(GetType());
                serializer.Serialize(stream, this);
                stream.Position = 0;
                // ファイルから読んだ木（XElement.Load(path) は空白だけのテキストを捨てる）と比べられるように、空白を捨てて読む
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true }))
                {
                    return XElement.Load(reader);
                }
            }
        }

        // 準備タスクと UI スレッドなど、複数のスレッドから使うので lock する
        private static readonly Dictionary<Type, XmlSerializer> setXElementToThis_XmlSerializer = new Dictionary<Type, XmlSerializer>();
        private static XmlSerializer GetXmlSerializer(Type type)
        {
            lock (setXElementToThis_XmlSerializer)
            {
                if (!setXElementToThis_XmlSerializer.TryGetValue(type, out var serializer))
                {
                    var temp = loaded;
                    loaded = true;
                    try
                    {
                        serializer = new XmlSerializer(type);
                    }
                    finally
                    {
                        loaded = temp;
                    }
                    setXElementToThis_XmlSerializer[type] = serializer;
                }
                return serializer;
            }
        }

        public class NotPreparedException : Exception
        {
            public NotPreparedException(string message) : base(message) { }
        }

        private static Dictionary<Type, Configuration> GetPreparedConfigration_Dictionary = null;
        private static Configuration GetPreparedConfigration(Dictionary<Type, Configuration> prepared, Type type)
        {
            if (prepared.TryGetValue(type, out var resultFromDictionary))
            {
                return resultFromDictionary;
            }
            else throw new NotPreparedException($"Not prepared ({type.Name})");
        }

        private static Task PrepareXmlSerializer_Task = null;
        public static void PrepareXmlSerializer(params Type[] configrationTypes)
        {
#if DEBUG
            foreach (var type in configrationTypes)
            {
                // IsSubclassOf: This method also returns false if c and the current Type are equal.
                if (!type.IsSubclassOf(typeof(Configuration)))
                {
                    throw new Exception($"{type.Name} is not Configration.");
                }
            }
#endif
            prepareMode = true;
            GetPreparedConfigration_Dictionary = new Dictionary<Type, Configuration>();
            if (configrationTypes.Length == 0) return;
            var dictionary = GetPreparedConfigration_Dictionary;

            PrepareXmlSerializer_Task = Task.Run(() =>
            {
                // loaded はスレッドごとなので、このタスクのスレッドで立てる
                loaded = true;
                try
                {
                    XElement xmlFile = null;
                    Exception lastException = null;
                    for (var i = 0; i < configrationTypes.Length; i++)
                    {
                        var type = configrationTypes[i];
                        var serializer = GetXmlSerializer(type);
                        Configuration configration = null;
                        try
                        {
                            if (lastException != null) throw lastException;
                            lock (cacheLocker)
                            {
                                if (i == 0) xmlFile = GetCachedRoot();
                                configration = GetConfigration(serializer, type, xmlFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            if (configration == null)
                            {
                                configration = GetEmptyConfigration(type);
                            }
                            configration.lastException = ex;
                        }
                        dictionary.Add(type, configration);
                    }
                }
                finally
                {
                    loaded = false;
                }
            });
        }

        public static void WaitPrepareTask()
        {
            if (PrepareXmlSerializer_Task != null)
            {
                PrepareXmlSerializer_Task.Wait();
                PrepareXmlSerializer_Task = null;
            }
        }

        private static Configuration GetEmptyConfigration(Type type)
        {
            var temp = loaded;
            loaded = true;
            var configration = Activator.CreateInstance(type) as Configuration;
            loaded = temp;
            return configration;
        }

        private static Configuration GetConfigration(XmlSerializer serializer, Type type, XElement xmlFile)
        {
            if (xmlFile == null) return GetEmptyConfigration(type);
            var xElement = xmlFile.Element(type.Name);
            return xElement != null ? GetConfigration(serializer, xElement) : GetEmptyConfigration(type);
        }

        private static Configuration GetConfigration(XmlSerializer serializer, XElement xElement)
        {
            using (var stream = new MemoryStream())
            {
                xElement.Save(stream);
                stream.Position = 0;
                return serializer.Deserialize(stream) as Configuration;
            }
        }

        private static bool prepareMode = false;

        public static void ClosePrepareMode()
        {
            if (prepareMode)
            {
#if DEBUG
                foreach (var pair in setXElementToThis_XmlSerializer)
                {
                    if (!(pair.Value is XmlSerializer)) throw new Exception($"{pair.Key.Name} is prepared but not loaded in prepareMode.");
                }
#endif
                prepareMode = false;
                GetPreparedConfigration_Dictionary = null;
            }
        }

        private void SetXElementToThis(Type thisType, XElement xElement)
        {
            var serializer = GetXmlSerializer(thisType);
            var configration = GetConfigration(serializer, xElement);
            SetXElementToThis(thisType, configration);
        }

        private void SetXElementToThis(Type thisType, object configration)
        {
                                                          //ZipPla.Program.RunTimeMeasure?.Block("after sirialize");
            foreach (var element in thisType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                element.SetValue(this, element.GetValue(configration));
            }

            foreach (var element in thisType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                element.SetValue(this, element.GetValue(configration));
            }

            var asConfigration = configration as Configuration;
            var newException = asConfigration.lastException;
            if (newException != null)
            {
                lastException = newException;
            }
        }

        /// <summary>
        /// DictionaryをKeyAndValueのListに変換する
        /// </summary>
        /// <typeparam name="TKey">Dictionaryのキーの型</typeparam>
        /// <typeparam name="TValue">Dictionaryの値の型</typeparam>
        /// <param name="dic">変換するDictionary</param>
        /// <returns>変換されたKeyAndValueの配列</returns>
        public static KeyAndValue<TKey, TValue>[] ConvertDictionaryToArray<TKey, TValue>(Dictionary<TKey, TValue> dic)
        {
            if (dic == null)
            {
                return null;
            }
            else
            {
                return (from pair in dic select new KeyAndValue<TKey, TValue>(pair)).ToArray();
            }
        }

        /// <summary>
        /// KeyAndValueのListをDictionaryに変換する
        /// </summary>
        /// <typeparam name="TKey">KeyAndValueのKeyの型</typeparam>
        /// <typeparam name="TValue">KeyAndValueのValueの型</typeparam>
        /// <param name="array">変換するKeyAndValueのIEnumerableクラス</param>
        /// <returns>変換されたDictionary</returns>
        public static Dictionary<TKey, TValue> ConvertArrayToDictionary<TKey, TValue>(IEnumerable<KeyAndValue<TKey, TValue>> array)
        {
            var dic = new Dictionary<TKey, TValue>();
            if (array != null) foreach (var pair in array) dic.Add(pair.Key, pair.Value);
            return dic;
        }
    }

    /// <summary>
    /// シリアル化できる、KeyValuePairに代わる構造体
    /// </summary>
    /// <typeparam name="TKey">Keyの型</typeparam>
    /// <typeparam name="TValue">Valueの型</typeparam>
    [Serializable]
    public struct KeyAndValue<TKey, TValue>
    {
        public TKey Key;
        public TValue Value;

        public KeyAndValue(KeyValuePair<TKey, TValue> pair)
        {
            Key = pair.Key;
            Value = pair.Value;
        }
    }
}
